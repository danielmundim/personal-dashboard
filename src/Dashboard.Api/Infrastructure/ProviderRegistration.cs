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
        services.AddSingleton<ShoppingStore>();
        services.AddSingleton<PhotoStore>();

        // Widgets backed by external services talk through one interface each, so swapping a mock for
        // a real integration is a one-line change here, e.g.:
        //   services.AddHttpClient<IWeatherProvider, IpmaWeatherProvider>(c => c.BaseAddress = new("https://api.ipma.pt/"));
        if (options.UseMockData)
        {
            services.AddSingleton<IWeatherProvider, MockWeatherProvider>();
            services.AddSingleton<ICalendarProvider, MockCalendarProvider>();
            services.AddSingleton<IAppointmentProvider, MockAppointmentProvider>();
            services.AddSingleton<ITransitProvider, MockTransitProvider>();
            services.AddSingleton<ICommuteProvider, MockCommuteProvider>();
            services.AddSingleton<ICityEventProvider, MockCityEventProvider>();
            services.AddSingleton<IPartyProvider, MockPartyProvider>();
        }

        return services;
    }
}
