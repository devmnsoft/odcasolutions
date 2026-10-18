namespace Odca.Application.Support;

/// <summary>Resultado da abertura de uma sessão de suporte (D-OC39/F-2).</summary>
public enum SupportSessionOpenError
{
    None,
    /// <summary>Validação rejeitada (duração/motivo fora dos limites do SQL ou tenant inexistente).</summary>
    Validation,
    /// <summary>Já existe sessão ativa do mesmo superadministrador para o tenant (23505).</summary>
    AlreadyActive,
    /// <summary>Falha de infraestrutura.</summary>
    Unavailable
}

public sealed record SupportSessionInfo(
    Guid Id,
    Guid TenantId,
    string TenantName,
    Guid SuperadminUserId,
    string SuperadminName,
    string Reason,
    string Status,
    bool Valid,
    DateTimeOffset StartedAt,
    DateTimeOffset ExpiresAt,
    DateTimeOffset? EndedAt);

public sealed record SupportSessionEvent(
    Guid SessionId,
    Guid ActorUserId,
    string ActorName,
    string EventType,
    DateTimeOffset OccurredAt,
    string Details);

public sealed record SupportSessionListResult(bool Ok, IReadOnlyList<SupportSessionInfo> Sessions);

public interface ISupportSessionRepository
{
    Task<SupportSessionListResult> ListRecentAsync(Guid actorId, int limit, CancellationToken cancellationToken);
    Task<SupportSessionInfo?> GetAsync(Guid actorId, Guid sessionId, CancellationToken cancellationToken);
    Task<(Guid Id, SupportSessionOpenError Error)> OpenAsync(
        Guid actorId, Guid tenantId, string reason, int durationMinutes, CancellationToken cancellationToken);
    Task<DateTimeOffset?> ExtendAsync(Guid actorId, Guid sessionId, int minutes, CancellationToken cancellationToken);
    Task<bool> CloseAsync(Guid actorId, Guid sessionId, bool revoke, CancellationToken cancellationToken);
    Task<IReadOnlyList<SupportSessionEvent>> EventsAsync(Guid sessionId, CancellationToken cancellationToken);
}
