using Dashboard.Api.Infrastructure;

namespace Dashboard.Api.Features.Parties;

public sealed class MockPartyProvider(LocalClock clock) : IPartyProvider
{
    public Task<PartyList> GetUpcomingAsync(CancellationToken cancellationToken)
    {
        var d = clock.Today;

        var parties = new List<Party>
        {
            new("Aniversário de 40 anos do Tiago", clock.At(d.AddDays(7), 20), null, false, "Terraço, Rua das Flores", "Tiago e Marta", "Presente (vaquinha)"),
            new("Festa de Halloween", clock.At(d.AddDays(14), 21), null, false, "Casa da Joana, Matosinhos", "Joana", "Petiscos", "Fantasia"),
            new("Open house", clock.At(d.AddDays(22), 19, 30), null, false, "Foz do Douro", "Pedro e Sara", "Uma planta"),
            new("Jantar de Natal do salão", clock.At(new DateOnly(d.Year, 12, 12), 20), null, false, "Restaurante O Paparico", "Equipe do salão", DressCode: "Esporte fino"),
        };

        return Task.FromResult(new PartyList(parties.Where(p => p.Start > clock.Now).OrderBy(p => p.Start).ToList(), clock.Now));
    }
}
