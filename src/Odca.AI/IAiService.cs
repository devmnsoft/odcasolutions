namespace Odca.AI;

/// <summary>Entrada da análise: texto já extraído (camada de extração/OCR), tamanho total e cultura-alvo da resposta.</summary>
public sealed record AiContractInput(string Text, int TotalChars, string Culture);

/// <summary>Cláusula detectada. Code é um identificador estável (a UI resolve rótulos por catálogo); Risk ∈ low|medium|high.</summary>
public sealed record AiClauseFinding(string Code, string Risk, string Evidence);

/// <summary>
/// Resultado da análise com degradação honesta: qualquer falha de provedor vira
/// Available=false com Reason estável — a interface nunca inventa conteúdo nem silencia o problema.
/// </summary>
public sealed record AiAnalysis(
    bool Available,
    string? Reason,
    string? Summary,
    string? OverallRisk,
    IReadOnlyList<AiClauseFinding> Findings,
    IReadOnlyList<string> Categories,
    string ProviderName)
{
    public static AiAnalysis Unavailable(string providerName) => new(false, "provider_unavailable", null, null, [], [], providerName);
}

public interface IAiService
{
    Task<AiAnalysis> AnalyzeContractAsync(AiContractInput input, CancellationToken ct);

    /// <summary>
    /// D-OC53 (Sprint 19/F-D): interpreta uma frase de busca em pistas estruturadas para a
    /// central de documentos. Provedor ausente ⇒ <see cref="SearchQueryParser.FromText"/>
    /// (fallback determinístico); qualquer falha degrada ao mesmo fallback — nunca lança.
    /// </summary>
    Task<AiInterpretation> InterpretSearchQueryAsync(string query, CancellationToken ct);
}

/// <summary>Provedor padrão (configuração ausente ou Provider=none): análise indisponível, sem efeito colateral.</summary>
public sealed class NullAiService : IAiService
{
    public Task<AiAnalysis> AnalyzeContractAsync(AiContractInput input, CancellationToken ct) => Task.FromResult(AiAnalysis.Unavailable("none"));

    public Task<AiInterpretation> InterpretSearchQueryAsync(string query, CancellationToken ct) => Task.FromResult(new AiInterpretation(SearchQueryParser.FromText(query), false));
}
