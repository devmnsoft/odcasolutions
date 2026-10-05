using Dapper;
using Npgsql;

namespace Odca.IntegrationTests;

public sealed class HierarchicalAdministrationTests(DatabaseFixture database) : IClassFixture<DatabaseFixture>
{
    private static readonly Guid PlatformActorId = Guid.Parse("20000000-0000-0000-0000-000000000001");
    private static readonly Guid BasicV1 = Guid.Parse("30000000-0000-0000-0000-000000000001");
    private static readonly Guid EnterpriseV2 = Guid.Parse("30000000-0000-0000-0000-000000000013");
    private static readonly Guid BasicV2 = Guid.Parse("30000000-0000-0000-0000-000000000011");

    [Fact]
    public async Task PublishedCatalogDeclaresModulesWithoutChangingAnExistingContract()
    {
        await database.ResetAuthenticationScenarioAsync();
        await using var connection = new NpgsqlConnection(database.AdminConnectionString);
        await connection.OpenAsync();
        var published = (await connection.QueryAsync<(string Code, int Version, int Modules)>(
            """
            SELECT p.code AS Code, p.version AS Version,
                   (SELECT count(*)::int FROM odca.plan_entitlements e
                     WHERE e.plan_version_id = p.id AND e.enabled AND e.entitlement_code LIKE 'module.%') AS Modules
              FROM odca.plan_versions p
             WHERE p.status = 'published'
               AND p.effective_from <= now()
               AND (p.effective_until IS NULL OR p.effective_until > now())
             ORDER BY (SELECT min(e.limit_value) FROM odca.plan_entitlements e
                        WHERE e.plan_version_id = p.id AND e.entitlement_code = 'active_seats')
            """)).AsList();
        Assert.Equal(new[] { ("basic", 2, 7), ("intermediate", 2, 7), ("enterprise", 2, 7) },
            published.Select(item => (item.Code, item.Version, item.Modules)).ToArray());
        Assert.Equal(1, await connection.ExecuteScalarAsync<int>(
            "SELECT version FROM odca.plan_versions WHERE id = @id", new { id = BasicV1 }));
    }

    [Fact]
    public async Task DowngradeKeepsPeoplePatientsAndDoesNotRecordPayment()
    {
        await database.ResetAuthenticationScenarioAsync();
        var tenantId = Guid.NewGuid();
        var patientId = Guid.NewGuid();
        var users = Enumerable.Range(0, 4).Select(_ => Guid.NewGuid()).ToArray();
        await using var connection = new NpgsqlConnection(database.AdminConnectionString);
        await connection.OpenAsync();
        try
        {
            await connection.ExecuteAsync(
                """
                INSERT INTO odca.tenants(id, business_code, display_name, status)
                VALUES (@tenantId, @code, 'Organização do downgrade', 'active');
                INSERT INTO odca.subscriptions(tenant_id, plan_version_id, commercial_state, status, created_by)
                VALUES (@tenantId, @planId, 'active', 'active', @actor);
                """,
                new { tenantId, code = $"DG{tenantId:N}"[..20], planId = EnterpriseV2, actor = PlatformActorId });
            foreach (var userId in users)
            {
                await connection.ExecuteAsync(
                    """
                    INSERT INTO odca.users(id, email, email_normalized, login_normalized, display_name, password_hash, must_change_password, email_verified_at)
                    VALUES (@userId, @email, @email, @email, @email, 'hash', false, now());
                    INSERT INTO odca.memberships(tenant_id, user_id, status)
                    VALUES (@tenantId, @userId, 'active');
                    """,
                    new { userId, tenantId, email = $"{userId:N}@hierarchy.test" });
            }

            await connection.ExecuteAsync(
                """
                INSERT INTO odca.patients(id, tenant_id, full_name, created_by, updated_by)
                VALUES (@patientId, @tenantId, 'Paciente Preservado', @userId, @userId);
                """,
                new { patientId, tenantId, userId = users[0] });

            await using var tx = await connection.BeginTransactionAsync();
            await connection.ExecuteAsync(
                "SELECT set_config('odca.user_id', @actor, true)",
                new { actor = PlatformActorId.ToString() }, tx);
            var preview = await connection.QuerySingleAsync<PlanImpactRow>(
                """
                SELECT seats_over_limit AS "SeatsOverLimit", patient_count AS "PatientCount", policy AS "Policy"
                  FROM odca.preview_organization_plan_change(@actor, @tenant, 'basic')
                """,
                new { actor = PlatformActorId, tenant = tenantId }, tx);
            Assert.True(preview.SeatsOverLimit);
            Assert.Equal(1L, preview.PatientCount);
            Assert.Equal("keep_members_and_documents", preview.Policy);
            var applied = await connection.ExecuteScalarAsync<string>(
                "SELECT odca.apply_organization_plan_change(@actor, @tenant, 'basic', @reason)",
                new { actor = PlatformActorId, tenant = tenantId, reason = "Adequação contratual homologada" }, tx);
            Assert.Equal("applied", applied);
            await tx.CommitAsync();

            Assert.Equal(BasicV2, await connection.ExecuteScalarAsync<Guid>(
                "SELECT plan_version_id FROM odca.subscriptions WHERE tenant_id = @tenantId", new { tenantId }));
            Assert.Equal(4, await connection.ExecuteScalarAsync<int>(
                "SELECT count(*)::int FROM odca.memberships WHERE tenant_id = @tenantId AND status = 'active'", new { tenantId }));
            Assert.Equal(1, await connection.ExecuteScalarAsync<int>(
                "SELECT count(*)::int FROM odca.patients WHERE id = @patientId", new { patientId }));
            var metadata = await connection.ExecuteScalarAsync<string>(
                """
                SELECT metadata::text FROM odca.audit_events
                 WHERE tenant_id = @tenantId AND action = 'organization.plan_changed'
                 ORDER BY occurred_at DESC LIMIT 1
                """,
                new { tenantId });
            Assert.Contains("paymentRecorded", metadata, StringComparison.Ordinal);
            Assert.Contains("false", metadata, StringComparison.Ordinal);
            Assert.DoesNotContain("password", metadata, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            await connection.ExecuteAsync(
                """
                DELETE FROM odca.audit_events WHERE tenant_id = @tenantId;
                DELETE FROM odca.patients WHERE tenant_id = @tenantId;
                DELETE FROM odca.subscriptions WHERE tenant_id = @tenantId;
                DELETE FROM odca.memberships WHERE tenant_id = @tenantId;
                DELETE FROM odca.users WHERE id = ANY(@users);
                DELETE FROM odca.tenants WHERE id = @tenantId;
                """,
                new { tenantId, users });
        }
    }

    [Fact]
    public async Task StandardRolesStayDelegatedAndRejectPlatformPermissions()
    {
        await database.ResetAuthenticationScenarioAsync();
        var tenantId = Guid.NewGuid();
        await using var connection = new NpgsqlConnection(database.AdminConnectionString);
        await connection.OpenAsync();
        try
        {
            await connection.ExecuteAsync(
                "INSERT INTO odca.tenants(id, business_code, display_name, status) VALUES (@tenantId, @code, 'Papéis padrão', 'active')",
                new { tenantId, code = $"RL{tenantId:N}"[..20] });
            await connection.ExecuteAsync("SELECT odca.ensure_tenant_standard_roles(@tenantId)", new { tenantId });
            var delegated = await connection.QuerySingleAsync<(string Code, bool BillingManage)>(
                """
                SELECT r.code AS Code,
                       EXISTS(SELECT 1 FROM odca.role_permissions rp WHERE rp.role_id = r.id AND rp.permission_code = 'tenant.billing.manage') AS BillingManage
                  FROM odca.roles r
                 WHERE r.tenant_id = @tenantId AND r.display_name = 'Administrador delegado'
                """,
                new { tenantId });
            Assert.Equal("tenant-delegated-administrator", delegated.Code);
            Assert.False(delegated.BillingManage);
            var roleId = await connection.ExecuteScalarAsync<Guid>(
                "SELECT id FROM odca.roles WHERE tenant_id = @tenantId AND code = 'tenant-member'", new { tenantId });
            var exception = await Assert.ThrowsAsync<PostgresException>(() => connection.ExecuteAsync(
                "INSERT INTO odca.role_permissions(role_id, permission_code) VALUES (@roleId, 'platform.audit.read')",
                new { roleId }));
            Assert.Equal("42501", exception.SqlState);
        }
        finally
        {
            await connection.ExecuteAsync(
                """
                DELETE FROM odca.role_permissions WHERE role_id IN (SELECT id FROM odca.roles WHERE tenant_id = @tenantId);
                DELETE FROM odca.roles WHERE tenant_id = @tenantId;
                DELETE FROM odca.tenants WHERE id = @tenantId;
                """,
                new { tenantId });
        }
    }

    [Fact]
    public async Task ConcurrentRemovalCannotLeaveTheOrganizationWithoutAPrincipal()
    {
        await database.ResetAuthenticationScenarioAsync();
        var tenantId = Guid.NewGuid();
        var first = Guid.NewGuid();
        var second = Guid.NewGuid();
        await using var setup = new NpgsqlConnection(database.AdminConnectionString);
        await setup.OpenAsync();
        try
        {
            await setup.ExecuteAsync(
                """
                INSERT INTO odca.tenants(id, business_code, display_name, status)
                VALUES (@tenantId, @code, 'Dois principais', 'active');
                INSERT INTO odca.users(id, email, email_normalized, login_normalized, display_name, password_hash, must_change_password, email_verified_at)
                VALUES (@first, @firstEmail, @firstEmail, @firstEmail, 'Primeiro principal', 'hash', false, now()),
                       (@second, @secondEmail, @secondEmail, @secondEmail, 'Segundo principal', 'hash', false, now());
                INSERT INTO odca.memberships(tenant_id, user_id, status)
                VALUES (@tenantId, @first, 'active'), (@tenantId, @second, 'active');
                INSERT INTO odca.roles(scope_type, tenant_id, code, display_name, is_system)
                VALUES ('tenant', @tenantId, 'tenant-administrator', 'Administrador da organização', true);
                INSERT INTO odca.member_roles(tenant_id, user_id, role_id, assigned_by)
                SELECT @tenantId, member.user_id, role.id, @first
                  FROM odca.roles role
                  CROSS JOIN (VALUES (@first), (@second)) AS member(user_id)
                 WHERE role.tenant_id = @tenantId AND role.code = 'tenant-administrator';
                """,
                new
                {
                    tenantId,
                    code = $"AD{tenantId:N}"[..20],
                    first,
                    second,
                    firstEmail = $"{first:N}@hierarchy.test",
                    secondEmail = $"{second:N}@hierarchy.test"
                });

            var firstRemoval = RemovePrincipalAsync(tenantId, first);
            var secondRemoval = RemovePrincipalAsync(tenantId, second);
            var results = await Task.WhenAll(firstRemoval, secondRemoval);
            Assert.Equal(1, results.Count(item => item));
            Assert.Equal(1, await setup.ExecuteScalarAsync<int>(
                "SELECT odca.count_active_tenant_administrators(@tenantId)", new { tenantId }));
        }
        finally
        {
            await setup.ExecuteAsync(
                """
                DELETE FROM odca.member_roles WHERE tenant_id = @tenantId;
                DELETE FROM odca.roles WHERE tenant_id = @tenantId;
                DELETE FROM odca.memberships WHERE tenant_id = @tenantId;
                DELETE FROM odca.users WHERE id IN (@first, @second);
                DELETE FROM odca.tenants WHERE id = @tenantId;
                """,
                new { tenantId, first, second });
        }
    }

    [Fact]
    public async Task BlockingOneMembershipPreservesTheOtherOrganization()
    {
        await database.ResetAuthenticationScenarioAsync();
        var person = Guid.NewGuid();
        var tenantA = Guid.NewGuid();
        var tenantB = Guid.NewGuid();
        await using var connection = new NpgsqlConnection(database.AdminConnectionString);
        await connection.OpenAsync();
        try
        {
            await connection.ExecuteAsync(
                """
                INSERT INTO odca.users(id, email, email_normalized, login_normalized, display_name, password_hash, must_change_password, email_verified_at)
                VALUES (@person, @email, @email, @email, 'Pessoa em duas organizações', 'hash', false, now());
                INSERT INTO odca.tenants(id, business_code, display_name, status)
                VALUES (@tenantA, @codeA, 'Organização A', 'active'), (@tenantB, @codeB, 'Organização B', 'active');
                INSERT INTO odca.memberships(tenant_id, user_id, status)
                VALUES (@tenantA, @person, 'blocked'), (@tenantB, @person, 'active');
                """,
                new
                {
                    person,
                    email = $"{person:N}@hierarchy.test",
                    tenantA,
                    tenantB,
                    codeA = $"AA{tenantA:N}"[..20],
                    codeB = $"BB{tenantB:N}"[..20]
                });
            Assert.Equal("membership_blocked", await connection.ExecuteScalarAsync<string>(
                "SELECT state FROM odca.organization_operation_gate(@person, @tenantA, 'patients')",
                new { person, tenantA }));
            Assert.Equal("allowed", await connection.ExecuteScalarAsync<string>(
                "SELECT state FROM odca.organization_operation_gate(@person, @tenantB, 'patients')",
                new { person, tenantB }));
            Assert.False(await connection.ExecuteScalarAsync<bool>(
                "SELECT odca.tenant_actor_has_permission(@person, @tenantA, 'tenant.patients.read')",
                new { person, tenantA }));
        }
        finally
        {
            await connection.ExecuteAsync(
                """
                DELETE FROM odca.memberships WHERE user_id = @person;
                DELETE FROM odca.tenants WHERE id IN (@tenantA, @tenantB);
                DELETE FROM odca.users WHERE id = @person;
                """,
                new { person, tenantA, tenantB });
        }
    }

    private sealed class PlanImpactRow
    {
        public bool SeatsOverLimit { get; set; }
        public long PatientCount { get; set; }
        public string Policy { get; set; } = string.Empty;
    }

    private async Task<bool> RemovePrincipalAsync(Guid tenantId, Guid userId)
    {
        await using var connection = new NpgsqlConnection(database.AdminConnectionString);
        await connection.OpenAsync();
        await using var tx = await connection.BeginTransactionAsync();
        var allowed = await connection.ExecuteScalarAsync<bool>(
            "SELECT odca.ensure_not_removing_last_admin(@tenantId, @userId, false, true)",
            new { tenantId, userId }, tx);
        if (allowed)
        {
            await connection.ExecuteAsync(
                "UPDATE odca.memberships SET status = 'inactive' WHERE tenant_id = @tenantId AND user_id = @userId",
                new { tenantId, userId }, tx);
        }

        await tx.CommitAsync();
        return allowed;
    }
}
