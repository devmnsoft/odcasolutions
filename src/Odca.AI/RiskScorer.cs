namespace Odca.AI;

/// <summary>Fator que compôs o score de risco: código estável, severidade declarada e peso somado.</summary>
public sealed record AiRiskFactor(string Code, string Severity, int Weight);

/// <summary>Score de risco 0–100 com os fatores que o compõem (explicável e reprodutível).</summary>
public sealed record AiRiskScore(int Value, IReadOnlyList<AiRiskFactor> Factors);

/// <summary>
/// D-OC53 (Sprint 19/F-A): score de risco por contrato com fórmula DETERMINÍSTICA — a IA
/// descreve, o código decide o número. Mesma entrada produz o mesmo resultado independentemente
/// do provedor: base pelo risco geral (low=20, medium=45, high=70), acréscimo por achado
/// (low +2, medium +6, high +12) e +3 por código de cláusula fora das 8 padrão (atípica).
/// Truncado a [0,100]. O score apoia a revisão humana: nunca bloqueia fluxo nem cria obrigação.
/// </summary>
public static class RiskScorer
{
    public const string AtypicalClauseCode = "atypical_clause";

    private static readonly Dictionary<string, int> BaseByOverall = new(StringComparer.OrdinalIgnoreCase)
    {
        ["low"] = 20,
        ["medium"] = 45,
        ["high"] = 70,
    };

    private static readonly Dictionary<string, int> WeightBySeverity = new(StringComparer.OrdinalIgnoreCase)
    {
        ["low"] = 2,
        ["medium"] = 6,
        ["high"] = 12,
    };

    public static AiRiskScore Score(AiAnalysis analysis)
    {
        if (analysis is null || !analysis.Available) return new AiRiskScore(0, []);

        var factors = new List<AiRiskFactor>();
        var value = 0;

        var overall = analysis.OverallRisk?.Trim();
        if (!string.IsNullOrEmpty(overall) && BaseByOverall.TryGetValue(overall, out var baseValue))
        {
            value += baseValue;
            factors.Add(new AiRiskFactor("overall_" + overall.ToLowerInvariant(), overall.ToLowerInvariant(), baseValue));
        }

        foreach (var finding in analysis.Findings)
        {
            var severity = (finding.Risk ?? string.Empty).Trim().ToLowerInvariant();
            if (!WeightBySeverity.TryGetValue(severity, out var weight)) continue;
            var code = string.IsNullOrWhiteSpace(finding.Code) ? "unknown" : finding.Code.Trim();
            value += weight;
            factors.Add(new AiRiskFactor(code, severity, weight));
        }

        foreach (var atypical in analysis.Findings
                     .Select(f => f.Code?.Trim())
                     .Where(c => !string.IsNullOrEmpty(c) && Array.IndexOf(AiResponseParser.ClauseCodes, c!) < 0)
                     .Distinct())
        {
            value += 3;
            factors.Add(new AiRiskFactor(AtypicalClauseCode, "info", 3));
        }

        return new AiRiskScore(Math.Clamp(value, 0, 100), factors);
    }
}
