namespace Odca.Contracts.Support;

public sealed record OpenSupportSessionRequest(Guid TenantId, string Reason, int DurationMinutes);

public sealed record ExtendSupportSessionRequest(int Minutes);

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
