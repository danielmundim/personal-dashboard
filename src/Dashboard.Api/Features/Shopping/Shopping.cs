namespace Dashboard.Api.Features.Shopping;

public sealed record ShoppingItem(Guid Id, string Name, string? Quantity, bool Done, DateTimeOffset AddedAt);

public sealed record ShoppingList(IReadOnlyList<ShoppingItem> Items, DateTimeOffset UpdatedAt);

public sealed record AddShoppingItem(string Name, string? Quantity);

public sealed record UpdateShoppingItem(bool? Done, string? Name, string? Quantity);
