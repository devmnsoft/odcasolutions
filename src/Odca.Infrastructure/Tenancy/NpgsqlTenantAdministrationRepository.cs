using System.Security.Cryptography;
using System.Text;
using Dapper;
using Microsoft.AspNetCore.DataProtection;
using Npgsql;
using Odca.Application.Tenancy;

namespace Odca.Infrastructure.Tenancy;

public sealed class NpgsqlTenantAdministrationRepository(
    NpgsqlDataSource dataSource,
    IDataProtectionProvider protectionProvider) : ITenantAdministrationRepository
{
    private readonly IDataProtector tokenProtector = protectionProvider.CreateProtector("ODCA.TenantInvitation.v1");

    public async Task<IReadOnlyList<OrganizationAccess>> ListOrganizationsAsync(Guid userId, CancellationToken cancellationToken)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        var rows = await connection.QueryAsync<OrganizationAccess>(new CommandDefinition(
            """
            SELECT id AS "Id", name AS "Name", status AS "Status", version AS "Version", permissions AS "Permissions"
            FROM odca.user_organizations(@userId);
            """,
            new { userId },
            cancellationToken: cancellationToken));
        return rows.AsList();
    }

    public async Task<OrganizationRecord?> GetOrganizationAsync(Guid actorId, Guid tenantId, CancellationToken cancellationToken)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction = await AuthorizedTransactionAsync(connection, actorId, tenantId, "tenant.organization.read", cancellationToken);
        if (transaction is null)
        {
            return null;
        }

        var result = await connection.QuerySingleOrDefaultAsync<OrganizationRecord>(new CommandDefinition(
            """
            SELECT id AS "Id", display_name AS "Name", timezone AS "Timezone", status AS "Status", version AS "Version"
            FROM odca.tenants
            WHERE id = @tenantId;
            """,
            new { tenantId },
            transaction,
            cancellationToken: cancellationToken));
        await transaction.CommitAsync(cancellationToken);
        return result;
    }

    public async Task<UpdateOrganizationResult> UpdateOrganizationAsync(
        Guid actorId,
        Guid tenantId,
        string name,
        string timezone,
        long version,
        CancellationToken cancellationToken)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction = await AuthorizedTransactionAsync(connection, actorId, tenantId, "tenant.organization.manage", cancellationToken);
        if (transaction is null)
        {
            return UpdateOrganizationResult.Forbidden;
        }

        var updated = await connection.ExecuteAsync(new CommandDefinition(
            """
            UPDATE odca.tenants
               SET display_name = @name,
                   timezone = @timezone,
                   version = version + 1,
                   updated_at = now()
             WHERE id = @tenantId
               AND version = @version;
            """,
            new { tenantId, name, timezone, version },
            transaction,
            cancellationToken: cancellationToken));
        if (updated == 0)
        {
            await transaction.RollbackAsync(cancellationToken);
            return UpdateOrganizationResult.Conflict;
        }

        await connection.ExecuteAsync(new CommandDefinition(
            """
            INSERT INTO odca.audit_events(scope_type,tenant_id,actor_user_id,action,entity_type,entity_id,result,metadata)
            VALUES ('tenant',@tenantId,@actorId,'tenant.organization.updated','tenant',@tenantId,'success',jsonb_build_object('version',@nextVersion));
            """,
            new { actorId, tenantId, nextVersion = version + 1 },
            transaction,
            cancellationToken: cancellationToken));
        await transaction.CommitAsync(cancellationToken);
        return UpdateOrganizationResult.Updated;
    }

    public async Task<OrganizationOverview?> GetOrganizationOverviewAsync(Guid actorId, Guid tenantId, CancellationToken cancellationToken)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction = await AuthorizedTransactionAsync(connection, actorId, tenantId, "tenant.team.read", cancellationToken);
        if (transaction is null)
        {
            return null;
        }

        await connection.ExecuteAsync(new CommandDefinition(
            "SELECT odca.expire_tenant_invitations(@tenantId);",
            new { tenantId },
            transaction,
            cancellationToken: cancellationToken));

        var metrics = await connection.QuerySingleAsync<OverviewMetrics>(new CommandDefinition(
            """
            SELECT
              (SELECT count(*)::int FROM odca.memberships WHERE tenant_id = @tenantId AND status = 'active') AS "ActiveMembers",
              (SELECT count(*)::int FROM odca.tenant_invitations WHERE tenant_id = @tenantId AND status IN ('pending','sent') AND expires_at > now()) AS "ValidInvitations",
              COALESCE((
                SELECT e.limit_value
                  FROM odca.subscriptions s
                  JOIN odca.plan_entitlements e ON e.plan_version_id = s.plan_version_id
                 WHERE s.tenant_id = @tenantId
                   AND s.status = 'active'
                   AND s.commercial_state = 'active'
                   AND e.entitlement_code = 'active_seats'
                   AND e.enabled
                 LIMIT 1), 0) AS "SeatLimit",
              (SELECT count(*)::int FROM odca.tenant_invitations WHERE tenant_id = @tenantId AND status = 'failed') AS "FailedInvitations",
              (SELECT count(*)::int FROM odca.memberships WHERE tenant_id = @tenantId AND status = 'blocked') AS "BlockedMembers";
            """,
            new { tenantId },
            transaction,
            cancellationToken: cancellationToken));

        var pendencies = new List<OrganizationPendencyItem>();
        if (metrics.FailedInvitations > 0)
        {
            pendencies.Add(new OrganizationPendencyItem(
                $"{metrics.FailedInvitations} convite(s) com falha de entrega.",
                "convites",
                "failed"));
        }

        if (metrics.BlockedMembers > 0)
        {
            pendencies.Add(new OrganizationPendencyItem(
                $"{metrics.BlockedMembers} membro(s) bloqueado(s).",
                "pessoas",
                "blocked"));
        }

        var overview = new OrganizationOverview(
            metrics.ActiveMembers,
            metrics.ValidInvitations,
            metrics.SeatLimit,
            Math.Max(0, metrics.SeatLimit - metrics.ActiveMembers - metrics.ValidInvitations),
            pendencies);

        await transaction.CommitAsync(cancellationToken);
        return overview;
    }

    public async Task<QueryAccess<TenantPage<TeamMember>>> ListMembersAsync(
        Guid actorId,
        Guid tenantId,
        string? search,
        string? status,
        int page,
        int pageSize,
        CancellationToken cancellationToken)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction = await AuthorizedTransactionAsync(connection, actorId, tenantId, "tenant.team.read", cancellationToken);
        if (transaction is null)
        {
            return new QueryAccess<TenantPage<TeamMember>>(QueryAccessStatus.Forbidden, null);
        }

        var normalizedSearch = string.IsNullOrWhiteSpace(search) ? null : search.Trim();
        var total = await connection.ExecuteScalarAsync<int>(new CommandDefinition(
            """
            SELECT count(*)::int
              FROM odca.memberships m
              JOIN odca.users u ON u.id = m.user_id
             WHERE m.tenant_id = @tenantId
               AND (@status IS NULL OR m.status = @status)
               AND (@search IS NULL OR u.display_name ILIKE '%' || @search || '%' OR u.email ILIKE '%' || @search || '%');
            """,
            new { tenantId, status, search = normalizedSearch },
            transaction,
            cancellationToken: cancellationToken));

        var rows = await connection.QueryAsync<TeamMember>(new CommandDefinition(
            """
            SELECT u.id AS "UserId",
                   u.display_name AS "Name",
                   u.email AS "Email",
                   m.status AS "Status",
                   COALESCE(array_agg(DISTINCT r.display_name) FILTER (WHERE r.id IS NOT NULL), ARRAY[]::text[]) AS "Roles"
              FROM odca.memberships m
              JOIN odca.users u ON u.id = m.user_id
              LEFT JOIN odca.member_roles mr ON (mr.tenant_id, mr.user_id) = (m.tenant_id, m.user_id)
              LEFT JOIN odca.roles r ON r.id = mr.role_id
             WHERE m.tenant_id = @tenantId
               AND (@status IS NULL OR m.status = @status)
               AND (@search IS NULL OR u.display_name ILIKE '%' || @search || '%' OR u.email ILIKE '%' || @search || '%')
             GROUP BY u.id, u.display_name, u.email, m.status
             ORDER BY u.display_name
             OFFSET @offset LIMIT @pageSize;
            """,
            new { tenantId, status, search = normalizedSearch, offset = Pagination.Offset(page, pageSize), pageSize },
            transaction,
            cancellationToken: cancellationToken));

        await transaction.CommitAsync(cancellationToken);
        return new QueryAccess<TenantPage<TeamMember>>(QueryAccessStatus.Ok, new TenantPage<TeamMember>(rows.AsList(), total, page, pageSize));
    }

    public async Task<QueryAccess<TeamMemberDetail?>> GetMemberAsync(
        Guid actorId,
        Guid tenantId,
        Guid userId,
        CancellationToken cancellationToken)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction = await AuthorizedTransactionAsync(connection, actorId, tenantId, "tenant.team.read", cancellationToken);
        if (transaction is null)
        {
            return new QueryAccess<TeamMemberDetail?>(QueryAccessStatus.Forbidden, null);
        }

        var row = await connection.QuerySingleOrDefaultAsync<TeamMemberDetail>(new CommandDefinition(
            """
            SELECT u.id AS "UserId",
                   u.display_name AS "Name",
                   u.email AS "Email",
                   m.status AS "Status",
                   COALESCE(array_agg(DISTINCT r.id) FILTER (WHERE r.id IS NOT NULL), ARRAY[]::uuid[]) AS "RoleIds",
                   COALESCE(array_agg(DISTINCT r.display_name) FILTER (WHERE r.id IS NOT NULL), ARRAY[]::text[]) AS "Roles",
                   m.security_version AS "SecurityVersion"
              FROM odca.memberships m
              JOIN odca.users u ON u.id = m.user_id
              LEFT JOIN odca.member_roles mr ON (mr.tenant_id, mr.user_id) = (m.tenant_id, m.user_id)
              LEFT JOIN odca.roles r ON r.id = mr.role_id
             WHERE m.tenant_id = @tenantId
               AND m.user_id = @userId
             GROUP BY u.id, u.display_name, u.email, m.status, m.security_version;
            """,
            new { tenantId, userId },
            transaction,
            cancellationToken: cancellationToken));

        await transaction.CommitAsync(cancellationToken);
        return new QueryAccess<TeamMemberDetail?>(QueryAccessStatus.Ok, row);
    }

    public async Task<MemberActionResult> ChangeMemberStatusAsync(
        Guid actorId,
        Guid tenantId,
        Guid userId,
        string targetStatus,
        string? reason,
        CancellationToken cancellationToken)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction = await AuthorizedTransactionAsync(connection, actorId, tenantId, "tenant.team.manage", cancellationToken);
        if (transaction is null)
        {
            return MemberActionResult.Forbidden;
        }

        await connection.ExecuteAsync(new CommandDefinition(
            "SELECT pg_advisory_xact_lock(hashtextextended(@key, 0));",
            new { key = tenantId.ToString() },
            transaction,
            cancellationToken: cancellationToken));

        var currentStatus = await connection.QuerySingleOrDefaultAsync<string>(new CommandDefinition(
            """
            SELECT m.status
              FROM odca.memberships m
             WHERE m.tenant_id = @tenantId
               AND m.user_id = @userId
             FOR UPDATE;
            """,
            new { tenantId, userId },
            transaction,
            cancellationToken: cancellationToken));

        if (currentStatus is null)
        {
            await transaction.RollbackAsync(cancellationToken);
            return MemberActionResult.NotFound;
        }

        var leavingActive = string.Equals(currentStatus, "active", StringComparison.Ordinal)
            && !string.Equals(targetStatus, "active", StringComparison.Ordinal);
        if (leavingActive)
        {
            var allowed = await connection.ExecuteScalarAsync<bool>(new CommandDefinition(
                "SELECT odca.ensure_not_removing_last_admin(@tenantId, @userId, false, true);",
                new { tenantId, userId },
                transaction,
                cancellationToken: cancellationToken));
            if (!allowed)
            {
                await connection.ExecuteAsync(new CommandDefinition(
                    """
                    INSERT INTO odca.audit_events(scope_type,tenant_id,actor_user_id,action,entity_type,entity_id,result,metadata)
                    VALUES ('tenant',@tenantId,@actorId,'tenant.member.status_denied','membership',@userId,'denied',jsonb_build_object('reason','last_admin', 'targetStatus', @targetStatus));
                    """,
                    new { tenantId, actorId, userId, targetStatus },
                    transaction,
                    cancellationToken: cancellationToken));
                await transaction.CommitAsync(cancellationToken);
                return MemberActionResult.LastAdminProtected;
            }
        }

        var updated = await connection.ExecuteAsync(new CommandDefinition(
            """
            UPDATE odca.memberships
               SET status = @targetStatus,
                   security_version = security_version + 1,
                   updated_at = now()
             WHERE tenant_id = @tenantId
               AND user_id = @userId;
            """,
            new { tenantId, userId, targetStatus },
            transaction,
            cancellationToken: cancellationToken));
        if (updated == 0)
        {
            await transaction.RollbackAsync(cancellationToken);
            return MemberActionResult.NotFound;
        }

        if (!string.Equals(targetStatus, "active", StringComparison.Ordinal))
        {
            await connection.ExecuteAsync(new CommandDefinition(
                """
                UPDATE odca.sessions
                   SET revoked_at = now()
                 WHERE user_id = @userId
                   AND revoked_at IS NULL;
                UPDATE odca.users
                   SET security_version = security_version + 1
                 WHERE id = @userId;
                """,
                new { userId },
                transaction,
                cancellationToken: cancellationToken));
        }

        await connection.ExecuteAsync(new CommandDefinition(
            """
            INSERT INTO odca.audit_events(scope_type,tenant_id,actor_user_id,action,entity_type,entity_id,result,metadata)
            VALUES (
              'tenant',
              @tenantId,
              @actorId,
              'tenant.member.status_changed',
              'membership',
              @userId,
              'success',
              jsonb_build_object('from', @fromStatus, 'to', @targetStatus, 'reason', @reason));
            """,
            new { tenantId, actorId, userId, fromStatus = currentStatus, targetStatus, reason },
            transaction,
            cancellationToken: cancellationToken));
        await transaction.CommitAsync(cancellationToken);
        return MemberActionResult.Succeeded;
    }

    public async Task<MemberActionResult> UpdateMemberRolesAsync(
        Guid actorId,
        Guid tenantId,
        Guid userId,
        IReadOnlyCollection<Guid> roleIds,
        CancellationToken cancellationToken)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction = await AuthorizedTransactionAsync(connection, actorId, tenantId, "tenant.team.manage", cancellationToken);
        if (transaction is null)
        {
            return MemberActionResult.Forbidden;
        }

        await connection.ExecuteAsync(new CommandDefinition(
            "SELECT pg_advisory_xact_lock(hashtextextended(@key, 0));",
            new { key = tenantId.ToString() },
            transaction,
            cancellationToken: cancellationToken));

        var membershipExists = await connection.ExecuteScalarAsync<bool>(new CommandDefinition(
            """
            SELECT EXISTS(
              SELECT 1 FROM odca.memberships
               WHERE tenant_id = @tenantId AND user_id = @userId);
            """,
            new { tenantId, userId },
            transaction,
            cancellationToken: cancellationToken));
        if (!membershipExists)
        {
            await transaction.RollbackAsync(cancellationToken);
            return MemberActionResult.NotFound;
        }

        var distinctRoleIds = roleIds.Distinct().ToArray();
        var validCount = await connection.ExecuteScalarAsync<int>(new CommandDefinition(
            """
            SELECT count(*)::int
              FROM odca.roles
             WHERE tenant_id = @tenantId
               AND id = ANY(@roleIds);
            """,
            new { tenantId, roleIds = distinctRoleIds },
            transaction,
            cancellationToken: cancellationToken));
        if (validCount != distinctRoleIds.Length)
        {
            await transaction.RollbackAsync(cancellationToken);
            return MemberActionResult.Conflict;
        }

        var currentlyAdmin = await connection.ExecuteScalarAsync<bool>(new CommandDefinition(
            "SELECT odca.member_has_tenant_administrator_role(@tenantId, @userId);",
            new { tenantId, userId },
            transaction,
            cancellationToken: cancellationToken));
        var willRemainAdmin = await connection.ExecuteScalarAsync<bool>(new CommandDefinition(
            """
            SELECT EXISTS(
              SELECT 1 FROM odca.roles
               WHERE tenant_id = @tenantId
                 AND id = ANY(@roleIds)
                 AND code = 'tenant-administrator');
            """,
            new { tenantId, roleIds = distinctRoleIds },
            transaction,
            cancellationToken: cancellationToken));

        if (currentlyAdmin && !willRemainAdmin)
        {
            var allowed = await connection.ExecuteScalarAsync<bool>(new CommandDefinition(
                "SELECT odca.ensure_not_removing_last_admin(@tenantId, @userId, true, false);",
                new { tenantId, userId },
                transaction,
                cancellationToken: cancellationToken));
            if (!allowed)
            {
                await connection.ExecuteAsync(new CommandDefinition(
                    """
                    INSERT INTO odca.audit_events(scope_type,tenant_id,actor_user_id,action,entity_type,entity_id,result,metadata)
                    VALUES ('tenant',@tenantId,@actorId,'tenant.member.roles_denied','membership',@userId,'denied',jsonb_build_object('reason','last_admin'));
                    """,
                    new { tenantId, actorId, userId },
                    transaction,
                    cancellationToken: cancellationToken));
                await transaction.CommitAsync(cancellationToken);
                return MemberActionResult.LastAdminProtected;
            }
        }

        await connection.ExecuteAsync(new CommandDefinition(
            "DELETE FROM odca.member_roles WHERE tenant_id = @tenantId AND user_id = @userId;",
            new { tenantId, userId },
            transaction,
            cancellationToken: cancellationToken));

        if (distinctRoleIds.Length > 0)
        {
            await connection.ExecuteAsync(new CommandDefinition(
                """
                INSERT INTO odca.member_roles(tenant_id, user_id, role_id, assigned_by)
                SELECT @tenantId, @userId, role_id, @actorId
                  FROM unnest(@roleIds) AS role_id;
                """,
                new { tenantId, userId, actorId, roleIds = distinctRoleIds },
                transaction,
                cancellationToken: cancellationToken));
        }

        await connection.ExecuteAsync(new CommandDefinition(
            """
            UPDATE odca.memberships
               SET security_version = security_version + 1, updated_at = now()
             WHERE tenant_id = @tenantId AND user_id = @userId;
            UPDATE odca.users SET security_version = security_version + 1 WHERE id = @userId;
            UPDATE odca.sessions SET revoked_at = now() WHERE user_id = @userId AND revoked_at IS NULL;
            INSERT INTO odca.audit_events(scope_type,tenant_id,actor_user_id,action,entity_type,entity_id,result,metadata)
            VALUES ('tenant',@tenantId,@actorId,'tenant.member.roles_updated','membership',@userId,'success',jsonb_build_object('roleCount', @roleCount));
            """,
            new { tenantId, userId, actorId, roleCount = distinctRoleIds.Length },
            transaction,
            cancellationToken: cancellationToken));

        await transaction.CommitAsync(cancellationToken);
        return MemberActionResult.Succeeded;
    }

    public async Task<QueryAccess<IReadOnlyList<TenantRole>>> ListRolesAsync(Guid actorId, Guid tenantId, CancellationToken cancellationToken)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction = await AuthorizedTransactionAsync(connection, actorId, tenantId, "tenant.team.read", cancellationToken);
        if (transaction is null)
        {
            return new QueryAccess<IReadOnlyList<TenantRole>>(QueryAccessStatus.Forbidden, null);
        }

        var rows = await connection.QueryAsync<TenantRole>(new CommandDefinition(
            """
            SELECT r.id AS "Id",
                   r.display_name AS "Name",
                   r.is_system AS "IsSystem",
                   COALESCE(array_agg(rp.permission_code) FILTER (WHERE rp.permission_code IS NOT NULL), ARRAY[]::text[]) AS "Permissions"
              FROM odca.roles r
              LEFT JOIN odca.role_permissions rp ON rp.role_id = r.id
             WHERE r.tenant_id = @tenantId
             GROUP BY r.id
             ORDER BY r.display_name;
            """,
            new { tenantId },
            transaction,
            cancellationToken: cancellationToken));
        await transaction.CommitAsync(cancellationToken);
        return new QueryAccess<IReadOnlyList<TenantRole>>(QueryAccessStatus.Ok, rows.AsList());
    }

    public async Task<TenantRole?> CreateRoleAsync(
        Guid actorId,
        Guid tenantId,
        string name,
        IReadOnlyCollection<string> permissions,
        CancellationToken cancellationToken)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction = await AuthorizedTransactionAsync(connection, actorId, tenantId, "tenant.team.manage", cancellationToken);
        if (transaction is null)
        {
            return null;
        }

        var allowed = await ResolveAssignablePermissionsAsync(connection, transaction, actorId, tenantId, permissions, cancellationToken);
        if (allowed is null)
        {
            await transaction.RollbackAsync(cancellationToken);
            return null;
        }

        var id = Guid.NewGuid();
        var code = "custom-" + id.ToString("N");
        await connection.ExecuteAsync(new CommandDefinition(
            """
            INSERT INTO odca.roles(id, scope_type, tenant_id, code, display_name, is_system)
            VALUES (@id, 'tenant', @tenantId, @code, @name, false);
            INSERT INTO odca.role_permissions(role_id, permission_code)
            SELECT @id, unnest(@allowed);
            INSERT INTO odca.audit_events(scope_type,tenant_id,actor_user_id,action,entity_type,entity_id,result)
            VALUES ('tenant',@tenantId,@actorId,'tenant.role.created','role',@id,'success');
            """,
            new { id, tenantId, code, name, allowed, actorId },
            transaction,
            cancellationToken: cancellationToken));
        await transaction.CommitAsync(cancellationToken);
        return new TenantRole(id, name, false, allowed);
    }

    public async Task<UpdateRolePermissionsResult> UpdateRolePermissionsAsync(
        Guid actorId,
        Guid tenantId,
        Guid roleId,
        IReadOnlyCollection<string> permissions,
        CancellationToken cancellationToken)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction = await AuthorizedTransactionAsync(connection, actorId, tenantId, "tenant.team.manage", cancellationToken);
        if (transaction is null)
        {
            return new UpdateRolePermissionsResult(UpdateRolePermissionsStatus.Forbidden, null, 0);
        }

        var role = await connection.QuerySingleOrDefaultAsync<RoleRow>(new CommandDefinition(
            """
            SELECT id AS "Id", display_name AS "Name", is_system AS "IsSystem"
              FROM odca.roles
             WHERE tenant_id = @tenantId
               AND id = @roleId
             FOR UPDATE;
            """,
            new { tenantId, roleId },
            transaction,
            cancellationToken: cancellationToken));
        if (role is null)
        {
            await transaction.RollbackAsync(cancellationToken);
            return new UpdateRolePermissionsResult(UpdateRolePermissionsStatus.NotFound, null, 0);
        }

        var allowed = await ResolveAssignablePermissionsAsync(connection, transaction, actorId, tenantId, permissions, cancellationToken);
        if (allowed is null)
        {
            await transaction.RollbackAsync(cancellationToken);
            return new UpdateRolePermissionsResult(UpdateRolePermissionsStatus.InvalidPermissions, null, 0);
        }

        await connection.ExecuteAsync(new CommandDefinition(
            """
            DELETE FROM odca.role_permissions WHERE role_id = @roleId;
            INSERT INTO odca.role_permissions(role_id, permission_code)
            SELECT @roleId, unnest(@allowed);
            """,
            new { roleId, allowed },
            transaction,
            cancellationToken: cancellationToken));

        var affected = await connection.ExecuteScalarAsync<int>(new CommandDefinition(
            """
            SELECT count(DISTINCT user_id)::int
              FROM odca.member_roles
             WHERE tenant_id = @tenantId
               AND role_id = @roleId;
            """,
            new { tenantId, roleId },
            transaction,
            cancellationToken: cancellationToken));

        await connection.ExecuteAsync(new CommandDefinition(
            """
            INSERT INTO odca.audit_events(scope_type,tenant_id,actor_user_id,action,entity_type,entity_id,result,metadata)
            VALUES ('tenant',@tenantId,@actorId,'tenant.role.permissions_updated','role',@roleId,'success',jsonb_build_object('affectedMembers', @affected));
            """,
            new { tenantId, actorId, roleId, affected },
            transaction,
            cancellationToken: cancellationToken));

        await transaction.CommitAsync(cancellationToken);
        return new UpdateRolePermissionsResult(
            UpdateRolePermissionsStatus.Updated,
            new TenantRole(role.Id, role.Name, role.IsSystem, allowed),
            affected);
    }

    public async Task<QueryAccess<TenantPage<InvitationListItem>>> ListInvitationsAsync(
        Guid actorId,
        Guid tenantId,
        string? status,
        int page,
        int pageSize,
        CancellationToken cancellationToken)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction = await AuthorizedTransactionAsync(connection, actorId, tenantId, "tenant.team.read", cancellationToken);
        if (transaction is null)
        {
            return new QueryAccess<TenantPage<InvitationListItem>>(QueryAccessStatus.Forbidden, null);
        }

        await connection.ExecuteAsync(new CommandDefinition(
            "SELECT odca.expire_tenant_invitations(@tenantId);",
            new { tenantId },
            transaction,
            cancellationToken: cancellationToken));

        var total = await connection.ExecuteScalarAsync<int>(new CommandDefinition(
            """
            SELECT count(*)::int
              FROM odca.tenant_invitations i
             WHERE i.tenant_id = @tenantId
               AND (@status IS NULL OR i.status = @status);
            """,
            new { tenantId, status },
            transaction,
            cancellationToken: cancellationToken));

        var rows = await connection.QueryAsync<InvitationListItem>(new CommandDefinition(
            """
            SELECT i.id AS "Id",
                   i.recipient_email AS "Recipient",
                   r.display_name AS "RoleName",
                   i.role_id AS "RoleId",
                   i.created_at AS "CreatedAt",
                   i.expires_at AS "ExpiresAt",
                   i.status AS "Status",
                   o.status AS "DeliveryStatus",
                   o.last_error_code AS "DeliveryErrorCode"
              FROM odca.tenant_invitations i
              JOIN odca.roles r ON r.id = i.role_id AND r.tenant_id = i.tenant_id
              LEFT JOIN LATERAL (
                SELECT n.status, n.last_error_code
                  FROM odca.notification_outbox n
                 WHERE n.invitation_id = i.id
                   AND n.kind = 'invitation'
                 ORDER BY n.created_at DESC
                 LIMIT 1
              ) o ON TRUE
             WHERE i.tenant_id = @tenantId
               AND (@status IS NULL OR i.status = @status)
             ORDER BY i.created_at DESC
             OFFSET @offset LIMIT @pageSize;
            """,
            new { tenantId, status, offset = Pagination.Offset(page, pageSize), pageSize },
            transaction,
            cancellationToken: cancellationToken));

        await transaction.CommitAsync(cancellationToken);
        return new QueryAccess<TenantPage<InvitationListItem>>(
            QueryAccessStatus.Ok,
            new TenantPage<InvitationListItem>(rows.AsList(), total, page, pageSize));
    }

    public async Task<CreateInvitationResult> CreateInvitationAsync(
        Guid actorId,
        Guid tenantId,
        string email,
        Guid roleId,
        string idempotencyKey,
        CancellationToken cancellationToken)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction = await AuthorizedTransactionAsync(connection, actorId, tenantId, "tenant.team.manage", cancellationToken);
        if (transaction is null)
        {
            return new CreateInvitationResult(CreateInvitationResultStatus.Forbidden, null);
        }

        await connection.ExecuteAsync(new CommandDefinition(
            "SELECT pg_advisory_xact_lock(hashtextextended(@key, 0));",
            new { key = tenantId.ToString() },
            transaction,
            cancellationToken: cancellationToken));

        await connection.ExecuteAsync(new CommandDefinition(
            "SELECT odca.expire_tenant_invitations(@tenantId);",
            new { tenantId },
            transaction,
            cancellationToken: cancellationToken));

        var normalized = email.Trim().ToUpperInvariant();
        var existing = await connection.QuerySingleOrDefaultAsync<dynamic>(new CommandDefinition(
            """
            SELECT id, recipient_normalized, role_id, recipient_email, status, expires_at
              FROM odca.tenant_invitations
             WHERE tenant_id = @tenantId
               AND idempotency_key = @idempotencyKey;
            """,
            new { tenantId, idempotencyKey },
            transaction,
            cancellationToken: cancellationToken));
        if (existing is not null)
        {
            if (existing.recipient_normalized == normalized && existing.role_id == roleId)
            {
                await transaction.CommitAsync(cancellationToken);
                return new CreateInvitationResult(
                    CreateInvitationResultStatus.Existing,
                    new InvitationRecord((Guid)existing.id, (string)existing.recipient_email, (string)existing.status, (DateTimeOffset)existing.expires_at));
            }

            await transaction.RollbackAsync(cancellationToken);
            return new CreateInvitationResult(CreateInvitationResultStatus.Conflict, null);
        }

        var rolePermissions = (await connection.QueryAsync<string>(new CommandDefinition(
            """
            SELECT rp.permission_code
              FROM odca.roles r
              LEFT JOIN odca.role_permissions rp ON rp.role_id = r.id
             WHERE r.tenant_id = @tenantId
               AND r.id = @roleId;
            """,
            new { tenantId, roleId },
            transaction,
            cancellationToken: cancellationToken))).Where(x => x is not null).ToArray();

        var roleExists = await connection.ExecuteScalarAsync<bool>(new CommandDefinition(
            "SELECT EXISTS(SELECT 1 FROM odca.roles WHERE tenant_id = @tenantId AND id = @roleId);",
            new { tenantId, roleId },
            transaction,
            cancellationToken: cancellationToken));
        if (!roleExists)
        {
            await transaction.RollbackAsync(cancellationToken);
            return new CreateInvitationResult(CreateInvitationResultStatus.InvalidRole, null);
        }

        if (rolePermissions.Length > 0)
        {
            var allowed = await ResolveAssignablePermissionsAsync(connection, transaction, actorId, tenantId, rolePermissions, cancellationToken);
            if (allowed is null)
            {
                await transaction.RollbackAsync(cancellationToken);
                return new CreateInvitationResult(CreateInvitationResultStatus.InvalidRole, null);
            }
        }

        var seatLimit = await connection.ExecuteScalarAsync<int?>(new CommandDefinition(
            """
            SELECT e.limit_value
              FROM odca.subscriptions s
              JOIN odca.plan_entitlements e ON e.plan_version_id = s.plan_version_id
             WHERE s.tenant_id = @tenantId
               AND s.status = 'active'
               AND s.commercial_state = 'active'
               AND e.entitlement_code = 'active_seats'
               AND e.enabled;
            """,
            new { tenantId },
            transaction,
            cancellationToken: cancellationToken));
        var occupied = await connection.ExecuteScalarAsync<long>(new CommandDefinition(
            """
            SELECT
              (SELECT count(*) FROM odca.memberships WHERE tenant_id = @tenantId AND status = 'active')
              + (SELECT count(*) FROM odca.tenant_invitations WHERE tenant_id = @tenantId AND status IN ('pending','sent') AND expires_at > now());
            """,
            new { tenantId },
            transaction,
            cancellationToken: cancellationToken));
        if (seatLimit is null || occupied >= seatLimit)
        {
            await transaction.RollbackAsync(cancellationToken);
            return new CreateInvitationResult(CreateInvitationResultStatus.QuotaExceeded, null);
        }

        var token = Convert.ToHexString(RandomNumberGenerator.GetBytes(32)).ToLowerInvariant();
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token))).ToLowerInvariant();
        var protectedToken = tokenProtector.Protect(token);
        var id = Guid.NewGuid();
        var expires = DateTimeOffset.UtcNow.AddDays(7);

        var row = await connection.QuerySingleAsync<InvitationRecord>(new CommandDefinition(
            """
            INSERT INTO odca.tenant_invitations(
              id, tenant_id, recipient_email, recipient_normalized, role_id, token_hash, protected_token, idempotency_key, expires_at, created_by)
            VALUES (@id, @tenantId, @email, @normalized, @roleId, @hash, @protectedToken, @idempotencyKey, @expires, @actorId)
            RETURNING id AS "Id", recipient_email AS "Recipient", status AS "State", expires_at AS "ExpiresAt";
            """,
            new { id, tenantId, email = email.Trim(), normalized, roleId, hash, protectedToken, idempotencyKey, expires, actorId },
            transaction,
            cancellationToken: cancellationToken));

        await connection.ExecuteAsync(new CommandDefinition(
            """
            INSERT INTO odca.notification_outbox(id, tenant_id, invitation_id, kind, destination, protected_payload)
            VALUES (gen_random_uuid(), @tenantId, @id, 'invitation', @email, @protectedToken);
            INSERT INTO odca.audit_events(scope_type,tenant_id,actor_user_id,action,entity_type,entity_id,result)
            VALUES ('tenant',@tenantId,@actorId,'tenant.invitation.created','invitation',@id,'success');
            """,
            new { tenantId, id, email = email.Trim(), protectedToken, actorId },
            transaction,
            cancellationToken: cancellationToken));

        await transaction.CommitAsync(cancellationToken);
        return new CreateInvitationResult(CreateInvitationResultStatus.Created, row);
    }

    public async Task<InvitationMutationResult> CancelInvitationAsync(
        Guid actorId,
        Guid tenantId,
        Guid invitationId,
        CancellationToken cancellationToken)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction = await AuthorizedTransactionAsync(connection, actorId, tenantId, "tenant.team.manage", cancellationToken);
        if (transaction is null)
        {
            return InvitationMutationResult.Forbidden;
        }

        var belongs = await connection.ExecuteScalarAsync<bool>(new CommandDefinition(
            """
            SELECT EXISTS(
              SELECT 1 FROM odca.tenant_invitations
               WHERE id = @invitationId AND tenant_id = @tenantId);
            """,
            new { invitationId, tenantId },
            transaction,
            cancellationToken: cancellationToken));
        if (!belongs)
        {
            await transaction.RollbackAsync(cancellationToken);
            return InvitationMutationResult.NotFound;
        }

        var cancelled = await connection.ExecuteScalarAsync<bool>(new CommandDefinition(
            "SELECT odca.cancel_tenant_invitation(@actorId, @invitationId);",
            new { actorId, invitationId },
            transaction,
            cancellationToken: cancellationToken));
        if (!cancelled)
        {
            await transaction.RollbackAsync(cancellationToken);
            return InvitationMutationResult.Conflict;
        }

        await transaction.CommitAsync(cancellationToken);
        return InvitationMutationResult.Succeeded;
    }

    public async Task<InvitationMutationResult> ResendInvitationAsync(
        Guid actorId,
        Guid tenantId,
        Guid invitationId,
        CancellationToken cancellationToken)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction = await AuthorizedTransactionAsync(connection, actorId, tenantId, "tenant.team.manage", cancellationToken);
        if (transaction is null)
        {
            return InvitationMutationResult.Forbidden;
        }

        var belongs = await connection.ExecuteScalarAsync<bool>(new CommandDefinition(
            """
            SELECT EXISTS(
              SELECT 1 FROM odca.tenant_invitations
               WHERE id = @invitationId AND tenant_id = @tenantId);
            """,
            new { invitationId, tenantId },
            transaction,
            cancellationToken: cancellationToken));
        if (!belongs)
        {
            await transaction.RollbackAsync(cancellationToken);
            return InvitationMutationResult.NotFound;
        }

        var token = Convert.ToHexString(RandomNumberGenerator.GetBytes(32)).ToLowerInvariant();
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token))).ToLowerInvariant();
        var protectedToken = tokenProtector.Protect(token);

        var resent = await connection.ExecuteScalarAsync<bool>(new CommandDefinition(
            "SELECT odca.resend_tenant_invitation(@actorId, @invitationId, @hash, @protectedToken);",
            new { actorId, invitationId, hash, protectedToken },
            transaction,
            cancellationToken: cancellationToken));
        if (!resent)
        {
            await transaction.RollbackAsync(cancellationToken);
            return InvitationMutationResult.Conflict;
        }

        await transaction.CommitAsync(cancellationToken);
        return InvitationMutationResult.Succeeded;
    }

    public async Task<bool> AcceptInvitationAsync(Guid actorId, Guid invitationId, string tokenHash, CancellationToken cancellationToken)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        return await connection.ExecuteScalarAsync<bool>(new CommandDefinition(
            "SELECT odca.accept_tenant_invitation(@actorId, @invitationId, @tokenHash);",
            new { actorId, invitationId, tokenHash },
            cancellationToken: cancellationToken));
    }

    private static async Task<string[]?> ResolveAssignablePermissionsAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        Guid actorId,
        Guid tenantId,
        IReadOnlyCollection<string> permissions,
        CancellationToken cancellationToken)
    {
        var distinct = permissions.Distinct(StringComparer.Ordinal).ToArray();
        var allowed = (await connection.QueryAsync<string>(new CommandDefinition(
            """
            SELECT p.code
              FROM odca.permissions p
             WHERE p.delegable
               AND p.code = ANY(@permissions)
               AND EXISTS (
                 SELECT 1
                   FROM odca.member_roles mr
                   JOIN odca.roles ar ON ar.id = mr.role_id
                   LEFT JOIN odca.role_permissions arp ON arp.role_id = ar.id
                  WHERE mr.tenant_id = @tenantId
                    AND mr.user_id = @actorId
                    AND (ar.code = 'tenant-administrator' OR arp.permission_code = p.code));
            """,
            new { permissions = distinct, tenantId, actorId },
            transaction,
            cancellationToken: cancellationToken))).ToArray();

        return allowed.Length == distinct.Length ? allowed : null;
    }

    private static async Task<NpgsqlTransaction?> AuthorizedTransactionAsync(
        NpgsqlConnection connection,
        Guid actorId,
        Guid tenantId,
        string permission,
        CancellationToken cancellationToken)
    {
        var tx = await connection.BeginTransactionAsync(cancellationToken);
        var allowed = await connection.ExecuteScalarAsync<bool>(new CommandDefinition(
            "SELECT odca.tenant_actor_has_permission(@actorId, @tenantId, @permission);",
            new { actorId, tenantId, permission },
            tx,
            cancellationToken: cancellationToken));
        if (!allowed)
        {
            await tx.RollbackAsync(cancellationToken);
            await tx.DisposeAsync();
            return null;
        }

        await connection.ExecuteAsync(new CommandDefinition(
            "SELECT set_config('odca.tenant_id', @tenantId, true);",
            new { tenantId = tenantId.ToString() },
            tx,
            cancellationToken: cancellationToken));
        return tx;
    }

    public async Task<InvitationPreview?> GetInvitationPreviewAsync(
        Guid invitationId,
        string tokenHash,
        CancellationToken cancellationToken)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        return await connection.QuerySingleOrDefaultAsync<InvitationPreview>(new CommandDefinition(
            """
            SELECT invitation_id AS "InvitationId",
                   organization_name AS "OrganizationName",
                   role_name AS "RoleName",
                   recipient_email AS "RecipientEmail",
                   expires_at AS "ExpiresAt",
                   status AS "Status"
              FROM odca.preview_tenant_invitation(@invitationId, @tokenHash);
            """,
            new { invitationId, tokenHash },
            cancellationToken: cancellationToken));
    }

    public async Task<UpdateRolePermissionsResult> UpdateRoleAsync(
        Guid actorId,
        Guid tenantId,
        Guid roleId,
        string name,
        IReadOnlyCollection<string> permissions,
        CancellationToken cancellationToken)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction = await AuthorizedTransactionAsync(connection, actorId, tenantId, "tenant.team.manage", cancellationToken);
        if (transaction is null)
        {
            return new UpdateRolePermissionsResult(UpdateRolePermissionsStatus.Forbidden, null, 0);
        }

        var role = await connection.QuerySingleOrDefaultAsync<RoleRow>(new CommandDefinition(
            """
            SELECT id AS "Id", display_name AS "Name", is_system AS "IsSystem"
              FROM odca.roles
             WHERE tenant_id = @tenantId
               AND id = @roleId
             FOR UPDATE;
            """,
            new { tenantId, roleId },
            transaction,
            cancellationToken: cancellationToken));
        if (role is null)
        {
            await transaction.RollbackAsync(cancellationToken);
            return new UpdateRolePermissionsResult(UpdateRolePermissionsStatus.NotFound, null, 0);
        }

        var allowed = await ResolveAssignablePermissionsAsync(connection, transaction, actorId, tenantId, permissions, cancellationToken);
        if (allowed is null)
        {
            await transaction.RollbackAsync(cancellationToken);
            return new UpdateRolePermissionsResult(UpdateRolePermissionsStatus.InvalidPermissions, null, 0);
        }

        await connection.ExecuteAsync(new CommandDefinition(
            """
            UPDATE odca.roles
               SET display_name = @name
             WHERE id = @roleId
               AND tenant_id = @tenantId
               AND is_system = false;
            """,
            new { name, roleId, tenantId },
            transaction,
            cancellationToken: cancellationToken));

        if (!role.IsSystem)
        {
            await connection.ExecuteAsync(new CommandDefinition(
                """
                DELETE FROM odca.role_permissions WHERE role_id = @roleId;
                INSERT INTO odca.role_permissions(role_id, permission_code)
                SELECT @roleId, unnest(@allowed);
                """,
                new { roleId, allowed },
                transaction,
                cancellationToken: cancellationToken));
        }

        var affected = await connection.ExecuteScalarAsync<int>(new CommandDefinition(
            """
            SELECT count(DISTINCT user_id)::int
              FROM odca.member_roles
             WHERE tenant_id = @tenantId
               AND role_id = @roleId;
            """,
            new { tenantId, roleId },
            transaction,
            cancellationToken: cancellationToken));

        await connection.ExecuteAsync(new CommandDefinition(
            """
            INSERT INTO odca.audit_events(scope_type,tenant_id,actor_user_id,action,entity_type,entity_id,result,metadata)
            VALUES ('tenant',@tenantId,@actorId,'tenant.role.updated','role',@roleId,'success',jsonb_build_object('affectedMembers', @affected));
            """,
            new { tenantId, actorId, roleId, affected },
            transaction,
            cancellationToken: cancellationToken));

        await transaction.CommitAsync(cancellationToken);
        return new UpdateRolePermissionsResult(
            UpdateRolePermissionsStatus.Updated,
            new TenantRole(role.Id, role.IsSystem ? role.Name : name, role.IsSystem, allowed),
            affected);
    }

    private sealed record RoleRow(Guid Id, string Name, bool IsSystem);

    private sealed record OverviewMetrics(
        int ActiveMembers,
        int ValidInvitations,
        int SeatLimit,
        int FailedInvitations,
        int BlockedMembers);
}
