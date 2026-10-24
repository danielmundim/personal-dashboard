using System.Text.Json;
using System.Text.Json.Serialization;
using Dashboard.Api.Infrastructure;

namespace Dashboard.Api.Features.Trips;

/// <summary>
/// Our trips, edited from the dashboard's Viagens tab and kept in DataPath/trips.json. Same pattern as
/// the shopping list: one small file, whole-file atomic rewrites, a lock around every change.
/// </summary>
public sealed class TripStore(DataDirectory dataDirectory, LocalClock clock)
{
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

    public Task<(TripList List, Trip? Trip)> CreateAsync(SaveTrip request, CancellationToken ct) =>
        MutateAsync(trips =>
        {
            var trip = new Trip(Guid.NewGuid(), request.Name.Trim(), Clean(request.Destination), request.StartDate,
                ValidEnd(request), Clean(request.Notes), [], clock.Now);
            trips.Add(trip);
            return trip;
        }, ct);

    public Task<(TripList List, Trip? Trip)> UpdateAsync(Guid id, SaveTrip request, CancellationToken ct) =>
        MutateTripAsync(id, t => t with
        {
            Name = request.Name.Trim(),
            Destination = Clean(request.Destination),
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
            var tmp = FilePath + ".tmp";
            await File.WriteAllTextAsync(tmp, JsonSerializer.Serialize(new { version = 1, trips }, Json), ct);
            File.Move(tmp, FilePath, overwrite: true);
            return (ToList(trips), changed);
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task<List<Trip>> LoadAsync(CancellationToken ct)
    {
        if (!File.Exists(FilePath)) return [];
        await using var stream = File.OpenRead(FilePath);
        var file = await JsonSerializer.DeserializeAsync<TripFile>(stream, Json, ct);
        return file?.Trips ?? [];
    }

    /// <summary>A trip stays "upcoming" until its last day has passed.</summary>
    private TripList ToList(List<Trip> trips)
    {
        var today = clock.Today;
        bool IsPast(Trip t) => (t.EndDate ?? t.StartDate) < today;
        return new TripList(
            trips.Where(t => !IsPast(t)).OrderBy(t => t.StartDate).ToList(),
            trips.Where(IsPast).OrderByDescending(t => t.StartDate).ToList(),
            clock.Now);
    }

    private static DateOnly? ValidEnd(SaveTrip r) => r.EndDate is { } end && end > r.StartDate ? end : null;

    private static string? Clean(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();

    private sealed record TripFile(List<Trip>? Trips);
}
