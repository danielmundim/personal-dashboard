namespace Dashboard.Api.Features.Calendar;

public sealed record CalendarEvent(
    string Title,
    DateTimeOffset Start,
    DateTimeOffset End,
    bool AllDay,
    string? Location);

public sealed record CalendarAgenda(string PersonId, IReadOnlyList<CalendarEvent> Events, DateTimeOffset UpdatedAt);

/// <summary>Future real implementation: Google Calendar API (or ICS feed), one calendar per person.</summary>
public interface ICalendarProvider
{
    /// <summary>Events for <paramref name="personId"/> from the start of today through <paramref name="days"/> days ahead.</summary>
    Task<CalendarAgenda> GetAgendaAsync(string personId, int days, CancellationToken cancellationToken);
}
