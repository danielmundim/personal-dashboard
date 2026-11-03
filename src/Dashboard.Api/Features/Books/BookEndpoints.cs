using Dashboard.Api.Infrastructure;
using Microsoft.Extensions.Options;

namespace Dashboard.Api.Features.Books;

/// <summary>
/// /api/books: list, add, edit and delete books, plus quick updates for the bookmark and the status.
/// Every change returns the whole list, so the Leituras tab redraws from one source.
/// </summary>
public static class BookEndpoints
{
    public static void MapBooks(this RouteGroupBuilder api)
    {
        var g = api.MapGroup("/books");

        g.MapGet("/", (BookStore s, CancellationToken ct) => s.GetAsync(ct));

        g.MapPost("/", async (SaveBook req, BookStore s, BookCovers covers, IOptions<DashboardOptions> o, CancellationToken ct) =>
            Invalid(req, o.Value) is { } problem ? problem : await Saved(await s.CreateAsync(req, ct), covers, ct));

        g.MapPut("/{id:guid}", async (Guid id, SaveBook req, BookStore s, BookCovers covers, IOptions<DashboardOptions> o, CancellationToken ct) =>
            Invalid(req, o.Value) is { } problem ? problem : await Saved(await s.UpdateAsync(id, req, ct), covers, ct));

        g.MapPatch("/{id:guid}/progress", async (Guid id, UpdateProgress req, BookStore s, CancellationToken ct) =>
            req.CurrentPage < 0
                ? Results.ValidationProblem(new Dictionary<string, string[]> { ["currentPage"] = ["A página não pode ser negativa."] })
                : Found(await s.UpdateProgressAsync(id, req.CurrentPage, ct)));

        g.MapPatch("/{id:guid}/status", async (Guid id, UpdateStatus req, BookStore s, IOptions<DashboardOptions> o, CancellationToken ct) =>
            req.ReaderId is { } reader && o.Value.People.All(p => p.Id != reader)
                ? Results.ValidationProblem(new Dictionary<string, string[]> { ["readerId"] = ["Essa pessoa não está no painel."] })
                : Found(await s.UpdateStatusAsync(id, req, ct)));

        g.MapDelete("/{id:guid}", async (Guid id, BookStore s, BookCovers covers, CancellationToken ct) =>
        {
            covers.Delete(id);
            return Results.Ok((await s.DeleteAsync(id, ct)).List);
        });

        g.MapGet("/{id:guid}/cover", (Guid id, BookCovers covers) =>
            covers.Find(id) is { } path ? Results.File(path, "image/jpeg") : Results.NotFound());

        // Prefills the form from Open Library; 404 when the ISBN is unknown there, 502 when it can't be reached.
        g.MapGet("/lookup/{isbn}", async (string isbn, OpenLibrary library, CancellationToken ct) =>
        {
            if (Isbn.Normalize(isbn) is not { } clean)
                return Results.ValidationProblem(new Dictionary<string, string[]> { ["isbn"] = ["O ISBN deve ter 10 ou 13 dígitos."] });
            try
            {
                return await library.LookupAsync(clean, ct) is { } found ? Results.Ok(found) : Results.NotFound();
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or System.Text.Json.JsonException)
            {
                return Results.Problem("Não foi possível falar com o Open Library agora.", statusCode: StatusCodes.Status502BadGateway);
            }
        });
    }

    /// <summary>Fetches the cover right after a save so the card shows it on the same response.</summary>
    private static async Task<IResult> Saved((BookList List, Book? Book) result, BookCovers covers, CancellationToken ct) =>
        result.Book is null ? Results.NotFound() : Results.Ok(await covers.SyncAsync(result.Book, ct) ?? result.List);

    private static IResult Found((BookList List, Book? Book) result) =>
        result.Book is null ? Results.NotFound() : Results.Ok(result.List);

    private static IResult? Invalid(SaveBook r, DashboardOptions o)
    {
        var errors = new Dictionary<string, string[]>();
        if (string.IsNullOrWhiteSpace(r.Title) || r.Title.Length > 200) errors["title"] = ["Informe o título (máx. 200 caracteres)."];
        if ((r.Author?.Length ?? 0) > 120) errors["author"] = ["O autor pode ter até 120 caracteres."];
        if (r.TotalPages is < 0 or > 20000) errors["totalPages"] = ["Número de páginas inválido."];
        if (r.CurrentPage is < 0) errors["currentPage"] = ["A página não pode ser negativa."];
        if (r.StartedOn is { } s && r.FinishedOn is { } f && f < s) errors["finishedOn"] = ["O fim não pode ser antes do início."];
        if ((r.Notes?.Length ?? 0) > 4000) errors["notes"] = ["As notas podem ter até 4000 caracteres."];
        if (!string.IsNullOrWhiteSpace(r.Isbn) && Isbn.Normalize(r.Isbn) is null) errors["isbn"] = ["O ISBN deve ter 10 ou 13 dígitos."];
        // Required whenever the household is configured: every book is someone's.
        if (o.People.Length > 0 && string.IsNullOrWhiteSpace(r.ReaderId)) errors["readerId"] = ["Escolha quem está lendo."];
        else if (!string.IsNullOrWhiteSpace(r.ReaderId) && o.People.All(p => p.Id != r.ReaderId)) errors["readerId"] = ["Essa pessoa não está no painel."];
        return errors.Count > 0 ? Results.ValidationProblem(errors) : null;
    }
}
