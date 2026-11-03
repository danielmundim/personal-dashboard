using System.Text.Json;
using System.Text.Json.Serialization;
using Dashboard.Api.Infrastructure;
using Microsoft.Extensions.Options;

namespace Dashboard.Api.Features.Health;

/// <summary>
/// Weights, exams and recurring checkups for the Saúde tab, kept in DataPath/health.json. Same pattern
/// as trips and books: one small file, whole-file atomic rewrites, a lock around every change. The
/// first read seeds example data so the tab isn't empty; delete it once real records are in.
/// </summary>
public sealed class HealthStore(DataDirectory dataDirectory, LocalClock clock, IOptions<DashboardOptions> options)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
    };

    private readonly SemaphoreSlim _gate = new(1, 1);
    private string FilePath => Path.Combine(dataDirectory.Root, "health.json");

    public bool IsPerson(string? id) => options.Value.People.Any(p => p.Id == id);

    public async Task<HealthData> GetAsync(CancellationToken ct)
    {
        await _gate.WaitAsync(ct);
        try
        {
            return ToData(await LoadAsync(ct));
        }
        finally
        {
            _gate.Release();
        }
    }

    // ---------- Weight ----------

    /// <summary>Records a weigh-in; a second one on the same day for the same person replaces the first.</summary>
    public Task<(HealthData Data, bool Found)> SaveWeightAsync(SaveWeight r, CancellationToken ct) =>
        MutateAsync(s =>
        {
            s.Weights.RemoveAll(w => w.PersonId == r.PersonId && w.Date == r.Date);
            s.Weights.Add(new WeightEntry(Guid.NewGuid(), r.PersonId, r.Date, Math.Round(r.Kg, 1), Clean(r.Note), WeightSource.Manual));
            return true;
        }, ct);

    public Task<(HealthData Data, bool Found)> DeleteWeightAsync(Guid id, CancellationToken ct) =>
        MutateAsync(s => s.Weights.RemoveAll(w => w.Id == id) > 0, ct);

    public Task<(HealthData Data, bool Found)> SaveProfileAsync(string personId, SaveProfile r, CancellationToken ct) =>
        MutateAsync(s =>
        {
            s.Profiles.RemoveAll(p => p.PersonId == personId);
            s.Profiles.Add(new HealthProfile(personId, r.HeightCm, r.StartWeightKg, r.GoalWeightKg));
            return true;
        }, ct);

    // ---------- Exams ----------

    public Task<(HealthData Data, bool Found)> CreateExamAsync(SaveExam r, CancellationToken ct) =>
        MutateAsync(s =>
        {
            var exam = NewExam(Guid.NewGuid(), r, clock.Now);
            s.Exams.Add(exam);
            BumpCheckups(s, exam);
            return true;
        }, ct);

    public Task<(HealthData Data, bool Found)> UpdateExamAsync(Guid id, SaveExam r, CancellationToken ct) =>
        MutateAsync(s =>
        {
            var i = s.Exams.FindIndex(e => e.Id == id);
            if (i < 0) return false;
            s.Exams[i] = NewExam(id, r, s.Exams[i].CreatedAt);
            BumpCheckups(s, s.Exams[i]);
            return true;
        }, ct);

    public Task<(HealthData Data, bool Found)> DeleteExamAsync(Guid id, CancellationToken ct) =>
        MutateAsync(s => s.Exams.RemoveAll(e => e.Id == id) > 0, ct);

    // ---------- Checkups ----------

    public Task<(HealthData Data, bool Found)> CreateCheckupAsync(SaveCheckup r, CancellationToken ct) =>
        MutateAsync(s =>
        {
            s.Checkups.Add(NewCheckup(Guid.NewGuid(), r));
            return true;
        }, ct);

    public Task<(HealthData Data, bool Found)> UpdateCheckupAsync(Guid id, SaveCheckup r, CancellationToken ct) =>
        MutateAsync(s =>
        {
            var i = s.Checkups.FindIndex(c => c.Id == id);
            if (i < 0) return false;
            s.Checkups[i] = NewCheckup(id, r);
            return true;
        }, ct);

    public Task<(HealthData Data, bool Found)> MarkCheckupDoneAsync(Guid id, DateOnly date, CancellationToken ct) =>
        MutateAsync(s =>
        {
            var i = s.Checkups.FindIndex(c => c.Id == id);
            if (i < 0) return false;
            s.Checkups[i] = s.Checkups[i] with { LastDone = date };
            return true;
        }, ct);

    public Task<(HealthData Data, bool Found)> DeleteCheckupAsync(Guid id, CancellationToken ct) =>
        MutateAsync(s => s.Checkups.RemoveAll(c => c.Id == id) > 0, ct);

    /// <summary>
    /// Saving an exam counts as doing the matching checkup: same person and the same name ("Dentista"),
    /// or any blood panel for a blood-panel checkup. Only moves LastDone forward.
    /// </summary>
    private static void BumpCheckups(State s, Exam exam)
    {
        for (var i = 0; i < s.Checkups.Count; i++)
        {
            var c = s.Checkups[i];
            var matches = c.PersonId == exam.PersonId
                && (string.Equals(c.Name, exam.Title, StringComparison.CurrentCultureIgnoreCase)
                    || (c.Kind == ExamKind.Sangue && exam.Kind == ExamKind.Sangue));
            if (matches && (c.LastDone is null || c.LastDone < exam.Date)) s.Checkups[i] = c with { LastDone = exam.Date };
        }
    }

    // ---------- Storage ----------

    private async Task<(HealthData Data, bool Found)> MutateAsync(Func<State, bool> change, CancellationToken ct)
    {
        await _gate.WaitAsync(ct);
        try
        {
            var state = await LoadAsync(ct);
            var found = change(state);
            if (found) await SaveAsync(state, ct);
            return (ToData(state), found);
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task SaveAsync(State state, CancellationToken ct)
    {
        var tmp = FilePath + ".tmp";
        var file = new HealthFile(1, state.Profiles, state.Weights, state.Exams, state.Checkups);
        await File.WriteAllTextAsync(tmp, JsonSerializer.Serialize(file, Json), ct);
        File.Move(tmp, FilePath, overwrite: true);
    }

    private async Task<State> LoadAsync(CancellationToken ct)
    {
        if (!File.Exists(FilePath))
        {
            var seeded = Samples();
            await SaveAsync(seeded, ct);
            return seeded;
        }
        var file = JsonSerializer.Deserialize<HealthFile>(await File.ReadAllTextAsync(FilePath, ct), Json);
        return new State(
            file?.Profiles?.ToList() ?? [],
            file?.Weights?.ToList() ?? [],
            file?.Exams?.Select(e => e with { Results = e.Results ?? [] }).ToList() ?? [],
            file?.Checkups?.ToList() ?? []);
    }

    /// <summary>Sorted for display (newest weights and exams first), with a profile for every configured person.</summary>
    private HealthData ToData(State s)
    {
        var profiles = options.Value.People
            .Select(p => s.Profiles.FirstOrDefault(x => x.PersonId == p.Id) ?? new HealthProfile(p.Id, null, null, null))
            .ToList();
        return new HealthData(
            profiles,
            s.Weights.OrderBy(w => w.Date).ToList(),
            s.Exams.OrderByDescending(e => e.Date).ThenByDescending(e => e.CreatedAt).ToList(),
            s.Checkups.OrderBy(c => c.Name, StringComparer.CurrentCulture).ToList(),
            Markers.All,
            clock.Now);
    }

    private static Exam NewExam(Guid id, SaveExam r, DateTimeOffset createdAt) =>
        new(id, r.PersonId, r.Date, r.Kind, r.Title.Trim(), Clean(r.Provider), r.Outcome, Clean(r.Notes), r.Results ?? [], createdAt);

    private static Checkup NewCheckup(Guid id, SaveCheckup r) => new(id, r.PersonId, r.Name.Trim(), r.Kind, r.EveryMonths, r.LastDone);

    private static string? Clean(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();

    // ---------- Example data ----------

    private State Samples()
    {
        var today = clock.Today;
        var now = clock.Now;
        var ids = options.Value.People.Select(p => p.Id).ToArray();
        if (ids.Length == 0) return new State([], [], [], []);
        var a = ids[0];
        var b = ids.Length > 1 ? ids[1] : ids[0];
        DateOnly Ago(int days) => today.AddDays(-days);

        var profiles = new List<HealthProfile>
        {
            new(a, 180, 94.6m, 82),
            new(b, 165, 70.1m, 60),
        };

        // Four months of weigh-ins trending down, with day-to-day noise and the odd missed day.
        var random = new Random(42);
        var weights = new List<WeightEntry>();
        const int days = 120;
        foreach (var (person, from, to) in new[] { (a, 94.6, 88.4), (b, 70.1, 66.1) })
        {
            for (var d = days; d >= 1; d--)
            {
                if (random.NextDouble() < 0.1) continue;
                var trend = from + (to - from) * (days - d) / (days - 1.0);
                var kg = Math.Round((decimal)(trend + (random.NextDouble() - 0.5) * 0.8), 1);
                weights.Add(new WeightEntry(Guid.NewGuid(), person, Ago(d), kg, null, d % 3 == 0 ? WeightSource.Foto : WeightSource.Manual));
            }
        }

        static IReadOnlyList<ExamResult> Results(params (string Code, decimal Value)[] values) =>
            values.Select(v => new ExamResult(v.Code, v.Value, null, null)).ToList();

        Exam Panel(string person, int ago, string provider, params (string, decimal)[] values) =>
            new(Guid.NewGuid(), person, Ago(ago), ExamKind.Sangue, "Check-up de sangue", provider, null, null, Results(values), now.AddDays(-ago));

        Exam Other(string person, int ago, ExamKind kind, string title, ExamOutcome? outcome, string? notes, params (string, decimal)[] values) =>
            new(Guid.NewGuid(), person, Ago(ago), kind, title, null, outcome, notes, Results(values), now.AddDays(-ago));

        var exams = new List<Exam>
        {
            Panel(a, 410, "Laboratório Sabin",
                ("glicose", 104), ("hba1c", 5.8m), ("colesterol", 228), ("ldl", 168), ("hdl", 38), ("triglicerideos", 210),
                ("tgo", 34), ("tgp", 52), ("ggt", 58), ("creatinina", 1.0m), ("ureia", 34), ("acido-urico", 7.6m),
                ("tsh", 2.1m), ("hemoglobina", 15.4m), ("vitamina-d", 28), ("pcr", 4.1m)),
            Panel(a, 230, "Laboratório Sabin",
                ("glicose", 103), ("hba1c", 5.7m), ("colesterol", 215), ("ldl", 158), ("hdl", 41), ("triglicerideos", 182),
                ("tgo", 31), ("tgp", 46), ("ggt", 51), ("creatinina", 1.0m), ("acido-urico", 7.3m), ("vitamina-d", 34), ("pcr", 3.2m)),
            Panel(a, 48, "Laboratório Sabin",
                ("glicose", 101), ("hba1c", 5.6m), ("insulina", 14.2m), ("colesterol", 205), ("ldl", 148), ("hdl", 43), ("triglicerideos", 149),
                ("tgo", 28), ("tgp", 38), ("ggt", 44), ("creatinina", 0.98m), ("ureia", 31), ("acido-urico", 6.9m),
                ("tsh", 2.3m), ("t4-livre", 1.2m), ("hemoglobina", 15.1m), ("leucocitos", 6800), ("plaquetas", 238000),
                ("vitamina-d", 31), ("vitamina-b12", 420), ("ferritina", 190), ("pcr", 2.4m)),
            Panel(b, 380, "Unilabs",
                ("glicose", 86), ("hba1c", 5.2m), ("colesterol", 182), ("ldl", 104), ("hdl", 58), ("triglicerideos", 98),
                ("tgo", 22), ("tgp", 19), ("creatinina", 0.8m), ("tsh", 2.8m), ("hemoglobina", 12.9m), ("vitamina-d", 26), ("ferritina", 32)),
            Panel(b, 95, "Unilabs",
                ("glicose", 84), ("hba1c", 5.1m), ("colesterol", 176), ("ldl", 98), ("hdl", 61), ("triglicerideos", 90),
                ("tgo", 21), ("tgp", 18), ("ggt", 17), ("creatinina", 0.8m), ("ureia", 26), ("tsh", 3.1m), ("t4-livre", 1.3m),
                ("hemoglobina", 12.6m), ("leucocitos", 5900), ("plaquetas", 262000),
                ("vitamina-d", 24), ("vitamina-b12", 380), ("ferritina", 28), ("ferro", 75)),

            Other(a, 230, ExamKind.Imagem, "Ultrassom abdominal", ExamOutcome.Alterado, "Esteatose hepática leve. Repetir em 1 ano."),
            Other(a, 48, ExamKind.Cardio, "Eletrocardiograma", ExamOutcome.Normal, "Ritmo sinusal, sem alterações."),
            Other(a, 48, ExamKind.Medidas, "Pressão no consultório", null, null,
                ("pressao-sistolica", 134), ("pressao-diastolica", 86), ("frequencia-cardiaca", 74)),
            Other(a, 12, ExamKind.Medidas, "Pressão em casa", null, "Média de 3 medições de manhã.",
                ("pressao-sistolica", 127), ("pressao-diastolica", 83), ("frequencia-cardiaca", 68)),
            Other(b, 95, ExamKind.Medidas, "Pressão no consultório", null, null,
                ("pressao-sistolica", 112), ("pressao-diastolica", 72), ("frequencia-cardiaca", 66)),
            Other(b, 88, ExamKind.Consulta, "Oftalmologista", ExamOutcome.Normal, "Grau mantido."),
            Other(a, 220, ExamKind.Consulta, "Dentista", ExamOutcome.Normal, "Limpeza."),
            Other(b, 220, ExamKind.Consulta, "Dentista", ExamOutcome.Normal, "Limpeza."),
        };

        var checkups = new List<Checkup>
        {
            new(Guid.NewGuid(), a, "Check-up de sangue", ExamKind.Sangue, 12, Ago(48)),
            new(Guid.NewGuid(), b, "Check-up de sangue", ExamKind.Sangue, 12, Ago(95)),
            new(Guid.NewGuid(), a, "Dentista", ExamKind.Consulta, 6, Ago(220)),
            new(Guid.NewGuid(), b, "Dentista", ExamKind.Consulta, 6, Ago(220)),
            new(Guid.NewGuid(), a, "Ultrassom abdominal", ExamKind.Imagem, 12, Ago(230)),
            new(Guid.NewGuid(), b, "Ginecologista", ExamKind.Consulta, 12, Ago(345)),
            new(Guid.NewGuid(), b, "Oftalmologista", ExamKind.Consulta, 24, Ago(88)),
        };

        return new State(profiles, weights, exams, checkups);
    }

    private sealed record State(List<HealthProfile> Profiles, List<WeightEntry> Weights, List<Exam> Exams, List<Checkup> Checkups);

    private sealed record HealthFile(
        int Version,
        IReadOnlyList<HealthProfile>? Profiles,
        IReadOnlyList<WeightEntry>? Weights,
        IReadOnlyList<Exam>? Exams,
        IReadOnlyList<Checkup>? Checkups);
}
