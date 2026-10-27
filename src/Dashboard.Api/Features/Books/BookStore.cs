using System.Text.Json;
using System.Text.Json.Serialization;
using Dashboard.Api.Infrastructure;

namespace Dashboard.Api.Features.Books;

/// <summary>
/// Our books, edited from the Leituras tab and kept in DataPath/books.json. Same pattern as trips:
/// one small file, whole-file atomic rewrites, a lock around every change. The first read seeds a
/// few example books so the tab isn't empty; delete them once real ones are in.
/// </summary>
public sealed class BookStore(DataDirectory dataDirectory, LocalClock clock)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
    };

    private readonly SemaphoreSlim _gate = new(1, 1);
    private string FilePath => Path.Combine(dataDirectory.Root, "books.json");

    public async Task<BookList> GetAsync(CancellationToken ct)
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

    public Task<(BookList List, Book? Book)> CreateAsync(SaveBook request, CancellationToken ct) =>
        MutateAsync(books =>
        {
            var book = Apply(new Book(Guid.NewGuid(), "", null, BookStatus.QueroLer, BookFormat.Fisico, null, 0, null, null, null, null, clock.Now), request);
            books.Add(book);
            return book;
        }, ct);

    public Task<(BookList List, Book? Book)> UpdateAsync(Guid id, SaveBook request, CancellationToken ct) =>
        MutateBookAsync(id, b => Apply(b, request), ct);

    public Task<(BookList List, Book? Book)> UpdateProgressAsync(Guid id, int currentPage, CancellationToken ct) =>
        MutateBookAsync(id, b => b with
        {
            CurrentPage = ClampPage(currentPage, b.TotalPages),
            // Moving the bookmark on a queued book means we started it.
            Status = b.Status == BookStatus.QueroLer && currentPage > 0 ? BookStatus.Lendo : b.Status,
            StartedOn = b.StartedOn ?? (currentPage > 0 ? clock.Today : null),
        }, ct);

    public Task<(BookList List, Book? Book)> UpdateStatusAsync(Guid id, UpdateStatus request, CancellationToken ct) =>
        MutateBookAsync(id, b => WithStatus(b, request.Status) with { Rating = ValidRating(request.Rating) ?? b.Rating }, ct);

    public Task<(BookList List, Book? Book)> DeleteAsync(Guid id, CancellationToken ct) =>
        MutateAsync(books =>
        {
            books.RemoveAll(b => b.Id == id);
            return null;
        }, ct);

    // ---------- Rules ----------

    private Book Apply(Book b, SaveBook r)
    {
        var total = r.TotalPages is > 0 ? r.TotalPages : null;
        var updated = WithStatus(b with { TotalPages = total }, r.Status) with
        {
            Title = r.Title.Trim(),
            Author = Clean(r.Author),
            Format = r.Format,
            Notes = Clean(r.Notes),
            Rating = ValidRating(r.Rating),
        };
        // Dates and page typed in the form win over the ones the status change filled in.
        return updated with
        {
            CurrentPage = r.CurrentPage is { } page ? ClampPage(page, total) : ClampPage(updated.CurrentPage, total),
            StartedOn = r.StartedOn ?? updated.StartedOn,
            FinishedOn = r.Status is BookStatus.Lido or BookStatus.Abandonado ? r.FinishedOn ?? updated.FinishedOn : null,
        };
    }

    /// <summary>Fills in the dates a status change implies: started today, finished today, bookmark at the end.</summary>
    private Book WithStatus(Book b, BookStatus status)
    {
        var today = clock.Today;
        return status switch
        {
            BookStatus.QueroLer => b with { Status = status, FinishedOn = null },
            BookStatus.Lendo => b with { Status = status, StartedOn = b.StartedOn ?? today, FinishedOn = null },
            BookStatus.Lido => b with
            {
                Status = status,
                StartedOn = b.StartedOn ?? today,
                FinishedOn = b.Status == BookStatus.Lido ? b.FinishedOn ?? today : today,
                CurrentPage = b.TotalPages ?? b.CurrentPage,
            },
            BookStatus.Abandonado => b with { Status = status, FinishedOn = b.FinishedOn ?? today },
            _ => b,
        };
    }

    private BookList ToList(List<Book> books)
    {
        var year = clock.Today.Year;
        var readThisYear = books.Where(b => b.Status == BookStatus.Lido && b.FinishedOn?.Year == year).ToList();
        return new BookList(
            books.Where(b => b.Status == BookStatus.Lendo).OrderByDescending(b => b.StartedOn).ToList(),
            books.Where(b => b.Status == BookStatus.QueroLer).OrderBy(b => b.CreatedAt).ToList(),
            books.Where(b => b.Status is BookStatus.Lido or BookStatus.Abandonado).OrderByDescending(b => b.FinishedOn).ToList(),
            new ReadingStats(year, readThisYear.Count, readThisYear.Sum(b => b.TotalPages ?? 0)),
            clock.Now);
    }

    private static int ClampPage(int page, int? total) => Math.Clamp(page, 0, total ?? int.MaxValue);

    private static int? ValidRating(int? rating) => rating is >= 1 and <= 5 ? rating : null;

    private static string? Clean(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();

    // ---------- Storage ----------

    private Task<(BookList List, Book? Book)> MutateBookAsync(Guid id, Func<Book, Book> change, CancellationToken ct) =>
        MutateAsync(books =>
        {
            var i = books.FindIndex(b => b.Id == id);
            if (i < 0) return null;
            books[i] = change(books[i]);
            return books[i];
        }, ct);

    private async Task<(BookList List, Book? Book)> MutateAsync(Func<List<Book>, Book?> change, CancellationToken ct)
    {
        await _gate.WaitAsync(ct);
        try
        {
            var books = await LoadAsync(ct);
            var changed = change(books);
            await SaveAsync(books, ct);
            return (ToList(books), changed);
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task SaveAsync(List<Book> books, CancellationToken ct)
    {
        var tmp = FilePath + ".tmp";
        await File.WriteAllTextAsync(tmp, JsonSerializer.Serialize(new { version = 1, books }, Json), ct);
        File.Move(tmp, FilePath, overwrite: true);
    }

    private async Task<List<Book>> LoadAsync(CancellationToken ct)
    {
        if (!File.Exists(FilePath))
        {
            var seeded = Samples();
            await SaveAsync(seeded, ct);
            return seeded;
        }
        var file = JsonSerializer.Deserialize<BookFile>(await File.ReadAllTextAsync(FilePath, ct), Json);
        return file?.Books?.ToList() ?? [];
    }

    private List<Book> Samples()
    {
        var today = clock.Today;
        var now = clock.Now;
        return
        [
            new(Guid.NewGuid(), "Torto Arado", "Itamar Vieira Junior", BookStatus.Lendo, BookFormat.Fisico, 264, 142, today.AddDays(-12), null, null, "Emprestado da biblioteca, devolver até o fim do mês.", now),
            new(Guid.NewGuid(), "O Avesso da Pele", "Jeferson Tenório", BookStatus.Lendo, BookFormat.Ebook, 192, 40, today.AddDays(-3), null, null, null, now),
            new(Guid.NewGuid(), "Memórias Póstumas de Brás Cubas", "Machado de Assis", BookStatus.QueroLer, BookFormat.Fisico, 368, 0, null, null, null, null, now.AddDays(-30)),
            new(Guid.NewGuid(), "Ensaio sobre a Cegueira", "José Saramago", BookStatus.QueroLer, BookFormat.Fisico, 312, 0, null, null, null, "Recomendação da Ana.", now.AddDays(-10)),
            new(Guid.NewGuid(), "Sapiens", "Yuval Noah Harari", BookStatus.QueroLer, BookFormat.Audiobook, null, 0, null, null, null, null, now.AddDays(-5)),
            new(Guid.NewGuid(), "A Hora da Estrela", "Clarice Lispector", BookStatus.Lido, BookFormat.Fisico, 88, 88, today.AddDays(-50), today.AddDays(-44), 5, "Releitura. Ainda melhor.", now.AddDays(-60)),
            new(Guid.NewGuid(), "Capitães da Areia", "Jorge Amado", BookStatus.Lido, BookFormat.Fisico, 280, 280, today.AddDays(-120), today.AddDays(-95), 4, null, now.AddDays(-130)),
            new(Guid.NewGuid(), "Grande Sertão: Veredas", "Guimarães Rosa", BookStatus.Abandonado, BookFormat.Fisico, 560, 130, today.AddDays(-200), today.AddDays(-170), null, "Tentar de novo com mais calma.", now.AddDays(-210)),
        ];
    }

    private sealed record BookFile(int Version, IReadOnlyList<Book>? Books);
}
