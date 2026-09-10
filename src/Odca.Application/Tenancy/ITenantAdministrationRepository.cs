namespace Odca.Application.Tenancy;

public interface ITenantAdministrationRepository
{
    Task<IReadOnlyList<OrganizationAccess>> ListOrganizationsAsync(Guid userId, CancellationToken cancellationToken);
    Task<OrganizationRecord?> GetOrganizationAsync(Guid actorId, Guid tenantId, CancellationToken cancellationToken);
    Task<UpdateOrganizationResult> UpdateOrganizationAsync(Guid actorId, Guid tenantId, string name, string timezone, long version, CancellationToken cancellationToken);
    Task<OrganizationOverview?> GetOrganizationOverviewAsync(Guid actorId, Guid tenantId, CancellationToken cancellationToken);
    Task<QueryAccess<TenantPage<TeamMember>>> ListMembersAsync(Guid actorId, Guid tenantId, string? search, string? status, int page, int pageSize, CancellationToken cancellationToken);
    Task<QueryAccess<TeamMemberDetail?>> GetMemberAsync(Guid actorId, Guid tenantId, Guid userId, CancellationToken cancellationToken);
    Task<MemberActionResult> ChangeMemberStatusAsync(Guid actorId, Guid tenantId, Guid userId, string targetStatus, string? reason, CancellationToken cancellationToken);
    Task<MemberActionResult> UpdateMemberRolesAsync(Guid actorId, Guid tenantId, Guid userId, IReadOnlyCollection<Guid> roleIds, CancellationToken cancellationToken);
    Task<QueryAccess<IReadOnlyList<TenantRole>>> ListRolesAsync(Guid actorId, Guid tenantId, CancellationToken cancellationToken);
    Task<TenantRole?> CreateRoleAsync(Guid actorId, Guid tenantId, string name, IReadOnlyCollection<string> permissions, CancellationToken cancellationToken);
    Task<UpdateRolePermissionsResult> UpdateRolePermissionsAsync(Guid actorId, Guid tenantId, Guid roleId, IReadOnlyCollection<string> permissions, CancellationToken cancellationToken);
    Task<QueryAccess<TenantPage<InvitationListItem>>> ListInvitationsAsync(Guid actorId, Guid tenantId, string? status, int page, int pageSize, CancellationToken cancellationToken);
    Task<CreateInvitationResult> CreateInvitationAsync(Guid actorId, Guid tenantId, string email, Guid roleId, string idempotencyKey, CancellationToken cancellationToken);
    Task<InvitationMutationResult> CancelInvitationAsync(Guid actorId, Guid tenantId, Guid invitationId, CancellationToken cancellationToken);
    Task<InvitationMutationResult> ResendInvitationAsync(Guid actorId, Guid tenantId, Guid invitationId, CancellationToken cancellationToken);
    Task<InvitationPreview?> GetInvitationPreviewAsync(Guid invitationId, string tokenHash, CancellationToken cancellationToken);
    Task<bool> AcceptInvitationAsync(Guid actorId, Guid invitationId, string tokenHash, CancellationToken cancellationToken);
    Task<UpdateRolePermissionsResult> UpdateRoleAsync(Guid actorId, Guid tenantId, Guid roleId, string name, IReadOnlyCollection<string> permissions, CancellationToken cancellationToken);
}

public sealed record OrganizationAccess(Guid Id, string Name, string Status, long Version, string[] Permissions);
public sealed record OrganizationRecord(Guid Id, string Name, string Timezone, string Status, long Version);
public sealed record OrganizationPendencyItem(string Message, string? Tab, string? StatusFilter);
public sealed record OrganizationOverview(
    int ActiveMembers,
    int ValidInvitations,
    int SeatLimit,
    int AvailableSeats,
    IReadOnlyList<OrganizationPendencyItem> Pendencies);
public sealed record TeamMember(Guid UserId, string Name, string Email, string Status, string[] Roles);
public sealed record TeamMemberDetail(Guid UserId, string Name, string Email, string Status, Guid[] RoleIds, string[] Roles, int SecurityVersion);
public sealed record TenantRole(Guid Id, string Name, bool IsSystem, string[] Permissions);
public sealed record InvitationRecord(Guid Id, string Recipient, string State, DateTimeOffset ExpiresAt);
public sealed record InvitationListItem(
    Guid Id,
    string Recipient,
    string RoleName,
    Guid RoleId,
    DateTimeOffset CreatedAt,
    DateTimeOffset ExpiresAt,
    string Status,
    string? DeliveryStatus,
    string? DeliveryErrorCode);
public sealed record InvitationPreview(
    Guid InvitationId,
    string OrganizationName,
    string RoleName,
    string RecipientEmail,
    DateTimeOffset ExpiresAt,
    string Status);
public sealed record TenantPage<T>(IReadOnlyList<T> Items, int TotalCount, int Page, int PageSize);

public enum QueryAccessStatus { Ok, Forbidden }
public sealed record QueryAccess<T>(QueryAccessStatus Status, T? Value);

public enum UpdateOrganizationResult { Updated, NotFound, Forbidden, Conflict }
public enum CreateInvitationResultStatus { Created, Existing, Conflict, QuotaExceeded, Forbidden, InvalidRole }
public sealed record CreateInvitationResult(CreateInvitationResultStatus Status, InvitationRecord? Invitation);
public enum InvitationMutationResult { Succeeded, Forbidden, NotFound, Conflict }
public enum MemberActionResult { Succeeded, Forbidden, NotFound, Conflict, LastAdminProtected }
public enum UpdateRolePermissionsStatus { Updated, Forbidden, NotFound, InvalidPermissions }
public sealed record UpdateRolePermissionsResult(UpdateRolePermissionsStatus Status, TenantRole? Role, int AffectedMemberCount);
