namespace Dashboard.Api.Infrastructure;

/// <summary>
/// Wall-clock time in the household's time zone. Providers use this instead of DateTime.Now so
/// the Pi (usually on UTC) and dev machines produce the same local times.
/// </summary>
public sealed class LocalClock(TimeProvider timeProvider, TimeZoneInfo timeZone)
{
    public TimeZoneInfo TimeZone { get; } = timeZone;

    public DateTimeOffset Now => TimeZoneInfo.ConvertTime(timeProvider.GetUtcNow(), TimeZone);

    public DateOnly Today => DateOnly.FromDateTime(Now.DateTime);

    /// <summary>Builds a local timestamp for <paramref name="day"/> at the given time of day.</summary>
    public DateTimeOffset At(DateOnly day, int hour, int minute = 0)
    {
        var local = day.ToDateTime(new TimeOnly(hour, minute));
        return new DateTimeOffset(local, TimeZone.GetUtcOffset(local));
    }

    /// <summary>Attaches the household zone's offset to a local wall-clock time (e.g. from an API that returns local times).</summary>
    public DateTimeOffset FromLocal(DateTime local) =>
        new(DateTime.SpecifyKind(local, DateTimeKind.Unspecified), TimeZone.GetUtcOffset(local));
}
