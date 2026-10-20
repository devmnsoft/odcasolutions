namespace Odca.AI;

/// <summary>
/// Delta determinístico entre as cláusulas presentes na análise e as cláusulas padrão.
/// "Template padrão" = as 8 cláusulas declaradas em <see cref="AiResponseParser.ClauseCodes"/>
/// (fonte única existente; nenhum outro catálogo de cláusulas foi inventado — D-OC53/F-B).
/// </summary>
public sealed record AiTemplateComparison(
    IReadOnlyList<string> Expected,
    IReadOnlyList<string> Detected,
    IReadOnlyList<string> Missing,
    IReadOnlyList<string> Unexpected);

public static class TemplateComparisonBuilder
{
    public static AiTemplateComparison Build(AiAnalysis analysis)
    {
        var expected = AiResponseParser.ClauseCodes;
        var detected = new List<string>();
        var unexpected = new List<string>();

        foreach (var finding in analysis?.Findings ?? [])
        {
            var code = finding.Code?.Trim();
            if (string.IsNullOrEmpty(code)) continue;
            if (Array.IndexOf(expected, code) >= 0)
            {
                if (!detected.Contains(code)) detected.Add(code);
            }
            else if (!unexpected.Contains(code))
            {
                unexpected.Add(code);
            }
        }

        var missing = expected.Where(e => !detected.Contains(e)).ToArray();
        return new AiTemplateComparison(expected, detected, missing, unexpected);
    }
}
