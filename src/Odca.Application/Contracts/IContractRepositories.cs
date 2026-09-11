using Odca.Application.Tenancy;

namespace Odca.Application.Contracts;

public interface ICounterpartyRepository
{
    Task<QueryAccess<TenantPage<CounterpartyRecord>>> ListAsync(
        Guid actorId,
        Guid tenantId,
        string? search,
        string? status,
        int page,
        int pageSize,
        CancellationToken cancellationToken);

    Task<QueryAccess<CounterpartyRecord?>> GetAsync(
        Guid actorId,
        Guid tenantId,
        Guid id,
        CancellationToken cancellationToken);

    Task<MutationResult<CounterpartyRecord>> CreateAsync(
        Guid actorId,
        Guid tenantId,
        CounterpartyWriteModel model,
        CancellationToken cancellationToken);

    Task<MutationResult<CounterpartyRecord>> UpdateAsync(
        Guid actorId,
        Guid tenantId,
        Guid id,
        long version,
        CounterpartyWriteModel model,
        CancellationToken cancellationToken);

    Task<MutationResult> InactivateAsync(
        Guid actorId,
        Guid tenantId,
        Guid id,
        long version,
        CancellationToken cancellationToken);
}

public interface IContractTypeRepository
{
    Task<QueryAccess<IReadOnlyList<ContractTypeRecord>>> ListAsync(
        Guid actorId,
        Guid tenantId,
        string? status,
        CancellationToken cancellationToken);

    Task<QueryAccess<ContractTypeRecord?>> GetAsync(
        Guid actorId,
        Guid tenantId,
        Guid id,
        CancellationToken cancellationToken);

    Task<MutationResult<ContractTypeRecord>> CreateAsync(
        Guid actorId,
        Guid tenantId,
        ContractTypeWriteModel model,
        CancellationToken cancellationToken);

    Task<MutationResult<ContractTypeRecord>> UpdateAsync(
        Guid actorId,
        Guid tenantId,
        Guid id,
        long version,
        ContractTypeWriteModel model,
        CancellationToken cancellationToken);

    Task<MutationResult> SetStatusAsync(
        Guid actorId,
        Guid tenantId,
        Guid id,
        long version,
        string status,
        CancellationToken cancellationToken);
}

public interface IContractRepository
{
    Task<QueryAccess<TenantPage<ContractListItem>>> ListAsync(
        Guid actorId,
        Guid tenantId,
        ContractListFilter filter,
        int page,
        int pageSize,
        CancellationToken cancellationToken);

    Task<QueryAccess<ContractDetail?>> GetAsync(
        Guid actorId,
        Guid tenantId,
        Guid id,
        DateOnly referenceDate,
        CancellationToken cancellationToken);

    Task<QueryAccess<ContractOverviewMetrics>> OverviewAsync(
        Guid actorId,
        Guid tenantId,
        DateOnly referenceDate,
        CancellationToken cancellationToken);

    Task<MutationResult<ContractDetail>> CreateDraftAsync(
        Guid actorId,
        Guid tenantId,
        ContractWriteModel model,
        DateOnly referenceDate,
        CancellationToken cancellationToken);

    Task<MutationResult<ContractDetail>> UpdateAsync(
        Guid actorId,
        Guid tenantId,
        Guid id,
        long version,
        ContractWriteModel model,
        DateOnly referenceDate,
        CancellationToken cancellationToken);

    Task<MutationResult> ActivateAsync(
        Guid actorId,
        Guid tenantId,
        Guid id,
        long version,
        CancellationToken cancellationToken);

    Task<MutationResult> RenewAsync(
        Guid actorId,
        Guid tenantId,
        Guid id,
        long version,
        DateOnly newEndDate,
        string? reason,
        CancellationToken cancellationToken);

    Task<MutationResult> CloseAsync(
        Guid actorId,
        Guid tenantId,
        Guid id,
        long version,
        CancellationToken cancellationToken);

    Task<MutationResult> CancelAsync(
        Guid actorId,
        Guid tenantId,
        Guid id,
        long version,
        CancellationToken cancellationToken);

    Task<MutationResult> SoftDeleteAsync(
        Guid actorId,
        Guid tenantId,
        Guid id,
        long version,
        string reason,
        CancellationToken cancellationToken);

    Task<MutationResult> RestoreAsync(
        Guid actorId,
        Guid tenantId,
        Guid id,
        long version,
        CancellationToken cancellationToken);
}

public interface IUserNotificationRepository
{
    Task<QueryAccess<TenantPage<UserNotificationRecord>>> ListAsync(
        Guid actorId,
        Guid tenantId,
        bool unreadOnly,
        int page,
        int pageSize,
        CancellationToken cancellationToken);

    Task<QueryAccess<int>> UnreadCountAsync(
        Guid actorId,
        Guid tenantId,
        CancellationToken cancellationToken);

    Task<QueryAccess<UserNotificationRecord?>> GetAsync(
        Guid actorId,
        Guid tenantId,
        Guid id,
        CancellationToken cancellationToken);

    Task<MutationResult> MarkReadAsync(
        Guid actorId,
        Guid tenantId,
        Guid id,
        CancellationToken cancellationToken);
}

public interface IContractAlertRepository
{
    Task EvaluateDueAlertsAsync(CancellationToken cancellationToken);
}
