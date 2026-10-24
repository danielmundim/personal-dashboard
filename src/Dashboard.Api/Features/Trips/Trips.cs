namespace Dashboard.Api.Features.Trips;

/// <summary>What a checklist line is about, so the list can be grouped: buy before, pack, or do.</summary>
public enum ChecklistKind
{
    Comprar,
    Levar,
    Fazer,
}

public sealed record ChecklistItem(Guid Id, string Text, ChecklistKind Kind, bool Done);

/// <summary>A trip we're planning. Dates are local calendar days; notes hold free-form planning.</summary>
public sealed record Trip(
    Guid Id,
    string Name,
    string? Destination,
    DateOnly StartDate,
    DateOnly? EndDate,
    string? Notes,
    IReadOnlyList<ChecklistItem> Checklist,
    DateTimeOffset CreatedAt);

/// <summary>Upcoming trips first (soonest at the top), then past ones (most recent first).</summary>
public sealed record TripList(IReadOnlyList<Trip> Upcoming, IReadOnlyList<Trip> Past, DateTimeOffset UpdatedAt);

public sealed record SaveTrip(string Name, string? Destination, DateOnly StartDate, DateOnly? EndDate, string? Notes);

public sealed record AddChecklistItem(string Text, ChecklistKind Kind = ChecklistKind.Comprar);

public sealed record UpdateChecklistItem(bool? Done, string? Text, ChecklistKind? Kind);
