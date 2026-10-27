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

        g.MapPost("/", async (SaveBook req, BookStore s, CancellationToken ct) =>
            Invalid(req) is { } problem ? problem : Results.Ok((await s.CreateAsync(req, ct)).List));

        g.MapPut("/{id:guid}", async (Guid id, SaveBook req, BookStore s, CancellationToken ct) =>
            Invalid(req) is { } problem ? problem : Found(await s.UpdateAsync(id, req, ct)));

        g.MapPatch("/{id:guid}/progress", async (Guid id, UpdateProgress req, BookStore s, CancellationToken ct) =>
            req.CurrentPage < 0
                ? Results.ValidationProblem(new Dictionary<string, string[]> { ["currentPage"] = ["A página não pode ser negativa."] })
                : Found(await s.UpdateProgressAsync(id, req.CurrentPage, ct)));

        g.MapPatch("/{id:guid}/status", async (Guid id, UpdateStatus req, BookStore s, CancellationToken ct) =>
            Found(await s.UpdateStatusAsync(id, req, ct)));

        g.MapDelete("/{id:guid}", async (Guid id, BookStore s, CancellationToken ct) => Results.Ok((await s.DeleteAsync(id, ct)).List));
    }

    private static IResult Found((BookList List, Book? Book) result) =>
        result.Book is null ? Results.NotFound() : Results.Ok(result.List);

    private static IResult? Invalid(SaveBook r)
    {
        var errors = new Dictionary<string, string[]>();
        if (string.IsNullOrWhiteSpace(r.Title) || r.Title.Length > 200) errors["title"] = ["Informe o título (máx. 200 caracteres)."];
        if ((r.Author?.Length ?? 0) > 120) errors["author"] = ["O autor pode ter até 120 caracteres."];
        if (r.TotalPages is < 0 or > 20000) errors["totalPages"] = ["Número de páginas inválido."];
        if (r.CurrentPage is < 0) errors["currentPage"] = ["A página não pode ser negativa."];
        if (r.StartedOn is { } s && r.FinishedOn is { } f && f < s) errors["finishedOn"] = ["O fim não pode ser antes do início."];
        if ((r.Notes?.Length ?? 0) > 4000) errors["notes"] = ["As notas podem ter até 4000 caracteres."];
        return errors.Count > 0 ? Results.ValidationProblem(errors) : null;
    }
}
