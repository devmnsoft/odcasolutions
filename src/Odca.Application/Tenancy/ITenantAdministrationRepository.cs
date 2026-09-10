namespace Odca.Application.Tenancy;

public interface ITenantAdministrationRepository
{
    Task<IReadOnlyList<OrganizationAccess>> ListOrganizationsAsync(Guid userId, CancellationToken cancellationToken);
    Task<OrganizationRecord?> GetOrganizationAsync(Guid actorId, Guid tenantId, CancellationToken cancellationToken);
    Task<UpdateOrganizationResult> UpdateOrganizationAsync(Guid actorId, Guid tenantId, string name, string timezone, long version, CancellationToken cancellationToken);
    Task<IReadOnlyList<TeamMember>> ListMembersAsync(Guid actorId, Guid tenantId, string? search, string? status, CancellationToken cancellationToken);
    Task<IReadOnlyList<TenantRole>> ListRolesAsync(Guid actorId, Guid tenantId, CancellationToken cancellationToken);
    Task<TenantRole?> CreateRoleAsync(Guid actorId, Guid tenantId, string name, IReadOnlyCollection<string> permissions, CancellationToken cancellationToken);
    Task<CreateInvitationResult> CreateInvitationAsync(Guid actorId, Guid tenantId, string email, Guid roleId, string idempotencyKey, CancellationToken cancellationToken);
    Task<bool> AcceptInvitationAsync(Guid actorId, Guid invitationId, string tokenHash, CancellationToken cancellationToken);
}

public sealed record OrganizationAccess(Guid Id, string Name, string Status, long Version, string[] Permissions);
public sealed record OrganizationRecord(Guid Id, string Name, string Timezone, string Status, long Version);
public sealed record TeamMember(Guid UserId, string Name, string Email, string Status, string[] Roles);
public sealed record TenantRole(Guid Id, string Name, bool IsSystem, string[] Permissions);
public sealed record InvitationRecord(Guid Id, string Recipient, string State, DateTimeOffset ExpiresAt);
public enum UpdateOrganizationResult { Updated, NotFound, Forbidden, Conflict }
public enum CreateInvitationResultStatus { Created, Existing, Conflict, QuotaExceeded, Forbidden, InvalidRole }
public sealed record CreateInvitationResult(CreateInvitationResultStatus Status, InvitationRecord? Invitation);
