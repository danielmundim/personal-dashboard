using Dashboard.Api.Infrastructure;
using Microsoft.Extensions.Options;

namespace Dashboard.Api.Features.Weather;

public sealed class MockWeatherProvider(LocalClock clock, IOptions<DashboardOptions> options) : IWeatherProvider
{
    public Task<WeatherReport> GetReportAsync(CancellationToken cancellationToken)
    {
        var now = clock.Now;
        var today = clock.Today;
        var startHour = new DateTimeOffset(now.Year, now.Month, now.Day, now.Hour, 0, 0, now.Offset);

        // A plausible autumn day in Porto: mild, clouds rolling in, rain from late afternoon.
        var hourly = Enumerable.Range(0, 24)
            .Select(i =>
            {
                var time = startHour.AddHours(i);
                var h = time.Hour;
                var temp = 13 + 5 * Math.Sin((h - 9) / 24.0 * 2 * Math.PI);
                var rainPct = h switch { >= 17 and <= 21 => 60 + (h - 17) * 6, >= 14 and < 17 => 15 + (h - 14) * 12, >= 22 => 45, < 6 => 30, _ => 5 };
                var mm = rainPct >= 50 ? Math.Round((rainPct - 40) / 15.0, 1) : 0;
                var uv = h is >= 9 and <= 17 ? Math.Max(0, 4 - Math.Abs(13 - h)) : 0;
                var wind = 10 + (rainPct / 6);
                var condition = rainPct switch { >= 70 => WeatherCondition.Rain, >= 45 => WeatherCondition.Drizzle, >= 15 => WeatherCondition.Cloudy, _ => h is >= 8 and <= 19 ? WeatherCondition.PartlyCloudy : WeatherCondition.Clear };
                return new HourlyForecast(time, Math.Round(temp, 1), Math.Min(rainPct, 95), mm, wind, uv, condition);
            })
            .ToList();

        var daily = new List<DailyForecast>
        {
            new(today, 12, 18, 80, 6.4, 28, 3, WeatherCondition.Rain),
            new(today.AddDays(1), 11, 16, 90, 14.2, 45, 2, WeatherCondition.Thunderstorm),
            new(today.AddDays(2), 10, 17, 40, 1.8, 22, 3, WeatherCondition.Drizzle),
            new(today.AddDays(3), 9, 19, 10, 0, 15, 4, WeatherCondition.PartlyCloudy),
            new(today.AddDays(4), 10, 21, 0, 0, 12, 5, WeatherCondition.Clear),
            new(today.AddDays(5), 11, 20, 5, 0, 18, 4, WeatherCondition.PartlyCloudy),
            new(today.AddDays(6), 12, 18, 55, 3.1, 30, 2, WeatherCondition.Rain),
        };

        var hints = new List<WeatherHint>();
        var wetHour = hourly.Take(12).FirstOrDefault(h => h.PrecipitationPct >= 50);
        if (wetHour is not null)
            hints.Add(new(HintKind.Umbrella, $"Leve guarda-chuva: chuva provável a partir das {wetHour.Time:HH}h"));
        if (hourly.Take(12).Min(h => h.TemperatureC) < 15)
            hints.Add(new(HintKind.Jacket, "Leve um casaco: esfria para ~13° à noite"));

        var sunrise = clock.At(today, 7, 54);
        var sunset = clock.At(today, 18, 58);

        var report = new WeatherReport(
            options.Value.LocationName,
            new WeatherNow(hourly[0].TemperatureC, hourly[0].TemperatureC - 0.6, hourly[0].Condition, Describe(hourly[0].Condition), 72, hourly[0].WindKmh, hourly[0].WindKmh + 12, "NW", hourly[0].UvIndex),
            hints,
            hourly,
            daily,
            new SunTimes(sunrise, sunset, sunset - sunrise, TimeSpan.FromMinutes(-2.6)),
            now);

        return Task.FromResult(report);
    }

    private static string Describe(WeatherCondition c) => c switch
    {
        WeatherCondition.Clear => "Céu limpo",
        WeatherCondition.PartlyCloudy => "Parcialmente nublado",
        WeatherCondition.Cloudy => "Nublado",
        WeatherCondition.Fog => "Nevoeiro",
        WeatherCondition.Drizzle => "Garoa",
        WeatherCondition.Rain => "Chuva",
        _ => "Trovoadas",
    };
}
