using Odca.Contracts.Administration;

namespace Odca.Application.Administration;

public enum OrganizationFeatureAccess
{
    Ok,
    Forbidden,
    Missing,
    Invalid
}

public sealed record OrganizationFeatureList(
    OrganizationFeatureAccess Access,
    OrganizationFeatureCatalogResponse? Catalog);

public sealed record OrganizationFeatureMutation(
    OrganizationFeatureAccess Access,
    OrganizationFeatureStatus? Feature);

public interface IOrganizationFeatureRepository
{
    Task<OrganizationFeatureList> ListAsync(Guid actorId, Guid tenantId, CancellationToken cancellationToken);

    Task<OrganizationFeatureMutation> SetAsync(
        Guid actorId,
        Guid tenantId,
        string featureCode,
        bool blocked,
        string reason,
        CancellationToken cancellationToken);
}
