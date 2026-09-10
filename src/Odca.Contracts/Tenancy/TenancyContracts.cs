namespace Odca.Contracts.Tenancy;

public sealed record OrganizationSummary(Guid Id, string Name, string Status, long Version, string[] Permissions);

public sealed record OrganizationDetails(Guid Id, string Name, string Timezone, string Status, long Version);

public sealed record UpdateOrganizationRequest(string Name, string Timezone, long Version);

public sealed record TeamMemberResponse(Guid UserId, string Name, string Email, string Status, string[] Roles);

public sealed record TenantRoleResponse(Guid Id, string Name, bool IsSystem, string[] Permissions);

public sealed record CreateTenantRoleRequest(string Name, string[] Permissions);

public sealed record CreateInvitationRequest(string Email, Guid RoleId, string IdempotencyKey);

public sealed record InvitationResponse(Guid Id, string Recipient, string State, DateTimeOffset ExpiresAt);
public sealed record AcceptInvitationRequest(Guid InvitationId, string Token);
