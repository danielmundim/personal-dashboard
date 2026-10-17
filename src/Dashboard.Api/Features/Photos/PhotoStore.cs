using Dashboard.Api.Infrastructure;

namespace Dashboard.Api.Features.Photos;

public sealed record Photo(string Id, string Url, bool Sample, DateTimeOffset AddedAt);

public sealed record PhotoList(IReadOnlyList<Photo> Photos, int RotateSeconds);

/// <summary>
/// Uploaded photos are stored as files under DataPath/photos with server-generated names, so a
/// filename from the client never touches the filesystem. Until something is uploaded, built-in
/// sample images are served so the slideshow can be previewed.
/// </summary>
public sealed class PhotoStore(DataDirectory dataDirectory)
{
    public const long MaxBytes = 25 * 1024 * 1024;
    public const int RotateSeconds = 15;

    private static readonly Dictionary<string, string> ContentTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        [".jpg"] = "image/jpeg",
        [".jpeg"] = "image/jpeg",
        [".png"] = "image/png",
        [".webp"] = "image/webp",
        [".gif"] = "image/gif",
    };

    private string Folder => dataDirectory.Sub("photos");

    public PhotoList List()
    {
        var files = new DirectoryInfo(Folder)
            .EnumerateFiles()
            .Where(f => ContentTypes.ContainsKey(f.Extension))
            .OrderByDescending(f => f.CreationTimeUtc)
            .Select(f => new Photo(f.Name, $"/api/photos/{f.Name}", false, f.CreationTimeUtc))
            .ToList();

        if (files.Count == 0)
            files = SamplePhotos.Names.Select(n => new Photo(n, $"/api/photos/samples/{n}", true, DateTimeOffset.MinValue)).ToList();

        return new PhotoList(files, RotateSeconds);
    }

    public async Task<Photo> SaveAsync(IFormFile file, CancellationToken ct)
    {
        var ext = Path.GetExtension(file.FileName);
        if (!ContentTypes.ContainsKey(ext))
            throw new BadHttpRequestException("Só é possível enviar imagens JPG, PNG, WebP ou GIF.");
        if (file.Length is 0 or > MaxBytes)
            throw new BadHttpRequestException("As fotos devem ter no máximo 25 MB.");

        var id = $"{DateTime.UtcNow:yyyyMMddHHmmss}-{Guid.NewGuid():N}{ext.ToLowerInvariant()}";
        await using (var target = File.Create(Path.Combine(Folder, id)))
            await file.CopyToAsync(target, ct);

        return new Photo(id, $"/api/photos/{id}", false, DateTimeOffset.UtcNow);
    }

    /// <summary>Returns the full path for a stored photo, or null if the id isn't a plain file name we own.</summary>
    public (string Path, string ContentType)? Find(string id)
    {
        if (id != System.IO.Path.GetFileName(id) || !ContentTypes.TryGetValue(System.IO.Path.GetExtension(id), out var type))
            return null;
        var path = System.IO.Path.Combine(Folder, id);
        return File.Exists(path) ? (path, type) : null;
    }

    public bool Delete(string id)
    {
        if (Find(id) is not { } found) return false;
        File.Delete(found.Path);
        return true;
    }
}
