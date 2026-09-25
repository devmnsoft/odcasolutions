using Odca.Contracts.Consumption;

namespace Odca.Application.Consumption;

public interface IConsumptionRepository
{
    Task<ConsumptionSummary?> GetSummaryAsync(Guid actorId, Guid tenantId, bool platformAccess, CancellationToken cancellationToken);
    Task<IReadOnlyList<StoragePackage>> ListPackagesAsync(CancellationToken cancellationToken);
    Task<IReadOnlyList<AdditionalStorageRequest>> ListRequestsAsync(Guid actorId, Guid tenantId, bool platformAccess, CancellationToken cancellationToken);
    Task<AdditionalStorageRequest?> RequestStorageAsync(Guid actorId, Guid tenantId, CreateStorageRequest request, CancellationToken cancellationToken);
    Task<bool> DecideRequestAsync(Guid actorId, Guid tenantId, Guid requestId, DecideStorageRequest request, CancellationToken cancellationToken);
    Task<bool> GrantStorageAsync(Guid actorId, Guid tenantId, ManualStorageGrant request, CancellationToken cancellationToken);
    Task<bool> ChangeOrganizationStatusAsync(Guid actorId, Guid tenantId, bool restore, string reason, CancellationToken cancellationToken);
    Task<IReadOnlyList<PlatformCustomer>> ListCustomersAsync(Guid actorId, string? search, CancellationToken cancellationToken);
}
