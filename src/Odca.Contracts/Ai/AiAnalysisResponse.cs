namespace Odca.Contracts.Ai;

// D-OC52 (Sprint 18): análise IA de contrato por versão exata do documento (assinatura↔versão).
// Códigos e níveis de risco são identificadores estáveis; os rótulos são resolvidos na cultura
// do usuário pela camada de apresentação (catálogo i18n, fallback para o valor cru).
public sealed record AiClauseFindingDto(string Code, string Risk, string Evidence);

// D-OC53 (Sprint 19): score 0–100 com fatores explicáveis + delta contra as cláusulas padrão.
// Identificadores estáveis; a camada de apresentação resolve rótulos e linguagem para leigos.
public sealed record AiRiskFactorDto(string Code, string Severity, int Weight);

public sealed record AiRiskScoreDto(int Value, AiRiskFactorDto[] Factors);

public sealed record AiTemplateComparisonDto(
    string[] Expected,
    string[] Detected,
    string[] Missing,
    string[] Unexpected);

public sealed record AiAnalysisResponse(
    bool Available,
    string? Reason,
    string? Summary,
    string? OverallRisk,
    AiClauseFindingDto[] Findings,
    string[] Categories,
    string ProviderName,
    string? ContentHash,
    int AnalyzedChars,
    AiRiskScoreDto? Score = null,
    AiTemplateComparisonDto? TemplateComparison = null);
