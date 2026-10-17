using Dashboard.Api.Infrastructure;
using Microsoft.Extensions.Options;

namespace Dashboard.Api.Features.Calendar;

public sealed class MockCalendarProvider(LocalClock clock, IOptions<DashboardOptions> options) : ICalendarProvider
{
    public Task<CalendarAgenda> GetAgendaAsync(string personId, int days, CancellationToken cancellationToken)
    {
        var d0 = clock.Today;
        var d1 = d0.AddDays(1);
        var d2 = d0.AddDays(2);
        var d4 = d0.AddDays(4);

        // The first configured person gets a "work + gym" week, anyone else a salon-owner week.
        List<CalendarEvent> events = personId == options.Value.People.FirstOrDefault()?.Id
            ?
            [
                new("Feira de produtores", clock.At(d0, 9, 30), clock.At(d0, 10, 30), false, "Mercado da Beira-Rio"),
                new("Almoço com o Rui", clock.At(d0, 13), clock.At(d0, 14), false, "Mercado do Bolhão"),
                new("Buscar encomenda de vinis", clock.At(d0, 18, 30), clock.At(d0, 19), false, "Matéria Prima"),
                new("Academia", clock.At(d1, 8), clock.At(d1, 9), false, "Solinca"),
                new("Inspeção do carro", clock.At(d2, 10), clock.At(d2, 11), false, "Centro de Inspeções Gaia"),
                new("Futebol com os amigos", clock.At(d4, 20), clock.At(d4, 21, 30), false, "Campo da Constituição"),
            ]
            :
            [
                new("Salão aberto", clock.At(d0, 0), clock.At(d0, 23, 59), true, null),
                new("Ligação com fornecedor", clock.At(d0, 12, 30), clock.At(d0, 13), false, "Telefone"),
                new("Pilates", clock.At(d0, 19), clock.At(d0, 20), false, "Studio Foz"),
                new("Dentista", clock.At(d1, 11), clock.At(d1, 11, 45), false, "Clínica da Boavista"),
                new("Jantar com a Joana", clock.At(d2, 20, 30), clock.At(d2, 22, 30), false, "Cantinho do Avillez"),
                new("Curso de formação", clock.At(d4, 0), clock.At(d4, 23, 59), true, "Lisboa"),
            ];

        var horizon = clock.At(d0.AddDays(days), 0);
        var agenda = new CalendarAgenda(personId, events.Where(e => e.Start < horizon).ToList(), clock.Now);
        return Task.FromResult(agenda);
    }
}
