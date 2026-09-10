using System.Security.Cryptography;
using System.Text;
using Dapper;
using Npgsql;
using Odca.Application.Tenancy;
using Microsoft.AspNetCore.DataProtection;

namespace Odca.Infrastructure.Tenancy;

public sealed class NpgsqlTenantAdministrationRepository(NpgsqlDataSource dataSource, IDataProtectionProvider protectionProvider) : ITenantAdministrationRepository
{
    private readonly IDataProtector tokenProtector = protectionProvider.CreateProtector("ODCA.TenantInvitation.v1");
    public async Task<IReadOnlyList<OrganizationAccess>> ListOrganizationsAsync(Guid userId, CancellationToken cancellationToken)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        var rows = await connection.QueryAsync<OrganizationAccess>(new CommandDefinition(
            """
SELECT id AS "Id", name AS "Name", status AS "Status", version AS "Version", permissions AS "Permissions" FROM odca.user_organizations(@userId);
""",
            new { userId }, cancellationToken: cancellationToken));
        return rows.AsList();
    }

    public async Task<OrganizationRecord?> GetOrganizationAsync(Guid actorId, Guid tenantId, CancellationToken cancellationToken)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction = await AuthorizedTransactionAsync(connection, actorId, tenantId, "tenant.organization.read", cancellationToken);
        if (transaction is null) return null;
        var result = await connection.QuerySingleOrDefaultAsync<OrganizationRecord>(new CommandDefinition(
            """
SELECT id AS "Id", display_name AS "Name", timezone AS "Timezone", status AS "Status", version AS "Version" FROM odca.tenants WHERE id=@tenantId;
""",
            new { tenantId }, transaction, cancellationToken: cancellationToken));
        await transaction.CommitAsync(cancellationToken);
        return result;
    }

    public async Task<UpdateOrganizationResult> UpdateOrganizationAsync(Guid actorId, Guid tenantId, string name, string timezone, long version, CancellationToken cancellationToken)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction = await AuthorizedTransactionAsync(connection, actorId, tenantId, "tenant.organization.manage", cancellationToken);
        if (transaction is null) return UpdateOrganizationResult.Forbidden;
        var updated = await connection.ExecuteAsync(new CommandDefinition(
            "UPDATE odca.tenants SET display_name=@name,timezone=@timezone,version=version+1,updated_at=now() WHERE id=@tenantId AND version=@version;",
            new { actorId, tenantId, name, timezone, version }, transaction, cancellationToken: cancellationToken));
        if (updated == 0) { await transaction.RollbackAsync(cancellationToken); return UpdateOrganizationResult.Conflict; }
        await connection.ExecuteAsync(new CommandDefinition("INSERT INTO odca.audit_events(scope_type,tenant_id,actor_user_id,action,entity_type,entity_id,result,metadata) VALUES('tenant',@tenantId,@actorId,'tenant.organization.updated','tenant',@tenantId,'success',jsonb_build_object('version',@nextVersion));", new { actorId, tenantId, nextVersion=version+1 }, transaction, cancellationToken:cancellationToken));
        await transaction.CommitAsync(cancellationToken); return UpdateOrganizationResult.Updated;
    }

    public async Task<IReadOnlyList<TeamMember>> ListMembersAsync(Guid actorId, Guid tenantId, string? search, string? status, CancellationToken cancellationToken)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction = await AuthorizedTransactionAsync(connection, actorId, tenantId, "tenant.team.read", cancellationToken);
        if (transaction is null) return [];
        var rows = await connection.QueryAsync<TeamMember>(new CommandDefinition(
            """
SELECT u.id AS "UserId",u.display_name AS "Name",u.email AS "Email",m.status AS "Status",
                COALESCE(array_agg(DISTINCT r.display_name) FILTER(WHERE r.id IS NOT NULL),ARRAY[]::text[]) AS "Roles"
                FROM odca.memberships m JOIN odca.users u ON u.id=m.user_id LEFT JOIN odca.member_roles mr ON (mr.tenant_id,mr.user_id)=(m.tenant_id,m.user_id)
                LEFT JOIN odca.roles r ON r.id=mr.role_id WHERE m.tenant_id=@tenantId
                AND (@status IS NULL OR m.status=@status) AND (@search IS NULL OR u.display_name ILIKE '%'||@search||'%' OR u.email ILIKE '%'||@search||'%')
                GROUP BY u.id,u.display_name,u.email,m.status ORDER BY u.display_name LIMIT 100;
""",
            new { tenantId, search = string.IsNullOrWhiteSpace(search) ? null : search.Trim(), status }, transaction, cancellationToken: cancellationToken));
        await transaction.CommitAsync(cancellationToken); return rows.AsList();
    }

    public async Task<IReadOnlyList<TenantRole>> ListRolesAsync(Guid actorId, Guid tenantId, CancellationToken cancellationToken)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction = await AuthorizedTransactionAsync(connection, actorId, tenantId, "tenant.team.read", cancellationToken);
        if (transaction is null) return [];
        var rows = await connection.QueryAsync<TenantRole>(new CommandDefinition(
            """
SELECT r.id AS "Id",r.display_name AS "Name",r.is_system AS "IsSystem",COALESCE(array_agg(rp.permission_code) FILTER(WHERE rp.permission_code IS NOT NULL),ARRAY[]::text[]) AS "Permissions" FROM odca.roles r LEFT JOIN odca.role_permissions rp ON rp.role_id=r.id WHERE r.tenant_id=@tenantId GROUP BY r.id ORDER BY r.display_name;
""",
            new { tenantId }, transaction, cancellationToken: cancellationToken));
        await transaction.CommitAsync(cancellationToken); return rows.AsList();
    }

    public async Task<TenantRole?> CreateRoleAsync(Guid actorId, Guid tenantId, string name, IReadOnlyCollection<string> permissions, CancellationToken cancellationToken)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction = await AuthorizedTransactionAsync(connection, actorId, tenantId, "tenant.team.manage", cancellationToken);
        if (transaction is null) return null;
        var allowed = (await connection.QueryAsync<string>(new CommandDefinition("""
SELECT p.code FROM odca.permissions p WHERE p.delegable AND p.code=ANY(@permissions) AND EXISTS(SELECT 1 FROM odca.member_roles mr JOIN odca.roles ar ON ar.id=mr.role_id LEFT JOIN odca.role_permissions arp ON arp.role_id=ar.id WHERE mr.tenant_id=@tenantId AND mr.user_id=@actorId AND (ar.code='tenant-administrator' OR arp.permission_code=p.code));
""",new { permissions=permissions.ToArray(),tenantId,actorId },transaction,cancellationToken:cancellationToken))).ToArray();
        if (allowed.Length != permissions.Distinct(StringComparer.Ordinal).Count()) { await transaction.RollbackAsync(cancellationToken); return null; }
        var id=Guid.NewGuid(); var code="custom-"+id.ToString("N");
        await connection.ExecuteAsync(new CommandDefinition("INSERT INTO odca.roles(id,scope_type,tenant_id,code,display_name,is_system) VALUES(@id,'tenant',@tenantId,@code,@name,false); INSERT INTO odca.role_permissions(role_id,permission_code) SELECT @id,unnest(@allowed);",new{id,tenantId,code,name,allowed},transaction,cancellationToken:cancellationToken));
        await transaction.CommitAsync(cancellationToken); return new(id,name,false,allowed);
    }

    public async Task<CreateInvitationResult> CreateInvitationAsync(Guid actorId, Guid tenantId, string email, Guid roleId, string idempotencyKey, CancellationToken cancellationToken)
    {
        await using var connection=await dataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction=await AuthorizedTransactionAsync(connection,actorId,tenantId,"tenant.team.manage",cancellationToken); 
        if(transaction is null) return new(CreateInvitationResultStatus.Forbidden, null);
        await connection.ExecuteAsync(new CommandDefinition("SELECT pg_advisory_xact_lock(hashtextextended(@key,0));",new{key=tenantId.ToString()},transaction,cancellationToken:cancellationToken));
        
        var normalized = email.Trim().ToUpperInvariant();
        var existing = await connection.QuerySingleOrDefaultAsync<dynamic>(new CommandDefinition("SELECT id, recipient_normalized, role_id, recipient_email, status, expires_at FROM odca.tenant_invitations WHERE tenant_id=@tenantId AND idempotency_key=@idempotencyKey;", new { tenantId, idempotencyKey }, transaction, cancellationToken: cancellationToken));
        if (existing is not null)
        {
            if (existing.recipient_normalized == normalized && existing.role_id == roleId)
                return new(CreateInvitationResultStatus.Existing, new InvitationRecord((Guid)existing.id, (string)existing.recipient_email, (string)existing.status, (DateTimeOffset)existing.expires_at));
            return new(CreateInvitationResultStatus.Conflict, null);
        }

        var seatLimit=await connection.ExecuteScalarAsync<int?>(new CommandDefinition("SELECT e.limit_value FROM odca.subscriptions s JOIN odca.plan_entitlements e ON e.plan_version_id=s.plan_version_id WHERE s.tenant_id=@tenantId AND s.status='active' AND s.commercial_state='active' AND e.entitlement_code='active_seats';",new{tenantId},transaction,cancellationToken:cancellationToken));
        var occupied=await connection.ExecuteScalarAsync<long>(new CommandDefinition("SELECT (SELECT count(*) FROM odca.memberships WHERE tenant_id=@tenantId AND status='active')+(SELECT count(*) FROM odca.tenant_invitations WHERE tenant_id=@tenantId AND status IN ('pending','sent') AND expires_at>now());",new{tenantId},transaction,cancellationToken:cancellationToken));
        var validRole=await connection.ExecuteScalarAsync<bool>(new CommandDefinition("SELECT EXISTS(SELECT 1 FROM odca.roles WHERE tenant_id=@tenantId AND id=@roleId);",new{tenantId,roleId},transaction,cancellationToken:cancellationToken));
        
        if (!validRole) { await transaction.RollbackAsync(cancellationToken); return new(CreateInvitationResultStatus.InvalidRole, null); }
        if (seatLimit is null || occupied >= seatLimit) { await transaction.RollbackAsync(cancellationToken); return new(CreateInvitationResultStatus.QuotaExceeded, null); }
        
        var token=Convert.ToHexString(RandomNumberGenerator.GetBytes(32)).ToLowerInvariant(); var hash=Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token))).ToLowerInvariant(); var protectedToken=tokenProtector.Protect(token); var id=Guid.NewGuid(); var expires=DateTimeOffset.UtcNow.AddDays(7);
        
        var row=await connection.QuerySingleOrDefaultAsync<InvitationRecord>(new CommandDefinition("""
            INSERT INTO odca.tenant_invitations(id,tenant_id,recipient_email,recipient_normalized,role_id,token_hash,protected_token,idempotency_key,expires_at,created_by) 
            VALUES(@id,@tenantId,@email,@normalized,@roleId,@hash,@protectedToken,@idempotencyKey,@expires,@actorId) 
            RETURNING id AS "Id",recipient_email AS "Recipient",status AS "State",expires_at AS "ExpiresAt";
            """,new{id,tenantId,email=email.Trim(),normalized,roleId,hash,protectedToken,idempotencyKey,expires,actorId},transaction,cancellationToken:cancellationToken));
        
        await connection.ExecuteAsync(new CommandDefinition("""
            INSERT INTO odca.notification_outbox(id,tenant_id,invitation_id,kind,destination,protected_payload) 
            VALUES(gen_random_uuid(),@tenantId,@id,'invitation',@email,@protectedToken);
            """,new{tenantId,id,email=email.Trim(),protectedToken},transaction,cancellationToken:cancellationToken));
        
        await transaction.CommitAsync(cancellationToken); 
        return new(CreateInvitationResultStatus.Created, row!);
    }

    public async Task<bool> AcceptInvitationAsync(Guid actorId,Guid invitationId,string tokenHash,CancellationToken cancellationToken)
    {
        await using var connection=await dataSource.OpenConnectionAsync(cancellationToken);
        return await connection.ExecuteScalarAsync<bool>(new CommandDefinition("SELECT odca.accept_tenant_invitation(@actorId,@invitationId,@tokenHash);",new{actorId,invitationId,tokenHash},cancellationToken:cancellationToken));
    }

    private static async Task<NpgsqlTransaction?> AuthorizedTransactionAsync(NpgsqlConnection connection,Guid actorId,Guid tenantId,string permission,CancellationToken cancellationToken)
    {
        var tx=await connection.BeginTransactionAsync(cancellationToken); var allowed=await connection.ExecuteScalarAsync<bool>(new CommandDefinition("SELECT odca.tenant_actor_has_permission(@actorId,@tenantId,@permission);",new{actorId,tenantId,permission},tx,cancellationToken:cancellationToken));
        if(!allowed){await tx.RollbackAsync(cancellationToken);await tx.DisposeAsync();return null;} await connection.ExecuteAsync(new CommandDefinition("SELECT set_config('odca.tenant_id',@tenantId,true);",new{tenantId=tenantId.ToString()},tx,cancellationToken:cancellationToken)); return tx;
    }
}
