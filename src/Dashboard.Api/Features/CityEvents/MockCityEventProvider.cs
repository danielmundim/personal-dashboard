using Dashboard.Api.Infrastructure;
using Microsoft.Extensions.Options;

namespace Dashboard.Api.Features.CityEvents;

public sealed class MockCityEventProvider(LocalClock clock, IOptions<DashboardOptions> options) : ICityEventProvider
{
    public Task<CityEventList> GetUpcomingAsync(CancellationToken cancellationToken)
    {
        var d = clock.Today;

        var events = new List<CityEvent>
        {
            new("Jazz no Parque", "Parque de Serralves", clock.At(d, 17), clock.At(d, 19), "Música", "Grátis", null),
            new("Mercado de Natal antecipado", "Praça D. João I", clock.At(d.AddDays(1), 10), clock.At(d.AddDays(1), 20), "Feira", "Grátis", null),
            new("Porto Wine Fest", "Alfândega do Porto", clock.At(d.AddDays(3), 18), null, "Gastronomia", "€15", null),
            new("Exposição: Fotografia Portuguesa", "Centro Português de Fotografia", clock.At(d.AddDays(4), 10), null, "Exposição", "Grátis", null),
            new("Stand-up: noite de humor", "Teatro Sá da Bandeira", clock.At(d.AddDays(6), 21, 30), null, "Comédia", "€18", null),
            new("Orquestra Sinfónica", "Casa da Música", clock.At(d.AddDays(8), 21), null, "Clássica", "€22", null),
        };

        return Task.FromResult(new CityEventList(options.Value.LocationName, "Fonte de exemplo", events, clock.Now));
    }
}
