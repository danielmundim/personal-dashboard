using System.Globalization;
using System.Net.Http.Headers;
using System.Text.Json.Serialization;
using Dashboard.Api.Infrastructure;
using Microsoft.Extensions.Options;

namespace Dashboard.Api.Features.Commute;

/// <summary>
/// Live drive time from the Google Routes API (computeRoutes, TRAFFIC_AWARE). Each call is billed, so
/// the endpoint caches it per <see cref="CommuteOptions.CacheTtl"/> rather than per screen refresh.
/// </summary>
public sealed class GoogleRoutesCommuteProvider(
    HttpClient http,
    CommuteHistory history,
    LocalClock clock,
    IOptions<DashboardOptions> options,
    ILogger<GoogleRoutesCommuteProvider> logger) : ICommuteProvider
{
    // Only the fields we read; Google bills by the most expensive field requested.
    private const string FieldMask = "routes.duration,routes.staticDuration,routes.distanceMeters,routes.description";

    public async Task<DriveEstimate> GetEstimateAsync(CancellationToken cancellationToken)
    {
        var o = options.Value;
        var commute = o.Commute;

        using var request = new HttpRequestMessage(HttpMethod.Post, "directions/v2:computeRoutes")
        {
            Content = JsonContent.Create(new
            {
                origin = Waypoint(o.Home),
                destination = Waypoint(commute.Destination),
                travelMode = "DRIVE",
                routingPreference = "TRAFFIC_AWARE",
                languageCode = "pt-BR",
                units = "METRIC",
            }),
        };
        request.Headers.Add("X-Goog-Api-Key", commute.GoogleApiKey);
        request.Headers.Add("X-Goog-FieldMask", FieldMask);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        using var response = await http.SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            // Google explains key/billing problems in the body; log it (never the key) and fail the tile.
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            logger.LogWarning("Google Routes returned {Status}: {Body}", (int)response.StatusCode, body.Length > 500 ? body[..500] : body);
            response.EnsureSuccessStatusCode();
        }

        var result = await response.Content.ReadFromJsonAsync<RoutesResponse>(cancellationToken);
        var route = result?.Routes?.FirstOrDefault()
            ?? throw new InvalidOperationException("Google Routes found no route between home and the destination.");

        var now = clock.Now;
        var current = Minutes(route.Duration);
        var freeFlow = Minutes(route.StaticDuration ?? route.Duration);

        // Usual = our own median for this hour before today's reading is added, so a jam doesn't drag it up.
        var usual = await history.UsualAtAsync(now, cancellationToken);
        await history.RecordAsync(now, current, cancellationToken);

        var ratio = freeFlow > 0 ? (double)current / freeFlow : 1;
        var traffic = ratio switch { < 1.15 => TrafficLevel.Light, < 1.4 => TrafficLevel.Moderate, _ => TrafficLevel.Heavy };

        return new DriveEstimate(
            commute.Label,
            commute.Destination.Name,
            current,
            usual ?? freeFlow,
            usual is null ? UsualSource.FreeFlow : UsualSource.History,
            Math.Round(route.DistanceMeters / 1000.0, 1),
            string.IsNullOrWhiteSpace(route.Description) ? "Rota mais rápida" : route.Description,
            traffic,
            now.AddMinutes(current),
            await history.TypicalDayAsync(commute.ActiveFromHour, commute.ActiveToHour, cancellationToken),
            Incident: null,
            now);
    }

    private static object Waypoint(PlaceOptions place) =>
        new { location = new { latLng = new { latitude = place.Latitude, longitude = place.Longitude } } };

    /// <summary>Durations come as protobuf strings like "1234s".</summary>
    private static int Minutes(string? duration) =>
        duration is { Length: > 1 } d && double.TryParse(d.TrimEnd('s'), NumberStyles.Float, CultureInfo.InvariantCulture, out var seconds)
            ? (int)Math.Round(seconds / 60)
            : 0;

    private sealed record RoutesResponse(Route[]? Routes);

    private sealed record Route(
        string? Duration,
        [property: JsonPropertyName("staticDuration")] string? StaticDuration,
        int DistanceMeters,
        string? Description);
}
