using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using Dashboard.Api.Infrastructure;
using Microsoft.Extensions.Options;

namespace Dashboard.Api.Features.Noona;

/// <summary>
/// The next bookings from Noona HQ (GET /v1/hq/companies/{id}/events), looking a week ahead so the
/// tile isn't empty on quiet days. Cancelled and no-show bookings are left out; names are shortened
/// unless Noona:ShowFullNames is on.
/// </summary>
public sealed class NoonaAppointmentProvider(HttpClient http, NoonaAuth auth, LocalClock clock, IOptions<NoonaOptions> options)
    : IAppointmentProvider
{
    private const int MaxShown = 6;
    private const int DaysAhead = 8;
    private static readonly string[] HiddenStatuses = ["cancel", "noshow", "no_show", "deleted", "declined"];

    public async Task<AppointmentList> GetUpcomingAsync(CancellationToken cancellationToken)
    {
        var tokens = await auth.GetValidTokensAsync(cancellationToken);
        var json = await FetchEventsAsync(tokens, cancellationToken);
        if (json is null)
        {
            // Access token rejected despite not being expired: refresh once and retry.
            tokens = await auth.GetValidTokensAsync(cancellationToken, forceRefresh: true);
            json = await FetchEventsAsync(tokens, cancellationToken)
                ?? throw new NoonaNotConnectedException("Noona rejected the refreshed token; reconnect needed.");
        }

        using var doc = JsonDocument.Parse(json);
        var now = clock.Now;
        var today = clock.Today;
        var o = options.Value;

        var all = doc.RootElement.EnumerateArray()
            .Where(e => !IsHidden(e))
            .Where(e => string.IsNullOrWhiteSpace(o.EmployeeId) || Str(e, "employee") == o.EmployeeId)
            .Select(e => ToAppointment(e, now, o.ShowFullNames))
            .Where(a => a is not null)
            .Select(a => a!)
            .OrderBy(a => a.Start)
            .ToList();

        var todays = all.Where(a => DateOnly.FromDateTime(a.Start.ToOffset(now.Offset).DateTime) == today).ToList();
        var upcoming = all.Where(a => a.Start.AddMinutes(a.DurationMinutes) > now).Take(MaxShown).ToList();

        return new AppointmentList(upcoming, todays.Count, todays.Count(a => a.Start > now), now);
    }

    /// <summary>The raw JSON array, or null when the access token was rejected (401).</summary>
    private async Task<string?> FetchEventsAsync(NoonaTokens tokens, CancellationToken ct)
    {
        // Noona's parser wants plain RFC 3339 UTC ("2026-10-17T00:00:00Z"); .NET's round-trip format
        // (7 decimals, "+00:00" escaped by System.Text.Json) makes it reject the filter.
        static string Utc(DateTimeOffset t) => t.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture);
        var from = clock.At(clock.Today, 0);
        var to = clock.At(clock.Today.AddDays(DaysAhead), 0);
        var filter = $$"""{"from":"{{Utc(from)}}","to":"{{Utc(to)}}"}""";
        var url = $"v1/hq/companies/{Uri.EscapeDataString(tokens.CompanyId)}/events" +
                  $"?filter={Uri.EscapeDataString(filter)}" +
                  $"&pagination={Uri.EscapeDataString("""{"limit":100}""")}";

        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", tokens.AccessToken);
        using var response = await http.SendAsync(request, ct);
        if (response.StatusCode == HttpStatusCode.Unauthorized) return null;
        await auth.EnsureOkAsync(response, "listing events", ct);
        return await response.Content.ReadAsStringAsync(ct);
    }

    private static Appointment? ToAppointment(JsonElement e, DateTimeOffset now, bool fullNames)
    {
        if (!DateTimeOffset.TryParse(Str(e, "starts_at"), out var start)) return null;
        var duration = e.TryGetProperty("duration", out var d) && d.TryGetInt32(out var mins)
            ? mins
            : DateTimeOffset.TryParse(Str(e, "ends_at"), out var end) ? (int)(end - start).TotalMinutes : 30;

        var checkedIn = e.TryGetProperty("check_in_at", out var ci) && ci.ValueKind is JsonValueKind.Number or JsonValueKind.String;
        var unconfirmed = e.TryGetProperty("unconfirmed", out var u) && u.ValueKind == JsonValueKind.True;
        var status = checkedIn || (start <= now && now < start.AddMinutes(duration))
            ? AppointmentStatus.CheckedIn
            : unconfirmed ? AppointmentStatus.Unconfirmed : AppointmentStatus.Confirmed;

        var name = Str(e, "customer_name")?.Trim() is { Length: > 0 } n ? n : "Cliente";
        return new Appointment(
            fullNames ? string.Join(' ', name.Split(' ', StringSplitOptions.RemoveEmptyEntries).Select(Title)) : ShortName(name),
            Services(e),
            start.ToOffset(now.Offset),
            duration,
            status,
            FirstVisit: false, // Noona's event list doesn't say; would need a customer lookup per booking
            Note: NullIfBlank(Str(e, "comment")));
    }

    /// <summary>Service titles from event_types (Noona includes them inline); generic label if missing.</summary>
    private static string Services(JsonElement e)
    {
        if (!e.TryGetProperty("event_types", out var types) || types.ValueKind != JsonValueKind.Array) return "Atendimento";
        var titles = types.EnumerateArray()
            .Select(t => t.ValueKind == JsonValueKind.Object ? Str(t, "title")?.Trim() : null)
            .Where(t => !string.IsNullOrWhiteSpace(t))
            .ToList();
        return titles.Count > 0 ? string.Join(" + ", titles) : "Atendimento";
    }

    private static bool IsHidden(JsonElement e)
    {
        var status = Str(e, "status")?.ToLowerInvariant() ?? "";
        return HiddenStatuses.Any(status.Contains) || (e.TryGetProperty("deleted_at", out var del) && del.ValueKind == JsonValueKind.String);
    }

    /// <summary>"CARLA SILVA ALVES" → "Carla A." so full client names don't sit on every screen in the house.</summary>
    internal static string ShortName(string name)
    {
        var parts = name.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0) return name;
        var first = Title(parts[0]);
        return parts.Length < 2 ? first : $"{first} {char.ToUpperInvariant(parts[^1][0])}.";
    }

    private static string Title(string word) =>
        word.Length == 0 ? word : char.ToUpperInvariant(word[0]) + word[1..].ToLowerInvariant();

    private static string? Str(JsonElement e, string property) =>
        e.TryGetProperty(property, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    private static string? NullIfBlank(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();
}
