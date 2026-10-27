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
    DateTimeOffset CreatedAt);

/// <summary>Counters for the page header, for the current calendar year.</summary>
public sealed record ReadingStats(int Year, int FinishedThisYear, int PagesThisYear);

/// <summary>
/// The shelf split the way the Leituras tab shows it: reading now (most recently started first), the
/// queue (in the order added), and finished or dropped books (most recently finished first).
/// </summary>
public sealed record BookList(
    IReadOnlyList<Book> Reading,
    IReadOnlyList<Book> Queue,
    IReadOnlyList<Book> Finished,
    ReadingStats Stats,
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
    string? Notes);

/// <summary>Moves the bookmark; reaching the last page doesn't finish the book by itself.</summary>
public sealed record UpdateProgress(int CurrentPage);

/// <summary>Changes only the status (Começar, Terminei, Abandonar), letting the store fill the dates.</summary>
public sealed record UpdateStatus(BookStatus Status, int? Rating);
