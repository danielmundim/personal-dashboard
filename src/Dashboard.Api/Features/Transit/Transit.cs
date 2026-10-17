namespace Dashboard.Api.Features.Transit;

public sealed record Departure(
    string Line,
    string Destination,
    int MinutesAway,
    bool RealTime,
    bool Delayed);

public sealed record StopBoard(
    string StopId,
    string StopName,
    int WalkMinutes,
    IReadOnlyList<Departure> Departures,
    DateTimeOffset UpdatedAt);

/// <summary>Future real implementation: STCP real-time departures (or the Porto GTFS-RT feed).</summary>
public interface ITransitProvider
{
    Task<StopBoard> GetStopBoardAsync(string stopId, CancellationToken cancellationToken);
}
