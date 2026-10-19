namespace Odca.AI;

/// <summary>
/// D-OC52 (Sprint 18): seção "Ai" da configuração. Provedor configurável por operador:
/// "none" (padrão — fallback honesto na interface) ou "http" (qualquer endpoint compatível
/// com a API de chat em /chat/completions). A chave de API nunca é commitada: o valor em
/// desenvolvimento vem de development-runtime.json (ignorado pelo Git) ou de variáveis de
/// ambiente (Ai__ApiKey). Sem Endpoint/ApiKey válidos o registro cai em "none" por construção.
/// </summary>
public sealed class AiOptions
{
    public const string Section = "Ai";

    public string? Provider { get; init; }

    /// <summary>Base absoluta do endpoint compatível (ex.: https://api.openai.com/v1).</summary>
    public string? Endpoint { get; init; }

    public string? ApiKey { get; init; }

    /// <summary>Nome do modelo reportado nas respostas (exibição honesta para o usuário).</summary>
    public string? Model { get; init; }

    public int TimeoutSeconds { get; init; } = 20;

    /// <summary>Limite de caracteres de texto enviado ao provedor (o restante é ignorado e declarado).</summary>
    public int MaxInputChars { get; init; } = 24_000;
}
