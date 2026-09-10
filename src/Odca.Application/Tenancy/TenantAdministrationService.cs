namespace Odca.Application.Tenancy;

public sealed class TenantAdministrationService
{
    private readonly ITenantAdministrationRepository _repository;

    public TenantAdministrationService(ITenantAdministrationRepository repository)
    {
        _repository = repository;
    }

    public Task<IReadOnlyList<OrganizationAccess>> ListOrganizationsAsync(Guid userId, CancellationToken cancellationToken)
        => _repository.ListOrganizationsAsync(userId, cancellationToken);

    public Task<OrganizationRecord?> GetOrganizationAsync(Guid actorId, Guid tenantId, CancellationToken cancellationToken)
        => _repository.GetOrganizationAsync(actorId, tenantId, cancellationToken);

    public Task<UpdateOrganizationResult> UpdateOrganizationAsync(
        Guid actorId,
        Guid tenantId,
        string name,
        string timezone,
        long version,
        CancellationToken cancellationToken)
        => _repository.UpdateOrganizationAsync(actorId, tenantId, name, timezone, version, cancellationToken);

    public Task<OrganizationOverview?> GetOrganizationOverviewAsync(Guid actorId, Guid tenantId, CancellationToken cancellationToken)
        => _repository.GetOrganizationOverviewAsync(actorId, tenantId, cancellationToken);

    public Task<QueryAccess<TenantPage<TeamMember>>> ListMembersAsync(
        Guid actorId,
        Guid tenantId,
        string? search,
        string? status,
        int? page,
        int? pageSize,
        CancellationToken cancellationToken)
    {
        var (normalizedPage, normalizedSize) = Pagination.Normalize(page, pageSize);
        return _repository.ListMembersAsync(actorId, tenantId, search, status, normalizedPage, normalizedSize, cancellationToken);
    }

    public Task<QueryAccess<TeamMemberDetail?>> GetMemberAsync(
        Guid actorId,
        Guid tenantId,
        Guid userId,
        CancellationToken cancellationToken)
        => _repository.GetMemberAsync(actorId, tenantId, userId, cancellationToken);

    public Task<MemberActionResult> ChangeMemberStatusAsync(
        Guid actorId,
        Guid tenantId,
        Guid userId,
        string targetStatus,
        string? reason,
        CancellationToken cancellationToken)
        => _repository.ChangeMemberStatusAsync(actorId, tenantId, userId, targetStatus, reason, cancellationToken);

    public Task<MemberActionResult> UpdateMemberRolesAsync(
        Guid actorId,
        Guid tenantId,
        Guid userId,
        IReadOnlyCollection<Guid> roleIds,
        CancellationToken cancellationToken)
        => _repository.UpdateMemberRolesAsync(actorId, tenantId, userId, roleIds, cancellationToken);

    public Task<QueryAccess<IReadOnlyList<TenantRole>>> ListRolesAsync(Guid actorId, Guid tenantId, CancellationToken cancellationToken)
        => _repository.ListRolesAsync(actorId, tenantId, cancellationToken);

    public Task<TenantRole?> CreateRoleAsync(
        Guid actorId,
        Guid tenantId,
        string name,
        IReadOnlyCollection<string> permissions,
        CancellationToken cancellationToken)
        => _repository.CreateRoleAsync(actorId, tenantId, name, permissions, cancellationToken);

    public Task<UpdateRolePermissionsResult> UpdateRolePermissionsAsync(
        Guid actorId,
        Guid tenantId,
        Guid roleId,
        IReadOnlyCollection<string> permissions,
        CancellationToken cancellationToken)
        => _repository.UpdateRolePermissionsAsync(actorId, tenantId, roleId, permissions, cancellationToken);

    public Task<QueryAccess<TenantPage<InvitationListItem>>> ListInvitationsAsync(
        Guid actorId,
        Guid tenantId,
        string? status,
        int? page,
        int? pageSize,
        CancellationToken cancellationToken)
    {
        var (normalizedPage, normalizedSize) = Pagination.Normalize(page, pageSize);
        return _repository.ListInvitationsAsync(actorId, tenantId, status, normalizedPage, normalizedSize, cancellationToken);
    }

    public Task<CreateInvitationResult> CreateInvitationAsync(
        Guid actorId,
        Guid tenantId,
        string email,
        Guid roleId,
        string idempotencyKey,
        CancellationToken cancellationToken)
        => _repository.CreateInvitationAsync(actorId, tenantId, email, roleId, idempotencyKey, cancellationToken);

    public Task<InvitationMutationResult> CancelInvitationAsync(
        Guid actorId,
        Guid tenantId,
        Guid invitationId,
        CancellationToken cancellationToken)
        => _repository.CancelInvitationAsync(actorId, tenantId, invitationId, cancellationToken);

    public Task<InvitationMutationResult> ResendInvitationAsync(
        Guid actorId,
        Guid tenantId,
        Guid invitationId,
        CancellationToken cancellationToken)
        => _repository.ResendInvitationAsync(actorId, tenantId, invitationId, cancellationToken);

    public Task<InvitationPreview?> GetInvitationPreviewAsync(Guid invitationId, string tokenHash, CancellationToken cancellationToken)
        => _repository.GetInvitationPreviewAsync(invitationId, tokenHash, cancellationToken);

    public Task<bool> AcceptInvitationAsync(Guid actorId, Guid invitationId, string tokenHash, CancellationToken cancellationToken)
        => _repository.AcceptInvitationAsync(actorId, invitationId, tokenHash, cancellationToken);

    public Task<UpdateRolePermissionsResult> UpdateRoleAsync(
        Guid actorId,
        Guid tenantId,
        Guid roleId,
        string name,
        IReadOnlyCollection<string> permissions,
        CancellationToken cancellationToken)
        => _repository.UpdateRoleAsync(actorId, tenantId, roleId, name, permissions, cancellationToken);
}
