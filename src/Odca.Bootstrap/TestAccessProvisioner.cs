using System.Globalization;
using Dapper;
using Npgsql;
using Odca.Application.Identity;

namespace Odca.Bootstrap;

public sealed class TestAccessProvisioner(IPasswordService passwordService, string seedSqlPath)
{
    public const string AdministratorEmail = "admin@odca.local";
    public const string ClientEmail = "cliente.teste@odca.local";
    public const string DemoTenantName = "ODCA Cliente de Demonstração";
    public const string DemoTenantCode = "ODCA-DEMO-LOCAL";
    private const string DemoGrantReason = "development-demo-access";

    public async Task<TestAccessResult> ProvisionAsync(
        string connectionString,
        string administratorPassword,
        string? clientPassword,
        bool rotateAdministrator,
        bool rotateClient,
        Func<string> passwordFactory,
        string? requestedAdministratorPassword = null,
        string? requestedClientPassword = null)
    {
        if (!File.Exists(seedSqlPath))
        {
            throw new FileNotFoundException("O SQL de provisionamento de Development não foi encontrado.", seedSqlPath);
        }

        var seedSql = await File.ReadAllTextAsync(seedSqlPath);
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var transaction = await connection.BeginTransactionAsync();
        await connection.ExecuteAsync(new CommandDefinition(
            "SELECT pg_advisory_xact_lock(hashtext('odca.development.test-access'));", transaction: transaction));

        await EnsureSchemaAsync(connection, transaction);
        var administrator = await FindUserAsync(connection, transaction, Normalize(AdministratorEmail));
        var client = await FindUserAsync(connection, transaction, Normalize(ClientEmail));
        var tenant = await FindTenantAsync(connection, transaction);
        await ValidateExistingAsync(connection, transaction, administrator, client, tenant);

        var administratorCreated = administrator is null;
        var clientCreated = client is null;
        if (administratorCreated || rotateAdministrator)
        {
            administratorPassword = requestedAdministratorPassword ?? passwordFactory();
        }
        if (clientCreated || rotateClient)
        {
            do { clientPassword = requestedClientPassword ?? passwordFactory(); }
            while (string.Equals(clientPassword, administratorPassword, StringComparison.Ordinal));
        }

        var administratorId = administrator?.Id ?? Guid.NewGuid();
        var clientId = client?.Id ?? Guid.NewGuid();
        var tenantId = tenant?.Id ?? Guid.NewGuid();
        var administratorHash = administratorCreated || rotateAdministrator
            ? Hash(administratorId, AdministratorEmail, "Administrador da plataforma", administratorPassword, true)
            : administrator!.PasswordHash;
        var clientHash = clientCreated || rotateClient
            ? Hash(clientId, ClientEmail, "Cliente de demonstração", clientPassword!, false)
            : client!.PasswordHash;

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

        await connection.ExecuteAsync(new CommandDefinition(seedSql, new
        {
            AdministratorId = administratorId,
            AdministratorEmail,
            AdministratorNormalized = Normalize(AdministratorEmail),
            AdministratorName = "Administrador da plataforma",
            AdministratorHash = administratorHash,
            RotateAdministrator = rotateAdministrator,
            ClientId = clientId,
            ClientEmail,
            ClientNormalized = Normalize(ClientEmail),
            ClientName = "Cliente de demonstração",
            ClientHash = clientHash,
            RotateClient = rotateClient,
            TenantId = tenantId,
            TenantCode = DemoTenantCode,
            TenantName = DemoTenantName,
            PlanVersionId = planVersionId.Value,
            GrantReason = DemoGrantReason
        }, transaction));

        administrator = await FindUserAsync(connection, transaction, Normalize(AdministratorEmail))
            ?? throw new InvalidOperationException("A identidade superadministradora não foi persistida.");
        client = await FindUserAsync(connection, transaction, Normalize(ClientEmail))
            ?? throw new InvalidOperationException("A identidade cliente não foi persistida.");
        var verification = await VerifyAsync(connection, transaction, administratorId, clientId, tenantId);
        var administratorMatches = passwordService.Verify(ToCredential(administrator), administrator.PasswordHash, administratorPassword);
        var clientMatches = clientPassword is not null &&
            passwordService.Verify(ToCredential(client), client.PasswordHash, clientPassword);
        if (!AllVerified(verification) ||
            (administratorCreated || rotateAdministrator) && (!administratorMatches || !administrator.MustChangePassword) ||
            (clientCreated || rotateClient) && (!clientMatches || !client.MustChangePassword))
        {
            throw new InvalidOperationException("As pós-condições do provisionamento não foram confirmadas; transação revertida.");
        }

        await transaction.CommitAsync();
        return new(administratorPassword, clientPassword, administratorCreated, clientCreated,
            administratorMatches, clientMatches, verification);
    }

    private static async Task EnsureSchemaAsync(NpgsqlConnection connection, NpgsqlTransaction transaction)
    {
        var version = await connection.ExecuteScalarAsync<int?>(new CommandDefinition(
            "SELECT max(version) FROM odca.schema_migrations;", transaction: transaction));
        if (version != Odca.Infrastructure.Database.DatabaseSchema.CurrentVersion)
        {
            throw new InvalidOperationException(
                $"Schema ausente ou desatualizado (encontrado {version?.ToString(CultureInfo.InvariantCulture) ?? "nenhum"}, esperado {Odca.Infrastructure.Database.DatabaseSchema.CurrentVersion}). Execute 'dotnet run --project src/Odca.Bootstrap -- migrate'.");
        }
    }

    private static async Task ValidateExistingAsync(NpgsqlConnection connection, NpgsqlTransaction transaction,
        ProvisionedUser? administrator, ProvisionedUser? client, ProvisionedTenant? tenant)
    {
        if (administrator is not null && (!administrator.IsPlatformAdministrator || administrator.IsDeleted ||
            administrator.LockedUntil is not null))
        {
            throw new InvalidOperationException(
                $"A identidade {AdministratorEmail} existe, mas não é um superadministrador ativo e desbloqueado; nenhuma alteração foi feita.");
        }
        if (client is not null && (client.IsPlatformAdministrator || client.IsDeleted || client.LockedUntil is not null))
        {
            throw new InvalidOperationException(
                $"A identidade {ClientEmail} existe com estado ou privilégio incompatível; nenhuma alteração foi feita.");
        }
        if (tenant is not null && (tenant.DisplayName != DemoTenantName || tenant.IsDeleted || tenant.Status != "active"))
        {
            throw new InvalidOperationException("O código reservado pertence a uma organização incompatível, excluída ou suspensa.");
        }
        if (tenant is not null)
        {
            var role = await connection.QuerySingleOrDefaultAsync<ExistingRole>(new CommandDefinition(
                "SELECT display_name AS DisplayName,is_system AS IsSystem FROM odca.roles WHERE tenant_id=@tenantId AND code='tenant-administrator' FOR UPDATE;",
                new { tenantId = tenant.Id }, transaction));
            if (role is not null && (role.DisplayName != "Administrador da organização" || !role.IsSystem))
                throw new InvalidOperationException("O perfil tenant-administrator existente é incompatível; nenhuma alteração foi feita.");
        }
        if (client is null) return;

        var memberships = (await connection.QueryAsync<ExistingMembership>(new CommandDefinition(
            """
            SELECT m.tenant_id AS TenantId, t.business_code AS BusinessCode, m.status AS Status
              FROM odca.memberships m JOIN odca.tenants t ON t.id=m.tenant_id
             WHERE m.user_id=@id FOR UPDATE OF m;
            """, new { client.Id }, transaction))).ToArray();
        if (memberships.Length != 1 || memberships[0].BusinessCode != DemoTenantCode || memberships[0].Status != "active")
        {
            throw new InvalidOperationException(
                $"A identidade {ClientEmail} não possui exatamente um vínculo ativo com a organização de demonstração; conta preservada.");
        }
        if (tenant is null || memberships[0].TenantId != tenant.Id)
        {
            throw new InvalidOperationException("O vínculo existente do cliente não corresponde à organização reservada.");
        }

        var hasExpectedRole = await connection.ExecuteScalarAsync<bool>(new CommandDefinition(
            """
            SELECT EXISTS(SELECT 1 FROM odca.member_roles mr JOIN odca.roles r ON r.id=mr.role_id
              WHERE mr.tenant_id=@tenantId AND mr.user_id=@userId AND r.tenant_id=@tenantId
                AND r.code='tenant-administrator');
            """, new { tenantId = tenant.Id, userId = client.Id }, transaction));
        if (!hasExpectedRole) throw new InvalidOperationException("O cliente existente não possui o perfil esperado; conta preservada.");

        var subscription = await connection.QuerySingleOrDefaultAsync<ExistingSubscription>(new CommandDefinition(
            """
            SELECT p.code AS PlanCode, s.status AS Status, s.commercial_state AS CommercialState,
                   s.manual_grant_reason AS ManualGrantReason
              FROM odca.subscriptions s JOIN odca.plan_versions p ON p.id=s.plan_version_id
             WHERE s.tenant_id=@tenantId FOR UPDATE OF s;
            """, new { tenantId = tenant.Id }, transaction));
        if (subscription is not null && (subscription.PlanCode != "basic" || subscription.Status != "active" ||
            subscription.CommercialState != "active" || subscription.ManualGrantReason != DemoGrantReason))
        {
            throw new InvalidOperationException("A organização possui assinatura comercial ou concessão incompatível; assinatura preservada.");
        }
    }

    private string Hash(Guid id, string email, string name, string password, bool platform) =>
        passwordService.Hash(new UserCredential(id, email, name, string.Empty, 1, true, platform, null, false, null), password);

    private static Task<ProvisionedUser?> FindUserAsync(NpgsqlConnection connection, NpgsqlTransaction transaction, string normalized) =>
        connection.QuerySingleOrDefaultAsync<ProvisionedUser>(new CommandDefinition(
            """SELECT id AS Id,email AS Email,display_name AS DisplayName,password_hash AS PasswordHash,security_version AS SecurityVersion,is_platform_administrator AS IsPlatformAdministrator,must_change_password AS MustChangePassword,locked_until AS LockedUntil,is_deleted AS IsDeleted,mfa_confirmed_at AS MfaConfirmedAt FROM odca.users WHERE email_normalized=@normalized FOR UPDATE;""",
            new { normalized }, transaction));

    private static Task<ProvisionedTenant?> FindTenantAsync(NpgsqlConnection connection, NpgsqlTransaction transaction) =>
        connection.QuerySingleOrDefaultAsync<ProvisionedTenant>(new CommandDefinition(
            "SELECT id AS Id,display_name AS DisplayName,status AS Status,is_deleted AS IsDeleted FROM odca.tenants WHERE business_code=@code FOR UPDATE;",
            new { code = DemoTenantCode }, transaction));

    private static Task<TestAccessVerification> VerifyAsync(NpgsqlConnection connection, NpgsqlTransaction transaction,
        Guid administratorId, Guid clientId, Guid tenantId) => connection.QuerySingleAsync<TestAccessVerification>(new CommandDefinition(
            """
            SELECT EXISTS(SELECT 1 FROM odca.users WHERE id=@administratorId AND is_platform_administrator AND NOT is_deleted) AS AdministratorPersisted,
                   EXISTS(SELECT 1 FROM odca.users WHERE id=@clientId AND NOT is_platform_administrator AND NOT is_deleted AND locked_until IS NULL) AS ClientPersisted,
                   ((SELECT count(*)=1 FROM odca.memberships WHERE user_id=@clientId) AND
                    EXISTS(SELECT 1 FROM odca.memberships WHERE user_id=@clientId AND tenant_id=@tenantId AND status='active')) AS MembershipActive,
                   EXISTS(SELECT 1 FROM odca.member_roles mr JOIN odca.roles r ON r.id=mr.role_id AND r.tenant_id=mr.tenant_id WHERE mr.tenant_id=@tenantId AND mr.user_id=@clientId AND r.code='tenant-administrator') AS TenantAdministrator,
                   EXISTS(SELECT 1 FROM odca.subscriptions s JOIN odca.plan_versions p ON p.id=s.plan_version_id WHERE s.tenant_id=@tenantId AND s.status='active' AND s.commercial_state='active' AND s.manual_grant_reason=@reason AND p.code='basic') AS BasicPlanActive;
            """, new { administratorId, clientId, tenantId, reason = DemoGrantReason }, transaction));

    private static bool AllVerified(TestAccessVerification value) => value.AdministratorPersisted && value.ClientPersisted &&
        value.MembershipActive && value.TenantAdministrator && value.BasicPlanActive;
    private static UserCredential ToCredential(ProvisionedUser user) => new(user.Id, user.Email, user.DisplayName,
        user.PasswordHash, user.SecurityVersion, user.MustChangePassword, user.IsPlatformAdministrator,
        user.MfaConfirmedAt is null ? null : new DateTimeOffset(DateTime.SpecifyKind(user.MfaConfirmedAt.Value, DateTimeKind.Utc)),
        user.IsDeleted, user.LockedUntil is null ? null : new DateTimeOffset(DateTime.SpecifyKind(user.LockedUntil.Value, DateTimeKind.Utc)));
    private static string Normalize(string email) => email.Trim().ToUpperInvariant();

    private sealed record ProvisionedUser(Guid Id, string Email, string DisplayName, string PasswordHash,
        int SecurityVersion, bool IsPlatformAdministrator, bool MustChangePassword, DateTime? LockedUntil,
        bool IsDeleted, DateTime? MfaConfirmedAt);
    private sealed record ProvisionedTenant(Guid Id, string DisplayName, string Status, bool IsDeleted);
    private sealed record ExistingMembership(Guid TenantId, string BusinessCode, string Status);
    private sealed record ExistingSubscription(string PlanCode, string Status, string CommercialState, string? ManualGrantReason);
    private sealed record ExistingRole(string DisplayName, bool IsSystem);
}

public sealed record TestAccessVerification(bool AdministratorPersisted, bool ClientPersisted,
    bool MembershipActive, bool TenantAdministrator, bool BasicPlanActive);

public sealed record TestAccessResult(string AdministratorPassword, string? ClientPassword,
    bool AdministratorCreated, bool ClientCreated, bool AdministratorPasswordMatches,
    bool ClientPasswordMatches, TestAccessVerification Verification);
