namespace Dashboard.Api.Infrastructure;

public sealed class DashboardOptions
{
    public const string SectionName = "Dashboard";

    /// <summary>IANA time zone of the household, e.g. "Europe/Lisbon".</summary>
    public string TimeZone { get; init; } = "Europe/Lisbon";

    /// <summary>City used for the city events tile.</summary>
    public string LocationName { get; init; } = "Porto";

    /// <summary>
    /// Where the house is: weather is forecast for this point and the commute starts here. Exact
    /// coordinates belong in the git-ignored appsettings.Local.json, not in appsettings.json.
    /// </summary>
    public PlaceOptions Home { get; init; } = new() { Name = "Vila Nova de Gaia", Latitude = 41.1239, Longitude = -8.6118 };

    /// <summary>Which provider backs each widget. Anything not set to a real source stays on its mock.</summary>
    public IntegrationOptions Integrations { get; init; } = new();

    /// <summary>Folder for data the dashboard owns (shopping list, uploaded photos). Relative to the content root.</summary>
    public string DataPath { get; init; } = "App_Data";

    /// <summary>The people whose calendars are shown, one tile each.</summary>
    public PersonOptions[] People { get; init; } = [];

    /// <summary>
    /// iCal links per person id, merged into that person's tile (e.g. personal + work). The links are
    /// secret, so this section belongs in appsettings.Local.json.
    /// </summary>
    public Dictionary<string, Dashboard.Api.Features.Calendar.CalendarFeedOptions[]> Calendars { get; init; } = new();

    /// <summary>Bus stops near the house, one tile each.</summary>
    public BusStopOptions[] BusStops { get; init; } = [];

    public CommuteOptions Commute { get; init; } = new();

    /// <summary>Optional municipal holiday added to the national ones (Porto: São João, 24 June).</summary>
    public MunicipalHolidayOptions? MunicipalHoliday { get; init; }
}

public sealed class PersonOptions
{
    public string Id { get; init; } = "";

    public string Name { get; init; } = "";

    /// <summary>Accent colour key used by the frontend (sky, violet, green, amber, rose, teal).</summary>
    public string Color { get; init; } = "sky";
}

public sealed class BusStopOptions
{
    public string Id { get; init; } = "";

    public string Name { get; init; } = "";

    /// <summary>Operator stop code (e.g. STCP "AVRP2"), used once the real feed is wired.</summary>
    public string Code { get; init; } = "";

    public int WalkMinutes { get; init; }
}

public sealed class CommuteOptions
{
    public string Label { get; init; } = "Trajeto até o trabalho";

    public PlaceOptions Destination { get; init; } = new();

    /// <summary>Google Routes API key. Put it in appsettings.Local.json or user secrets, never in appsettings.json.</summary>
    public string GoogleApiKey { get; init; } = "";

    /// <summary>Hours of the day (local) when the drive time matters and is refreshed often.</summary>
    public int ActiveFromHour { get; init; } = 6;

    public int ActiveToHour { get; init; } = 22;

    /// <summary>
    /// How long the server reuses a reading during active hours. Every request is billed, so keep this
    /// modest. 0 = no server cache: each page load asks Google (the dashboard doesn't poll this tile).
    /// </summary>
    public int RefreshMinutes { get; init; } = 10;

    /// <summary>How often to ask Google outside active hours.</summary>
    public int NightRefreshMinutes { get; init; } = 60;

    /// <summary>
    /// Server cache lifetime for the drive time at <paramref name="now"/>. With the defaults that is about
    /// 100 requests a day (~3,100 a month), however many screens are open.
    /// </summary>
    public TimeSpan CacheTtl(DateTimeOffset now) =>
        TimeSpan.FromMinutes(now.Hour >= ActiveFromHour && now.Hour < ActiveToHour ? RefreshMinutes : NightRefreshMinutes);
}

public sealed class PlaceOptions
{
    public string Name { get; init; } = "";

    public double Latitude { get; init; }

    public double Longitude { get; init; }
}

/// <summary>Provider names per widget. "Mock" (the default) serves sample data.</summary>
public sealed class IntegrationOptions
{
    public const string Mock = "Mock";

    /// <summary>"OpenMeteo" or "Mock".</summary>
    public string Weather { get; init; } = Mock;

    /// <summary>"Stcp" or "Mock".</summary>
    public string Transit { get; init; } = Mock;

    /// <summary>"GoogleRoutes" (needs Commute:GoogleApiKey) or "Mock".</summary>
    public string Commute { get; init; } = Mock;

    /// <summary>"Ics" (links in Dashboard:Calendars) or "Mock".</summary>
    public string Calendar { get; init; } = Mock;

    /// <summary>"Noona" (needs Dashboard:Noona:ClientId/ClientSecret) or "Mock".</summary>
    public string Appointments { get; init; } = Mock;

    /// <summary>"File" (App_Data/city-events.json, edited by hand) or "Mock".</summary>
    public string CityEvents { get; init; } = Mock;

    /// <summary>"File" (App_Data/parties.json, edited by hand) or "Mock".</summary>
    public string Parties { get; init; } = Mock;

}

/// <summary>Which widgets actually ended up on a mock provider (e.g. a real source without its API key).</summary>
public sealed record IntegrationStatus(IReadOnlyList<string> MockWidgets);

public sealed class MunicipalHolidayOptions
{
    public string Name { get; init; } = "";

    public int Month { get; init; }

    public int Day { get; init; }
}
