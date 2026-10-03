using Odca.Contracts.Administration;

namespace Odca.Web.Models;

public sealed record PlatformAuditTenantOption(Guid TenantId, string Name);

public sealed record PlatformAuditListViewModel(
    PlatformAuditPageResponse Page,
    string? Search,
    Guid? TenantId,
    int PageSize,
    IReadOnlyList<PlatformAuditTenantOption> TenantOptions,
    bool TenantOptionsFailed);
