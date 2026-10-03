using System.Text.Json;

namespace Odca.Contracts.Administration;

public sealed record PlatformAuditEvent(
    long Id,
    string ScopeType,
    Guid? TenantId,
    string? TenantName,
    Guid? ActorUserId,
    string? ActorName,
    string Action,
    string EntityType,
    Guid? EntityId,
    DateTimeOffset OccurredAt,
    string Result,
    JsonElement Metadata);

public sealed record PlatformAuditPageResponse(
    IReadOnlyList<PlatformAuditEvent> Items,
    int Page,
    int PageSize,
    int Total);
