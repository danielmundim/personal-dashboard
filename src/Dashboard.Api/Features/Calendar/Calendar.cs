namespace Dashboard.Api.Features.Calendar;

/// <summary>
/// <see cref="Source"/> is the feed's name ("Pessoal", "Trabalho"); <see cref="Busy"/> means the feed is
/// configured busy-only, so the title is replaced and the location dropped.
/// </summary>
public sealed record CalendarEvent(
    string Title,
    DateTimeOffset Start,
    DateTimeOffset End,
    bool AllDay,
    string? Location,
    string? Source = null,
    bool Busy = false);

/// <summary>
/// <see cref="Configured"/> is false when the person has no calendar links yet.
/// <see cref="FailedSources"/> names feeds that couldn't be read this time (the others still show).
/// </summary>
public sealed record CalendarAgenda(
    string PersonId,
    IReadOnlyList<CalendarEvent> Events,
    DateTimeOffset UpdatedAt,
    bool Configured = true,
    IReadOnlyList<string>? FailedSources = null);

/// <summary>One iCal link merged into a person's tile. Links are secret: keep them in appsettings.Local.json.</summary>
public sealed class CalendarFeedOptions
{
    public string Name { get; init; } = "";

    public string Url { get; init; } = "";

    /// <summary>Show "Ocupado" instead of titles (e.g. a work calendar on a shared screen).</summary>
    public bool BusyOnly { get; init; }
}

public interface ICalendarProvider
{
    /// <summary>Events for <paramref name="personId"/> from the start of today through <paramref name="days"/> days ahead.</summary>
    Task<CalendarAgenda> GetAgendaAsync(string personId, int days, CancellationToken cancellationToken);
}
