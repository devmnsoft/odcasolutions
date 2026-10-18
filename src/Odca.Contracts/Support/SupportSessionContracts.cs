namespace Odca.Contracts.Support;

public sealed record OpenSupportSessionRequest(Guid TenantId, string Reason, int DurationMinutes);

public sealed record ExtendSupportSessionRequest(int Minutes);

// D-OC43 (Sprint 9/F-1): impersonação profunda na janela de suporte.
public sealed record StartSupportImpersonationRequest(Guid TargetUserId, int Minutes);

public sealed record EndSupportImpersonationRequest(bool Revoked);

public sealed record StartSupportImpersonationResponse(Guid GrantId, DateTimeOffset ExpiresAt, string AccessToken);

public sealed record SupportSessionMemberResponse(Guid UserId, string DisplayName, string Email);

public sealed record SupportSessionResponse(
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

public sealed record SupportSessionEventResponse(
    Guid SessionId,
    string ActorName,
    string EventType,
    DateTimeOffset OccurredAt,
    string Details);
