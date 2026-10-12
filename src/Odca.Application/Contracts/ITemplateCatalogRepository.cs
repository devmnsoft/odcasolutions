using Odca.Contracts.Studio;

namespace Odca.Application.Contracts;

public interface ITemplateCatalogRepository
{
    Task<TemplateCatalogPage> ListAsync(
        Guid tenantId,
        string? search,
        string? contractType,
        string? scope,
        string? documentPurpose,
        int page,
        int pageSize,
        CancellationToken cancellationToken);
}
