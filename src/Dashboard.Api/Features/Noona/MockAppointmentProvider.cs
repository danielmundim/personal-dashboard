using Dashboard.Api.Infrastructure;

namespace Dashboard.Api.Features.Noona;

public sealed class MockAppointmentProvider(LocalClock clock) : IAppointmentProvider
{
    public Task<AppointmentList> GetUpcomingAsync(CancellationToken cancellationToken)
    {
        var d0 = clock.Today;
        var d1 = d0.AddDays(1);
        var now = clock.Now;

        var all = new List<Appointment>
        {
            new("Marta S.", "Corte e escova", clock.At(d0, 10), 60, AppointmentStatus.Confirmed, false, null),
            new("Inês R.", "Coloração + tratamento", clock.At(d0, 11, 30), 120, AppointmentStatus.Confirmed, false, "Alérgica a amônia"),
            new("Carla M.", "Manicure", clock.At(d0, 14, 30), 45, AppointmentStatus.Confirmed, true, null),
            new("Sofia P.", "Mechas balayage", clock.At(d0, 15, 30), 150, AppointmentStatus.Unconfirmed, false, null),
            new("Beatriz L.", "Corte", clock.At(d0, 18, 15), 45, AppointmentStatus.Confirmed, false, null),
            new("Ana F.", "Design de sobrancelhas", clock.At(d1, 9, 30), 30, AppointmentStatus.Confirmed, true, null),
            new("Rita C.", "Corte e escova", clock.At(d1, 10, 30), 60, AppointmentStatus.Unconfirmed, false, null),
        };

        var today = all.Where(a => DateOnly.FromDateTime(a.Start.DateTime) == d0).ToList();
        var upcoming = all
            .Where(a => a.Start.AddMinutes(a.DurationMinutes) > now)
            .Select(a => a.Start <= now ? a with { Status = AppointmentStatus.CheckedIn } : a)
            .Take(6)
            .ToList();

        return Task.FromResult(new AppointmentList(upcoming, today.Count, today.Count(a => a.Start > now), now));
    }
}
