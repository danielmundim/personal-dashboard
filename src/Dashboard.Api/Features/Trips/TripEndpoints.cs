namespace Dashboard.Api.Features.Trips;

/// <summary>
/// /api/trips: list, create, edit and delete trips, plus their checklist items. Every change returns
/// the whole list, so the Viagens tab and the "Próxima viagem" card on Hoje stay in sync.
/// </summary>
public static class TripEndpoints
{
    public static void MapTrips(this RouteGroupBuilder api)
    {
        var g = api.MapGroup("/trips");

        g.MapGet("/", (TripStore s, CancellationToken ct) => s.GetAsync(ct));

        g.MapPost("/", async (SaveTrip req, TripStore s, CancellationToken ct) =>
            Invalid(req) is { } problem ? problem : Results.Ok((await s.CreateAsync(req, ct)).List));

        g.MapPut("/{id:guid}", async (Guid id, SaveTrip req, TripStore s, CancellationToken ct) =>
            Invalid(req) is { } problem ? problem : Found(await s.UpdateAsync(id, req, ct)));

        g.MapDelete("/{id:guid}", async (Guid id, TripStore s, CancellationToken ct) => Results.Ok((await s.DeleteAsync(id, ct)).List));

        g.MapPost("/{id:guid}/items", async (Guid id, AddChecklistItem req, TripStore s, CancellationToken ct) =>
            string.IsNullOrWhiteSpace(req.Text) || req.Text.Length > 120
                ? Results.ValidationProblem(new Dictionary<string, string[]> { ["text"] = ["Descreva o item (máx. 120 caracteres)."] })
                : Found(await s.AddItemAsync(id, req, ct)));

        g.MapPatch("/{id:guid}/items/{itemId:guid}", async (Guid id, Guid itemId, UpdateChecklistItem req, TripStore s, CancellationToken ct) =>
            Found(await s.UpdateItemAsync(id, itemId, req, ct)));

        g.MapDelete("/{id:guid}/items/{itemId:guid}", async (Guid id, Guid itemId, TripStore s, CancellationToken ct) =>
            Found(await s.DeleteItemAsync(id, itemId, ct)));
    }

    private static IResult Found((TripList List, Trip? Trip) result) =>
        result.Trip is null ? Results.NotFound() : Results.Ok(result.List);

    private static IResult? Invalid(SaveTrip r)
    {
        var errors = new Dictionary<string, string[]>();
        if (string.IsNullOrWhiteSpace(r.Name) || r.Name.Length > 100) errors["name"] = ["Dê um nome à viagem (máx. 100 caracteres)."];
        if (r.EndDate is { } end && end < r.StartDate) errors["endDate"] = ["A volta não pode ser antes da ida."];
        if ((r.Notes?.Length ?? 0) > 4000) errors["notes"] = ["As notas podem ter até 4000 caracteres."];
        return errors.Count > 0 ? Results.ValidationProblem(errors) : null;
    }
}
