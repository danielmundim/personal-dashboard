namespace Dashboard.Api.Features.CityEvents;

public sealed record CityEvent(
    string Title,
    string Venue,
    DateTimeOffset Start,
    DateTimeOffset? End,
    string Category,
    string? Price,
    string? Url);

public sealed record CityEventList(string City, string Source, IReadOnlyList<CityEvent> Events, DateTimeOffset UpdatedAt);

/// <summary>Source to be decided (e.g. Agenda Porto, Viral Agenda, Ticketline). Several feeds can be merged here.</summary>
public interface ICityEventProvider
{
    Task<CityEventList> GetUpcomingAsync(CancellationToken cancellationToken);
}
