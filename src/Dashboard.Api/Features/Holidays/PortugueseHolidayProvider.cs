using Dashboard.Api.Infrastructure;
using Microsoft.Extensions.Options;

namespace Dashboard.Api.Features.Holidays;

/// <summary>
/// Real data, computed locally: Portuguese public holidays (Código do Trabalho, art. 234) plus the
/// configured municipal holiday and Carnival. No external API needed.
/// </summary>
public sealed class PortugueseHolidayProvider(LocalClock clock, IOptions<DashboardOptions> options) : IHolidayProvider
{
    public Task<HolidayList> GetUpcomingAsync(int count, CancellationToken cancellationToken)
    {
        var today = clock.Today;

        var upcoming = ForYear(today.Year)
            .Concat(ForYear(today.Year + 1))
            .Where(h => h.Date >= today)
            .OrderBy(h => h.Date)
            .Take(count)
            .Select(h => new Holiday(
                h.Name,
                h.Date,
                h.Scope,
                h.Date.DayNumber - today.DayNumber,
                h.Date.DayOfWeek is DayOfWeek.Monday or DayOfWeek.Friday,
                h.Date.DayOfWeek is DayOfWeek.Tuesday or DayOfWeek.Thursday))
            .ToList();

        return Task.FromResult(new HolidayList("PT", upcoming, clock.Now));
    }

    private IEnumerable<(string Name, DateOnly Date, HolidayScope Scope)> ForYear(int year)
    {
        var easter = EasterSunday(year);

        yield return ("Ano Novo", new(year, 1, 1), HolidayScope.National);
        yield return ("Carnaval", easter.AddDays(-47), HolidayScope.Optional);
        yield return ("Sexta-feira Santa", easter.AddDays(-2), HolidayScope.National);
        yield return ("Páscoa", easter, HolidayScope.National);
        yield return ("Dia da Liberdade", new(year, 4, 25), HolidayScope.National);
        yield return ("Dia do Trabalhador", new(year, 5, 1), HolidayScope.National);
        yield return ("Corpo de Deus", easter.AddDays(60), HolidayScope.National);
        yield return ("Dia de Portugal", new(year, 6, 10), HolidayScope.National);
        yield return ("Assunção de Nossa Senhora", new(year, 8, 15), HolidayScope.National);
        yield return ("Implantação da República", new(year, 10, 5), HolidayScope.National);
        yield return ("Dia de Todos os Santos", new(year, 11, 1), HolidayScope.National);
        yield return ("Restauração da Independência", new(year, 12, 1), HolidayScope.National);
        yield return ("Imaculada Conceição", new(year, 12, 8), HolidayScope.National);
        yield return ("Natal", new(year, 12, 25), HolidayScope.National);

        if (options.Value.MunicipalHoliday is { Month: > 0, Day: > 0 } m)
            yield return (m.Name, new(year, m.Month, m.Day), HolidayScope.Municipal);
    }

    /// <summary>Anonymous Gregorian algorithm (Meeus/Jones/Butcher).</summary>
    internal static DateOnly EasterSunday(int year)
    {
        int a = year % 19, b = year / 100, c = year % 100, d = b / 4, e = b % 4;
        int f = (b + 8) / 25, g = (b - f + 1) / 3, h = (19 * a + b - d - g + 15) % 30;
        int i = c / 4, k = c % 4, l = (32 + 2 * e + 2 * i - h - k) % 7;
        int m = (a + 11 * h + 22 * l) / 451;
        int month = (h + l - 7 * m + 114) / 31, day = (h + l - 7 * m + 114) % 31 + 1;
        return new DateOnly(year, month, day);
    }
}
