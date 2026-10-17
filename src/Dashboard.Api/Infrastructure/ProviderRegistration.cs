using Microsoft.AspNetCore.DataProtection;
using Dashboard.Api.Features.Calendar;
using Dashboard.Api.Features.CityEvents;
using Dashboard.Api.Features.Commute;
using Dashboard.Api.Features.Holidays;
using Dashboard.Api.Features.Noona;
using Dashboard.Api.Features.Parties;
using Dashboard.Api.Features.Photos;
using Dashboard.Api.Features.Shopping;
using Dashboard.Api.Features.Transit;
using Dashboard.Api.Features.Weather;

namespace Dashboard.Api.Infrastructure;

public static class ProviderRegistration
{
    public static IServiceCollection AddDashboardProviders(this IServiceCollection services, IConfiguration configuration)
    {
        var section = configuration.GetSection(DashboardOptions.SectionName);
        services.Configure<DashboardOptions>(section);
        var options = section.Get<DashboardOptions>() ?? new DashboardOptions();

        services.AddSingleton(TimeProvider.System);
        services.AddSingleton(sp => new LocalClock(
            sp.GetRequiredService<TimeProvider>(),
            TimeZoneInfo.FindSystemTimeZoneById(options.TimeZone)));
        services.AddSingleton<DataDirectory>();

        // Data the dashboard owns or can compute itself: always real.
        services.AddSingleton<IHolidayProvider, PortugueseHolidayProvider>();
        services.Configure<ShoppingOptions>(configuration.GetSection(ShoppingOptions.SectionName));
        services.AddSingleton<ShoppingStore>();
        services.AddSingleton<PhotoStore>();

        // Widgets backed by external services talk through one interface each. Dashboard:Integrations
        // picks the implementation per widget, so real sources can be switched on one at a time.
        var integrations = options.Integrations;
        static bool Is(string value, string name) => string.Equals(value, name, StringComparison.OrdinalIgnoreCase);

        var mock = new List<string> { "weather", "transit", "commute", "calendar", "appointments", "cityEvents", "parties" };

        if (Is(integrations.Weather, "OpenMeteo"))
        {
            services.AddHttpClient<IWeatherProvider, OpenMeteoWeatherProvider>(c => c.BaseAddress = new Uri("https://api.open-meteo.com/"))
                .AddStandardResilienceHandler();
            mock.Remove("weather");
        }
        else
        {
            services.AddSingleton<IWeatherProvider, MockWeatherProvider>();
        }

        if (Is(integrations.Transit, "Stcp"))
        {
            services.AddHttpClient<ITransitProvider, StcpTransitProvider>(c => c.BaseAddress = new Uri("https://stcp.pt/"))
                .AddStandardResilienceHandler();
            mock.Remove("transit");
        }
        else
        {
            services.AddSingleton<ITransitProvider, MockTransitProvider>();
        }

        var commute = options.Commute;
        if (Is(integrations.Commute, "GoogleRoutes") && !string.IsNullOrWhiteSpace(commute.GoogleApiKey))
        {
            services.AddSingleton<CommuteHistory>();
            services.AddHttpClient<ICommuteProvider, GoogleRoutesCommuteProvider>(c => c.BaseAddress = new Uri("https://routes.googleapis.com/"))
                .AddStandardResilienceHandler();
            mock.Remove("commute");
        }
        else
        {
            // Asked for Google Routes but no key yet: keep the tile alive on sample data.
            services.AddSingleton<ICommuteProvider, MockCommuteProvider>();
        }

        if (Is(integrations.Calendar, "Ics"))
        {
            // The feed URL *is* the secret for Google iCal links, so keep HttpClient from logging request URIs.
            services.AddHttpClient<ICalendarProvider, IcsCalendarProvider>(c => c.Timeout = TimeSpan.FromSeconds(20))
                .RemoveAllLoggers()
                .AddStandardResilienceHandler();
            mock.Remove("calendar");
        }
        else
        {
            services.AddSingleton<ICalendarProvider, MockCalendarProvider>();
        }
        // Noona: real once the OAuth app credentials are configured. Until someone completes the
        // one-time consent, the tile shows a "connect" prompt instead of sample data.
        var noona = configuration.GetSection(NoonaOptions.SectionName).Get<NoonaOptions>() ?? new NoonaOptions();
        services.Configure<NoonaOptions>(configuration.GetSection(NoonaOptions.SectionName));
        if (Is(integrations.Appointments, "Noona") && noona.IsConfigured)
        {
            // Encrypts the stored tokens; a fixed app name keeps them readable across restarts and redeploys.
            services.AddDataProtection().SetApplicationName("personal-home-dashboard");
            services.AddSingleton<NoonaTokenStore>();
            services.AddHttpClient<NoonaAuth>(c => c.BaseAddress = new Uri("https://api.noona.is/"));
            services.AddHttpClient<IAppointmentProvider, NoonaAppointmentProvider>(c => c.BaseAddress = new Uri("https://api.noona.is/"))
                .AddStandardResilienceHandler();
            mock.Remove("appointments");
        }
        else
        {
            services.AddSingleton<IAppointmentProvider, MockAppointmentProvider>();
        }
        if (Is(integrations.CityEvents, "File"))
        {
            services.AddSingleton<ICityEventProvider, FileCityEventProvider>();
            mock.Remove("cityEvents");
        }
        else
        {
            services.AddSingleton<ICityEventProvider, MockCityEventProvider>();
        }
        if (Is(integrations.Parties, "File"))
        {
            services.AddSingleton<IPartyProvider, FilePartyProvider>();
            mock.Remove("parties");
        }
        else
        {
            services.AddSingleton<IPartyProvider, MockPartyProvider>();
        }

        services.AddSingleton(new IntegrationStatus(mock));

        // Identify ourselves to the public APIs we call.
        services.ConfigureHttpClientDefaults(b => b.ConfigureHttpClient(c =>
            c.DefaultRequestHeaders.UserAgent.ParseAdd("personal-home-dashboard/1.0")));

        return services;
    }
}
