using System.Globalization;
using System.Text;
using System.Text.Json.Serialization;

namespace Dashboard.Api.Features.Photos;

public sealed record GeoPoint(double Latitude, double Longitude);

/// <summary>A located place. Country and City are the official pt spellings when the match is confident, else null.</summary>
public sealed record GeoMatch(GeoPoint Point, string? Country, string? City);

/// <summary>
/// Turns the country and city typed for a photo into coordinates for the globe, using Open-Meteo's
/// geocoding (free, no key). Called once when the place is saved; the result is stored with the
/// photo, so the globe never waits on the internet. Returns null when the place isn't found or the
/// service is unreachable: the photo keeps its country and city, just without a pin.
/// </summary>
public sealed class PlaceGeocoder(HttpClient http, ILogger<PlaceGeocoder> logger)
{
    public async Task<GeoMatch?> LocateAsync(string? country, string? city, CancellationToken ct)
    {
        var name = city ?? country;
        if (name is null) return null;
        try
        {
            var url = $"https://geocoding-api.open-meteo.com/v1/search?name={Uri.EscapeDataString(name)}&count=10&language=pt&format=json";
            var results = (await http.GetFromJsonAsync<Response>(url, ct))?.Results ?? [];

            // Only a country: its own entry (feature code PCLI and friends), not a town that shares the name.
            // With a city: the first match in the typed country, so "Porto, Portugal" isn't Porto Velho.
            var match = city is null
                ? results.FirstOrDefault(r => r.FeatureCode?.StartsWith("PCL", StringComparison.Ordinal) == true)
                : results.FirstOrDefault(r => country is null || Same(r.Country, country) || Same(r.CountryCode, country));
            if (match is not null)
            {
                // Confident: keep the proper spelling ("sao paulo" → "São Paulo"), so pins and filters don't split.
                // A city's name is kept only when it's the same word, not e.g. "Lisboa" for a typed "Lisbon".
                var cityName = city is not null && match.Name is not null && Same(match.Name, city) ? match.Name : null;
                return new GeoMatch(new GeoPoint(match.Latitude, match.Longitude), match.Country, cityName);
            }
            var first = results.FirstOrDefault();
            return first is null ? null : new GeoMatch(new GeoPoint(first.Latitude, first.Longitude), null, null);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or System.Text.Json.JsonException)
        {
            logger.LogWarning(ex, "Could not geocode photo place {City}, {Country}", city, country);
            return null;
        }
    }

    /// <summary>Case- and accent-insensitive, so "espanha" matches "Espanha" and "Sao Paulo" matches "São Paulo".</summary>
    public static bool Same(string? a, string? b) =>
        a is not null && b is not null && string.Equals(Fold(a), Fold(b), StringComparison.OrdinalIgnoreCase);

    private static string Fold(string s) =>
        new(s.Trim().Normalize(NormalizationForm.FormD).Where(c => CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark).ToArray());

    private sealed record Response(IReadOnlyList<Result>? Results);

    private sealed record Result(
        string? Name,
        double Latitude,
        double Longitude,
        string? Country,
        [property: JsonPropertyName("country_code")] string? CountryCode,
        [property: JsonPropertyName("feature_code")] string? FeatureCode);
}
