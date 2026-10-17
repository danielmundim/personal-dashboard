using System.Globalization;
using System.Text.Json.Serialization;
using Dashboard.Api.Infrastructure;
using Microsoft.Extensions.Options;

namespace Dashboard.Api.Features.Weather;

/// <summary>
/// Forecast from Open-Meteo (free, no key; ECMWF/ICON/AROME blend, includes hourly rain probability
/// and UV). Requested in the household time zone, so every timestamp comes back as local wall time.
/// </summary>
public sealed class OpenMeteoWeatherProvider(HttpClient http, LocalClock clock, IOptions<DashboardOptions> options) : IWeatherProvider
{
    private const int HourlyCount = 24;
    private const int DailyCount = 7;

    public async Task<WeatherReport> GetReportAsync(CancellationToken cancellationToken)
    {
        var o = options.Value;
        const string current = "temperature_2m,apparent_temperature,relative_humidity_2m,weather_code,wind_speed_10m,wind_gusts_10m,wind_direction_10m,uv_index";
        const string hourlyFields = "temperature_2m,precipitation_probability,precipitation,wind_speed_10m,uv_index,weather_code";
        const string dailyFields = "weather_code,temperature_2m_max,temperature_2m_min,precipitation_probability_max,precipitation_sum,wind_speed_10m_max,uv_index_max,sunrise,sunset,daylight_duration";
        var url = FormattableString.Invariant(
            $"v1/forecast?latitude={o.Home.Latitude}&longitude={o.Home.Longitude}&current={current}&hourly={hourlyFields}&daily={dailyFields}&timezone={Uri.EscapeDataString(o.TimeZone)}&past_days=1&forecast_days=8");

        var r = await http.GetFromJsonAsync<Response>(url, cancellationToken)
            ?? throw new InvalidOperationException("Open-Meteo returned an empty response.");

        var now = clock.Now;
        var currentHour = new DateTime(now.Year, now.Month, now.Day, now.Hour, 0, 0);

        var hourly = r.Hourly.Time
            .Select((t, i) => (Time: Parse(t), i))
            .Where(x => x.Time >= currentHour)
            .Take(HourlyCount)
            .Select(x => new HourlyForecast(
                clock.FromLocal(x.Time),
                Math.Round(r.Hourly.Temperature[x.i] ?? 0, 1),
                r.Hourly.PrecipitationProbability[x.i] ?? 0,
                Math.Round(r.Hourly.Precipitation[x.i] ?? 0, 1),
                (int)Math.Round(r.Hourly.WindSpeed[x.i] ?? 0),
                (int)Math.Round(r.Hourly.UvIndex[x.i] ?? 0),
                Condition(r.Hourly.WeatherCode[x.i] ?? 0)))
            .ToList();

        var todayIndex = Array.IndexOf(r.Daily.Time, clock.Today.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
        if (todayIndex < 0) todayIndex = 0;

        var daily = Enumerable.Range(todayIndex, Math.Min(DailyCount, r.Daily.Time.Length - todayIndex))
            .Select(i => new DailyForecast(
                DateOnly.ParseExact(r.Daily.Time[i], "yyyy-MM-dd", CultureInfo.InvariantCulture),
                Math.Round(r.Daily.TemperatureMin[i] ?? 0),
                Math.Round(r.Daily.TemperatureMax[i] ?? 0),
                r.Daily.PrecipitationProbabilityMax[i] ?? 0,
                Math.Round(r.Daily.PrecipitationSum[i] ?? 0, 1),
                (int)Math.Round(r.Daily.WindSpeedMax[i] ?? 0),
                (int)Math.Round(r.Daily.UvIndexMax[i] ?? 0),
                Condition(r.Daily.WeatherCode[i] ?? 0)))
            .ToList();

        var c = r.Current;
        var currentNow = new WeatherNow(
            Math.Round(c.Temperature, 1),
            Math.Round(c.ApparentTemperature, 1),
            Condition(c.WeatherCode),
            Describe(c.WeatherCode),
            c.RelativeHumidity,
            (int)Math.Round(c.WindSpeed),
            (int)Math.Round(c.WindGusts),
            Compass(c.WindDirection),
            (int)Math.Round(c.UvIndex));

        var daylight = TimeSpan.FromSeconds(r.Daily.DaylightDuration[todayIndex] ?? 0);
        var yesterdayDaylight = todayIndex > 0 ? TimeSpan.FromSeconds(r.Daily.DaylightDuration[todayIndex - 1] ?? 0) : daylight;
        var sun = new SunTimes(
            clock.FromLocal(Parse(r.Daily.Sunrise[todayIndex])),
            clock.FromLocal(Parse(r.Daily.Sunset[todayIndex])),
            daylight,
            daylight - yesterdayDaylight);

        return new WeatherReport(o.Home.Name, currentNow, Hints(hourly, daily, currentNow), hourly, daily, sun, now);
    }

    /// <summary>Plain-language nudges for the next 12 hours: umbrella, jacket, sunscreen, wind.</summary>
    private static List<WeatherHint> Hints(List<HourlyForecast> hourly, List<DailyForecast> daily, WeatherNow now)
    {
        var hints = new List<WeatherHint>();
        var next = hourly.Take(12).ToList();
        if (next.Count == 0) return hints;

        var wet = next.FirstOrDefault(h => h.PrecipitationPct >= 50);
        if (wet is not null)
        {
            hints.Add(new(HintKind.Umbrella, wet == next[0]
                ? "Leve guarda-chuva: chuva provável agora"
                : $"Leve guarda-chuva: chuva provável a partir das {wet.Time:HH}h"));
        }

        var coldest = next.MinBy(h => h.TemperatureC)!;
        if (coldest.TemperatureC < 12)
            hints.Add(new(HintKind.Jacket, $"Leve um casaco quente: chega a {coldest.TemperatureC:0}° às {coldest.Time:HH}h"));
        else if (coldest.TemperatureC < 16)
            hints.Add(new(HintKind.Jacket, $"Leve um casaco leve: esfria para {coldest.TemperatureC:0}° às {coldest.Time:HH}h"));

        var uvToday = daily.FirstOrDefault()?.UvMax ?? now.UvIndex;
        if (next.Any(h => h.UvIndex >= 6) || (uvToday >= 6 && next.Any(h => h.UvIndex > 0)))
            hints.Add(new(HintKind.Sunscreen, $"Use protetor solar: UV chega a {Math.Max(uvToday, next.Max(h => h.UvIndex))}"));

        if (now.WindGustKmh >= 50 || next.Any(h => h.WindKmh >= 35))
            hints.Add(new(HintKind.Wind, $"Vento forte: rajadas de até {Math.Max(now.WindGustKmh, next.Max(h => h.WindKmh))} km/h"));

        return hints;
    }

    private static DateTime Parse(string local) =>
        DateTime.ParseExact(local, "yyyy-MM-dd'T'HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None);

    /// <summary>WMO weather interpretation codes, as used by Open-Meteo.</summary>
    private static WeatherCondition Condition(int code) => code switch
    {
        0 => WeatherCondition.Clear,
        1 or 2 => WeatherCondition.PartlyCloudy,
        3 => WeatherCondition.Cloudy,
        45 or 48 => WeatherCondition.Fog,
        >= 51 and <= 57 => WeatherCondition.Drizzle,
        >= 61 and <= 67 or >= 80 and <= 82 or >= 71 and <= 77 or 85 or 86 => WeatherCondition.Rain,
        >= 95 => WeatherCondition.Thunderstorm,
        _ => WeatherCondition.Cloudy,
    };

    private static string Describe(int code) => code switch
    {
        0 => "Céu limpo",
        1 => "Poucas nuvens",
        2 => "Parcialmente nublado",
        3 => "Nublado",
        45 or 48 => "Nevoeiro",
        51 or 53 or 55 => "Garoa",
        56 or 57 => "Garoa gelada",
        61 => "Chuva fraca",
        63 => "Chuva",
        65 => "Chuva forte",
        66 or 67 => "Chuva gelada",
        71 or 73 or 75 or 77 => "Neve",
        80 => "Aguaceiros fracos",
        81 => "Aguaceiros",
        82 => "Aguaceiros fortes",
        85 or 86 => "Aguaceiros de neve",
        95 => "Trovoada",
        _ => "Trovoada com granizo",
    };

    /// <summary>Wind direction in degrees to a Portuguese compass point (L = leste, O = oeste).</summary>
    private static string Compass(double degrees)
    {
        string[] points = ["N", "NE", "L", "SE", "S", "SO", "O", "NO"];
        return points[(int)Math.Round(((degrees % 360) + 360) % 360 / 45) % 8];
    }

    private sealed record Response(Current Current, Hourly Hourly, Daily Daily);

    private sealed record Current(
        [property: JsonPropertyName("temperature_2m")] double Temperature,
        [property: JsonPropertyName("apparent_temperature")] double ApparentTemperature,
        [property: JsonPropertyName("relative_humidity_2m")] int RelativeHumidity,
        [property: JsonPropertyName("weather_code")] int WeatherCode,
        [property: JsonPropertyName("wind_speed_10m")] double WindSpeed,
        [property: JsonPropertyName("wind_gusts_10m")] double WindGusts,
        [property: JsonPropertyName("wind_direction_10m")] double WindDirection,
        [property: JsonPropertyName("uv_index")] double UvIndex);

    private sealed record Hourly(
        string[] Time,
        [property: JsonPropertyName("temperature_2m")] double?[] Temperature,
        [property: JsonPropertyName("precipitation_probability")] int?[] PrecipitationProbability,
        double?[] Precipitation,
        [property: JsonPropertyName("wind_speed_10m")] double?[] WindSpeed,
        [property: JsonPropertyName("uv_index")] double?[] UvIndex,
        [property: JsonPropertyName("weather_code")] int?[] WeatherCode);

    private sealed record Daily(
        string[] Time,
        [property: JsonPropertyName("weather_code")] int?[] WeatherCode,
        [property: JsonPropertyName("temperature_2m_max")] double?[] TemperatureMax,
        [property: JsonPropertyName("temperature_2m_min")] double?[] TemperatureMin,
        [property: JsonPropertyName("precipitation_probability_max")] int?[] PrecipitationProbabilityMax,
        [property: JsonPropertyName("precipitation_sum")] double?[] PrecipitationSum,
        [property: JsonPropertyName("wind_speed_10m_max")] double?[] WindSpeedMax,
        [property: JsonPropertyName("uv_index_max")] double?[] UvIndexMax,
        string[] Sunrise,
        string[] Sunset,
        [property: JsonPropertyName("daylight_duration")] double?[] DaylightDuration);
}
