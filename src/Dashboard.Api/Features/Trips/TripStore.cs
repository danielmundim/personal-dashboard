using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using Dashboard.Api.Infrastructure;

namespace Dashboard.Api.Features.Trips;

/// <summary>
/// Our trips, edited from the dashboard's Viagens tab and kept in DataPath/trips.json. Same pattern as
/// the shopping list: one small file, whole-file atomic rewrites, a lock around every change.
/// </summary>
public sealed class TripStore(DataDirectory dataDirectory, LocalClock clock)
{
    /// <summary>
    /// A trip without a return date is still "on" while any of its dates is recent: we may be hopping
    /// between places without having planned the next one yet.
    /// </summary>
    private const int OpenTripGraceDays = 14;

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
    };

    private readonly SemaphoreSlim _gate = new(1, 1);
    private string FilePath => Path.Combine(dataDirectory.Root, "trips.json");

    public async Task<TripList> GetAsync(CancellationToken ct)
    {
        await _gate.WaitAsync(ct);
        try
        {
            return ToList(await LoadAsync(ct));
        }
        finally
        {
            _gate.Release();
        }
    }

    // ---------- Trips ----------

    public Task<(TripList List, Trip? Trip)> CreateAsync(SaveTrip request, CancellationToken ct) =>
        MutateAsync(trips =>
        {
            var trip = new Trip(Guid.NewGuid(), request.Name.Trim(), request.StartDate, ValidEnd(request), Clean(request.Notes), [], [], clock.Now);
            trips.Add(trip);
            return trip;
        }, ct);

    public Task<(TripList List, Trip? Trip)> UpdateAsync(Guid id, SaveTrip request, CancellationToken ct) =>
        MutateTripAsync(id, t => t with
        {
            Name = request.Name.Trim(),
            StartDate = request.StartDate,
            EndDate = ValidEnd(request),
            Notes = Clean(request.Notes),
        }, ct);

    public Task<(TripList List, Trip? Trip)> DeleteAsync(Guid id, CancellationToken ct) =>
        MutateAsync(trips =>
        {
            trips.RemoveAll(t => t.Id == id);
            return null;
        }, ct);

    // ---------- Itinerary ----------

    public Task<(TripList List, Trip? Trip)> AddStopAsync(Guid tripId, SaveStop request, CancellationToken ct) =>
        MutateTripAsync(tripId, t => t with { Stops = [.. t.Stops, NewStop(Guid.NewGuid(), request)] }, ct);

    public Task<(TripList List, Trip? Trip)> UpdateStopAsync(Guid tripId, Guid stopId, SaveStop request, CancellationToken ct) =>
        MutateTripAsync(tripId, t => t with
        {
            Stops = t.Stops
                .Select(s => s.Id != stopId ? s : NewStop(s.Id, request) with
                {
                    OutboundTicket = s.OutboundTicket,
                    Lodging = s.Lodging,
                })
                .ToList(),
        }, ct);

    public Task<(TripList List, Trip? Trip)> UpdateStopBookingsAsync(Guid tripId, Guid stopId, UpdateStopBookings request, CancellationToken ct) =>
        MutateTripAsync(tripId, t => t with
        {
            Stops = t.Stops
                .Select(s => s.Id != stopId ? s : s with
                {
                    OutboundTicket = request.OutboundTicket ?? s.OutboundTicket,
                    Lodging = request.Lodging ?? s.Lodging,
                })
                .ToList(),
        }, ct);

    public Task<(TripList List, Trip? Trip)> DeleteStopAsync(Guid tripId, Guid stopId, CancellationToken ct) =>
        MutateTripAsync(tripId, t => t with { Stops = t.Stops.Where(s => s.Id != stopId).ToList() }, ct);

    /// <summary>Moves a stop to <paramref name="index"/> (clamped to the list), keeping the others in order.</summary>
    public Task<(TripList List, Trip? Trip)> MoveStopAsync(Guid tripId, Guid stopId, int index, CancellationToken ct) =>
        MutateTripAsync(tripId, t =>
        {
            var stops = t.Stops.ToList();
            var from = stops.FindIndex(s => s.Id == stopId);
            if (from < 0) return t;
            var stop = stops[from];
            stops.RemoveAt(from);
            stops.Insert(Math.Clamp(index, 0, stops.Count), stop);
            return t with { Stops = stops };
        }, ct);

    // ---------- Checklist ----------

    public Task<(TripList List, Trip? Trip)> AddItemAsync(Guid tripId, AddChecklistItem request, CancellationToken ct) =>
        MutateTripAsync(tripId, t => t with
        {
            Checklist = [.. t.Checklist, new ChecklistItem(Guid.NewGuid(), request.Text.Trim(), request.Kind, false)],
        }, ct);

    public Task<(TripList List, Trip? Trip)> UpdateItemAsync(Guid tripId, Guid itemId, UpdateChecklistItem request, CancellationToken ct) =>
        MutateTripAsync(tripId, t => t with
        {
            Checklist = t.Checklist
                .Select(i => i.Id != itemId ? i : i with
                {
                    Done = request.Done ?? i.Done,
                    Text = string.IsNullOrWhiteSpace(request.Text) ? i.Text : request.Text.Trim(),
                    Kind = request.Kind ?? i.Kind,
                })
                .ToList(),
        }, ct);

    public Task<(TripList List, Trip? Trip)> DeleteItemAsync(Guid tripId, Guid itemId, CancellationToken ct) =>
        MutateTripAsync(tripId, t => t with { Checklist = t.Checklist.Where(i => i.Id != itemId).ToList() }, ct);

    // ---------- Storage ----------

    private Task<(TripList List, Trip? Trip)> MutateTripAsync(Guid id, Func<Trip, Trip> change, CancellationToken ct) =>
        MutateAsync(trips =>
        {
            var i = trips.FindIndex(t => t.Id == id);
            if (i < 0) return null;
            trips[i] = change(trips[i]);
            return trips[i];
        }, ct);

    private async Task<(TripList List, Trip? Trip)> MutateAsync(Func<List<Trip>, Trip?> change, CancellationToken ct)
    {
        await _gate.WaitAsync(ct);
        try
        {
            var trips = await LoadAsync(ct);
            var changed = change(trips);
            await SaveAsync(trips, ct);
            return (ToList(trips), changed);
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task SaveAsync(List<Trip> trips, CancellationToken ct)
    {
        var tmp = FilePath + ".tmp";
        await File.WriteAllTextAsync(tmp, JsonSerializer.Serialize(new { version = 2, trips }, Json), ct);
        File.Move(tmp, FilePath, overwrite: true);
    }

    private async Task<List<Trip>> LoadAsync(CancellationToken ct)
    {
        if (!File.Exists(FilePath)) return [];
        var root = JsonNode.Parse(await File.ReadAllTextAsync(FilePath, ct));
        var nodes = root?["trips"] as JsonArray ?? [];

        // Version 1 had a single free-text "destination" per trip; it becomes the itinerary's first stop.
        var migrated = false;
        var trips = new List<Trip>();
        foreach (var node in nodes)
        {
            if (node is not JsonObject o) continue;
            var destination = o["destination"]?.GetValue<string?>();
            o.Remove("destination");
            var trip = o.Deserialize<Trip>(Json);
            if (trip is null) continue;
            trip = trip with { Stops = trip.Stops ?? [], Checklist = trip.Checklist ?? [] };
            if (!string.IsNullOrWhiteSpace(destination) && trip.Stops.Count == 0)
            {
                trip = trip with { Stops = [new TripStop(Guid.NewGuid(), destination.Trim(), trip.StartDate, trip.EndDate, null, null)] };
                migrated = true;
            }
            trips.Add(trip);
        }

        if (migrated) await SaveAsync(trips, ct);
        return trips;
    }

    /// <summary>The trip is over when its return has passed, or (without a return) a while after its last known date.</summary>
    private TripList ToList(List<Trip> trips)
    {
        var today = clock.Today;
        bool IsPast(Trip t)
        {
            if (t.EndDate is { } end) return end < today;
            var last = new[] { t.StartDate }
                .Concat(t.Stops.SelectMany(s => new[] { s.Arrive, s.Depart }).OfType<DateOnly>())
                .Max();
            return last.AddDays(OpenTripGraceDays) < today;
        }

        return new TripList(
            trips.Where(t => !IsPast(t)).OrderBy(t => t.StartDate).ToList(),
            trips.Where(IsPast).OrderByDescending(t => t.StartDate).ToList(),
            clock.Now);
    }

    private static TripStop NewStop(Guid id, SaveStop r) =>
        new(id, r.Place.Trim(), r.Arrive, r.Depart is { } d && (r.Arrive is null || d >= r.Arrive) ? d : null, r.Transport, Clean(r.Notes));

    private static DateOnly? ValidEnd(SaveTrip r) => r.EndDate is { } end && end >= r.StartDate ? end : null;

    private static string? Clean(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();
}
