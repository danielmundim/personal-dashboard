namespace Dashboard.Api.Features.Trips;

/// <summary>What a checklist line is about, so the list can be grouped: buy before, pack, or do.</summary>
public enum ChecklistKind
{
    Comprar,
    Levar,
    Fazer,
}

public sealed record ChecklistItem(Guid Id, string Text, ChecklistKind Kind, bool Done);

/// <summary>How we get to a stop from the previous one (or from home, for the first stop).</summary>
public enum Transport
{
    Aviao,
    Comboio,
    Onibus,
    Carro,
    Barco,
    Outro,
}

/// <summary>
/// One destination in the trip's itinerary. Dates are optional: we often decide the next jump on the
/// way. <see cref="Transport"/> says how we arrive here from the previous stop. The two flags track
/// what's already booked for this stop: the ticket to get there, and lodging. No "return ticket" flag:
/// the ticket onwards is the next stop's outbound ticket.
/// </summary>
public sealed record TripStop(
    Guid Id,
    string Place,
    DateOnly? Arrive,
    DateOnly? Depart,
    Transport? Transport,
    string? Notes,
    bool OutboundTicket = false,
    bool Lodging = false);

/// <summary>
/// A trip: one main journey made of an ordered list of stops. <see cref="EndDate"/> (the return) is
/// optional, because a trip can stay open while we hop from one place to the next.
/// </summary>
public sealed record Trip(
    Guid Id,
    string Name,
    DateOnly StartDate,
    DateOnly? EndDate,
    string? Notes,
    IReadOnlyList<TripStop> Stops,
    IReadOnlyList<ChecklistItem> Checklist,
    DateTimeOffset CreatedAt);

/// <summary>Upcoming and ongoing trips first (soonest at the top), then past ones (most recent first).</summary>
public sealed record TripList(IReadOnlyList<Trip> Upcoming, IReadOnlyList<Trip> Past, DateTimeOffset UpdatedAt);

public sealed record SaveTrip(string Name, DateOnly StartDate, DateOnly? EndDate, string? Notes);

public sealed record SaveStop(string Place, DateOnly? Arrive, DateOnly? Depart, Transport? Transport, string? Notes);

/// <summary>Ticks or unticks a stop's bookings; null leaves that flag as it is.</summary>
public sealed record UpdateStopBookings(bool? OutboundTicket, bool? Lodging);

/// <summary>Moves a stop to a new position in the itinerary (0 = first).</summary>
public sealed record MoveStop(int Index);

public sealed record AddChecklistItem(string Text, ChecklistKind Kind = ChecklistKind.Comprar);

public sealed record UpdateChecklistItem(bool? Done, string? Text, ChecklistKind? Kind);
