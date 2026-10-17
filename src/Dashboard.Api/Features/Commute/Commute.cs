namespace Dashboard.Api.Features.Commute;

public enum TrafficLevel
{
    Light,
    Moderate,
    Heavy,
}

public sealed record TypicalTime(DateTimeOffset Hour, int Minutes);

public sealed record DriveEstimate(
    string Label,
    string Destination,
    int CurrentMinutes,
    int UsualMinutes,
    double DistanceKm,
    string Via,
    TrafficLevel Traffic,
    DateTimeOffset ArriveIfLeavingNow,
    IReadOnlyList<TypicalTime> Today,
    string? Incident,
    DateTimeOffset UpdatedAt);

/// <summary>
/// Future real implementation: Waze has no official routing API for personal use. Options are the
/// unofficial Waze live-map routing endpoint, or Google Routes API (traffic-aware) as a stable fallback.
/// </summary>
public interface ICommuteProvider
{
    Task<DriveEstimate> GetEstimateAsync(CancellationToken cancellationToken);
}
