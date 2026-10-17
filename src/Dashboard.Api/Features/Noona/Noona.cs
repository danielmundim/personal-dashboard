namespace Dashboard.Api.Features.Noona;

public enum AppointmentStatus
{
    Confirmed,
    Unconfirmed,
    CheckedIn,
}

public sealed record Appointment(
    string Customer,
    string Service,
    DateTimeOffset Start,
    int DurationMinutes,
    AppointmentStatus Status,
    bool FirstVisit,
    string? Note);

public sealed record AppointmentList(
    IReadOnlyList<Appointment> Appointments,
    int TodayCount,
    int TodayRemaining,
    DateTimeOffset UpdatedAt);

/// <summary>Future real implementation: Noona HQ API (api.noona.is), bookings for the configured company.</summary>
public interface IAppointmentProvider
{
    Task<AppointmentList> GetUpcomingAsync(CancellationToken cancellationToken);
}
