using System.Globalization;
using System.Text.Json.Serialization;
using Dashboard.Api.Infrastructure;
using Microsoft.Extensions.Caching.Hybrid;
using Microsoft.Extensions.Options;

namespace Dashboard.Api.Features.Transit;

/// <summary>
/// Live arrivals from the JSON API behind stcp.pt's stop pages (/api/stops/{code}/realtime). It is
/// not a documented public API, so the shape may change without notice; failures surface as a tile
/// error rather than stale sample data.
/// </summary>
public sealed class StcpTransitProvider(HttpClient http, HybridCache cache, LocalClock clock, IOptions<DashboardOptions> options)
    : ITransitProvider
{
    private static readonly TextInfo Text = CultureInfo.GetCultureInfo("pt-PT").TextInfo;
    private static readonly HashSet<string> LowerWords = ["da", "das", "de", "do", "dos", "e"];

    public async Task<StopBoard> GetStopBoardAsync(string stopId, CancellationToken cancellationToken)
    {
        var stop = options.Value.BusStops.First(s => s.Id == stopId);
        var code = string.IsNullOrWhiteSpace(stop.Code) ? stop.Id : stop.Code;

        var realtime = await http.GetFromJsonAsync<RealtimeResponse>($"api/stops/{Uri.EscapeDataString(code)}/realtime", cancellationToken)
            ?? throw new InvalidOperationException($"STCP returned no data for stop {code}.");

        // The lines serving a stop rarely change, so look them up once a day.
        var lines = await cache.GetOrCreateAsync(
            $"stcp:lines:{code}",
            async ct => (await http.GetFromJsonAsync<StopResponse>($"api/stops/{Uri.EscapeDataString(code)}", ct))?.Routes.Select(r => r.Number).ToList() ?? [],
            new HybridCacheEntryOptions { Expiration = TimeSpan.FromDays(1), LocalCacheExpiration = TimeSpan.FromDays(1) },
            cancellationToken: cancellationToken);

        var now = clock.Now;
        var departures = realtime.Arrivals
            .Select(a => new Departure(
                a.RouteShortName,
                Pretty(a.TripHeadsign),
                MinutesUntil(a, now),
                RealTime: a.EstimatedArrivalTime is not null && realtime.DataSource == "realtime",
                Delayed: a.Status == "DELAYED",
                ArrivesAt: a.EstimatedArrivalTime ?? a.ScheduledArrivalTime,
                ScheduledAt: Scheduled(a),
                DelayMinutes: (int)Math.Round(a.DelayMinutes ?? 0),
                Color: a.RouteColor,
                TextColor: a.RouteTextColor))
            .Where(d => d.MinutesAway >= 0)
            .OrderBy(d => d.MinutesAway)
            .Take(6)
            .ToList();

        var name = string.IsNullOrWhiteSpace(stop.Name) ? Pretty(realtime.StopName) : stop.Name;
        return new StopBoard(stop.Id, name, code, stop.WalkMinutes, lines, departures, realtime.LastUpdated ?? now);
    }

    /// <summary>Prefers the timestamp over the API's rounded minutes, so the countdown stays exact between polls.</summary>
    private static int MinutesUntil(Arrival a, DateTimeOffset now)
    {
        var at = a.EstimatedArrivalTime ?? a.ScheduledArrivalTime;
        return at is { } t ? (int)Math.Floor((t - now).TotalMinutes) : a.ArrivalMinutes ?? -1;
    }

    /// <summary>
    /// STCP usually leaves scheduled_arrival_time empty but reports delay_minutes, so the timetable time
    /// is the estimate minus the delay, rounded to the minute like a printed timetable.
    /// </summary>
    private static DateTimeOffset? Scheduled(Arrival a)
    {
        if (a.ScheduledArrivalTime is { } scheduled) return scheduled;
        if (a.EstimatedArrivalTime is not { } estimated) return null;
        var t = estimated.AddMinutes(-(a.DelayMinutes ?? 0));
        return new DateTimeOffset(t.Year, t.Month, t.Day, t.Hour, t.Minute, 0, t.Offset).AddMinutes(t.Second >= 30 ? 1 : 0);
    }

    /// <summary>"BOAVISTA (BOM SUCESSO)*" → "Boavista (Bom Sucesso)"; "R. DAS MATAS" → "R. das Matas".</summary>
    internal static string Pretty(string raw)
    {
        var words = Text.ToTitleCase(raw.Trim().TrimEnd('*').Trim().ToLower(CultureInfo.GetCultureInfo("pt-PT"))).Split(' ');
        for (var i = 1; i < words.Length; i++)
            if (LowerWords.Contains(words[i].ToLowerInvariant())) words[i] = words[i].ToLowerInvariant();
        return string.Join(' ', words);
    }

    private sealed record RealtimeResponse(
        [property: JsonPropertyName("stop_name")] string StopName,
        Arrival[] Arrivals,
        [property: JsonPropertyName("last_updated")] DateTimeOffset? LastUpdated,
        [property: JsonPropertyName("data_source")] string? DataSource);

    private sealed record Arrival(
        [property: JsonPropertyName("route_short_name")] string RouteShortName,
        [property: JsonPropertyName("route_color")] string? RouteColor,
        [property: JsonPropertyName("route_text_color")] string? RouteTextColor,
        [property: JsonPropertyName("trip_headsign")] string TripHeadsign,
        [property: JsonPropertyName("estimated_arrival_time")] DateTimeOffset? EstimatedArrivalTime,
        [property: JsonPropertyName("scheduled_arrival_time")] DateTimeOffset? ScheduledArrivalTime,
        [property: JsonPropertyName("arrival_minutes")] int? ArrivalMinutes,
        [property: JsonPropertyName("delay_minutes")] double? DelayMinutes,
        string? Status);

    private sealed record StopResponse(Route[] Routes);

    private sealed record Route(string Number);
}
