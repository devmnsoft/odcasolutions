using Dapper;
using Npgsql;
using Odca.Application.Identity;

namespace Odca.Bootstrap;

internal sealed class TestAccessProvisioner(IPasswordService passwordService)
{
    internal const string ClientEmail = "cliente.teste@odca.local";
    internal const string DemoTenantName = "ODCA Cliente de Demonstração";
    private const string DemoTenantCode = "ODCA-DEMO-LOCAL";
    private const string DemoGrantReason = "development-demo-access";

    public async Task<TestAccessResult> ProvisionAsync(
        string connectionString,
        string administratorEmail,
        string administratorPassword,
        string? clientPassword,
        bool rotatePasswords,
        Func<string> passwordFactory)
    {
        var adminNormalized = Normalize(administratorEmail);
        var clientNormalized = Normalize(ClientEmail);
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var transaction = await connection.BeginTransactionAsync();

        var administrator = await FindUserAsync(connection, transaction, adminNormalized);
        var administratorCreated = administrator is null;
        if (administrator is not null && !administrator.IsPlatformAdministrator)
        {
            throw new InvalidOperationException(
                $"A identidade {administratorEmail} já existe sem o perfil de plataforma; nenhuma elevação foi feita.");
        }

        if (administrator is null)
        {
            administrator = await InsertUserAsync(
                connection, transaction, administratorEmail, "Administrador da plataforma", administratorPassword, true);
        }
        else if (rotatePasswords)
        {
            administratorPassword = passwordFactory();
            administrator = await RotatePasswordAsync(connection, transaction, administrator, administratorPassword);
        }

        var client = await FindUserAsync(connection, transaction, clientNormalized);
        var clientCreated = client is null;
        if (client is not null && client.IsPlatformAdministrator)
        {
            throw new InvalidOperationException("A identidade reservada ao cliente possui privilégio de plataforma; provisionamento recusado.");
        }

        if (client is not null)
        {
            var belongsToDemo = await connection.ExecuteScalarAsync<bool>(new CommandDefinition(
                """SELECT EXISTS (SELECT 1 FROM odca.memberships m JOIN odca.tenants t ON t.id=m.tenant_id WHERE m.user_id=@id AND t.business_code=@code);""",
                new { client.Id, code = DemoTenantCode }, transaction));
            if (!belongsToDemo)
            {
                throw new InvalidOperationException(
                    $"A identidade {ClientEmail} já existe sem vínculo com a demonstração; finalidade não confirmada e conta preservada.");
            }
        }

        if (client is null)
        {
            clientPassword = passwordFactory();
            client = await InsertUserAsync(
                connection, transaction, ClientEmail, "Cliente de demonstração", clientPassword, false);
        }
        else if (rotatePasswords)
        {
            clientPassword = passwordFactory();
            client = await RotatePasswordAsync(connection, transaction, client, clientPassword);
        }

        var tenantId = await connection.ExecuteScalarAsync<Guid>(new CommandDefinition(
            """
            INSERT INTO odca.tenants (business_code, display_name, status)
            VALUES (@code, @name, 'active')
            ON CONFLICT (business_code) DO UPDATE SET updated_at = now()
              WHERE odca.tenants.display_name = EXCLUDED.display_name
                AND NOT odca.tenants.is_deleted
            RETURNING id;
            """, new { code = DemoTenantCode, name = DemoTenantName }, transaction));
        if (tenantId == Guid.Empty)
        {
            throw new InvalidOperationException("O código reservado à demonstração pertence a outra organização ou a uma organização excluída.");
        }

        var planVersionId = await connection.ExecuteScalarAsync<Guid?>(new CommandDefinition(
            """
            SELECT id FROM odca.plan_versions
             WHERE code='basic' AND status='published' AND effective_from <= now()
               AND (effective_until IS NULL OR effective_until > now())
             ORDER BY version DESC LIMIT 1;
            """, transaction: transaction));
        if (planVersionId is null)
        {
            throw new InvalidOperationException("Não existe versão Basic publicada e vigente; nenhuma concessão foi criada.");
        }

        await connection.ExecuteAsync(new CommandDefinition(
            """
            INSERT INTO odca.memberships (tenant_id, user_id, status)
            VALUES (@tenantId, @userId, 'active')
            ON CONFLICT (tenant_id, user_id) DO UPDATE SET status='active', updated_at=now();

            INSERT INTO odca.roles (scope_type, tenant_id, code, display_name, is_system)
            VALUES ('tenant', @tenantId, 'tenant-administrator', 'Administrador da organização', true)
            ON CONFLICT DO NOTHING;

            INSERT INTO odca.member_roles (tenant_id, user_id, role_id, assigned_by)
            SELECT @tenantId, @userId, r.id, @userId
              FROM odca.roles r
             WHERE r.tenant_id=@tenantId AND r.code='tenant-administrator'
            ON CONFLICT DO NOTHING;

            INSERT INTO odca.subscriptions
                (tenant_id, plan_version_id, commercial_state, status, manual_grant_reason, created_by)
            VALUES (@tenantId, @planVersionId, 'active', 'active', @reason, @userId)
            ON CONFLICT (tenant_id) DO UPDATE
                SET plan_version_id=EXCLUDED.plan_version_id,
                    commercial_state='active', status='active',
                    manual_grant_reason=EXCLUDED.manual_grant_reason, updated_at=now();

            INSERT INTO odca.audit_events
                (scope_type, tenant_id, actor_user_id, action, entity_type, entity_id, result, metadata)
            VALUES ('tenant', @tenantId, @userId, 'development.test_access.provisioned',
                    'tenant', @tenantId, 'success', jsonb_build_object('grant', @reason));
            """, new { tenantId, userId = client.Id, planVersionId, reason = DemoGrantReason }, transaction));

        await transaction.CommitAsync();

        var verification = await VerifyAsync(connection, administrator.Id, client.Id, tenantId);
        return new(
            administratorPassword,
            clientPassword,
            administratorCreated,
            clientCreated,
            VerifyPassword(administrator, administratorPassword),
            clientPassword is not null && VerifyPassword(client, clientPassword),
            verification);
    }

    private async Task<ProvisionedUser> InsertUserAsync(
        NpgsqlConnection connection, NpgsqlTransaction transaction, string email, string name, string password, bool platform)
    {
        var id = Guid.NewGuid();
        var template = Credential(id, email, name, string.Empty, 1, platform, true, null, false);
        var hash = passwordService.Hash(template, password);
        return await connection.QuerySingleAsync<ProvisionedUser>(new CommandDefinition(
            """
            INSERT INTO odca.users
                (id, email, email_normalized, login_normalized, display_name, password_hash,
                 must_change_password, is_platform_administrator, email_verified_at)
            VALUES (@id, @email, @normalized, @normalized, @name, @hash, true, @platform, now())
            RETURNING id AS Id, email AS Email, display_name AS DisplayName, password_hash AS PasswordHash,
                      security_version AS SecurityVersion, is_platform_administrator AS IsPlatformAdministrator,
                      must_change_password AS MustChangePassword, locked_until AS LockedUntil,
                      is_deleted AS IsDeleted, mfa_confirmed_at AS MfaConfirmedAt;
            """, new { id, email, normalized = Normalize(email), name, hash, platform }, transaction));
    }

    private async Task<ProvisionedUser> RotatePasswordAsync(
        NpgsqlConnection connection, NpgsqlTransaction transaction, ProvisionedUser user, string password)
    {
        var hash = passwordService.Hash(
            Credential(user.Id, user.Email, user.DisplayName, user.PasswordHash, user.SecurityVersion, user.IsPlatformAdministrator,
                true, ToUtc(user.LockedUntil), user.IsDeleted), password);
        return await connection.QuerySingleAsync<ProvisionedUser>(new CommandDefinition(
            """
            UPDATE odca.users SET password_hash=@hash, must_change_password=true,
                security_version=security_version+1, failed_login_count=0, locked_until=NULL, updated_at=now()
             WHERE id=@id
            RETURNING id AS Id, email AS Email, display_name AS DisplayName, password_hash AS PasswordHash,
                      security_version AS SecurityVersion, is_platform_administrator AS IsPlatformAdministrator,
                      must_change_password AS MustChangePassword, locked_until AS LockedUntil,
                      is_deleted AS IsDeleted, mfa_confirmed_at AS MfaConfirmedAt;

            UPDATE odca.sessions SET revoked_at=COALESCE(revoked_at, now()) WHERE user_id=@id;
            """, new { user.Id, hash }, transaction));
    }

    private static Task<ProvisionedUser?> FindUserAsync(
        NpgsqlConnection connection, NpgsqlTransaction transaction, string normalized) =>
        connection.QuerySingleOrDefaultAsync<ProvisionedUser>(new CommandDefinition(
            """
            SELECT id AS Id, email AS Email, display_name AS DisplayName, password_hash AS PasswordHash,
                   security_version AS SecurityVersion, is_platform_administrator AS IsPlatformAdministrator,
                   must_change_password AS MustChangePassword, locked_until AS LockedUntil,
                   is_deleted AS IsDeleted, mfa_confirmed_at AS MfaConfirmedAt
              FROM odca.users WHERE email_normalized=@normalized FOR UPDATE;
            """, new { normalized }, transaction));

    private static async Task<TestAccessVerification> VerifyAsync(
        NpgsqlConnection connection, Guid administratorId, Guid clientId, Guid tenantId) =>
        await connection.QuerySingleAsync<TestAccessVerification>(
            """
            SELECT EXISTS(SELECT 1 FROM odca.users WHERE id=@administratorId AND is_platform_administrator AND NOT is_deleted) AS AdministratorPersisted,
                   EXISTS(SELECT 1 FROM odca.users WHERE id=@clientId AND NOT is_platform_administrator AND NOT is_deleted AND locked_until IS NULL) AS ClientPersisted,
                   EXISTS(SELECT 1 FROM odca.memberships WHERE tenant_id=@tenantId AND user_id=@clientId AND status='active') AS MembershipActive,
                   EXISTS(SELECT 1 FROM odca.member_roles mr JOIN odca.roles r ON r.id=mr.role_id AND r.tenant_id=mr.tenant_id WHERE mr.tenant_id=@tenantId AND mr.user_id=@clientId AND r.code='tenant-administrator') AS TenantAdministrator,
                   EXISTS(SELECT 1 FROM odca.subscriptions s JOIN odca.plan_versions p ON p.id=s.plan_version_id WHERE s.tenant_id=@tenantId AND s.status='active' AND p.code='basic') AS BasicPlanActive;
            """, new { administratorId, clientId, tenantId });

    private bool VerifyPassword(ProvisionedUser user, string password) => passwordService.Verify(
        Credential(user.Id, user.Email, user.DisplayName, user.PasswordHash, user.SecurityVersion,
            user.IsPlatformAdministrator, user.MustChangePassword, ToUtc(user.LockedUntil), user.IsDeleted), user.PasswordHash, password);

    private static UserCredential Credential(Guid id, string email, string name, string hash, int version,
        bool platform, bool mustChange, DateTimeOffset? lockedUntil, bool deleted) =>
        new(id, email, name, hash, version, mustChange, platform, null, deleted, lockedUntil);

    private static DateTimeOffset? ToUtc(DateTime? value) => value is null
        ? null
        : new DateTimeOffset(DateTime.SpecifyKind(value.Value, DateTimeKind.Utc));

    private static string Normalize(string email) => email.Trim().ToUpperInvariant();

    private sealed record ProvisionedUser(Guid Id, string Email, string DisplayName, string PasswordHash,
        int SecurityVersion, bool IsPlatformAdministrator, bool MustChangePassword,
        DateTime? LockedUntil, bool IsDeleted, DateTime? MfaConfirmedAt);
}

internal sealed record TestAccessVerification(bool AdministratorPersisted, bool ClientPersisted,
    bool MembershipActive, bool TenantAdministrator, bool BasicPlanActive);

internal sealed record TestAccessResult(string AdministratorPassword, string? ClientPassword,
    bool AdministratorCreated, bool ClientCreated, bool AdministratorPasswordMatches,
    bool ClientPasswordMatches, TestAccessVerification Verification);
