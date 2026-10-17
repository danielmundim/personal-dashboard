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

/// <summary>Real implementation: <see cref="NoonaAppointmentProvider"/> (Noona HQ API, OAuth 2.0).</summary>
public interface IAppointmentProvider
{
    Task<AppointmentList> GetUpcomingAsync(CancellationToken cancellationToken);
}
