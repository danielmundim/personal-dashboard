using System.Text.Json;
using Dashboard.Api.Infrastructure;

namespace Dashboard.Api.Features.Photos;

/// <summary><see cref="Album"/> groups photos of a moment or trip ("Lisboa 2026"); <see cref="Caption"/> is optional.</summary>
public sealed record Photo(string Id, string Url, bool Sample, DateTimeOffset AddedAt, string? Album = null, string? Caption = null);

public sealed record AlbumSummary(string Name, int Count, string CoverUrl);

public sealed record PhotoList(IReadOnlyList<Photo> Photos, IReadOnlyList<AlbumSummary> Albums, int RotateSeconds);

public sealed record UpdatePhoto(string? Album, string? Caption);

/// <summary>
/// Uploaded photos are stored as files under DataPath/photos with server-generated names, so a
/// filename from the client never touches the filesystem. Album and caption live next to them in
/// DataPath/photos.json. Until something is uploaded, built-in sample images are served so the
/// slideshow can be previewed.
/// </summary>
public sealed class PhotoStore(DataDirectory dataDirectory)
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
                return new Photo(f.Name, $"/api/photos/{f.Name}", false, f.CreationTimeUtc, m?.Album, m?.Caption);
            })
            .ToList();

        if (files.Count == 0)
            files = SamplePhotos.Names.Select(n => new Photo(n, $"/api/photos/samples/{n}", true, DateTimeOffset.MinValue)).ToList();

        var albums = files
            .Where(p => p.Album is not null)
            .GroupBy(p => p.Album!, StringComparer.CurrentCultureIgnoreCase)
            .Select(g => new AlbumSummary(g.First().Album!, g.Count(), g.First().Url))
            .OrderByDescending(a => files.First(p => string.Equals(p.Album, a.Name, StringComparison.CurrentCultureIgnoreCase)).AddedAt)
            .ToList();

        return new PhotoList(files, albums, RotateSeconds);
    }

    public async Task<Photo> SaveAsync(IFormFile file, string? album, string? caption, CancellationToken ct)
    {
        var ext = Path.GetExtension(file.FileName);
        if (!ContentTypes.ContainsKey(ext))
            throw new BadHttpRequestException("Só é possível enviar imagens JPG, PNG, WebP ou GIF.");
        if (file.Length is 0 or > MaxBytes)
            throw new BadHttpRequestException("As fotos devem ter no máximo 25 MB.");

        var id = $"{DateTime.UtcNow:yyyyMMddHHmmss}-{Guid.NewGuid():N}{ext.ToLowerInvariant()}";
        await using (var target = File.Create(Path.Combine(Folder, id)))
            await file.CopyToAsync(target, ct);

        album = Clean(album, 60);
        caption = Clean(caption, 200);
        if (album is not null || caption is not null)
            await UpdateMetaAsync(meta => meta[id] = new PhotoMeta(album, caption), ct);

        return new Photo(id, $"/api/photos/{id}", false, DateTimeOffset.UtcNow, album, caption);
    }

    /// <summary>Sets album and caption; empty strings clear them. Returns false for an unknown photo.</summary>
    public async Task<bool> UpdateAsync(string id, UpdatePhoto request, CancellationToken ct)
    {
        if (Find(id) is null) return false;
        await UpdateMetaAsync(meta =>
        {
            var current = meta.GetValueOrDefault(id) ?? new PhotoMeta(null, null);
            var next = new PhotoMeta(
                request.Album is null ? current.Album : Clean(request.Album, 60),
                request.Caption is null ? current.Caption : Clean(request.Caption, 200));
            if (next.Album is null && next.Caption is null) meta.Remove(id);
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

    private sealed record PhotoMeta(string? Album, string? Caption);
}
