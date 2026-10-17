namespace Dashboard.Api.Features.Transit;

/// <summary>
/// <see cref="ArrivesAt"/> is the best estimate (live when available) and <see cref="ScheduledAt"/> the
/// timetable time, so screens can count down themselves and show the delay. <see cref="MinutesAway"/>
/// is a snapshot at fetch time, kept for simple clients. Colors are the operator's line colors.
/// </summary>
public sealed record Departure(
    string Line,
    string Destination,
    int MinutesAway,
    bool RealTime,
    bool Delayed,
    DateTimeOffset? ArrivesAt = null,
    DateTimeOffset? ScheduledAt = null,
    int DelayMinutes = 0,
    string? Color = null,
    string? TextColor = null);

public sealed record StopBoard(
    string StopId,
    string StopName,
    string StopCode,
    int WalkMinutes,
    IReadOnlyList<string> Lines,
    IReadOnlyList<Departure> Departures,
    DateTimeOffset UpdatedAt);

public interface ITransitProvider
{
    Task<StopBoard> GetStopBoardAsync(string stopId, CancellationToken cancellationToken);
}
