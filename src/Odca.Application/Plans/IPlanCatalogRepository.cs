namespace Odca.Application.Plans;

public interface IPlanCatalogRepository
{
    Task<IReadOnlyList<PlanCatalogItem>> ListPublishedAsync(CancellationToken cancellationToken);
}
