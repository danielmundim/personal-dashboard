using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Dashboard.Api.Infrastructure;
using Microsoft.Extensions.Options;

namespace Dashboard.Api.Features.Shopping;

/// <summary>
/// The household's usual items plus what's missing for the next supermarket trip. Data the dashboard
/// owns, so it lives in DataPath/shopping.json and every device edits the same list.
/// </summary>
public sealed class ShoppingStore(DataDirectory dataDirectory, LocalClock clock, IOptionsMonitor<ShoppingOptions> options)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { WriteIndented = true };
    private const int MaxQuantity = 99;
    private static readonly CultureInfo PtBr = CultureInfo.GetCultureInfo("pt-BR");
    private readonly SemaphoreSlim _gate = new(1, 1);
    private string FilePath => Path.Combine(dataDirectory.Root, "shopping.json");

    public async Task<ShoppingList> GetAsync(CancellationToken ct)
    {
        await _gate.WaitAsync(ct);
        try
        {
            return ToList(await LoadAsync(ct));
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>Adds a household item (just the name). A name that already exists is left unchanged.</summary>
    public Task<ShoppingList> AddAsync(AddShoppingItem request, CancellationToken ct) =>
        MutateAsync(items =>
        {
            var name = request.Name.Trim();
            if (items.Any(x => string.Equals(x.Name, name, StringComparison.CurrentCultureIgnoreCase))) return;
            items.Add(new ShoppingItem(Guid.NewGuid(), name, 1, false, clock.Now));
        }, ct);

    public Task<ShoppingList> UpdateAsync(Guid id, UpdateShoppingItem request, CancellationToken ct) =>
        MutateAsync(items =>
        {
            var i = items.FindIndex(x => x.Id == id);
            if (i < 0) return;
            var item = request.Needed is { } needed ? SetNeeded(items[i], needed) : items[i];
            items[i] = item with
            {
                Name = string.IsNullOrWhiteSpace(request.Name) ? item.Name : request.Name.Trim(),
                // The quantity belongs to the trip, so it only changes while the item is on the list.
                Quantity = !item.Needed ? 1 : request.Quantity is { } q ? Math.Clamp(q, 1, MaxQuantity) : item.Quantity,
            };
        }, ct);

    /// <summary>Removes an item from the household list altogether.</summary>
    public Task<ShoppingList> RemoveAsync(Guid id, CancellationToken ct) =>
        MutateAsync(items => items.RemoveAll(x => x.Id == id), ct);

    /// <summary>After the shopping is done: nothing is missing any more. The household items stay.</summary>
    public Task<ShoppingList> ClearTripAsync(CancellationToken ct) =>
        MutateAsync(items =>
        {
            for (var i = 0; i < items.Count; i++)
                if (items[i].Needed) items[i] = SetNeeded(items[i], false);
        }, ct);

    /// <summary>A wa.me link with the trip list pre-filled; WhatsApp opens with the message ready to send.</summary>
    public async Task<WhatsAppMessage> BuildWhatsAppAsync(CancellationToken ct)
    {
        var list = await GetAsync(ct);
        var needed = list.Items.Where(x => x.Needed).ToList();

        var text = new StringBuilder();
        var date = clock.Now.ToString("dddd, d 'de' MMMM", PtBr);
        text.Append("🛒 *Lista de compras* (").Append(date).Append(")\n\n");
        foreach (var item in needed)
            text.Append("• ").Append(item.Quantity > 1 ? $"{item.Quantity}× " : "").Append(item.Name).Append('\n');
        text.Append('\n').Append(needed.Count == 1 ? "1 item" : $"{needed.Count} itens");

        var number = new string(options.CurrentValue.WhatsAppNumber.Where(char.IsDigit).ToArray());
        var url = $"https://wa.me/{number}?text={Uri.EscapeDataString(text.ToString())}";
        return new WhatsAppMessage(url, text.ToString(), needed.Count);
    }

    /// <summary>Selecting or unselecting an item resets its quantity to 1, so every trip starts clean.</summary>
    private ShoppingItem SetNeeded(ShoppingItem item, bool needed) =>
        item.Needed == needed ? item : item with { Needed = needed, NeededSince = needed ? clock.Now : null, Quantity = 1 };

    private async Task<ShoppingList> MutateAsync(Action<List<ShoppingItem>> change, CancellationToken ct)
    {
        await _gate.WaitAsync(ct);
        try
        {
            var items = await LoadAsync(ct);
            change(items);
            await SaveAsync(items, ct);
            return ToList(items);
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task<List<ShoppingItem>> LoadAsync(CancellationToken ct)
    {
        if (!File.Exists(FilePath))
        {
            var seed = Seed([]);
            await SaveAsync(seed, ct);
            return seed;
        }

        var node = JsonNode.Parse(await File.ReadAllTextAsync(FilePath, ct));

        // Version 1 was a plain array of { name, quantity, done }: an unticked item meant "still to buy".
        if (node is JsonArray v1)
        {
            var migrated = v1
                .Select(n => new ShoppingItem(
                    n?["id"]?.GetValue<Guid>() ?? Guid.NewGuid(),
                    n?["name"]?.GetValue<string>() ?? "",
                    ParseQuantity(n?["quantity"]),
                    Needed: !(n?["done"]?.GetValue<bool>() ?? false),
                    n?["addedAt"]?.GetValue<DateTimeOffset>() ?? clock.Now))
                .Where(x => x.Name.Length > 0)
                .ToList();
            var items = Seed(migrated);
            await SaveAsync(items, ct);
            return items;
        }

        // Version 2 briefly stored quantity as free text ("2 L"); read either form.
        return (node?["items"] as JsonArray ?? [])
            .Select(n => new ShoppingItem(
                n?["id"]?.GetValue<Guid>() ?? Guid.NewGuid(),
                n?["name"]?.GetValue<string>() ?? "",
                ParseQuantity(n?["quantity"]),
                n?["needed"]?.GetValue<bool>() ?? false,
                n?["addedAt"]?.GetValue<DateTimeOffset>() ?? clock.Now,
                n?["neededSince"]?.GetValue<DateTimeOffset?>()))
            .Where(x => x.Name.Length > 0)
            .ToList();
    }

    private async Task SaveAsync(List<ShoppingItem> items, CancellationToken ct)
    {
        var tmp = FilePath + ".tmp";
        await File.WriteAllTextAsync(tmp, JsonSerializer.Serialize(new { version = 2, items }, Json), ct);
        File.Move(tmp, FilePath, overwrite: true); // atomic replace so a power cut can't truncate the list
    }

    /// <summary>A starter set of everyday items (none marked as missing); easy to remove from the tile.</summary>
    private List<ShoppingItem> Seed(List<ShoppingItem> existing)
    {
        string[] usual =
        [
            "Leite", "Pão", "Ovos", "Manteiga", "Queijo", "Fiambre", "Iogurte", "Banana", "Maçã", "Tomate",
            "Alface", "Cebola", "Alho", "Batata", "Arroz", "Massa", "Feijão", "Azeite", "Café", "Açúcar",
            "Água", "Papel higiênico", "Detergente de louça", "Detergente de roupa", "Sacos de lixo",
        ];
        var at = clock.Now;
        foreach (var name in usual)
            if (!existing.Any(x => string.Equals(x.Name, name, StringComparison.CurrentCultureIgnoreCase)))
                existing.Add(new ShoppingItem(Guid.NewGuid(), name, 1, false, at));
        return existing;
    }

    private ShoppingList ToList(List<ShoppingItem> items) =>
        new(
            items.OrderBy(x => x.Name, StringComparer.Create(PtBr, ignoreCase: true)).ToList(),
            items.Count(x => x.Needed),
            !string.IsNullOrWhiteSpace(options.CurrentValue.WhatsAppNumber),
            clock.Now);

    /// <summary>Whole number from a stored quantity: 2 → 2, "6" → 6, "2 L" → 2, missing or text → 1.</summary>
    private static int ParseQuantity(JsonNode? node)
    {
        if (node is JsonValue v)
        {
            if (v.TryGetValue<int>(out var n)) return Math.Clamp(n, 1, MaxQuantity);
            if (v.TryGetValue<string>(out var s))
            {
                var digits = new string(s.TrimStart().TakeWhile(char.IsDigit).ToArray());
                if (int.TryParse(digits, out var parsed)) return Math.Clamp(parsed, 1, MaxQuantity);
            }
        }
        return 1;
    }
}
