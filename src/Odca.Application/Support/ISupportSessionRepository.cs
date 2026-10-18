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

/// <summary>Resultado do início de uma impersonação profunda na janela de suporte (D-OC43/F-1).</summary>
public enum SupportImpersonationStartError
{
    None,
    /// <summary>Validação rejeitada pelo SQL (janela ausente/inativa, alvo sem membership ativa, janela curta demais — 02000/22023).</summary>
    Validation,
    /// <summary>A sessão já tem uma impersonação ativa (23505).</summary>
    AlreadyActive,
    /// <summary>Falha de infraestrutura.</summary>
    Unavailable
}

public sealed record SupportImpersonationStart(
    bool Ok,
    SupportImpersonationStartError Error,
    Guid GrantId,
    Guid TargetUserId,
    Guid TargetSessionId,
    DateTimeOffset ExpiresAt);

public sealed record SupportSessionMemberInfo(Guid UserId, string DisplayName, string Email);

public interface ISupportSessionRepository
{
    Task<SupportSessionListResult> ListRecentAsync(Guid actorId, int limit, CancellationToken cancellationToken);
    Task<SupportSessionInfo?> GetAsync(Guid actorId, Guid sessionId, CancellationToken cancellationToken);
    Task<(Guid Id, SupportSessionOpenError Error)> OpenAsync(
        Guid actorId, Guid tenantId, string reason, int durationMinutes, CancellationToken cancellationToken);
    Task<DateTimeOffset?> ExtendAsync(Guid actorId, Guid sessionId, int minutes, CancellationToken cancellationToken);
    Task<bool> CloseAsync(Guid actorId, Guid sessionId, bool revoke, CancellationToken cancellationToken);
    Task<IReadOnlyList<SupportSessionEvent>> EventsAsync(Guid sessionId, CancellationToken cancellationToken);

    // D-OC43 (Sprint 9/F-1): impersonação profunda na janela de suporte — o alvo vira a
    // identidade efetiva da porta dos atos; o operador real fica nas concessões/eventos.
    Task<SupportImpersonationStart> StartImpersonationAsync(
        Guid actorId, Guid sessionId, Guid targetUserId, int minutes, CancellationToken cancellationToken);
    Task<bool> EndImpersonationAsync(Guid actorId, Guid grantId, bool revoked, CancellationToken cancellationToken);
    /// <summary>A concessão está ativa E a janela de suporte está viva (validação por request).</summary>
    Task<bool> IsImpersonationValidAsync(Guid grantId, CancellationToken cancellationToken);
    Task<IReadOnlyList<SupportSessionMemberInfo>> ListMembersAsync(Guid actorId, Guid tenantId, CancellationToken cancellationToken);
}
