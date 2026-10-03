using System.Globalization;
using System.Collections.ObjectModel;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using Odca.Application.Onboarding;

namespace Odca.Application.Contracts;

public enum ContractFieldType { ShortText, LongText, Date, Number, Currency, BrazilianDocument, Choice }

/// <summary>
/// Completeness level enforced over field values.
/// Structural only rejects malformed values that are present;
/// Complete also requires required fields to be filled;
/// Confirmed also requires every required value to be explicitly confirmed.
/// </summary>
public enum ContractValidationMode { Structural, Complete, Confirmed }

public sealed record ContractFieldDefinition(
    string Id,
    string Label,
    ContractFieldType Type,
    bool Required,
    IReadOnlyList<string>? Choices = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Origin = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? RequiredWhenFieldId = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] IReadOnlyList<string>? RequiredWhenAnyOf = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? SourceProperty = null);

public sealed record ContractFieldValue(string FieldId, string? Value, bool Confirmed, string? Source = null);

/// <summary>
/// Validates the canonical contract format. The JSON tree is the sole source of truth;
/// HTML is generated only at presentation/export boundaries.
/// </summary>
public sealed class StructuredContractDocument
{
    private static readonly HashSet<string> ContainerNodes = new(StringComparer.Ordinal)
        { "document", "paragraph", "heading", "bulletList", "orderedList", "listItem", "table", "tableRow", "tableCell", "pageBreak" };
    private static readonly HashSet<string> LeafNodes = new(StringComparer.Ordinal) { "text", "field" };
    private static readonly HashSet<string> Marks = new(StringComparer.Ordinal) { "bold", "italic", "underline" };
    private static readonly Regex SafeId = new("^[a-zA-Z][a-zA-Z0-9_.-]{0,79}$", RegexOptions.CultureInvariant);

    private StructuredContractDocument(string canonicalJson, IReadOnlyDictionary<string, int> occurrences)
    {
        CanonicalJson = canonicalJson;
        FieldOccurrences = occurrences;
    }

    public string CanonicalJson { get; }
    public IReadOnlyDictionary<string, int> FieldOccurrences { get; }

    public static StructuredContractDocument Parse(string json, IReadOnlyCollection<ContractFieldDefinition> fields)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(json);
        if (json.Length > 2_000_000) throw new InvalidDataException("O documento excede o limite de 2 MB.");

        JsonNode root;
        try { root = JsonNode.Parse(json, documentOptions: new() { MaxDepth = 64 })!; }
        catch (JsonException exception) { throw new InvalidDataException("O conteúdo estruturado é inválido.", exception); }

        if (fields.Any(x => string.IsNullOrWhiteSpace(x.Id) || !SafeId.IsMatch(x.Id)) ||
            fields.Select(x => x.Id).Distinct(StringComparer.Ordinal).Count() != fields.Count)
        {
            throw new InvalidDataException("Os campos precisam de identificadores únicos e estáveis.");
        }

        var definitions = fields.ToDictionary(x => x.Id, StringComparer.Ordinal);

        var occurrences = new Dictionary<string, int>(StringComparer.Ordinal);
        ValidateNode(root, definitions, occurrences, isRoot: true);
        return new(root.ToJsonString(new JsonSerializerOptions { WriteIndented = false, Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping }), occurrences);
    }

    public static void ValidateValues(
        IReadOnlyCollection<ContractFieldDefinition> definitions,
        IReadOnlyCollection<ContractFieldValue> values,
        ContractValidationMode mode)
    {
        if (values.Any(x => string.IsNullOrWhiteSpace(x.FieldId)))
            throw new InvalidDataException("Não é permitido valor com identificador de campo ausente.");

        Dictionary<string, ContractFieldValue> byId;
        try { byId = values.ToDictionary(x => x.FieldId, StringComparer.Ordinal); }
        catch (ArgumentException exception) { throw new InvalidDataException("Cada campo deve possuir somente um valor confirmado.", exception); }
        if (byId.Keys.Except(definitions.Select(x => x.Id), StringComparer.Ordinal).Any())
            throw new InvalidDataException("Foi informado valor para um campo que não pertence ao documento.");
        var presenceRequired = mode is not ContractValidationMode.Structural;
        foreach (var definition in definitions)
        {
            byId.TryGetValue(definition.Id, out var field);
            var value = field?.Value?.Trim();
            var active = IsRequirementActive(definition, byId);
            if (definition.Required && active && presenceRequired &&
                (string.IsNullOrEmpty(value) || (mode == ContractValidationMode.Confirmed && field?.Confirmed != true)))
                throw new InvalidDataException($"O campo obrigatório '{definition.Label}' está pendente.");
            if (string.IsNullOrEmpty(value)) continue;

            var valid = definition.Type switch
            {
                ContractFieldType.Date => DateOnly.TryParseExact(value, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out _),
                ContractFieldType.Number => CanonicalDecimal.IsStoredNumber(value),
                ContractFieldType.Currency => CanonicalDecimal.IsStoredCurrency(value),
                ContractFieldType.BrazilianDocument => BrazilianDocument.NormalizeAndValidate(value).Type != "invalid",
                ContractFieldType.Choice => definition.Choices?.Contains(value, StringComparer.Ordinal) == true,
                ContractFieldType.ShortText => value.Length <= 300,
                ContractFieldType.LongText => value.Length <= 20_000,
                _ => false
            };
            if (!valid) throw new InvalidDataException($"O valor de '{definition.Label}' é inválido.");
        }
    }

    public static string NormalizeStoredValues(string fieldsJson, string valuesJson)
    {
        var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true, PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
        options.Converters.Add(new JsonStringEnumConverter());
        var definitions = JsonSerializer.Deserialize<ContractFieldDefinition[]>(fieldsJson, options) ?? [];
        var values = JsonSerializer.Deserialize<ContractFieldValue[]>(valuesJson, options) ?? [];
        var byId = definitions.ToDictionary(item => item.Id, StringComparer.Ordinal);
        var normalized = values.Select(value =>
        {
            if (string.IsNullOrWhiteSpace(value.Value) || !byId.TryGetValue(value.FieldId, out var definition))
                return value;
            if (definition.Type is not (ContractFieldType.Currency or ContractFieldType.Number))
                return value;
            var parsed = CanonicalDecimal.Parse(value.Value, definition.Type == ContractFieldType.Currency);
            if (!parsed.Succeeded)
                throw new InvalidDataException($"O valor de '{definition.Label}' é inválido. {parsed.Error}");
            return value with { Value = parsed.Canonical };
        }).ToArray();
        return JsonSerializer.Serialize(normalized, options);
    }

    private static bool IsRequirementActive(ContractFieldDefinition definition, Dictionary<string, ContractFieldValue> values)
    {
        if (string.IsNullOrWhiteSpace(definition.RequiredWhenFieldId))
            return true;
        values.TryGetValue(definition.RequiredWhenFieldId, out var controller);
        var current = controller?.Value?.Trim() ?? string.Empty;
        return definition.RequiredWhenAnyOf?.Contains(current, StringComparer.Ordinal) == true;
    }

    private static void ValidateNode(JsonNode node, IReadOnlyDictionary<string, ContractFieldDefinition> fields,
        Dictionary<string, int> occurrences, bool isRoot = false)
    {
        if (node is not JsonObject item) throw new InvalidDataException("Cada nó deve ser um objeto.");
        var type = item["type"]?.GetValue<string>() ?? throw new InvalidDataException("Nó sem tipo.");
        if (isRoot && type != "document") throw new InvalidDataException("A raiz deve ser um documento.");
        if (!ContainerNodes.Contains(type) && !LeafNodes.Contains(type)) throw new InvalidDataException($"Elemento não suportado: {type}.");

        var allowed = type switch
        {
            "text" => new[] { "type", "text", "marks" },
            "field" => new[] { "type", "fieldId" },
            "heading" => new[] { "type", "level", "content" },
            "paragraph" => new[] { "type", "alignment", "content" },
            _ => new[] { "type", "content" }
        };
        if (item.Any(property => !allowed.Contains(property.Key, StringComparer.Ordinal)))
            throw new InvalidDataException("O documento contém atributo não suportado.");

        if (type == "pageBreak") return;
        if (type == "text")
        {
            var text = item["text"]?.GetValue<string>() ?? string.Empty;
            if (text.Length > 100_000) throw new InvalidDataException("Bloco de texto muito extenso.");
            if (item["marks"] is JsonArray marks && marks.Any(x => x is null || !Marks.Contains(x.GetValue<string>())))
                throw new InvalidDataException("Formatação não suportada.");
            return;
        }
        if (type == "field")
        {
            var id = item["fieldId"]?.GetValue<string>() ?? string.Empty;
            if (!fields.ContainsKey(id)) throw new InvalidDataException("Ocorrência aponta para campo inexistente.");
            occurrences[id] = occurrences.GetValueOrDefault(id) + 1;
            return;
        }
        if (type == "heading" && item["level"]?.GetValue<int>() is not (1 or 2 or 3))
            throw new InvalidDataException("O nível do título deve estar entre 1 e 3.");
        if (type == "paragraph" && item["alignment"] is JsonValue alignment &&
            alignment.GetValue<string>() is not ("left" or "center" or "right" or "justify"))
            throw new InvalidDataException("Alinhamento não suportado.");
        if (item["content"] is not JsonArray content) throw new InvalidDataException("Contêiner sem conteúdo.");
        foreach (var child in content) ValidateNode(child ?? throw new InvalidDataException("Nó vazio."), fields, occurrences);
    }

}

public sealed record PublishedContractVersion(int Number, string Content, IReadOnlyList<ContractFieldValue> Values, DateTimeOffset PublishedAt);

public sealed class ContractDraft
{
    private readonly List<PublishedContractVersion> versions = [];
    private readonly ReadOnlyCollection<PublishedContractVersion> readOnlyVersions;
    public ContractDraft(StructuredContractDocument content)
    {
        Content = content;
        readOnlyVersions = versions.AsReadOnly();
    }
    public StructuredContractDocument Content { get; private set; }
    public long Revision { get; private set; }
    public IReadOnlyList<PublishedContractVersion> Versions => readOnlyVersions;

    public void Save(long expectedRevision, StructuredContractDocument content)
    {
        if (expectedRevision != Revision) throw new ContractRevisionConflictException(expectedRevision, Revision);
        Content = content;
        Revision++;
    }

    public PublishedContractVersion Publish(IReadOnlyCollection<ContractFieldDefinition> definitions,
        IReadOnlyCollection<ContractFieldValue> values, DateTimeOffset now)
    {
        StructuredContractDocument.ValidateValues(definitions, values, ContractValidationMode.Confirmed);
        var version = new PublishedContractVersion(
            versions.Count + 1,
            Content.CanonicalJson,
            Array.AsReadOnly(values.ToArray()),
            now);
        versions.Add(version);
        return version;
    }

    public void Restore(int versionNumber, IReadOnlyCollection<ContractFieldDefinition> definitions)
    {
        var snapshot = versions.SingleOrDefault(x => x.Number == versionNumber)
            ?? throw new KeyNotFoundException("Versão não encontrada.");
        Content = StructuredContractDocument.Parse(snapshot.Content, definitions);
        Revision++;
    }
}

public sealed class ContractRevisionConflictException(long expected, long actual)
    : Exception($"Conflito de edição: revisão esperada {expected}, revisão atual {actual}.")
{
    public long Expected { get; } = expected;
    public long Actual { get; } = actual;
}
