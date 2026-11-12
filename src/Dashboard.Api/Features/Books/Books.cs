namespace Dashboard.Api.Features.Books;

/// <summary>Where a book is on our shelf: on the wish list, being read, finished, or dropped.</summary>
public enum BookStatus
{
    QueroLer,
    Lendo,
    Lido,
    Abandonado,
}

/// <summary>Paper, e-reader or audio: changes what "progress" means (pages vs. percent).</summary>
public enum BookFormat
{
    Fisico,
    Ebook,
    Audiobook,
}

/// <summary>
/// One book. <see cref="CurrentPage"/> is the bookmark; with <see cref="TotalPages"/> it gives the
/// progress bar. Dates are set by the store when the status changes (start reading, finish) but can
/// be corrected from the form. <see cref="Rating"/> is 1–5, only meaningful once read.
/// <see cref="ReaderId"/> is a Dashboard:People id, required on save. It is null only on books saved
/// before readers existed; those show on everyone's shelf until edited. Two people reading the same
/// book add it twice: the bookmark is personal.
/// <see cref="HasCover"/> is true once the cover was downloaded for <see cref="Isbn"/> (see BookCovers).
/// <see cref="Category"/> is free text ("Fantasia", "História do Brasil"): it picks the shelf the book
/// stands on in the Estantes view. Null puts it on "Sem categoria".
/// </summary>
public sealed record Book(
    Guid Id,
    string Title,
    string? Author,
    BookStatus Status,
    BookFormat Format,
    int? TotalPages,
    int CurrentPage,
    DateOnly? StartedOn,
    DateOnly? FinishedOn,
    int? Rating,
    string? Notes,
    DateTimeOffset CreatedAt,
    string? ReaderId = null,
    string? Isbn = null,
    bool HasCover = false,
    string? Category = null);

/// <summary>
/// The shelf split the way the Leituras tab shows it: reading now (most recently started first), the
/// queue (in the order added), and finished or dropped books (most recently finished first).
/// </summary>
public sealed record BookList(
    IReadOnlyList<Book> Reading,
    IReadOnlyList<Book> Queue,
    IReadOnlyList<Book> Finished,
    DateTimeOffset UpdatedAt);

public sealed record SaveBook(
    string Title,
    string? Author,
    BookStatus Status,
    BookFormat Format,
    int? TotalPages,
    int? CurrentPage,
    DateOnly? StartedOn,
    DateOnly? FinishedOn,
    int? Rating,
    string? Notes,
    string? ReaderId = null,
    string? Isbn = null,
    string? Category = null);

/// <summary>Moves the bookmark; reaching the last page doesn't finish the book by itself.</summary>
public sealed record UpdateProgress(int CurrentPage);

/// <summary>
/// Changes only the status (Começar, Terminei, Abandonar), letting the store fill the dates.
/// <see cref="ReaderId"/> claims a book without a reader (an old one) for whoever starts it; it never reassigns one.
/// </summary>
public sealed record UpdateStatus(BookStatus Status, int? Rating, string? ReaderId = null);

/// <summary>What Open Library knows about an ISBN, to prefill the form. Any field may be missing.</summary>
public sealed record BookLookup(string Isbn, string? Title, string? Author, int? TotalPages, string? CoverUrl);
