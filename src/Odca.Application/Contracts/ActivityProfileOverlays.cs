using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Odca.Application.Contracts;

/// <summary>
/// B.3.4 (D1): per-profile field layers applied once, at new-draft creation, over the
/// chosen official template. The therapy-clinic layer adds explicit monthly session
/// quantities and a computed mensalidade total (Σ sessions × fee) to the
/// multiple-therapies template, anchored on the investment clause. The overlay is
/// idempotent: templates that already carry the fields, or that have no anchor (e.g.
/// an NDA draft from a therapy-clinic tenant), pass through untouched.
/// </summary>
public static class ActivityProfileOverlays
{
    public const string General = "general";
    public const string TherapyClinic = "therapy_clinic";
    public const string PlasticSurgery = "plastic_surgery";

    private static readonly (string TherapyFieldId, string SessionsFieldId, string FeeFieldId, string SessionsLabel)[] Therapies =
    [
        ("therapy_neuropsychology", "sessions_neuropsychology", "fee_neuropsychology", "Sessões mensais — Neuropsicologia"),
        ("therapy_psychology", "sessions_psychology", "fee_psychology", "Sessões mensais — Psicologia"),
        ("therapy_occupational", "sessions_occupational_therapy", "fee_occupational_therapy", "Sessões mensais — Terapia Ocupacional"),
        ("therapy_speech", "sessions_speech_therapy", "fee_speech_therapy", "Sessões mensais — Fonoaudiologia")
    ];

    /// <summary>
    /// Returns the content/fields pair that must seed the new draft for this
    /// (official template, activity profile) combination. Inputs are returned
    /// unchanged whenever the profile has no layer for the template.
    /// </summary>
    public static (string Content, string Fields) Apply(string? officialKey, string? activityProfile, string contentJson, string fieldsJson)
    {
        if (!string.Equals(activityProfile, TherapyClinic, StringComparison.Ordinal) ||
            !string.Equals(officialKey, "multiple-therapies", StringComparison.Ordinal))
        {
            return (contentJson, fieldsJson);
        }

        var definitions = DeserializeFields(fieldsJson);
        // Idempotent: a template that already ships the layer keeps its own shape.
        if (definitions.Any(x => string.Equals(x.Id, "monthly_fee_total", StringComparison.Ordinal)))
        {
            return (contentJson, fieldsJson);
        }

        var root = JsonNode.Parse(contentJson) as JsonObject;
        if (root?["content"] is not JsonArray blocks)
        {
            return (contentJson, fieldsJson);
        }

        var anchor = -1;
        for (var index = 0; index < blocks.Count; index++)
        {
            if (ContainsField(blocks[index], "fee_neuropsychology"))
            {
                anchor = index;
                break;
            }
        }
        if (anchor < 0)
        {
            return (contentJson, fieldsJson);
        }

        foreach (var therapy in Therapies)
        {
            definitions.Add(new ContractFieldDefinition(
                therapy.SessionsFieldId,
                therapy.SessionsLabel,
                ContractFieldType.Number,
                true,
                null,
                null,
                therapy.TherapyFieldId,
                ["Sim"],
                null,
                "Quantidade de sessões no mês para esta terapia (somente quando contratada)."));
        }
        definitions.Add(new ContractFieldDefinition(
            "monthly_fee_total",
            "Mensalidade total (cálculo explícito)",
            ContractFieldType.Formula,
            false,
            null,
            null,
            null,
            null,
            null,
            "Cálculo automático: Σ(sessões mensais × valor por sessão) de cada terapia contratada.",
            Therapies.Select(t => new ContractFormulaTerm(t.SessionsFieldId, t.FeeFieldId)).ToArray()));

        blocks.Insert(anchor + 1, SessionsParagraph());
        blocks.Insert(anchor + 2, TotalParagraph());

        var overlaidContent = root.ToJsonString(OverlayContentOptions);
        var overlaidFields = JsonSerializer.Serialize(definitions);
        return (overlaidContent, overlaidFields);
    }

    private static readonly JsonSerializerOptions OverlayContentOptions = new()
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    private static readonly JsonSerializerOptions OverlayFieldOptions = BuildFieldOptions();

    private static JsonSerializerOptions BuildFieldOptions()
    {
        var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
        options.Converters.Add(new System.Text.Json.Serialization.JsonStringEnumConverter());
        return options;
    }

    private static List<ContractFieldDefinition> DeserializeFields(string fieldsJson) =>
        JsonSerializer.Deserialize<List<ContractFieldDefinition>>(fieldsJson, OverlayFieldOptions) ?? [];

    private static bool ContainsField(JsonNode? node, string fieldId)
    {
        if (node is JsonObject obj)
        {
            if (string.Equals(obj["type"]?.GetValue<string>(), "field", StringComparison.Ordinal) &&
                string.Equals(obj["fieldId"]?.GetValue<string>(), fieldId, StringComparison.Ordinal))
            {
                return true;
            }
            foreach (var child in obj)
            {
                if (ContainsField(child.Value, fieldId)) return true;
            }
            return false;
        }
        if (node is JsonArray array)
        {
            foreach (var child in array)
            {
                if (ContainsField(child, fieldId)) return true;
            }
        }
        return false;
    }

    private static JsonObject TextNode(string text) => new() { ["type"] = "text", ["text"] = text };

    private static JsonObject FieldNode(string id) => new() { ["type"] = "field", ["fieldId"] = id };

    private static JsonObject ParagraphNode(params JsonNode[] nodes)
    {
        var content = new JsonArray();
        foreach (var node in nodes) content.Add(node);
        return new JsonObject { ["type"] = "paragraph", ["alignment"] = "justify", ["content"] = content };
    }

    private static JsonObject SessionsParagraph() => ParagraphNode(
        TextNode("CLÁUSULA 2ª-A — DA QUANTIDADE MENSAL DE SESSÕES (somente terapias contratadas): Neuropsicologia: "),
        FieldNode("sessions_neuropsychology"),
        TextNode("; Psicologia: "),
        FieldNode("sessions_psychology"),
        TextNode("; Terapia Ocupacional: "),
        FieldNode("sessions_occupational_therapy"),
        TextNode("; Fonoaudiologia: "),
        FieldNode("sessions_speech_therapy"),
        TextNode("."));

    private static JsonObject TotalParagraph() => ParagraphNode(
        TextNode("MENSALIDADE TOTAL (cálculo explícito): a soma das sessões mensais multiplicadas pelo valor por sessão de cada terapia contratada resulta em R$ "),
        FieldNode("monthly_fee_total"),
        TextNode(", com vencimento conforme a cláusula de forma e vencimento do pagamento."));
}
