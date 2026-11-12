using System.Text.Json;
using Dashboard.Api.Infrastructure;

namespace Dashboard.Api.Features.Photos;

/// <summary>
/// <see cref="Album"/> groups photos of a moment or trip ("Lisboa 2026"); <see cref="Caption"/> is optional.
/// <see cref="Country"/> and <see cref="City"/> are where it was taken, as typed; <see cref="Latitude"/> and
/// <see cref="Longitude"/> are looked up from them when saved, and are null if the place wasn't found.
/// </summary>
public sealed record Photo(
    string Id,
    string Url,
    bool Sample,
    DateTimeOffset AddedAt,
    string? Album = null,
    string? Caption = null,
    string? Country = null,
    string? City = null,
    double? Latitude = null,
    double? Longitude = null);

public sealed record AlbumSummary(string Name, int Count, string CoverUrl);

/// <summary>One pin on the globe: the located photos of a city, or of a country when no city was given.</summary>
public sealed record PlaceSummary(string Country, string? City, int Count, string CoverUrl, double Latitude, double Longitude);

public sealed record PhotoList(IReadOnlyList<Photo> Photos, IReadOnlyList<AlbumSummary> Albums, IReadOnlyList<PlaceSummary> Places, int RotateSeconds);

/// <summary>Null leaves a field as it is; an empty string clears it.</summary>
public sealed record UpdatePhoto(string? Album, string? Caption, string? Country = null, string? City = null);

/// <summary>Where a photo was taken. <see cref="Point"/> is null when the place couldn't be located.</summary>
public sealed record Place(string? Country, string? City, GeoPoint? Point)
{
    public static readonly Place None = new(null, null, null);
}

/// <summary>
/// Uploaded photos are stored as files under DataPath/photos with server-generated names, so a
/// filename from the client never touches the filesystem. Album, caption and place live next to them
/// in DataPath/photos.json. Until something is uploaded, built-in sample images are served so the
/// slideshow and the globe can be previewed.
/// </summary>
public sealed class PhotoStore(DataDirectory dataDirectory, IServiceScopeFactory scopes)
{
    public const long MaxBytes = 25 * 1024 * 1024;
    public const int RotateSeconds = 5;

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { WriteIndented = true };

    private static readonly Dictionary<string, string> ContentTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        [".jpg"] = "image/jpeg",
        [".jpeg"] = "image/jpeg",
        [".png"] = "image/png",
        [".webp"] = "image/webp",
        [".gif"] = "image/gif",
    };

    private readonly SemaphoreSlim _gate = new(1, 1);

    private string Folder => dataDirectory.Sub("photos");

    private string MetaPath => Path.Combine(dataDirectory.Root, "photos.json");

    public async Task<PhotoList> ListAsync(CancellationToken ct)
    {
        var meta = await LoadMetaAsync(ct);
        var files = new DirectoryInfo(Folder)
            .EnumerateFiles()
            .Where(f => ContentTypes.ContainsKey(f.Extension))
            .OrderByDescending(f => f.CreationTimeUtc)
            .Select(f =>
            {
                var m = meta.GetValueOrDefault(f.Name);
                return new Photo(f.Name, $"/api/photos/{f.Name}", false, f.CreationTimeUtc, m?.Album, m?.Caption, m?.Country, m?.City, m?.Latitude, m?.Longitude);
            })
            .ToList();

        if (files.Count == 0)
            files = SamplePhotos.All.ToList();

        var albums = files
            .Where(p => p.Album is not null)
            .GroupBy(p => p.Album!, StringComparer.CurrentCultureIgnoreCase)
            .Select(g => new AlbumSummary(g.First().Album!, g.Count(), g.First().Url))
            .OrderByDescending(a => files.First(p => string.Equals(p.Album, a.Name, StringComparison.CurrentCultureIgnoreCase)).AddedAt)
            .ToList();

        // A city belongs to its country, so Porto in Portugal and a Porto in Brasil are two pins.
        var places = files
            .Where(p => p is { Country: not null, Latitude: not null, Longitude: not null })
            .GroupBy(p => (Country: p.Country!.ToLowerInvariant(), City: p.City?.ToLowerInvariant()))
            .Select(g => (Cover: g.First(), Count: g.Count()))
            .Select(x => new PlaceSummary(x.Cover.Country!, x.Cover.City, x.Count, x.Cover.Url, x.Cover.Latitude!.Value, x.Cover.Longitude!.Value))
            .OrderByDescending(p => p.Count)
            .ToList();

        return new PhotoList(files, albums, places, RotateSeconds);
    }

    /// <summary>Saves one uploaded file. The <paramref name="place"/> is shared by the whole upload, so the caller locates it once.</summary>
    public async Task<Photo> SaveAsync(IFormFile file, string? album, string? caption, Place place, CancellationToken ct)
    {
        var ext = Path.GetExtension(file.FileName);
        if (!ContentTypes.ContainsKey(ext))
            throw new BadHttpRequestException("Só é possível enviar imagens JPG, PNG, WebP ou GIF.");
        if (file.Length is 0 or > MaxBytes)
            throw new BadHttpRequestException("As fotos devem ter no máximo 25 MB.");

        var id = $"{DateTime.UtcNow:yyyyMMddHHmmss}-{Guid.NewGuid():N}{ext.ToLowerInvariant()}";
        await using (var target = File.Create(Path.Combine(Folder, id)))
            await file.CopyToAsync(target, ct);

        var m = new PhotoMeta(Clean(album, 60), Clean(caption, 200)).WithPlace(place);
        if (!m.IsEmpty)
            await UpdateMetaAsync(meta => meta[id] = m, ct);

        return new Photo(id, $"/api/photos/{id}", false, DateTimeOffset.UtcNow, m.Album, m.Caption, m.Country, m.City, m.Latitude, m.Longitude);
    }

    /// <summary>Cleans a typed country and city and looks up where they are. A city without a country isn't kept: the country anchors it.</summary>
    public async Task<Place> LocateAsync(string? country, string? city, CancellationToken ct)
    {
        country = Clean(country, 60);
        city = country is null ? null : Clean(city, 80);
        if (country is null) return Place.None;
        await using var scope = scopes.CreateAsyncScope(); // the geocoder holds a typed HttpClient
        var match = await scope.ServiceProvider.GetRequiredService<PlaceGeocoder>().LocateAsync(country, city, ct);
        return new Place(match?.Country ?? country, match?.City ?? city, match?.Point);
    }

    /// <summary>Sets album, caption and place; empty strings clear them. Returns false for an unknown photo.</summary>
    public async Task<bool> UpdateAsync(string id, UpdatePhoto request, CancellationToken ct)
    {
        if (Find(id) is null) return false;

        // Locate before taking the lock, and only when the place changed or never got coordinates.
        Place? place = null;
        if (request.Country is not null || request.City is not null)
        {
            var before = (await LoadMetaAsync(ct)).GetValueOrDefault(id) ?? PhotoMeta.Empty;
            var country = Clean(request.Country ?? before.Country, 60);
            var city = Clean(request.City ?? before.City, 80);
            place = before is { Latitude: { } lat, Longitude: { } lon }
                    && PlaceGeocoder.Same(country ?? "", before.Country ?? "") && PlaceGeocoder.Same(city ?? "", before.City ?? "")
                ? new Place(before.Country, before.City, new GeoPoint(lat, lon))
                : await LocateAsync(country, city, ct);
        }

        await UpdateMetaAsync(meta =>
        {
            var current = meta.GetValueOrDefault(id) ?? PhotoMeta.Empty;
            var next = current with
            {
                Album = request.Album is null ? current.Album : Clean(request.Album, 60),
                Caption = request.Caption is null ? current.Caption : Clean(request.Caption, 200),
            };
            if (place is not null) next = next.WithPlace(place);
            if (next.IsEmpty) meta.Remove(id);
            else meta[id] = next;
        }, ct);
        return true;
    }

    /// <summary>Returns the full path for a stored photo, or null if the id isn't a plain file name we own.</summary>
    public (string Path, string ContentType)? Find(string id)
    {
        if (id != System.IO.Path.GetFileName(id) || !ContentTypes.TryGetValue(System.IO.Path.GetExtension(id), out var type))
            return null;
        var path = System.IO.Path.Combine(Folder, id);
        return File.Exists(path) ? (path, type) : null;
    }

    public async Task<bool> DeleteAsync(string id, CancellationToken ct)
    {
        if (Find(id) is not { } found) return false;
        File.Delete(found.Path);
        await UpdateMetaAsync(meta => meta.Remove(id), ct);
        return true;
    }

    private async Task<Dictionary<string, PhotoMeta>> LoadMetaAsync(CancellationToken ct)
    {
        if (!File.Exists(MetaPath)) return [];
        await using var stream = File.OpenRead(MetaPath);
        return await JsonSerializer.DeserializeAsync<Dictionary<string, PhotoMeta>>(stream, Json, ct) ?? [];
    }

    private async Task UpdateMetaAsync(Action<Dictionary<string, PhotoMeta>> change, CancellationToken ct)
    {
        await _gate.WaitAsync(ct);
        try
        {
            var meta = await LoadMetaAsync(ct);
            change(meta);
            var tmp = MetaPath + ".tmp";
            await File.WriteAllTextAsync(tmp, JsonSerializer.Serialize(meta, Json), ct);
            File.Move(tmp, MetaPath, overwrite: true);
        }
        finally
        {
            _gate.Release();
        }
    }

    private static string? Clean(string? s, int max) =>
        string.IsNullOrWhiteSpace(s) ? null : s.Trim() is var t && t.Length > max ? t[..max] : s.Trim();

    /// <summary>The place fields were added later, so older photos.json entries simply lack them.</summary>
    private sealed record PhotoMeta(
        string? Album,
        string? Caption,
        string? Country = null,
        string? City = null,
        double? Latitude = null,
        double? Longitude = null)
    {
        public static readonly PhotoMeta Empty = new(null, null);

        public bool IsEmpty => Album is null && Caption is null && Country is null;

        public PhotoMeta WithPlace(Place place) =>
            this with { Country = place.Country, City = place.City, Latitude = place.Point?.Latitude, Longitude = place.Point?.Longitude };
    }
}
