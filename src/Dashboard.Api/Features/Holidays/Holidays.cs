namespace Dashboard.Api.Features.Holidays;

public enum HolidayScope
{
    National,
    Municipal,

    /// <summary>Commonly given (e.g. Carnival) but not a mandatory day off.</summary>
    Optional,
}

/// <summary>
/// <see cref="LongWeekend"/>: falls on a Monday or Friday. <see cref="BridgeDay"/>: falls on a Tuesday or
/// Thursday, so one day off ("ponte") gives a four-day weekend.
/// </summary>
public sealed record Holiday(
    string Name,
    DateOnly Date,
    HolidayScope Scope,
    int DaysUntil,
    bool LongWeekend,
    bool BridgeDay);

public sealed record HolidayList(string Country, IReadOnlyList<Holiday> Holidays, DateTimeOffset UpdatedAt);

public interface IHolidayProvider
{
    Task<HolidayList> GetUpcomingAsync(int count, CancellationToken cancellationToken);
}
