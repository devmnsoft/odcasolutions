namespace Odca.Contracts.Tenancy;

public sealed record OrganizationSummary(Guid Id, string Name, string Status, long Version, string[] Permissions);

public sealed record OrganizationDetails(Guid Id, string Name, string Timezone, string Status, long Version);

public sealed record OrganizationPendency(string Message, string? Tab, string? StatusFilter);

public sealed record OrganizationOverviewResponse(
    int ActiveMembers,
    int ValidInvitations,
    int SeatLimit,
    int AvailableSeats,
    IReadOnlyList<OrganizationPendency> Pendencies);

public sealed record UpdateOrganizationRequest(string Name, string Timezone, long Version);

public sealed record PaginatedResponse<T>(IReadOnlyList<T> Items, int TotalCount, int Page, int PageSize);

public sealed record TeamMemberResponse(Guid UserId, string Name, string Email, string Status, string[] Roles);

public sealed record TeamMemberDetailResponse(
    Guid UserId,
    string Name,
    string Email,
    string Status,
    Guid[] RoleIds,
    string[] Roles,
    int SecurityVersion);

public sealed record TenantRoleResponse(Guid Id, string Name, bool IsSystem, string[] Permissions);

public sealed record CreateTenantRoleRequest(string Name, string[] Permissions);

public sealed record UpdateTenantRoleRequest(string Name, string[] Permissions);

public sealed record UpdateTenantRolePermissionsRequest(string[] Permissions);

public sealed record UpdateTenantRolePermissionsResponse(Guid RoleId, string[] Permissions, int AffectedMemberCount);

public sealed record CreateInvitationRequest(string Email, Guid RoleId, string IdempotencyKey);

public sealed record InvitationResponse(Guid Id, string Recipient, string State, DateTimeOffset ExpiresAt);

public sealed record InvitationListItemResponse(
    Guid Id,
    string Recipient,
    string RoleName,
    Guid RoleId,
    DateTimeOffset CreatedAt,
    DateTimeOffset ExpiresAt,
    string Status,
    string? DeliveryStatus,
    string? DeliveryErrorCode);

public sealed record InvitationPreviewResponse(
    Guid InvitationId,
    string OrganizationName,
    string RoleName,
    string RecipientEmail,
    DateTimeOffset ExpiresAt,
    string Status);

public sealed record AcceptInvitationRequest(Guid InvitationId, string Token);

public sealed record MemberStatusChangeRequest(string? Reason);

public sealed record UpdateMemberRolesRequest(Guid[] RoleIds);
