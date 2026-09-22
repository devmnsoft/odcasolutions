namespace Odca.Application.Operations;

public sealed record OperationalInboxRow(
    OperationalWorkKind Kind,
    Guid SourceId,
    Guid TenantId,
    Guid ContractId,
    string ContractTitle,
    string Title,
    Guid? OwnerId,
    string? OwnerName,
    DateOnly? DueOn,
    string Status,
    long? Version);

public sealed record TenantCalendarContext(string TimeZoneId, DateOnly Today);

public interface IOperationalInboxRepository
{
    Task<TenantCalendarContext> ReadCalendarAsync(Guid tenantId, CancellationToken cancellationToken);

    Task<IReadOnlyList<OperationalInboxRow>> ListCandidatesAsync(
        Guid tenantId,
        Guid viewerId,
        bool canReadTenantObligations,
        bool canReadTenantReviews,
        bool canReadTenantRenewals,
        Guid? contractId,
        Guid? ownerId,
        DateOnly today,
        DateOnly renewalWindowEnd,
        CancellationToken cancellationToken);
}

public interface IMonthlyAgendaRepository
{
    Task<IReadOnlyList<OperationalInboxRow>> ListWindowAsync(
        Guid tenantId,
        Guid viewerId,
        bool canReadTenant,
        Guid? ownerId,
        DateOnly windowStart,
        DateOnly windowEnd,
        CancellationToken cancellationToken);
}

public interface IContractSheetRepository
{
    Task<Odca.Contracts.Operations.ContractSheetDto?> GetAsync(
        Guid tenantId,
        Guid contractId,
        Guid viewerId,
        bool canReadTenant,
        DateOnly today,
        CancellationToken cancellationToken);
}
