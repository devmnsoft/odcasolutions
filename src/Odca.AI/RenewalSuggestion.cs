namespace Odca.AI;

/// <summary>
/// D-OC53 (Sprint 19/F-C): sugestão de renovação derivada da última análise persistida.
/// Mapeamento determinístico: risco geral high OU achado penalty/price_change em medium/high
/// ⇒ renegotiate; medium ⇒ review; low ⇒ renew. Sem análise disponível ⇒ null (o cartão não
/// existe; nenhum veredito é inventado). A decisão continua sendo do usuário.
/// </summary>
public sealed record AiRenewalSuggestion(string Action, string BasisRisk);

public static class RenewalSuggestionBuilder
{
    public const string Renew = "renew";
    public const string Review = "review";
    public const string Renegotiate = "renegotiate";

    public static AiRenewalSuggestion? FromAnalysis(AiAnalysis? analysis)
    {
        if (analysis is null || !analysis.Available) return null;
        var risk = analysis.OverallRisk?.Trim().ToLowerInvariant();
        if (risk is not ("low" or "medium" or "high")) return null;

        var escalation = analysis.Findings.Any(f =>
            f.Code is "penalty" or "price_change" &&
            (f.Risk?.Trim().ToLowerInvariant() is "medium" or "high"));

        return risk == "high" || escalation
            ? new AiRenewalSuggestion(Renegotiate, risk)
            : risk == "medium"
                ? new AiRenewalSuggestion(Review, risk)
                : new AiRenewalSuggestion(Renew, risk);
    }
}
