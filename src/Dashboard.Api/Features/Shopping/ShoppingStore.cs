using System.Text.Json;
using Dashboard.Api.Infrastructure;

namespace Dashboard.Api.Features.Shopping;

/// <summary>
/// The shopping list is data the dashboard owns, so it lives in a JSON file under DataPath and every
/// device (wall, tablets, laptops) edits the same list. Small enough that a whole-file rewrite is fine.
/// </summary>
public sealed class ShoppingStore(DataDirectory dataDirectory, LocalClock clock)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { WriteIndented = true };
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

    public Task<ShoppingList> AddAsync(AddShoppingItem request, CancellationToken ct) =>
        MutateAsync(items => items.Add(new ShoppingItem(Guid.NewGuid(), request.Name.Trim(), Clean(request.Quantity), false, clock.Now)), ct);

    public Task<ShoppingList> UpdateAsync(Guid id, UpdateShoppingItem request, CancellationToken ct) =>
        MutateAsync(items =>
        {
            var i = items.FindIndex(x => x.Id == id);
            if (i < 0) return;
            var item = items[i];
            items[i] = item with
            {
                Done = request.Done ?? item.Done,
                Name = string.IsNullOrWhiteSpace(request.Name) ? item.Name : request.Name.Trim(),
                Quantity = request.Quantity is null ? item.Quantity : Clean(request.Quantity),
            };
        }, ct);

    public Task<ShoppingList> RemoveAsync(Guid id, CancellationToken ct) =>
        MutateAsync(items => items.RemoveAll(x => x.Id == id), ct);

    public Task<ShoppingList> ClearDoneAsync(CancellationToken ct) =>
        MutateAsync(items => items.RemoveAll(x => x.Done), ct);

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
            // Persist the sample items straight away so their ids stay stable between requests.
            var seed = Seed();
            await SaveAsync(seed, ct);
            return seed;
        }

        await using var stream = File.OpenRead(FilePath);
        return await JsonSerializer.DeserializeAsync<List<ShoppingItem>>(stream, Json, ct) ?? [];
    }

    private async Task SaveAsync(List<ShoppingItem> items, CancellationToken ct)
    {
        var tmp = FilePath + ".tmp";
        await File.WriteAllTextAsync(tmp, JsonSerializer.Serialize(items, Json), ct);
        File.Move(tmp, FilePath, overwrite: true); // atomic replace so a power cut can't truncate the list
    }

    /// <summary>Sample items written on first run, so the tile isn't empty before anyone adds something.</summary>
    private List<ShoppingItem> Seed()
    {
        var at = clock.Now;
        return
        [
            new(Guid.NewGuid(), "Leite", "2 L", false, at),
            new(Guid.NewGuid(), "Broa", null, false, at),
            new(Guid.NewGuid(), "Banana", "1 kg", false, at),
            new(Guid.NewGuid(), "Café em grão", null, false, at),
            new(Guid.NewGuid(), "Pastilhas para lava-louças", null, true, at),
            new(Guid.NewGuid(), "Bacalhau", "1 pacote", false, at),
        ];
    }

    private ShoppingList ToList(List<ShoppingItem> items) =>
        new(items.OrderBy(x => x.Done).ThenBy(x => x.AddedAt).ToList(), clock.Now);

    private static string? Clean(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();
}
