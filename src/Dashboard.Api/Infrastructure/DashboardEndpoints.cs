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

        api.MapGet("/config", (IOptions<DashboardOptions> o, IntegrationStatus status) => new
        {
            o.Value.LocationName,
            o.Value.TimeZone,
            status.MockWidgets,
            People = o.Value.People.Select(p => new { p.Id, p.Name, p.Color }),
            BusStops = o.Value.BusStops.Select(s => new { s.Id, s.Name, s.Code, s.WalkMinutes }),
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

        api.MapGet("/appointments", async (IAppointmentProvider p, HybridCache c, CancellationToken ct) =>
        {
            try
            {
                return Results.Ok(await Cached(c, "appointments", TimeSpan.FromMinutes(2), p.GetUpcomingAsync, ct));
            }
            catch (NoonaNotConnectedException ex)
            {
                // 409 tells the tile to show its "Conectar ao Noona" button instead of an error.
                return Results.Problem(ex.Message, statusCode: StatusCodes.Status409Conflict,
                    extensions: new Dictionary<string, object?> { ["connectUrl"] = "/api/noona/connect" });
            }
        });
        api.MapNoona();
        Dashboard.Api.Features.Stock.StockEndpoints.MapStock(api);
        Dashboard.Api.Features.Trips.TripEndpoints.MapTrips(api);
        Dashboard.Api.Features.Books.BookEndpoints.MapBooks(api);

        api.MapGet("/transit/{stopId}", async (string stopId, ITransitProvider p, IOptions<DashboardOptions> o, HybridCache c, CancellationToken ct) =>
            o.Value.BusStops.Any(x => x.Id == stopId)
                ? Results.Ok(await Cached(c, $"transit:{stopId}", TimeSpan.FromSeconds(30), t => p.GetStopBoardAsync(stopId, t), ct))
                : Results.NotFound());

        // Google Routes bills per request, so this TTL (not the screens' polling) sets the call volume.
        api.MapGet("/commute", (ICommuteProvider p, HybridCache c, LocalClock clock, IOptions<DashboardOptions> o, CancellationToken ct) =>
            o.Value.Commute.CacheTtl(clock.Now) is var ttl && ttl > TimeSpan.Zero
                ? Cached(c, "commute", ttl, p.GetEstimateAsync, ct)
                : p.GetEstimateAsync(ct));

        api.MapGet("/city-events", (ICityEventProvider p, HybridCache c, CancellationToken ct) =>
            Cached(c, "city-events", TimeSpan.FromMinutes(1), p.GetUpcomingAsync, ct)); // short: hand edits show up quickly

        api.MapGet("/parties", (IPartyProvider p, HybridCache c, CancellationToken ct) =>
            Cached(c, "parties", TimeSpan.FromMinutes(1), p.GetUpcomingAsync, ct)); // short: edits to parties.json show up quickly

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

        // "Compras feitas": empties the trip list (the usual items stay) and adds what was bought to the
        // stock items with the same name, so the Estoque view follows the shopping without extra typing.
        g.MapPost("/trip/clear", async (ShoppingStore s, Dashboard.Api.Features.Stock.StockStore stock, CancellationToken ct) =>
        {
            var bought = (await s.GetAsync(ct)).Items.Where(i => i.Needed).Select(i => (i.Name, i.Quantity)).ToList();
            var list = await s.ClearTripAsync(ct);
            var updated = await stock.AddPurchasedAsync(bought, ct);
            return new { list, stockUpdated = updated };
        });

        // The browser opens this wa.me link; WhatsApp shows the message ready to send (nothing is sent automatically).
        g.MapGet("/whatsapp", (ShoppingStore s, CancellationToken ct) => s.BuildWhatsAppAsync(ct));
    }

    private static void MapPhotos(RouteGroupBuilder g)
    {
        g.MapGet("/", (PhotoStore s, CancellationToken ct) => s.ListAsync(ct));

        // LAN-only dashboard without accounts, so the form upload skips antiforgery tokens.
        // Optional form fields "album" and "caption" apply to every file in the upload.
        g.MapPost("/", async (HttpRequest request, PhotoStore s, CancellationToken ct) =>
            {
                var form = await request.ReadFormAsync(ct);
                if (form.Files.Count == 0) return Results.BadRequest("Nenhum arquivo foi enviado.");
                var saved = new List<Photo>();
                try
                {
                    foreach (var f in form.Files) saved.Add(await s.SaveAsync(f, form["album"], form["caption"], ct));
                }
                catch (BadHttpRequestException ex)
                {
                    return Results.BadRequest(ex.Message);
                }
                return Results.Ok(saved);
            })
            .DisableAntiforgery();

        g.MapPatch("/{id}", async (string id, UpdatePhoto req, PhotoStore s, CancellationToken ct) =>
            await s.UpdateAsync(id, req, ct) ? Results.NoContent() : Results.NotFound());

        g.MapGet("/{id}", (string id, PhotoStore s) =>
            s.Find(id) is { } f ? Results.File(f.Path, f.ContentType, enableRangeProcessing: true) : Results.NotFound());

        g.MapDelete("/{id}", async (string id, PhotoStore s, CancellationToken ct) =>
            await s.DeleteAsync(id, ct) ? Results.NoContent() : Results.NotFound());

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
