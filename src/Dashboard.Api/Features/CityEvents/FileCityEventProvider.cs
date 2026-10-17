using System.Globalization;
using System.Text.Json;
using Dashboard.Api.Infrastructure;
using Microsoft.Extensions.Options;

namespace Dashboard.Api.Features.CityEvents;

/// <summary>
/// Events in Porto and Gaia we've picked ourselves, kept in DataPath/city-events.json so they can be
/// edited by hand (no public feed covers the city's agenda). Same date rules as parties.json:
/// local "yyyy-MM-dd" dates, optional "end" for multi-day events, optional "time" (HH:mm).
/// Past events drop off automatically; the file is created with examples in comments.
/// </summary>
public sealed class FileCityEventProvider(DataDirectory dataDirectory, LocalClock clock, IOptions<DashboardOptions> options, ILogger<FileCityEventProvider> logger)
    : ICityEventProvider
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { ReadCommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true };

    private const string Template = """
        {
          // Eventos em Porto e Gaia. Datas "aaaa-mm-dd"; "end" para eventos de vários dias; "time" (HH:mm) opcional.
          // Campos opcionais: venue, category (Música, Exposição, Feira, Teatro, Gastronomia, ...), price, url.
          // Exemplo:
          // { "title": "Jazz no Parque", "start": "2026-10-24", "time": "17:00", "venue": "Serralves", "category": "Música", "price": "Grátis" },
          // { "title": "Mercado de Natal", "start": "2026-12-01", "end": "2026-12-23", "venue": "Praça D. João I", "category": "Feira" }
          "events": [
          ]
        }
        """;

    private string FilePath => Path.Combine(dataDirectory.Root, "city-events.json");

    public async Task<CityEventList> GetUpcomingAsync(CancellationToken cancellationToken)
    {
        if (!File.Exists(FilePath))
            await File.WriteAllTextAsync(FilePath, Template + "\n", cancellationToken);

        EventFile? file;
        try
        {
            await using var stream = File.OpenRead(FilePath);
            file = await JsonSerializer.DeserializeAsync<EventFile>(stream, Json, cancellationToken);
        }
        catch (JsonException ex)
        {
            logger.LogWarning("city-events.json could not be read (line {Line}): {Error}", ex.LineNumber, ex.Message);
            throw new InvalidOperationException("city-events.json has a formatting error.", ex);
        }

        var today = clock.Today;
        var events = (file?.Events ?? [])
            .Select(ToEvent)
            .Where(e => e is not null)
            .Select(e => e!)
            .Where(e => DateOnly.FromDateTime((e.End ?? e.Start).DateTime) >= today)
            .OrderBy(e => e.Start)
            .ToList();

        return new CityEventList(options.Value.LocationName, "Nossa lista", events, clock.Now);
    }

    private CityEvent? ToEvent(EventEntry e)
    {
        if (string.IsNullOrWhiteSpace(e.Title) || !DateOnly.TryParseExact(e.Start, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var startDate))
        {
            logger.LogWarning("Skipping a city event without a title or a yyyy-MM-dd start date");
            return null;
        }

        var hasTime = TimeOnly.TryParseExact(e.Time ?? "", "HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out var time);
        DateTimeOffset? end = DateOnly.TryParseExact(e.End ?? "", "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var endDate) && endDate > startDate
            ? clock.At(endDate, 0)
            : null;
        var url = Uri.TryCreate(e.Url, UriKind.Absolute, out var u) && (u.Scheme == Uri.UriSchemeHttps || u.Scheme == Uri.UriSchemeHttp) ? u.ToString() : null;

        return new CityEvent(
            e.Title.Trim(),
            Blank(e.Venue),
            hasTime ? clock.At(startDate, time.Hour, time.Minute) : clock.At(startDate, 0),
            end,
            !hasTime,
            Blank(e.Category),
            Blank(e.Price),
            url);
    }

    private static string? Blank(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();

    private sealed record EventFile(List<EventEntry>? Events);

    private sealed record EventEntry(string? Title, string? Start, string? End, string? Time, string? Venue, string? Category, string? Price, string? Url);
}
