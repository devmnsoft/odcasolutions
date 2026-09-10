using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Odca.Application.Tenancy;

public sealed class TenantAdministrationService
{
    private readonly ITenantAdministrationRepository _repository;

    public TenantAdministrationService(ITenantAdministrationRepository repository)
    {
        _repository = repository;
    }

    public Task<IReadOnlyList<OrganizationAccess>> ListOrganizationsAsync(Guid userId, CancellationToken cancellationToken)
    {
        return _repository.ListOrganizationsAsync(userId, cancellationToken);
    }

    public Task<OrganizationRecord?> GetOrganizationAsync(Guid actorId, Guid tenantId, CancellationToken cancellationToken)
    {
        return _repository.GetOrganizationAsync(actorId, tenantId, cancellationToken);
    }

    public Task<UpdateOrganizationResult> UpdateOrganizationAsync(Guid actorId, Guid tenantId, string name, string timezone, long version, CancellationToken cancellationToken)
    {
        return _repository.UpdateOrganizationAsync(actorId, tenantId, name, timezone, version, cancellationToken);
    }

    public Task<IReadOnlyList<TeamMember>> ListMembersAsync(Guid actorId, Guid tenantId, string? search, string? status, CancellationToken cancellationToken)
    {
        return _repository.ListMembersAsync(actorId, tenantId, search, status, cancellationToken);
    }

    public Task<IReadOnlyList<TenantRole>> ListRolesAsync(Guid actorId, Guid tenantId, CancellationToken cancellationToken)
    {
        return _repository.ListRolesAsync(actorId, tenantId, cancellationToken);
    }

    public Task<TenantRole?> CreateRoleAsync(Guid actorId, Guid tenantId, string name, IReadOnlyCollection<string> permissions, CancellationToken cancellationToken)
    {
        return _repository.CreateRoleAsync(actorId, tenantId, name, permissions, cancellationToken);
    }

    public Task<CreateInvitationResult> CreateInvitationAsync(Guid actorId, Guid tenantId, string email, Guid roleId, string idempotencyKey, CancellationToken cancellationToken)
    {
        return _repository.CreateInvitationAsync(actorId, tenantId, email, roleId, idempotencyKey, cancellationToken);
    }

    public Task<bool> AcceptInvitationAsync(Guid actorId, Guid invitationId, string tokenHash, CancellationToken cancellationToken)
    {
        return _repository.AcceptInvitationAsync(actorId, invitationId, tokenHash, cancellationToken);
    }
}
