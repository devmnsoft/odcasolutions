using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Odca.AI;

/// <summary>
/// D-OC53 (Sprint 19/F-D): pistas de busca natural da central de documentos.
/// Terms alimentam ILIKE sobre o texto extraído/identificadores; PenaltyMinPercent filtra
/// ocorrências de "multa … X %" com comparação numérica no SQL (limitação declarada: a
/// formulação canônica, precisão de uma casa decimal).
/// </summary>
public sealed record AiQueryHints(IReadOnlyList<string> Terms, double? PenaltyMinPercent);

/// <summary>
/// Resultado da interpretação de busca: <see cref="Hints"/> + origem honesta.
/// ProviderUsed=true só quando o provedor interpretou a frase (o modo exibido é "ai");
/// qualquer queda interna degrada ao fallback determinístico com ProviderUsed=false ("keywords").
/// </summary>
public sealed record AiInterpretation(AiQueryHints Hints, bool ProviderUsed);

public static class SearchQueryParser
{
    public const int MaxTerms = 8;
    private const int MinTermLength = 3;
    private static readonly Regex PenaltyPattern = new(
        @"\b(multa|penalidade|multas|penalidades)\b[^0-9]{0,40}([0-9]+)(?:[.,]([0-9]))?\s*%",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    /// <summary>
    /// Fallback determinístico (provedor "none", falha do provedor ou resposta sem conteúdo):
    /// tokens alfanuméricos ≥ 3 + extração determinística de percentual de multa. Nunca lança.
    /// </summary>
    public static AiQueryHints FromText(string query)
    {
        if (string.IsNullOrWhiteSpace(query)) return new AiQueryHints([], null);

        var terms = query
            .ToLowerInvariant()
            .Split([' ', '\t', '\n', '\r', ',', ';', '.', '!', '?', '(', ')', '[', ']', '{', '}', '"', '\'', '+', '&', ':'],
                StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(t => t.Length >= MinTermLength && t.All(char.IsLetterOrDigit))
            .GroupBy(t => t, StringComparer.Ordinal)
            .Select(g => g.First())
            .Take(MaxTerms)
            .ToArray();

        double? penalty = TryReadPenaltyPercent(query, out var value) ? value : null;
        return new AiQueryHints(terms, penalty);
    }

    internal static bool TryReadPenaltyPercent(string text, out double value)
    {
        foreach (Match match in PenaltyPattern.Matches(text ?? string.Empty))
        {
            if (!double.TryParse(match.Groups[2].Value, System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out var whole))
            {
                continue;
            }
            var fractional = match.Groups[3].Success ? double.Parse(match.Groups[3].Value, System.Globalization.CultureInfo.InvariantCulture) / 10.0 : 0.0;
            value = whole + fractional;
            return true;
        }
        value = 0;
        return false;
    }

    /// <summary>
    /// Parser defensivo do JSON do provedor: {"terms":["..."],"penaltyMinPercent":10}.
    /// Qualquer desvio de forma (root não-object, termos não-string fora do teto, percentual
    /// fora de 0–100) degrada para null — o chamador usa <see cref="FromText"/>.
    /// </summary>
    public static AiQueryHints? ParseProviderJson(string content)
    {
        try
        {
            using var document = JsonDocument.Parse(StripCodeFence(content));
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object) return null;

            var terms = new List<string>();
            if (root.TryGetProperty("terms", out var termsElement) && termsElement.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in termsElement.EnumerateArray())
                {
                    if (item.ValueKind != JsonValueKind.String) continue;
                    var term = item.GetString()?.Trim().ToLowerInvariant();
                    if (string.IsNullOrEmpty(term) || term.Length > 60) continue;
                    if (!terms.Contains(term)) terms.Add(term);
                    if (terms.Count >= MaxTerms) break;
                }
            }

            double? penalty = null;
            if (root.TryGetProperty("penaltyMinPercent", out var penaltyElement) &&
                penaltyElement.ValueKind == JsonValueKind.Number &&
                penaltyElement.TryGetDouble(out var raw))
            {
                penalty = raw is >= 0 and <= 100 ? Math.Round(raw, 1) : null;
            }

            if (terms.Count == 0 && penalty is null) return null;
            return new AiQueryHints(terms, penalty);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static string StripCodeFence(string content)
    {
        if (string.IsNullOrWhiteSpace(content)) return string.Empty;
        var trimmed = content.Trim();
        if (trimmed.StartsWith("```", StringComparison.Ordinal))
        {
            var firstNewLine = trimmed.IndexOf('\n');
            var lastFence = trimmed.LastIndexOf("```", StringComparison.Ordinal);
            if (firstNewLine >= 0 && lastFence > firstNewLine)
            {
                trimmed = trimmed[(firstNewLine + 1)..lastFence];
            }
        }
        return trimmed;
    }
}
