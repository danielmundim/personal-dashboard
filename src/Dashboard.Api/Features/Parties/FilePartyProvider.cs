using System.Globalization;
using System.Text.Json;
using Dashboard.Api.Infrastructure;

namespace Dashboard.Api.Features.Parties;

/// <summary>
/// Our own party and festival agenda, kept in DataPath/parties.json so it can be edited by hand:
/// <code>
/// { "parties": [
///   { "title": "Tomorrowland Brasil", "start": "2027-04-30", "end": "2027-05-02", "location": "Itu (SP)" },
///   { "title": "Aniversário do Tiago", "start": "2026-10-24", "time": "20:00", "location": "Rua das Flores" }
/// ] }
/// </code>
/// Dates are local (yyyy-MM-dd); "time" is optional (HH:mm). Past events drop off automatically.
/// </summary>
public sealed class FilePartyProvider(DataDirectory dataDirectory, LocalClock clock, ILogger<FilePartyProvider> logger) : IPartyProvider
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { ReadCommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true };

    private string FilePath => Path.Combine(dataDirectory.Root, "parties.json");

    public async Task<PartyList> GetUpcomingAsync(CancellationToken cancellationToken)
    {
        var today = clock.Today;
        if (!File.Exists(FilePath))
            return new PartyList([], clock.Now);

        PartyFile? file;
        try
        {
            await using var stream = File.OpenRead(FilePath);
            file = await JsonSerializer.DeserializeAsync<PartyFile>(stream, Json, cancellationToken);
        }
        catch (JsonException ex)
        {
            // A typo while editing by hand shouldn't take the tile down silently: say where it is.
            logger.LogWarning("parties.json could not be read (line {Line}): {Error}", ex.LineNumber, ex.Message);
            throw new InvalidOperationException("parties.json has a formatting error.", ex);
        }

        var parties = (file?.Parties ?? [])
            .Select(ToParty)
            .Where(p => p is not null)
            .Select(p => p!)
            .Where(p => DateOnly.FromDateTime((p.End ?? p.Start).DateTime) >= today)
            .OrderBy(p => p.Start)
            .ToList();

        return new PartyList(parties, clock.Now);
    }

    private Party? ToParty(PartyEntry e)
    {
        if (string.IsNullOrWhiteSpace(e.Title) || !DateOnly.TryParseExact(e.Start, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var startDate))
        {
            logger.LogWarning("Skipping a party without a title or a yyyy-MM-dd start date");
            return null;
        }

        var hasTime = TimeOnly.TryParseExact(e.Time ?? "", "HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out var time);
        var start = hasTime ? clock.At(startDate, time.Hour, time.Minute) : clock.At(startDate, 0);
        DateTimeOffset? end = DateOnly.TryParseExact(e.End ?? "", "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var endDate) && endDate > startDate
            ? clock.At(endDate, 0)
            : null;

        return new Party(e.Title.Trim(), start, end, !hasTime, Blank(e.Location), Blank(e.Host), Blank(e.Bring), Blank(e.DressCode));
    }

    private static string? Blank(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();

    private sealed record PartyFile(List<PartyEntry>? Parties);

    private sealed record PartyEntry(
        string? Title,
        string? Start,
        string? End,
        string? Time,
        string? Location,
        string? Host,
        string? Bring,
        string? DressCode);
}
