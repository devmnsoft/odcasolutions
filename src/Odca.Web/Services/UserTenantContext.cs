using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Odca.Contracts.Tenancy;

namespace Odca.Web.Services;

public sealed class UserTenantContext(
    IHttpContextAccessor httpContextAccessor,
    OdcaApiClient apiClient) : IUserTenantContext
{
    private const string ContextItemsKey = "__UserTenantAccessList";

    public async Task<UserTenantAccess?> GetAccessAsync(Guid tenantId, CancellationToken cancellationToken = default)
    {
        var all = await GetAllAccessibleAsync(cancellationToken);
        return all.FirstOrDefault(x => x.TenantId == tenantId);
    }

    public async Task<IReadOnlyList<UserTenantAccess>> GetAllAccessibleAsync(CancellationToken cancellationToken = default)
    {
        var httpContext = httpContextAccessor.HttpContext;
        if (httpContext is null || httpContext.User.Identity?.IsAuthenticated != true)
        {
            return Array.Empty<UserTenantAccess>();
        }

        if (httpContext.Items.TryGetValue(ContextItemsKey, out var cached) &&
            cached is IReadOnlyList<UserTenantAccess> accessList)
        {
            return accessList;
        }

        var token = await httpContext.GetTokenAsync("access_token");
        if (string.IsNullOrWhiteSpace(token))
        {
            return Array.Empty<UserTenantAccess>();
        }

        var result = await apiClient.GetOrganizationsAsync(token, cancellationToken);
        if (!result.Succeeded || result.Value is null)
        {
            return Array.Empty<UserTenantAccess>();
        }

        var mapped = result.Value
            .Select(org => new UserTenantAccess(
                org.Id,
                org.Name,
                string.Equals(org.Status, "active", StringComparison.OrdinalIgnoreCase),
                new HashSet<string>(org.Permissions ?? Array.Empty<string>(), StringComparer.Ordinal)))
            .ToList()
            .AsReadOnly();

        httpContext.Items[ContextItemsKey] = mapped;
        return mapped;
    }

    public async Task<UserTenantAccess?> GetCurrentOrFirstActiveAsync(Guid? preferredTenantId = null, CancellationToken cancellationToken = default)
    {
        var all = await GetAllAccessibleAsync(cancellationToken);
        if (preferredTenantId.HasValue)
        {
            var preferred = all.FirstOrDefault(x => x.TenantId == preferredTenantId.Value);
            if (preferred is not null)
            {
                return preferred;
            }
        }

        return all.FirstOrDefault(x => x.IsActiveMembership) ?? (all.Count > 0 ? all[0] : null);
    }
}
