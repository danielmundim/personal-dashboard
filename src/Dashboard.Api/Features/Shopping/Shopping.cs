namespace Dashboard.Api.Features.Shopping;

/// <summary>
/// One of the household's usual items. <see cref="Needed"/> puts it on the next supermarket trip;
/// <see cref="Quantity"/> is how many to buy on that trip: starts at 1 and goes back to 1 once it's off the list.
/// </summary>
public sealed record ShoppingItem(
    Guid Id,
    string Name,
    int Quantity,
    bool Needed,
    DateTimeOffset AddedAt,
    DateTimeOffset? NeededSince = null);

/// <summary>All household items (alphabetical); the trip list is the ones with Needed = true.</summary>
public sealed record ShoppingList(IReadOnlyList<ShoppingItem> Items, int NeededCount, bool WhatsAppNumberSet, DateTimeOffset UpdatedAt);

/// <summary>Adds a household item by name only (not marked as missing); a known name is left as it is.</summary>
public sealed record AddShoppingItem(string Name);

public sealed record UpdateShoppingItem(bool? Needed, string? Name, int? Quantity);

public sealed record WhatsAppMessage(string Url, string Text, int ItemCount);

public sealed class ShoppingOptions
{
    public const string SectionName = "Dashboard:Shopping";

    /// <summary>
    /// International format, digits only (e.g. "351912345678"). Empty = WhatsApp asks which chat to send
    /// to, which is also how you'd send to a group. Personal data, so it belongs in appsettings.Local.json.
    /// </summary>
    public string WhatsAppNumber { get; init; } = "";
}
