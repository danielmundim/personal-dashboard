namespace Dashboard.Api.Features.Health;

/// <summary>
/// /api/health: weigh-ins, weight goals, exams and recurring checkups for the Saúde tab. Every change
/// returns the whole data set, which replaces the page's copy.
/// </summary>
public static class HealthEndpoints
{
    public static void MapHealth(this RouteGroupBuilder api)
    {
        var g = api.MapGroup("/health");

        g.MapGet("/", (HealthStore s, CancellationToken ct) => s.GetAsync(ct));

        g.MapPost("/weights", async (SaveWeight req, HealthStore s, CancellationToken ct) =>
            InvalidWeight(req, s) is { } problem ? problem : Found(await s.SaveWeightAsync(req, ct)));

        g.MapDelete("/weights/{id:guid}", async (Guid id, HealthStore s, CancellationToken ct) => Found(await s.DeleteWeightAsync(id, ct)));

        g.MapPut("/profiles/{personId}", async (string personId, SaveProfile req, HealthStore s, CancellationToken ct) =>
            !s.IsPerson(personId) ? Results.NotFound()
            : InvalidProfile(req) is { } problem ? problem
            : Found(await s.SaveProfileAsync(personId, req, ct)));

        g.MapPost("/exams", async (SaveExam req, HealthStore s, CancellationToken ct) =>
            InvalidExam(req, s) is { } problem ? problem : Found(await s.CreateExamAsync(req, ct)));

        g.MapPut("/exams/{id:guid}", async (Guid id, SaveExam req, HealthStore s, CancellationToken ct) =>
            InvalidExam(req, s) is { } problem ? problem : Found(await s.UpdateExamAsync(id, req, ct)));

        g.MapDelete("/exams/{id:guid}", async (Guid id, HealthStore s, CancellationToken ct) => Found(await s.DeleteExamAsync(id, ct)));

        g.MapPost("/checkups", async (SaveCheckup req, HealthStore s, CancellationToken ct) =>
            InvalidCheckup(req, s) is { } problem ? problem : Found(await s.CreateCheckupAsync(req, ct)));

        g.MapPut("/checkups/{id:guid}", async (Guid id, SaveCheckup req, HealthStore s, CancellationToken ct) =>
            InvalidCheckup(req, s) is { } problem ? problem : Found(await s.UpdateCheckupAsync(id, req, ct)));

        g.MapPost("/checkups/{id:guid}/done", async (Guid id, MarkCheckupDone req, HealthStore s, CancellationToken ct) =>
            Found(await s.MarkCheckupDoneAsync(id, req.Date, ct)));

        g.MapDelete("/checkups/{id:guid}", async (Guid id, HealthStore s, CancellationToken ct) => Found(await s.DeleteCheckupAsync(id, ct)));
    }

    private static IResult Found((HealthData Data, bool Found) result) =>
        result.Found ? Results.Ok(result.Data) : Results.NotFound();

    private static IResult? InvalidWeight(SaveWeight r, HealthStore s)
    {
        var errors = new Dictionary<string, string[]>();
        if (!s.IsPerson(r.PersonId)) errors["personId"] = ["Escolha de quem é o peso."];
        if (r.Kg is < 20 or > 300) errors["kg"] = ["Informe um peso entre 20 e 300 kg."];
        if ((r.Note?.Length ?? 0) > 200) errors["note"] = ["A nota pode ter até 200 caracteres."];
        return errors.Count > 0 ? Results.ValidationProblem(errors) : null;
    }

    private static IResult? InvalidProfile(SaveProfile r)
    {
        var errors = new Dictionary<string, string[]>();
        if (r.HeightCm is < 50 or > 250) errors["heightCm"] = ["Informe a altura em centímetros (50 a 250)."];
        if (r.StartWeightKg is < 20 or > 300) errors["startWeightKg"] = ["O peso inicial deve ficar entre 20 e 300 kg."];
        if (r.GoalWeightKg is < 20 or > 300) errors["goalWeightKg"] = ["A meta deve ficar entre 20 e 300 kg."];
        return errors.Count > 0 ? Results.ValidationProblem(errors) : null;
    }

    private static IResult? InvalidExam(SaveExam r, HealthStore s)
    {
        var errors = new Dictionary<string, string[]>();
        if (!s.IsPerson(r.PersonId)) errors["personId"] = ["Escolha de quem é o exame."];
        if (string.IsNullOrWhiteSpace(r.Title) || r.Title.Length > 100) errors["title"] = ["Dê um nome ao exame (máx. 100 caracteres)."];
        if ((r.Provider?.Length ?? 0) > 100) errors["provider"] = ["O local pode ter até 100 caracteres."];
        if ((r.Notes?.Length ?? 0) > 2000) errors["notes"] = ["As notas podem ter até 2000 caracteres."];
        var results = r.Results ?? [];
        if (results.Count > 100) errors["results"] = ["Um exame pode ter até 100 resultados."];
        if (results.Any(x => !Markers.Exists(x.Marker))) errors["results"] = ["Há um marcador desconhecido nos resultados."];
        if (results.GroupBy(x => x.Marker).Any(grp => grp.Count() > 1)) errors["results"] = ["Cada marcador só pode aparecer uma vez por exame."];
        if (results.Any(x => x.Value < 0)) errors["results"] = ["Os valores não podem ser negativos."];
        return errors.Count > 0 ? Results.ValidationProblem(errors) : null;
    }

    private static IResult? InvalidCheckup(SaveCheckup r, HealthStore s)
    {
        var errors = new Dictionary<string, string[]>();
        if (!s.IsPerson(r.PersonId)) errors["personId"] = ["Escolha de quem é a rotina."];
        if (string.IsNullOrWhiteSpace(r.Name) || r.Name.Length > 100) errors["name"] = ["Dê um nome à rotina (máx. 100 caracteres)."];
        if (r.EveryMonths is < 1 or > 120) errors["everyMonths"] = ["A frequência deve ficar entre 1 e 120 meses."];
        return errors.Count > 0 ? Results.ValidationProblem(errors) : null;
    }
}
