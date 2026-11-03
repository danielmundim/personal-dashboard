namespace Dashboard.Api.Features.Health;

/// <summary>What kind of exam a record is. Lab panels carry numeric results; the others mostly an outcome and notes.</summary>
public enum ExamKind
{
    Sangue,
    Urina,
    Imagem,
    Cardio,
    Consulta,
    Medidas,
    Outro,
}

/// <summary>The doctor's (or our) reading of an exam without numeric results, like an ECG or an eye exam.</summary>
public enum ExamOutcome
{
    Normal,
    Alterado,
}

/// <summary>Where a weight came from. Photos from the WhatsApp group will be read into entries later.</summary>
public enum WeightSource
{
    Manual,
    Foto,
}

/// <summary>Per-person weight goal and height (for BMI). The start weight is where the effort began.</summary>
public sealed record HealthProfile(string PersonId, decimal? HeightCm, decimal? StartWeightKg, decimal? GoalWeightKg);

/// <summary>One weigh-in. At most one per person per day: saving the same day again replaces it.</summary>
public sealed record WeightEntry(Guid Id, string PersonId, DateOnly Date, decimal Kg, string? Note, WeightSource Source);

/// <summary>
/// One measured value. <see cref="Marker"/> is a code from the marker catalog (e.g. "ldl"). The lab's
/// own reference range, when the report shows one, overrides the catalog's default.
/// </summary>
public sealed record ExamResult(string Marker, decimal Value, decimal? RefLow, decimal? RefHigh);

/// <summary>An exam or appointment on a given day: a blood panel, an ECG, the dentist, a measurement.</summary>
public sealed record Exam(
    Guid Id,
    string PersonId,
    DateOnly Date,
    ExamKind Kind,
    string Title,
    string? Provider,
    ExamOutcome? Outcome,
    string? Notes,
    IReadOnlyList<ExamResult> Results,
    DateTimeOffset CreatedAt);

/// <summary>Something to repeat every few months (dentist, yearly blood panel). Done = the last time we went.</summary>
public sealed record Checkup(Guid Id, string PersonId, string Name, ExamKind Kind, int EveryMonths, DateOnly? LastDone);

/// <summary>A marker in the catalog: which area it belongs to, its unit and the usual adult reference range.</summary>
public sealed record Marker(string Code, string Name, string Area, string Unit, decimal? RefLow, decimal? RefHigh);

/// <summary>
/// Everything the Saúde tab shows. The data is small, so the page gets it all and works out the
/// status for any date itself (which keeps the "ver como estava em" picker instant).
/// </summary>
public sealed record HealthData(
    IReadOnlyList<HealthProfile> Profiles,
    IReadOnlyList<WeightEntry> Weights,
    IReadOnlyList<Exam> Exams,
    IReadOnlyList<Checkup> Checkups,
    IReadOnlyList<Marker> Markers,
    DateTimeOffset UpdatedAt);

public sealed record SaveWeight(string PersonId, DateOnly Date, decimal Kg, string? Note);

public sealed record SaveProfile(decimal? HeightCm, decimal? StartWeightKg, decimal? GoalWeightKg);

public sealed record SaveExam(
    string PersonId,
    DateOnly Date,
    ExamKind Kind,
    string Title,
    string? Provider,
    ExamOutcome? Outcome,
    string? Notes,
    IReadOnlyList<ExamResult>? Results);

public sealed record SaveCheckup(string PersonId, string Name, ExamKind Kind, int EveryMonths, DateOnly? LastDone);

public sealed record MarkCheckupDone(DateOnly Date);
