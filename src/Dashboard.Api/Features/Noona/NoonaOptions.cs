namespace Dashboard.Api.Features.Noona;

/// <summary>
/// The Noona HQ OAuth app (created in the Noona Developer Portal). ClientId/ClientSecret belong in
/// the git-ignored appsettings.Local.json, never in appsettings.json.
/// </summary>
public sealed class NoonaOptions
{
    public const string SectionName = "Dashboard:Noona";

    public string ClientId { get; init; } = "";

    public string ClientSecret { get; init; } = "";

    /// <summary>
    /// Must match the redirect URL registered on the app exactly. It goes through the dashboard's own
    /// origin (dev proxy or nginx), so the browser lands back on the dashboard after consent.
    /// </summary>
    public string RedirectUri { get; init; } = "http://localhost:4300/api/noona/callback";

    /// <summary>
    /// Where the browser goes after the callback. "/" works when the callback comes through the
    /// dashboard's origin; set the full dashboard URL (e.g. "http://localhost:4300/") if RedirectUri
    /// points straight at the API (e.g. https://localhost:7112/...).
    /// </summary>
    public string ReturnUrl { get; init; } = "/";

    /// <summary>Read-only scopes: who is logged in (to find the company), bookings, and service names.</summary>
    public string Scopes { get; init; } = "user:read companies:read events:read event_types:read";

    /// <summary>Optional: overrides the company picked at connect time (the user's active company).</summary>
    public string? CompanyId { get; init; }

    /// <summary>Optional: only show bookings for this employee (useful if the salon has several staff).</summary>
    public string? EmployeeId { get; init; }

    /// <summary>When false (default), clients show as first name + initial ("Marta S.") on shared screens.</summary>
    public bool ShowFullNames { get; init; }

    public bool IsConfigured => !string.IsNullOrWhiteSpace(ClientId) && !string.IsNullOrWhiteSpace(ClientSecret);
}
