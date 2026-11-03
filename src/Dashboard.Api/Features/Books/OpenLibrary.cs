using System.Text.Json;

namespace Dashboard.Api.Features.Books;

/// <summary>ISBN-10 or ISBN-13, typed with or without dashes and spaces.</summary>
public static class Isbn
{
    /// <summary>
    /// Digits only (plus a final X for ISBN-10), or null when it isn't a valid ISBN. The check digit
    /// matters: Open Library has entries for many mistyped numbers, so a typo would fill the form
    /// with some other book.
    /// </summary>
    public static string? Normalize(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var clean = new string(value.Where(c => char.IsDigit(c) || c is 'x' or 'X').ToArray()).ToUpperInvariant();
        int D(int i) => clean[i] == 'X' ? 10 : clean[i] - '0';

        if (clean.Length == 13 && clean.All(char.IsDigit))
            return Enumerable.Range(0, 13).Sum(i => D(i) * (i % 2 == 0 ? 1 : 3)) % 10 == 0 ? clean : null;
        if (clean.Length == 10 && clean[..9].All(char.IsDigit))
            return Enumerable.Range(0, 10).Sum(i => D(i) * (10 - i)) % 11 == 0 ? clean : null;
        return null;
    }
}

/// <summary>
/// Open Library (openlibrary.org): free, no key. Looks up a book's details by ISBN for the form, and
/// downloads the cover so tablets show it from the Pi instead of each one fetching it from the internet.
/// </summary>
public sealed class OpenLibrary(HttpClient http, ILogger<OpenLibrary> logger)
{
    public async Task<BookLookup?> LookupAsync(string isbn, CancellationToken ct)
    {
        using var doc = JsonDocument.Parse(await http.GetStringAsync($"https://openlibrary.org/api/books?bibkeys=ISBN:{isbn}&format=json&jscmd=data", ct));
        if (!doc.RootElement.TryGetProperty($"ISBN:{isbn}", out var book)) return null;

        string? Text(JsonElement e, string name) => e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

        var authors = book.TryGetProperty("authors", out var a) && a.ValueKind == JsonValueKind.Array
            ? string.Join(", ", a.EnumerateArray().Select(x => Text(x, "name")).OfType<string>())
            : null;
        var pages = book.TryGetProperty("number_of_pages", out var p) && p.TryGetInt32(out var n) && n > 0 ? n : (int?)null;
        var cover = book.TryGetProperty("cover", out var c) ? Text(c, "medium") ?? Text(c, "large") : null;
        var title = Text(book, "title");
        if (Text(book, "subtitle") is { } subtitle && title is not null) title = $"{title}: {subtitle}";

        return new BookLookup(isbn, title, string.IsNullOrWhiteSpace(authors) ? null : authors, pages, cover);
    }

    /// <summary>The cover image for an ISBN, or null when Open Library has none (or can't be reached).</summary>
    public async Task<byte[]?> GetCoverAsync(string isbn, CancellationToken ct)
    {
        try
        {
            // default=false answers 404 instead of a blank placeholder image.
            using var response = await http.GetAsync($"https://covers.openlibrary.org/b/isbn/{isbn}-L.jpg?default=false", ct);
            return response.IsSuccessStatusCode ? await response.Content.ReadAsByteArrayAsync(ct) : null;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            logger.LogWarning(ex, "Could not download the cover for ISBN {Isbn}", isbn);
            return null;
        }
    }
}

/// <summary>Downloaded covers, one JPEG per book in DataPath/book-covers.</summary>
public sealed class BookCovers(Infrastructure.DataDirectory dataDirectory, OpenLibrary openLibrary, BookStore store)
{
    private string PathOf(Guid id) => Path.Combine(dataDirectory.Sub("book-covers"), $"{id}.jpg");

    public string? Find(Guid id) => File.Exists(PathOf(id)) ? PathOf(id) : null;

    /// <summary>
    /// After a save: fetches the cover when the book has an ISBN but no cover yet, and drops a stale one
    /// when the ISBN was cleared. Returns the list again only if something changed.
    /// </summary>
    public async Task<BookList?> SyncAsync(Book book, CancellationToken ct)
    {
        if (book.Isbn is null || book.HasCover)
        {
            if (book.Isbn is null) Delete(book.Id);
            return null;
        }
        var image = await openLibrary.GetCoverAsync(book.Isbn, ct);
        if (image is null) return null;
        await File.WriteAllBytesAsync(PathOf(book.Id), image, ct);
        return (await store.SetCoverAsync(book.Id, true, ct)).List;
    }

    public void Delete(Guid id)
    {
        if (File.Exists(PathOf(id))) File.Delete(PathOf(id));
    }
}
