using Odca.Contracts.Administration;

namespace Odca.Application.Administration;

public enum PlatformAuditAccess
{
    Ok,
    Forbidden
}

public sealed record PlatformAuditPage(
    PlatformAuditAccess Access,
    PlatformAuditPageResponse? Page);

public interface IPlatformAuditRepository
{
    Task<PlatformAuditPage> GetPageAsync(
        Guid actorId,
        string? search,
        Guid? tenantId,
        int page,
        int pageSize,
        CancellationToken cancellationToken);
}
