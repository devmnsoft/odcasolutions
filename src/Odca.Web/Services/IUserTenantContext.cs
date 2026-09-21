namespace Odca.Web.Services;

public sealed record UserTenantAccess(
    Guid TenantId,
    string OrganizationName,
    bool IsActiveMembership,
    IReadOnlySet<string> Permissions)
{
    public bool HasPermission(string permission) =>
        Permissions.Contains(permission);

    public bool HasAnyPermission(params string[] required)
    {
        for (var i = 0; i < required.Length; i++)
        {
            if (Permissions.Contains(required[i]))
            {
                return true;
            }
        }

        return false;
    }
}

public interface IUserTenantContext
{
    Task<UserTenantAccess?> GetAccessAsync(Guid tenantId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<UserTenantAccess>> GetAllAccessibleAsync(CancellationToken cancellationToken = default);
    Task<UserTenantAccess?> GetCurrentOrFirstActiveAsync(Guid? preferredTenantId = null, CancellationToken cancellationToken = default);
}
