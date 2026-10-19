namespace Odca.Contracts.Ai;

// D-OC52 (Sprint 18): análise IA de contrato por versão exata do documento (assinatura↔versão).
// Códigos e níveis de risco são identificadores estáveis; os rótulos são resolvidos na cultura
// do usuário pela camada de apresentação (catálogo i18n, fallback para o valor cru).
public sealed record AiClauseFindingDto(string Code, string Risk, string Evidence);

public sealed record AiAnalysisResponse(
    bool Available,
    string? Reason,
    string? Summary,
    string? OverallRisk,
    AiClauseFindingDto[] Findings,
    string[] Categories,
    string ProviderName,
    string? ContentHash,
    int AnalyzedChars);
