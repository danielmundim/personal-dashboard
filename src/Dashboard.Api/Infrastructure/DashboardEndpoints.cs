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
using Microsoft.Extensions.Caching.Hybrid;
using Microsoft.Extensions.Options;

namespace Dashboard.Api.Infrastructure;

public static class DashboardEndpoints
{
    public static IEndpointRouteBuilder MapDashboardEndpoints(this IEndpointRouteBuilder app)
    {
        var api = app.MapGroup("/api");

        api.MapGet("/config", (IOptions<DashboardOptions> o) => new
        {
            o.Value.LocationName,
            o.Value.TimeZone,
            o.Value.UseMockData,
            People = o.Value.People.Select(p => new { p.Id, p.Name, p.Color }),
            BusStops = o.Value.BusStops.Select(s => new { s.Id, s.Name, s.WalkMinutes }),
        });

        // Read-only widgets are cached server-side so every open screen (wall, tablets, laptops)
        // shares one upstream call per interval, which keeps us inside external API rate limits.
        api.MapGet("/weather", (IWeatherProvider p, HybridCache c, CancellationToken ct) =>
            Cached(c, "weather", TimeSpan.FromMinutes(10), p.GetReportAsync, ct));

        api.MapGet("/holidays", (IHolidayProvider p, HybridCache c, CancellationToken ct) =>
            Cached(c, "holidays", TimeSpan.FromHours(1), t => p.GetUpcomingAsync(6, t), ct));

        api.MapGet("/calendar/{personId}", async (string personId, ICalendarProvider p, IOptions<DashboardOptions> o, HybridCache c, CancellationToken ct) =>
            o.Value.People.Any(x => x.Id == personId)
                ? Results.Ok(await Cached(c, $"calendar:{personId}", TimeSpan.FromMinutes(5), t => p.GetAgendaAsync(personId, 7, t), ct))
                : Results.NotFound());

        api.MapGet("/appointments", (IAppointmentProvider p, HybridCache c, CancellationToken ct) =>
            Cached(c, "appointments", TimeSpan.FromMinutes(2), p.GetUpcomingAsync, ct));

        api.MapGet("/transit/{stopId}", async (string stopId, ITransitProvider p, IOptions<DashboardOptions> o, HybridCache c, CancellationToken ct) =>
            o.Value.BusStops.Any(x => x.Id == stopId)
                ? Results.Ok(await Cached(c, $"transit:{stopId}", TimeSpan.FromSeconds(30), t => p.GetStopBoardAsync(stopId, t), ct))
                : Results.NotFound());

        api.MapGet("/commute", (ICommuteProvider p, HybridCache c, CancellationToken ct) =>
            Cached(c, "commute", TimeSpan.FromMinutes(2), p.GetEstimateAsync, ct));

        api.MapGet("/city-events", (ICityEventProvider p, HybridCache c, CancellationToken ct) =>
            Cached(c, "city-events", TimeSpan.FromHours(1), p.GetUpcomingAsync, ct));

        api.MapGet("/parties", (IPartyProvider p, HybridCache c, CancellationToken ct) =>
            Cached(c, "parties", TimeSpan.FromMinutes(10), p.GetUpcomingAsync, ct));

        MapShopping(api.MapGroup("/shopping"));
        MapPhotos(api.MapGroup("/photos"));

        return app;
    }

    private static void MapShopping(RouteGroupBuilder g)
    {
        g.MapGet("/", (ShoppingStore s, CancellationToken ct) => s.GetAsync(ct));

        g.MapPost("/", async (AddShoppingItem req, ShoppingStore s, CancellationToken ct) =>
            string.IsNullOrWhiteSpace(req.Name) || req.Name.Length > 100
                ? Results.ValidationProblem(new Dictionary<string, string[]> { ["name"] = ["Informe o nome do item (máx. 100 caracteres)."] })
                : Results.Ok(await s.AddAsync(req, ct)));

        g.MapPatch("/{id:guid}", (Guid id, UpdateShoppingItem req, ShoppingStore s, CancellationToken ct) => s.UpdateAsync(id, req, ct));
        g.MapDelete("/{id:guid}", (Guid id, ShoppingStore s, CancellationToken ct) => s.RemoveAsync(id, ct));
        g.MapDelete("/done", (ShoppingStore s, CancellationToken ct) => s.ClearDoneAsync(ct));
    }

    private static void MapPhotos(RouteGroupBuilder g)
    {
        g.MapGet("/", (PhotoStore s) => s.List());

        // LAN-only dashboard without accounts, so the form upload skips antiforgery tokens.
        g.MapPost("/", async (IFormFileCollection files, PhotoStore s, CancellationToken ct) =>
            {
                if (files.Count == 0) return Results.BadRequest("Nenhum arquivo foi enviado.");
                var saved = new List<Photo>();
                try
                {
                    foreach (var f in files) saved.Add(await s.SaveAsync(f, ct));
                }
                catch (BadHttpRequestException ex)
                {
                    return Results.BadRequest(ex.Message);
                }
                return Results.Ok(saved);
            })
            .DisableAntiforgery();

        g.MapGet("/{id}", (string id, PhotoStore s) =>
            s.Find(id) is { } f ? Results.File(f.Path, f.ContentType, enableRangeProcessing: true) : Results.NotFound());

        g.MapDelete("/{id}", (string id, PhotoStore s) => s.Delete(id) ? Results.NoContent() : Results.NotFound());

        g.MapGet("/samples/{name}", (string name) =>
            SamplePhotos.Get(name) is { } svg ? Results.Content(svg, "image/svg+xml") : Results.NotFound());
    }

    private static async Task<T> Cached<T>(HybridCache cache, string key, TimeSpan ttl, Func<CancellationToken, Task<T>> fetch, CancellationToken ct) =>
        await cache.GetOrCreateAsync(
            $"widget:{key}",
            async token => await fetch(token),
            new HybridCacheEntryOptions { Expiration = ttl, LocalCacheExpiration = ttl },
            cancellationToken: ct);
}
