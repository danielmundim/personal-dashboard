namespace Dashboard.Api.Features.Health;

/// <summary>
/// The markers the Saúde tab knows, grouped by area, with usual adult reference ranges (as printed by
/// most Brazilian labs). A one-sided range leaves the other bound null, e.g. LDL only has an upper one.
/// These are defaults: a result can carry its lab's own range, which wins.
/// </summary>
public static class Markers
{
    public static readonly IReadOnlyList<Marker> All =
    [
        new("glicose", "Glicose em jejum", "Metabolismo", "mg/dL", 70, 99),
        new("hba1c", "Hemoglobina glicada", "Metabolismo", "%", null, 5.6m),
        new("insulina", "Insulina", "Metabolismo", "µUI/mL", 2.6m, 24.9m),

        new("colesterol", "Colesterol total", "Lipídios", "mg/dL", null, 190),
        new("ldl", "LDL", "Lipídios", "mg/dL", null, 130),
        new("hdl", "HDL", "Lipídios", "mg/dL", 40, null),
        new("triglicerideos", "Triglicerídeos", "Lipídios", "mg/dL", null, 150),

        new("tgo", "TGO (AST)", "Fígado", "U/L", null, 40),
        new("tgp", "TGP (ALT)", "Fígado", "U/L", null, 41),
        new("ggt", "Gama GT", "Fígado", "U/L", null, 60),

        new("creatinina", "Creatinina", "Rins", "mg/dL", 0.6m, 1.3m),
        new("ureia", "Ureia", "Rins", "mg/dL", 15, 45),
        new("acido-urico", "Ácido úrico", "Rins", "mg/dL", 2.5m, 7.2m),

        new("tsh", "TSH", "Tireoide", "mUI/L", 0.4m, 4.0m),
        new("t4-livre", "T4 livre", "Tireoide", "ng/dL", 0.9m, 1.8m),

        new("hemoglobina", "Hemoglobina", "Hemograma", "g/dL", 12, 17),
        new("leucocitos", "Leucócitos", "Hemograma", "/mm³", 4000, 11000),
        new("plaquetas", "Plaquetas", "Hemograma", "/mm³", 150000, 450000),

        new("vitamina-d", "Vitamina D", "Vitaminas", "ng/mL", 30, 100),
        new("vitamina-b12", "Vitamina B12", "Vitaminas", "pg/mL", 200, 900),
        new("ferritina", "Ferritina", "Vitaminas", "ng/mL", 30, 300),
        new("ferro", "Ferro", "Vitaminas", "µg/dL", 60, 170),

        new("pcr", "Proteína C reativa", "Inflamação", "mg/L", null, 5),

        new("pressao-sistolica", "Pressão sistólica", "Pressão", "mmHg", 90, 129),
        new("pressao-diastolica", "Pressão diastólica", "Pressão", "mmHg", 60, 84),
        new("frequencia-cardiaca", "Frequência cardíaca em repouso", "Pressão", "bpm", 50, 100),
    ];

    private static readonly HashSet<string> Codes = All.Select(m => m.Code).ToHashSet(StringComparer.Ordinal);

    public static bool Exists(string code) => Codes.Contains(code);
}
