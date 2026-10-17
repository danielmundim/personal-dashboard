namespace Dashboard.Api.Features.Weather;

/// <summary>Weather condition codes shared with the frontend icon set.</summary>
public enum WeatherCondition
{
    Clear,
    PartlyCloudy,
    Cloudy,
    Fog,
    Drizzle,
    Rain,
    Thunderstorm,
}

public sealed record WeatherNow(
    double TemperatureC,
    double FeelsLikeC,
    WeatherCondition Condition,
    string Description,
    int HumidityPct,
    int WindKmh,
    int WindGustKmh,
    string WindDirection,
    int UvIndex);

public sealed record HourlyForecast(
    DateTimeOffset Time,
    double TemperatureC,
    int PrecipitationPct,
    double PrecipitationMm,
    int WindKmh,
    int UvIndex,
    WeatherCondition Condition);

public sealed record DailyForecast(
    DateOnly Date,
    double MinC,
    double MaxC,
    int PrecipitationPct,
    double PrecipitationMm,
    int WindMaxKmh,
    int UvMax,
    WeatherCondition Condition);

public enum HintKind
{
    Umbrella,
    Jacket,
    Sunscreen,
    Wind,
}

public sealed record WeatherHint(HintKind Kind, string Text);

public sealed record SunTimes(
    DateTimeOffset Sunrise,
    DateTimeOffset Sunset,
    TimeSpan DaylightLength,
    TimeSpan DaylightChangeVsYesterday);

/// <summary>Hourly covers the next 24 hours from the current hour; Daily covers 7 days starting today.</summary>
public sealed record WeatherReport(
    string Location,
    WeatherNow Now,
    IReadOnlyList<WeatherHint> Hints,
    IReadOnlyList<HourlyForecast> Hourly,
    IReadOnlyList<DailyForecast> Daily,
    SunTimes Sun,
    DateTimeOffset UpdatedAt);

/// <summary>Future real implementation: IPMA (api.ipma.pt) or Open-Meteo, plus a sun calculator.</summary>
public interface IWeatherProvider
{
    Task<WeatherReport> GetReportAsync(CancellationToken cancellationToken);
}
