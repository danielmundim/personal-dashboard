namespace Dashboard.Api.Infrastructure;

public sealed class DashboardOptions
{
    public const string SectionName = "Dashboard";

    /// <summary>IANA time zone of the household, e.g. "Europe/Lisbon".</summary>
    public string TimeZone { get; init; } = "Europe/Lisbon";

    /// <summary>Display name of the location shown in the header.</summary>
    public string LocationName { get; init; } = "Porto";

    public double Latitude { get; init; } = 41.1579;

    public double Longitude { get; init; } = -8.6291;

    /// <summary>
    /// When true, widgets backed by external services are served from mock providers. Flip to false
    /// as real integrations land (see <see cref="ProviderRegistration"/>).
    /// </summary>
    public bool UseMockData { get; init; } = true;

    /// <summary>Folder for data the dashboard owns (shopping list, uploaded photos). Relative to the content root.</summary>
    public string DataPath { get; init; } = "App_Data";

    /// <summary>The people whose calendars are shown, one tile each.</summary>
    public PersonOptions[] People { get; init; } = [];

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

    public string DestinationName { get; init; } = "";
}

public sealed class MunicipalHolidayOptions
{
    public string Name { get; init; } = "";

    public int Month { get; init; }

    public int Day { get; init; }
}
