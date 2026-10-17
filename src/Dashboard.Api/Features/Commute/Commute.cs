namespace Dashboard.Api.Features.Commute;

public enum TrafficLevel
{
    Light,
    Moderate,
    Heavy,
}

/// <summary>Where <see cref="DriveEstimate.UsualMinutes"/> comes from.</summary>
public enum UsualSource
{
    /// <summary>Median of our own past readings for this hour and kind of day.</summary>
    History,

    /// <summary>Not enough history yet: the provider's no-traffic duration.</summary>
    FreeFlow,
}

public sealed record TypicalTime(DateTimeOffset Hour, int Minutes);

public sealed record DriveEstimate(
    string Label,
    string Destination,
    int CurrentMinutes,
    int UsualMinutes,
    UsualSource UsualSource,
    double DistanceKm,
    string Via,
    TrafficLevel Traffic,
    DateTimeOffset ArriveIfLeavingNow,
    IReadOnlyList<TypicalTime> Today,
    string? Incident,
    DateTimeOffset UpdatedAt);

/// <summary>
/// Real implementation: Google Routes API (traffic-aware). Waze's live-map routing endpoints reject
/// server-side requests, so they aren't an option.
/// </summary>
public interface ICommuteProvider
{
    Task<DriveEstimate> GetEstimateAsync(CancellationToken cancellationToken);
}
