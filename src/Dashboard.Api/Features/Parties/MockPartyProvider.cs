using Dashboard.Api.Infrastructure;

namespace Dashboard.Api.Features.Parties;

public sealed class MockPartyProvider(LocalClock clock) : IPartyProvider
{
    public Task<PartyList> GetUpcomingAsync(CancellationToken cancellationToken)
    {
        var d = clock.Today;

        var parties = new List<Party>
        {
            new("Aniversário de 40 anos do Tiago", "Tiago e Marta", clock.At(d.AddDays(7), 20), "Terraço, Rua das Flores", "Presente (vaquinha)", null),
            new("Festa de Halloween", "Joana", clock.At(d.AddDays(14), 21), "Casa da Joana, Matosinhos", "Petiscos", "Fantasia"),
            new("Open house", "Pedro e Sara", clock.At(d.AddDays(22), 19, 30), "Foz do Douro", "Uma planta", null),
            new("Jantar de Natal do salão", "Equipe do salão", clock.At(new DateOnly(d.Year, 12, 12), 20), "Restaurante O Paparico", null, "Esporte fino"),
        };

        return Task.FromResult(new PartyList(parties.Where(p => p.Start > clock.Now).OrderBy(p => p.Start).ToList(), clock.Now));
    }
}
