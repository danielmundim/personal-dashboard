using Dashboard.Api.Features.Shopping;

namespace Dashboard.Api.Features.Stock;

/// <summary>/api/stock: the household inventory. Every change returns the whole list.</summary>
public static class StockEndpoints
{
    public static void MapStock(this RouteGroupBuilder api)
    {
        var g = api.MapGroup("/stock");

        g.MapGet("/", (StockStore s, CancellationToken ct) => s.GetAsync(ct));

        g.MapPost("/", async (SaveStockItem req, StockStore s, CancellationToken ct) =>
            Invalid(req.Name, req.Quantity, req.MinQuantity) is { } problem ? problem : Results.Ok(await s.AddAsync(req, ct)));

        g.MapPatch("/{id:guid}", async (Guid id, UpdateStockItem req, StockStore s, CancellationToken ct) =>
        {
            if (req.Name is not null && Invalid(req.Name, req.Quantity ?? 0, req.MinQuantity) is { } problem) return problem;
            if (await s.FindAsync(id, ct) is null) return Results.NotFound();
            return Results.Ok(await s.UpdateAsync(id, req, ct));
        });

        g.MapDelete("/{id:guid}", async (Guid id, StockStore s, CancellationToken ct) => Results.Ok(await s.RemoveAsync(id, ct)));

        // "Pôr na próxima compra": marks the item (by name) in the shopping list.
        g.MapPost("/{id:guid}/to-shopping", async (Guid id, StockStore s, ShoppingStore shopping, CancellationToken ct) =>
            await s.FindAsync(id, ct) is { } item
                ? Results.Ok(await shopping.MarkNeededAsync(item.Name, ct))
                : Results.NotFound());
    }

    private static IResult? Invalid(string? name, decimal quantity, decimal? min)
    {
        var errors = new Dictionary<string, string[]>();
        if (string.IsNullOrWhiteSpace(name) || name.Length > 100) errors["name"] = ["Informe o nome do item (máx. 100 caracteres)."];
        if (quantity < 0) errors["quantity"] = ["A quantidade não pode ser negativa."];
        if (min < 0) errors["minQuantity"] = ["O mínimo não pode ser negativo."];
        return errors.Count > 0 ? Results.ValidationProblem(errors) : null;
    }
}
