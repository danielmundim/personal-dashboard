using Dashboard.Api.Infrastructure;
using Microsoft.Extensions.Options;

namespace Dashboard.Api.Features.Transit;

public sealed class MockTransitProvider(LocalClock clock, IOptions<DashboardOptions> options) : ITransitProvider
{
    // Per stop: (line, destination, headway in minutes, offset).
    private static readonly (string Line, string Destination, int Every, int Offset)[][] Schedules =
    [
        [("901", "Trindade", 12, 2), ("906", "Madalena", 20, 7), ("907", "Hospital Santos Silva", 15, 11)],
        [("600", "Aliados", 10, 4), ("905", "Boavista", 18, 1), ("ZR", "Vila d'Este", 30, 13)],
    ];

    public Task<StopBoard> GetStopBoardAsync(string stopId, CancellationToken cancellationToken)
    {
        var stops = options.Value.BusStops;
        var index = Math.Max(0, Array.FindIndex(stops, s => s.Id == stopId));
        var stop = stops.ElementAtOrDefault(index) ?? new BusStopOptions { Id = stopId, Name = stopId };
        var minuteOfDay = clock.Now.Hour * 60 + clock.Now.Minute;

        // Expand each line's headway into its next three departures so the list ticks down realistically.
        var departures = Schedules[index % Schedules.Length]
            .SelectMany(s => Enumerable.Range(0, 3).Select(k =>
            {
                var next = (s.Every - (minuteOfDay + s.Offset) % s.Every) % s.Every + k * s.Every;
                var delay = s.Line == "906" && k == 0 ? 4 : 0;
                var arrives = clock.Now.AddMinutes(next);
                return new Departure(s.Line, s.Destination, next, RealTime: next <= 20, Delayed: delay > 0,
                    ArrivesAt: arrives, ScheduledAt: arrives.AddMinutes(-delay), DelayMinutes: delay);
            }))
            .Where(d => d.MinutesAway <= 45)
            .OrderBy(d => d.MinutesAway)
            .Take(6)
            .ToList();

        var lines = Schedules[index % Schedules.Length].Select(s => s.Line).Distinct().ToList();
        return Task.FromResult(new StopBoard(stop.Id, stop.Name, stop.Code, stop.WalkMinutes, lines, departures, clock.Now));
    }
}
