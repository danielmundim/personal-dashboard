namespace Dashboard.Api.Features.Stock;

/// <summary>
/// Something we have at home and how much of it. <see cref="Location"/> is where it's kept (Despensa,
/// Geladeira…); <see cref="MinQuantity"/> marks it as running low; <see cref="ExpiresOn"/> is optional.
/// </summary>
public sealed record StockItem(
    Guid Id,
    string Name,
    decimal Quantity,
    string Unit,
    string Location,
    decimal? MinQuantity,
    DateOnly? ExpiresOn,
    DateTimeOffset UpdatedAt);

public enum StockStatus
{
    Ok,

    /// <summary>At or below the minimum.</summary>
    Low,

    /// <summary>Quantity is zero.</summary>
    Out,
}

/// <summary>An item plus what the dashboard should flag about it.</summary>
public sealed record StockView(StockItem Item, StockStatus Status, int? DaysToExpiry);

public sealed record StockList(
    IReadOnlyList<StockView> Items,
    IReadOnlyList<string> Locations,
    int LowCount,
    int ExpiringCount,
    DateTimeOffset UpdatedAt);

public sealed record SaveStockItem(string Name, decimal Quantity, string? Unit, string? Location, decimal? MinQuantity, DateOnly? ExpiresOn);

/// <summary>Partial update: only the fields sent change. <see cref="Delta"/> adds to the quantity (−/+ buttons).</summary>
public sealed record UpdateStockItem(
    decimal? Quantity,
    decimal? Delta,
    string? Name,
    string? Unit,
    string? Location,
    decimal? MinQuantity,
    bool? ClearMin,
    DateOnly? ExpiresOn,
    bool? ClearExpiry);
