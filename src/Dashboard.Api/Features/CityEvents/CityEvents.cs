namespace Dashboard.Api.Features.CityEvents;

/// <summary>
/// Something on in the city. <see cref="End"/> is the last day of multi-day events (exhibitions,
/// fairs); <see cref="AllDay"/> means no start time was given.
/// </summary>
public sealed record CityEvent(
    string Title,
    string? Venue,
    DateTimeOffset Start,
    DateTimeOffset? End,
    bool AllDay,
    string? Category,
    string? Price,
    string? Url);

public sealed record CityEventList(string City, string Source, IReadOnlyList<CityEvent> Events, DateTimeOffset UpdatedAt);

/// <summary>Today: <see cref="FileCityEventProvider"/> (our own list). Feeds could be merged in here later.</summary>
public interface ICityEventProvider
{
    Task<CityEventList> GetUpcomingAsync(CancellationToken cancellationToken);
}
