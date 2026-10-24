using System.Globalization;
using System.Text.Json;
using Dashboard.Api.Infrastructure;

namespace Dashboard.Api.Features.Stock;

/// <summary>
/// The household inventory: what we have at home, how much, and where. Kept in DataPath/stock.json and
/// edited from the Compras tab (Estoque). Same pattern as the shopping list: one file, atomic rewrites.
/// </summary>
public sealed class StockStore(DataDirectory dataDirectory, LocalClock clock)
{
    /// <summary>Suggested places, shown first; any other place typed in is kept too.</summary>
    public static readonly string[] DefaultLocations = ["Despensa", "Geladeira", "Congelador", "Limpeza", "Banheiro", "Outros"];

    private const int ExpiringWithinDays = 3;
    private const decimal MaxQuantity = 9999;
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { WriteIndented = true };
    private static readonly StringComparer Names = StringComparer.Create(CultureInfo.GetCultureInfo("pt-BR"), ignoreCase: true);

    private readonly SemaphoreSlim _gate = new(1, 1);
    private string FilePath => Path.Combine(dataDirectory.Root, "stock.json");

    public async Task<StockList> GetAsync(CancellationToken ct)
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

    /// <summary>Adds an item; if one with the same name and place exists, adds to its quantity instead.</summary>
    public Task<StockList> AddAsync(SaveStockItem r, CancellationToken ct) =>
        MutateAsync(items =>
        {
            var name = r.Name.Trim();
            var location = Location(r.Location);
            var i = items.FindIndex(x => Names.Equals(x.Name, name) && Names.Equals(x.Location, location));
            if (i >= 0)
            {
                items[i] = items[i] with { Quantity = Clamp(items[i].Quantity + r.Quantity), UpdatedAt = clock.Now };
                return true;
            }
            items.Add(new StockItem(Guid.NewGuid(), name, Clamp(r.Quantity), Unit(r.Unit), location, Min(r.MinQuantity), r.ExpiresOn, clock.Now));
            return true;
        }, ct);

    public Task<StockList> UpdateAsync(Guid id, UpdateStockItem r, CancellationToken ct) =>
        MutateAsync(items =>
        {
            var i = items.FindIndex(x => x.Id == id);
            if (i < 0) return false;
            var x = items[i];
            var quantity = r.Quantity ?? x.Quantity;
            if (r.Delta is { } delta) quantity += delta;
            items[i] = x with
            {
                Name = string.IsNullOrWhiteSpace(r.Name) ? x.Name : r.Name.Trim(),
                Quantity = Clamp(quantity),
                Unit = r.Unit is null ? x.Unit : Unit(r.Unit),
                Location = r.Location is null ? x.Location : Location(r.Location),
                MinQuantity = r.ClearMin == true ? null : r.MinQuantity is { } m ? Min(m) : x.MinQuantity,
                ExpiresOn = r.ClearExpiry == true ? null : r.ExpiresOn ?? x.ExpiresOn,
                UpdatedAt = clock.Now,
            };
            return true;
        }, ct);

    public Task<StockList> RemoveAsync(Guid id, CancellationToken ct) =>
        MutateAsync(items => items.RemoveAll(x => x.Id == id) > 0, ct);

    /// <summary>After shopping: adds what was bought to stock items with the same name. Returns how many matched.</summary>
    public async Task<int> AddPurchasedAsync(IEnumerable<(string Name, int Quantity)> bought, CancellationToken ct)
    {
        var matched = 0;
        await MutateAsync(items =>
        {
            foreach (var (name, quantity) in bought)
            {
                var i = items.FindIndex(x => Names.Equals(x.Name, name.Trim()));
                if (i < 0) continue;
                items[i] = items[i] with { Quantity = Clamp(items[i].Quantity + quantity), UpdatedAt = clock.Now };
                matched++;
            }
            return matched > 0;
        }, ct);
        return matched;
    }

    public async Task<StockItem?> FindAsync(Guid id, CancellationToken ct) =>
        (await GetAsync(ct)).Items.Select(v => v.Item).FirstOrDefault(x => x.Id == id);

    private async Task<StockList> MutateAsync(Func<List<StockItem>, bool> change, CancellationToken ct)
    {
        await _gate.WaitAsync(ct);
        try
        {
            var items = await LoadAsync(ct);
            if (change(items))
            {
                var tmp = FilePath + ".tmp";
                await File.WriteAllTextAsync(tmp, JsonSerializer.Serialize(new { version = 1, items }, Json), ct);
                File.Move(tmp, FilePath, overwrite: true);
            }
            return ToList(items);
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task<List<StockItem>> LoadAsync(CancellationToken ct)
    {
        if (!File.Exists(FilePath)) return [];
        await using var stream = File.OpenRead(FilePath);
        var file = await JsonSerializer.DeserializeAsync<StockFile>(stream, Json, ct);
        return file?.Items ?? [];
    }

    private StockList ToList(List<StockItem> items)
    {
        var today = clock.Today;
        var views = items
            .Select(x =>
            {
                var status = x.Quantity <= 0 ? StockStatus.Out
                    : x.MinQuantity is { } min && x.Quantity <= min ? StockStatus.Low
                    : StockStatus.Ok;
                int? days = x.ExpiresOn is { } e ? e.DayNumber - today.DayNumber : null;
                return new StockView(x, status, days);
            })
            .OrderBy(v => v.Item.Name, Names)
            .ToList();

        var locations = DefaultLocations
            .Concat(items.Select(x => x.Location))
            .Distinct(Names)
            .ToList();

        return new StockList(
            views,
            locations,
            views.Count(v => v.Status != StockStatus.Ok),
            views.Count(v => v.DaysToExpiry is { } d && d <= ExpiringWithinDays),
            clock.Now);
    }

    private static decimal Clamp(decimal q) => Math.Round(Math.Clamp(q, 0, MaxQuantity), 2);

    private static decimal? Min(decimal? m) => m is { } v && v > 0 ? Math.Round(Math.Min(v, MaxQuantity), 2) : null;

    private static string Unit(string? u) => string.IsNullOrWhiteSpace(u) ? "un" : u.Trim();

    private static string Location(string? l) =>
        string.IsNullOrWhiteSpace(l) ? "Outros"
        : DefaultLocations.FirstOrDefault(d => Names.Equals(d, l.Trim())) ?? l.Trim();

    private sealed record StockFile(List<StockItem>? Items);
}
