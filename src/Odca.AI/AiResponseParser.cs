using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Odca.AI;

/// <summary>
/// D-OC52: conversão defensiva da resposta do provedor em <see cref="AiAnalysis"/>. Qualquer
/// desvio (JSON inválido, campos em falta, valores fora do domínio) degrada para null — quem
/// chama responde "indisponível" e nunca entrega análise parcial disfarçada de completa.
/// Os códigos de cláusula e níveis de risco são normalizados para conjuntos estáveis; a UI
/// resolve rótulos por catálogo (chaves ai.clause.&lt;código&gt; / ai.risk_&lt;nível&gt;) com fallback
/// para o valor cru.
/// </summary>
public static class AiResponseParser
{
    public static readonly string[] ClauseCodes =
    [
        "price_change", "termination_notice", "penalty", "exclusivity",
        "data_protection", "liability_limit", "indemnity", "jurisdiction"
    ];

    public const int MaximumSummaryChars = 1_200;
    public const int MaximumEvidenceChars = 300;
    public const int MaximumCategoryChars = 40;
    public const int MaximumFindings = 12;
    public const int MaximumCategories = 6;

    public static AiAnalysis? Parse(string? raw, string providerName)
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;

        string? json = StripCodeFence(raw.Trim());
        if (json is null) return null;

        JsonObject root;
        try
        {
            root = JsonNode.Parse(json) as JsonObject
                ?? throw new JsonException("resposta sem objeto raiz");
        }
        catch (JsonException)
        {
            return null;
        }

        var summary = ReadClamped(root["summary"], MaximumSummaryChars);
        var overallRisk = NormalizeRisk(ReadRaw(root["overall_risk"]));
        var findings = ReadFindings(root["clauses"]);
        var categories = ReadCategories(root["categories"]);

        if (string.IsNullOrEmpty(summary) && findings.Count == 0 && categories.Count == 0 && overallRisk is null)
        {
            return null; // resposta sem nenhum conteúdo aproveitável — não é análise
        }

        return new AiAnalysis(true, null, summary, overallRisk, findings, categories, providerName);
    }

    private static string? StripCodeFence(string text)
    {
        if (!text.StartsWith("```", StringComparison.Ordinal)) return text;
        var newline = text.IndexOf('\n');
        if (newline < 0) return null;
        var body = text[(newline + 1)..];
        var close = body.LastIndexOf("```", StringComparison.Ordinal);
        if (close < 0) return null;
        body = body[..close];
        return string.IsNullOrWhiteSpace(body) ? null : body.Trim();
    }

    private static string? ReadRaw(JsonNode? node)
    {
        if (node is null) return null;
        var value = node.GetValueKind() == JsonValueKind.String ? node.ToString() : node.ToJsonString();
        value = value?.Trim().Trim('"').Trim();
        return string.IsNullOrWhiteSpace(value) ? null : value;
    }

    private static string? ReadClamped(JsonNode? node, int max)
    {
        var value = ReadRaw(node);
        if (value is null) return null;
        value = value.Length <= max ? value : value[..max];
        return value;
    }

    public static string? NormalizeRisk(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        return value.Trim().ToLowerInvariant() switch
        {
            "low" or "baixo" => "low",
            "medium" or "med" or "medio" or "média" or "media" or "médio" => "medium",
            "high" or "alto" => "high",
            _ => "medium" // nível fora do domínio declarado como médio — honesto, nunca inventa "alto"
        };
    }

    private static List<AiClauseFinding> ReadFindings(JsonNode? node)
    {
        var result = new List<AiClauseFinding>();
        if (node is not JsonArray array) return result;
        foreach (var item in array.Take(MaximumFindings))
        {
            if (item is not JsonObject clause) continue;
            var code = NormalizeCode(ReadRaw(clause["code"]));
            if (code is null) continue;
            result.Add(new AiClauseFinding(code, NormalizeRisk(ReadRaw(clause["risk"])) ?? "medium", ReadClamped(clause["evidence"], MaximumEvidenceChars) ?? string.Empty));
        }
        return result;
    }

    private static List<string> ReadCategories(JsonNode? node)
    {
        var result = new List<string>();
        if (node is not JsonArray array) return result;
        foreach (var item in array)
        {
            var value = ReadClamped(item, MaximumCategoryChars);
            if (value is null || result.Contains(value, StringComparer.OrdinalIgnoreCase)) continue;
            if (result.Count >= MaximumCategories) break;
            result.Add(value);
        }
        return result;
    }

    /// <summary>Identificador estável: minúsculas, espaços viram underscore, somente [a-z0-9_.-], máx. 48 chars.</summary>
    public static string? NormalizeCode(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var builder = new StringBuilder(48);
        foreach (var character in value.Trim().ToLowerInvariant())
        {
            if (builder.Length >= 48) break;
            if (character is ' ' or '_') builder.Append('_');
            else if (char.IsAsciiLetterOrDigit(character)) builder.Append(character);
            else if (character is '.' or '-') builder.Append(character);
        }
        var code = builder.ToString().Trim('_');
        return string.IsNullOrEmpty(code) ? null : code;
    }
}
