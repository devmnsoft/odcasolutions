namespace Odca.Application.Plans;

public interface IPlanCatalogRepository
{
    Task<IReadOnlyList<PlanCatalogItem>> ListPublishedAsync(CancellationToken cancellationToken);

    Task<PlanCatalogSelection?> FindPublishedAsync(string code, CancellationToken cancellationToken);

    Task<PlanChangePreview?> PreviewOrganizationPlanChangeAsync(
        Guid actorId,
        Guid tenantId,
        string planCode,
        CancellationToken cancellationToken);

    Task<string> ApplyOrganizationPlanChangeAsync(
        Guid actorId,
        Guid tenantId,
        string planCode,
        string justification,
        CancellationToken cancellationToken);
}

public sealed record PlanChangePreview(
    string CurrentCode,
    int CurrentVersion,
    Guid CurrentPlanVersionId,
    string TargetCode,
    int TargetVersion,
    Guid TargetPlanVersionId,
    int ActiveMembers,
    int ReservedInvitations,
    long CurrentSeatLimit,
    long TargetSeatLimit,
    bool SeatsOverLimit,
    long PatientCount,
    long DocumentCount,
    string Policy);

public sealed record PlanCatalogSelection(Guid Id, PlanCatalogItem Plan);
