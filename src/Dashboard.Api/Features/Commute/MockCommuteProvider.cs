using Dashboard.Api.Infrastructure;
using Microsoft.Extensions.Options;

namespace Dashboard.Api.Features.Commute;

public sealed class MockCommuteProvider(LocalClock clock, IOptions<DashboardOptions> options) : ICommuteProvider
{
    public Task<DriveEstimate> GetEstimateAsync(CancellationToken cancellationToken)
    {
        var now = clock.Now;
        var today = clock.Today;

        // Typical shape: morning and evening peaks over the bridge, quiet otherwise.
        static int Typical(double hour) =>
            (int)Math.Round(16 + 11 * Math.Exp(-Math.Pow(hour - 8.5, 2) / 1.5) + 9 * Math.Exp(-Math.Pow(hour - 18.25, 2) / 1.8));

        var curve = Enumerable.Range(6, 17).Select(h => new TypicalTime(clock.At(today, h), Typical(h))).ToList();
        var hourNow = now.Hour + now.Minute / 60.0;
        var usual = Typical(hourNow);
        var current = usual + 3; // a bit worse than usual right now
        var traffic = (current - 16) switch { >= 12 => TrafficLevel.Heavy, >= 5 => TrafficLevel.Moderate, _ => TrafficLevel.Light };

        var commute = options.Value.Commute;
        var estimate = new DriveEstimate(
            commute.Label,
            commute.Destination.Name,
            current,
            usual,
            UsualSource.History,
            11.4,
            "VCI · Ponte da Arrábida",
            traffic,
            now.AddMinutes(current),
            curve,
            "Trânsito lento na VCI perto das Antas",
            now);

        return Task.FromResult(estimate);
    }
}
