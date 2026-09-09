namespace Odca.Application.Plans;

public interface IPlanCatalogRepository
{
    Task<IReadOnlyList<PlanCatalogItem>> ListPublishedAsync(CancellationToken cancellationToken);

    Task<PlanCatalogSelection?> FindPublishedAsync(string code, CancellationToken cancellationToken);
}

public sealed record PlanCatalogSelection(Guid Id, PlanCatalogItem Plan);
