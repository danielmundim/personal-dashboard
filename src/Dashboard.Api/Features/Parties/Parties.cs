namespace Dashboard.Api.Features.Parties;

/// <summary>
/// A party or festival we're going to. Multi-day events (festivals) have <see cref="End"/>; events
/// without a time are <see cref="AllDay"/> and start at local midnight.
/// </summary>
public sealed record Party(
    string Title,
    DateTimeOffset Start,
    DateTimeOffset? End,
    bool AllDay,
    string? Location,
    string? Host = null,
    string? Bring = null,
    string? DressCode = null);

public sealed record PartyList(IReadOnlyList<Party> Parties, DateTimeOffset UpdatedAt);

public interface IPartyProvider
{
    Task<PartyList> GetUpcomingAsync(CancellationToken cancellationToken);
}
