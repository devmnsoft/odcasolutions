using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace Odca.Application.Contracts;

public sealed record StudioChange(string Category, string Reference, string? Before, string? After);
public sealed record StudioChecklistItem(string Severity, string Code, string Message, string? Reference);

/// <summary>Deterministic, bounded analysis over canonical structured snapshots.</summary>
public static class ContractStudioAnalysis
{
    public const int MaximumSnapshotBytes = 2_000_000;
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true, Converters = { new JsonStringEnumConverter() } };

    public static IReadOnlyList<StudioChange> Compare(string beforeContent, string beforeFields, string beforeValues,
        string afterContent, string afterFields, string afterValues)
    {
        EnsureBounded(beforeContent, beforeFields, beforeValues, afterContent, afterFields, afterValues);
        var changes = new List<StudioChange>();
        CompareNodes(JsonNode.Parse(beforeContent), JsonNode.Parse(afterContent), "document", changes);
        CompareObjects(beforeValues, afterValues, "fieldId", "field", changes);
        CompareObjects(beforeFields, afterFields, "id", "metadata", changes);
        return changes;
    }

    public static IReadOnlyList<StudioChecklistItem> Checklist(string content, string fields, string values,
        bool isSaved, bool hasConflict, int openComments)
    {
        EnsureBounded(content, fields, values);
        var result = new List<StudioChecklistItem>();
        if (!isSaved) result.Add(new("blocker", "draft.unsaved", "Salve o documento antes de encaminhar.", null));
        if (hasConflict) result.Add(new("blocker", "draft.conflict", "Resolva o conflito de edição antes de encaminhar.", null));
        var definitions = JsonSerializer.Deserialize<ContractFieldDefinition[]>(fields, JsonOptions) ?? [];
        var fieldValues = JsonSerializer.Deserialize<ContractFieldValue[]>(values, JsonOptions) ?? [];
        try { StructuredContractDocument.ValidateValues(definitions, fieldValues, true); }
        catch (InvalidDataException exception) { result.Add(new("blocker", "fields.invalid", exception.Message, "fields")); }
        if (openComments > 0) result.Add(new("warning", "comments.open", $"Há {openComments} comentário(s) aberto(s).", "comments"));
        foreach (var value in fieldValues.Where(x => string.Equals(x.Source, "manual", StringComparison.OrdinalIgnoreCase)))
            result.Add(new("warning", "field.manual", "Campo preenchido manualmente; confira a origem.", $"field:{value.FieldId}"));
        return result;
    }

    private static void CompareObjects(string before, string after, string keyName, string category, List<StudioChange> changes)
    {
        var left = (JsonNode.Parse(before) as JsonArray ?? []).OfType<JsonObject>().Where(x => x[keyName] is not null).ToDictionary(x => x[keyName]!.GetValue<string>(), x => x.ToJsonString(), StringComparer.Ordinal);
        var right = (JsonNode.Parse(after) as JsonArray ?? []).OfType<JsonObject>().Where(x => x[keyName] is not null).ToDictionary(x => x[keyName]!.GetValue<string>(), x => x.ToJsonString(), StringComparer.Ordinal);
        foreach (var key in left.Keys.Union(right.Keys, StringComparer.Ordinal).Order(StringComparer.Ordinal))
        {
            left.TryGetValue(key, out var oldValue);
            right.TryGetValue(key, out var newValue);
            if (!string.Equals(oldValue, newValue, StringComparison.Ordinal))
                changes.Add(new(category, $"{category}:{key}", oldValue, newValue));
        }
    }

    private static void CompareNodes(JsonNode? before, JsonNode? after, string path, List<StudioChange> changes)
    {
        if (changes.Count >= 10_000) throw new InvalidDataException("A comparação excede o limite de 10.000 alterações.");
        if (before is JsonObject left && after is JsonObject right)
        {
            var type = right["type"]?.GetValue<string>() ?? left["type"]?.GetValue<string>() ?? "node";
            if (type == "text" && left["text"]?.ToJsonString() != right["text"]?.ToJsonString()) changes.Add(new("text", path, left["text"]?.GetValue<string>(), right["text"]?.GetValue<string>()));
            if (type == "text" && left["marks"]?.ToJsonString() != right["marks"]?.ToJsonString()) changes.Add(new("formatting", path, left["marks"]?.ToJsonString(), right["marks"]?.ToJsonString()));
            var la = left["content"] as JsonArray; var ra = right["content"] as JsonArray;
            if (la is not null || ra is not null) for (var i = 0; i < Math.Max(la?.Count ?? 0, ra?.Count ?? 0); i++) CompareNodes(i < (la?.Count ?? 0) ? la![i] : null, i < (ra?.Count ?? 0) ? ra![i] : null, $"{path}/{type}:{i}", changes);
            return;
        }
        if (before?.ToJsonString() != after?.ToJsonString()) changes.Add(new(before is null ? "text-added" : "text-removed", path, before?.ToJsonString(), after?.ToJsonString()));
    }

    private static void EnsureBounded(params string[] documents)
    {
        if (documents.Any(x => x.Length > MaximumSnapshotBytes)) throw new InvalidDataException("Documento grande demais para processamento síncrono.");
    }
}
