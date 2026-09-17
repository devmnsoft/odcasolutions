using Odca.Contracts.Operations;

namespace Odca.Application.Operations;

public sealed class ContractSheetService(
    IContractSheetRepository repository,
    IOperationalInboxRepository calendar)
{
    public async Task<ContractSheetDto?> GetAsync(
        Guid tenantId,
        Guid contractId,
        Guid viewerId,
        bool canReadTenant,
        CancellationToken cancellationToken)
    {
        ArgumentOutOfRangeException.ThrowIfEqual(tenantId, Guid.Empty);
        ArgumentOutOfRangeException.ThrowIfEqual(contractId, Guid.Empty);
        ArgumentOutOfRangeException.ThrowIfEqual(viewerId, Guid.Empty);

        var today = (await calendar.ReadCalendarAsync(tenantId, cancellationToken)).Today;
        return await repository.GetAsync(tenantId, contractId, viewerId, canReadTenant, today, cancellationToken);
    }
}
