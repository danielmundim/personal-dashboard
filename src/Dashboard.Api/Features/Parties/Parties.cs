namespace Dashboard.Api.Features.Parties;

public sealed record Party(
    string Title,
    string Host,
    DateTimeOffset Start,
    string Location,
    string? Bring,
    string? DressCode);

public sealed record PartyList(IReadOnlyList<Party> Parties, DateTimeOffset UpdatedAt);

/// <summary>
/// Parties we confirmed attending. Likely sources later: calendar events tagged #party, or a list
/// edited from the dashboard (same file store as the shopping list).
/// </summary>
public interface IPartyProvider
{
    Task<PartyList> GetUpcomingAsync(CancellationToken cancellationToken);
}
