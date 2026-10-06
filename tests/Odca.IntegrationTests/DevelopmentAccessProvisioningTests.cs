using System.Net;
using System.Net.Http.Json;
using Dapper;
using Npgsql;
using Odca.Bootstrap;
using Odca.Contracts.Identity;
using Odca.Infrastructure.Identity;

namespace Odca.IntegrationTests;

public sealed class DevelopmentAccessProvisioningTests(DatabaseFixture database) : IClassFixture<DatabaseFixture>
{
    [Fact]
    public void RequiredDevelopmentPasswordsUseTheRealPasswordPolicy()
    {
        Assert.Empty(Odca.Domain.Identity.PasswordPolicy.Validate(TestAccessProvisioner.AdministratorInitialPassword));
        Assert.Empty(Odca.Domain.Identity.PasswordPolicy.Validate(TestAccessProvisioner.ClientInitialPassword));
        Assert.Empty(Odca.Domain.Identity.PasswordPolicy.Validate(TestAccessProvisioner.OperatorInitialPassword));
    }

    [Fact]
    public void DevelopmentSeedIsNativePostgreSqlAndContainsNoDapperVariables()
    {
        var sql = File.ReadAllText(FindRepositoryFile("database", "development", "seed-test-access.sql"));

        Assert.Contains("DO $seed$", sql, StringComparison.Ordinal);
        Assert.Contains("\\set ON_ERROR_STOP on", sql, StringComparison.Ordinal);
        Assert.DoesNotContain("@AdministratorId", sql, StringComparison.Ordinal);
        Assert.DoesNotContain("@OperatorId", sql, StringComparison.Ordinal);
        Assert.DoesNotContain("@ClientId", sql, StringComparison.Ordinal);
        Assert.DoesNotContain("@TenantId", sql, StringComparison.Ordinal);
        Assert.DoesNotContain(TestAccessProvisioner.AdministratorInitialPassword, sql, StringComparison.Ordinal);
        Assert.DoesNotContain(TestAccessProvisioner.ClientInitialPassword, sql, StringComparison.Ordinal);
        Assert.DoesNotContain("SET password_hash", sql, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("UPDATE odca.sessions", sql, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("failed_login_count=0", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Reexecution deliberately preserves password hashes", sql, StringComparison.Ordinal);
        Assert.Contains(TestAccessProvisioner.DemoTenantName, sql, StringComparison.Ordinal);
        Assert.Contains(TestAccessProvisioner.DemoTenantCode, sql, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ProvisionedAccountsAuthenticateFromDatabaseAndRemainRestricted()
    {
        await ResetScenarioAsync();
        var seed = FindRepositoryFile("database", "development", "seed-test-access.sql");
        var provisioner = new TestAccessProvisioner(new AspNetPasswordService(), seed);
        var generated = new Queue<string>([
            "Admin!Development2026-Unique",
            "Operator!Development2026-Unique",
            "Client!Development2026-Unique"
        ]);
        var first = await provisioner.ProvisionAsync(database.AdminConnectionString,
            "unused-admin", null, true, true, generated.Dequeue,
            operatorPassword: null, rotateOperator: true, integrationTestFixture: true);

        Assert.True(first.AdministratorPasswordMatches);
        Assert.True(first.OperatorPasswordMatches);
        Assert.True(first.ClientPasswordMatches);
        Assert.NotEqual(first.AdministratorPassword, first.OperatorPassword);
        Assert.NotEqual(first.OperatorPassword, first.ClientPassword);
        Assert.True(first.Verification.AdministratorPersisted);
        Assert.True(first.Verification.OperatorPersisted);
        Assert.True(first.Verification.ClientPersisted);
        Assert.True(first.Verification.MembershipActive);
        Assert.True(first.Verification.TenantAdministrator);
        Assert.True(first.Verification.TenantOperator);
        Assert.True(first.Verification.BasicPlanActive);

        await using (var verificationConnection = new NpgsqlConnection(database.AdminConnectionString))
        {
            var demoTenant = await verificationConnection.QuerySingleAsync<(string BusinessCode, string DisplayName)>(
                "SELECT business_code AS BusinessCode, display_name AS DisplayName FROM odca.tenants WHERE business_code=@code;",
                new { code = TestAccessProvisioner.DemoTenantCode });
            Assert.Equal(TestAccessProvisioner.DemoTenantCode, demoTenant.BusinessCode);
            Assert.Equal(TestAccessProvisioner.DemoTenantName, demoTenant.DisplayName);
        }

        var repeated = await provisioner.ProvisionAsync(database.AdminConnectionString,
            first.AdministratorPassword, first.ClientPassword, false, false,
            () => throw new InvalidOperationException("A reexecução não deve gerar senha."),
            operatorPassword: first.OperatorPassword, rotateOperator: false, integrationTestFixture: true);
        Assert.False(repeated.AdministratorCreated);
        Assert.False(repeated.OperatorCreated);
        Assert.False(repeated.ClientCreated);
        Assert.True(repeated.AdministratorPasswordMatches);
        Assert.True(repeated.OperatorPasswordMatches);
        Assert.True(repeated.ClientPasswordMatches);

        var concurrent = await Task.WhenAll(Enumerable.Range(0, 2).Select(_ =>
            provisioner.ProvisionAsync(database.AdminConnectionString,
                first.AdministratorPassword, first.ClientPassword, false, false,
                () => throw new InvalidOperationException("A reexecução concorrente não deve gerar senha."),
                operatorPassword: first.OperatorPassword, rotateOperator: false, integrationTestFixture: true)));
        Assert.All(concurrent, item =>
        {
            Assert.False(item.AdministratorCreated);
            Assert.False(item.OperatorCreated);
            Assert.False(item.ClientCreated);
            Assert.True(item.Verification.MembershipActive);
            Assert.True(item.Verification.TenantOperator);
        });

        await using var factory = database.CreateApi();
        using var http = factory.CreateClient();
        var admin = await LoginAsync(http, TestAccessProvisioner.AdministratorEmail, first.AdministratorPassword);
        var op = await LoginAsync(http, TestAccessProvisioner.OperatorEmail, first.OperatorPassword!);
        var client = await LoginAsync(http, TestAccessProvisioner.ClientEmail, first.ClientPassword!);
        Assert.True(admin.MustChangePassword);
        Assert.True(admin.IsPlatformAdministrator);
        Assert.True(op.MustChangePassword);
        Assert.False(op.IsPlatformAdministrator);
        Assert.True(client.MustChangePassword);
        Assert.False(client.IsPlatformAdministrator);

        using var global = new HttpRequestMessage(HttpMethod.Get, "/api/v1/platform/dashboard");
        global.Headers.Authorization = new("Bearer", client.AccessToken);
        using var globalResponse = await http.SendAsync(global);
        Assert.Equal(HttpStatusCode.Forbidden, globalResponse.StatusCode);

        using var wrong = await http.PostAsJsonAsync("/api/v1/auth/login",
            new LoginRequest(TestAccessProvisioner.ClientEmail, "Wrong!Password2026"));
        Assert.Equal(HttpStatusCode.Unauthorized, wrong.StatusCode);
    }

    [Fact]
    public async Task ClientCannotAuthenticateOrKeepSessionWhenDemoTenantIsBlocked()
    {
        await ResetScenarioAsync();
        var seed = FindRepositoryFile("database", "development", "seed-test-access.sql");
        var provisioner = new TestAccessProvisioner(new AspNetPasswordService(), seed);
        var provisioned = await provisioner.ProvisionAsync(
            database.AdminConnectionString,
            TestAccessProvisioner.AdministratorInitialPassword,
            TestAccessProvisioner.ClientInitialPassword,
            true,
            true,
            () => throw new InvalidOperationException("Credenciais reservadas devem ser reutilizadas."),
            requestedAdministratorPassword: TestAccessProvisioner.AdministratorInitialPassword,
            requestedClientPassword: TestAccessProvisioner.ClientInitialPassword,
            operatorPassword: TestAccessProvisioner.OperatorInitialPassword,
            rotateOperator: true,
            requestedOperatorPassword: TestAccessProvisioner.OperatorInitialPassword,
            integrationTestFixture: true);

        await using var factory = database.CreateApi();
        using var http = factory.CreateClient();
        var client = await LoginAsync(http, TestAccessProvisioner.ClientEmail, provisioned.ClientPassword!);

        await using var connection = new NpgsqlConnection(database.AdminConnectionString);
        await connection.ExecuteAsync(
            "UPDATE odca.tenants SET status = 'suspended' WHERE business_code = @code;",
            new { code = TestAccessProvisioner.DemoTenantCode });
        try
        {
            using var rejectedLogin = await http.PostAsJsonAsync(
                "/api/v1/auth/login",
                new LoginRequest(TestAccessProvisioner.ClientEmail, provisioned.ClientPassword!));
            Assert.Equal(HttpStatusCode.Unauthorized, rejectedLogin.StatusCode);

            using var authenticatedRequest = new HttpRequestMessage(HttpMethod.Get, "/api/v1/organizations");
            authenticatedRequest.Headers.Authorization = new("Bearer", client.AccessToken);
            using var rejectedSession = await http.SendAsync(authenticatedRequest);
            Assert.Equal(HttpStatusCode.Unauthorized, rejectedSession.StatusCode);
        }
        finally
        {
            await connection.ExecuteAsync(
                "UPDATE odca.tenants SET status = 'active' WHERE business_code = @code;",
                new { code = TestAccessProvisioner.DemoTenantCode });
        }
    }

    [Fact]
    public async Task OperatorPasswordIsNotRotatedWhenTheRequestedSecretDiffers()
    {
        await ResetScenarioAsync();
        var provisioner = new TestAccessProvisioner(new AspNetPasswordService(), FindRepositoryFile("database", "development", "seed-test-access.sql"));
        var created = await provisioner.ProvisionAsync(
            database.AdminConnectionString,
            TestAccessProvisioner.AdministratorInitialPassword,
            TestAccessProvisioner.ClientInitialPassword,
            true, true, () => throw new InvalidOperationException("A criação não deve sortear outra senha."),
            requestedAdministratorPassword: TestAccessProvisioner.AdministratorInitialPassword,
            requestedClientPassword: TestAccessProvisioner.ClientInitialPassword,
            requestedOperatorPassword: TestAccessProvisioner.OperatorInitialPassword,
            rotateOperator: true,
            integrationTestFixture: true);

        var conflict = await Assert.ThrowsAsync<InvalidOperationException>(() => provisioner.ProvisionAsync(
            database.AdminConnectionString,
            created.AdministratorPassword, created.ClientPassword, false, false,
            () => throw new InvalidOperationException("A preservação não deve gerar senha."),
            requestedOperatorPassword: "Outra!SenhaOperador2026",
            rotateOperator: false,
            integrationTestFixture: true));

        Assert.Contains("rotação explícita", conflict.Message, StringComparison.OrdinalIgnoreCase);
        await using var factory = database.CreateApi();
        using var http = factory.CreateClient();
        var login = await LoginAsync(http, TestAccessProvisioner.OperatorEmail, TestAccessProvisioner.OperatorInitialPassword);
        Assert.False(login.IsPlatformAdministrator);
    }

    [Fact]
    public void LocalInstructionsDoNotPublishASecondOperatorPassword()
    {
        var root = FindRepositoryFile("docs", "execution", "LOCAL_ACCESS.md");
        var docs = File.ReadAllText(root);
        var script = File.ReadAllText(FindRepositoryFile("scripts", "provision-local-superadmin.ps1"));
        var shell = File.ReadAllText(FindRepositoryFile("scripts", "provision-local-superadmin.sh"));
        Assert.DoesNotContain("OdcaOperador#2026Local", docs, StringComparison.Ordinal);
        Assert.DoesNotContain("OdcaOperador#2026Local", script, StringComparison.Ordinal);
        Assert.DoesNotContain("OdcaOperador#2026Local", shell, StringComparison.Ordinal);
        Assert.Contains("rotação", docs, StringComparison.OrdinalIgnoreCase);
    }

    private static async Task<LoginResponse> LoginAsync(HttpClient client, string email, string password)
    {
        using var response = await client.PostAsJsonAsync("/api/v1/auth/login", new LoginRequest(email, password));
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<LoginResponse>())!;
    }

    private static string FindRepositoryFile(params string[] segments)
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null)
        {
            var candidate = Path.Combine([current.FullName, .. segments]);
            if (File.Exists(candidate)) return candidate;
            current = current.Parent;
        }
        throw new FileNotFoundException(string.Join('/', segments));
    }

    private async Task ResetScenarioAsync()
    {
        await using var conn = new NpgsqlConnection(database.AdminConnectionString);
        await conn.ExecuteAsync("""
            -- Transitive FK closure of the demo tenants/users: children first, parents last.
            DELETE FROM odca.contract_change_applications WHERE tenant_id IN (SELECT id FROM odca.tenants WHERE business_code IN ('12345678000195', 'ODCA-DEMO-LOCAL'));
            DELETE FROM odca.contract_change_events WHERE tenant_id IN (SELECT id FROM odca.tenants WHERE business_code IN ('12345678000195', 'ODCA-DEMO-LOCAL'));
            DELETE FROM odca.contract_review_comments WHERE tenant_id IN (SELECT id FROM odca.tenants WHERE business_code IN ('12345678000195', 'ODCA-DEMO-LOCAL'));
            DELETE FROM odca.contract_review_events WHERE tenant_id IN (SELECT id FROM odca.tenants WHERE business_code IN ('12345678000195', 'ODCA-DEMO-LOCAL'));
            DELETE FROM odca.contract_review_notifications WHERE tenant_id IN (SELECT id FROM odca.tenants WHERE business_code IN ('12345678000195', 'ODCA-DEMO-LOCAL'));
            DELETE FROM odca.contract_review_steps WHERE tenant_id IN (SELECT id FROM odca.tenants WHERE business_code IN ('12345678000195', 'ODCA-DEMO-LOCAL'));
            DELETE FROM odca.extraction_suggestions WHERE tenant_id IN (SELECT id FROM odca.tenants WHERE business_code IN ('12345678000195', 'ODCA-DEMO-LOCAL'));
            DELETE FROM odca.signature_participants WHERE tenant_id IN (SELECT id FROM odca.tenants WHERE business_code IN ('12345678000195', 'ODCA-DEMO-LOCAL'));
            DELETE FROM odca.signature_preparation_events WHERE tenant_id IN (SELECT id FROM odca.tenants WHERE business_code IN ('12345678000195', 'ODCA-DEMO-LOCAL'));
            DELETE FROM odca.signature_preparation_operations WHERE tenant_id IN (SELECT id FROM odca.tenants WHERE business_code IN ('12345678000195', 'ODCA-DEMO-LOCAL'));
            DELETE FROM odca.studio_comment_events WHERE tenant_id IN (SELECT id FROM odca.tenants WHERE business_code IN ('12345678000195', 'ODCA-DEMO-LOCAL'));
            DELETE FROM odca.contract_change_requests WHERE tenant_id IN (SELECT id FROM odca.tenants WHERE business_code IN ('12345678000195', 'ODCA-DEMO-LOCAL'));
            DELETE FROM odca.contract_review_requests WHERE tenant_id IN (SELECT id FROM odca.tenants WHERE business_code IN ('12345678000195', 'ODCA-DEMO-LOCAL'));
            DELETE FROM odca.contract_copy_issuances WHERE tenant_id IN (SELECT id FROM odca.tenants WHERE business_code IN ('12345678000195', 'ODCA-DEMO-LOCAL'));
            DELETE FROM odca.contract_import_events WHERE tenant_id IN (SELECT id FROM odca.tenants WHERE business_code IN ('12345678000195', 'ODCA-DEMO-LOCAL'));
            DELETE FROM odca.signature_preparations WHERE tenant_id IN (SELECT id FROM odca.tenants WHERE business_code IN ('12345678000195', 'ODCA-DEMO-LOCAL'));
            DELETE FROM odca.studio_comments WHERE tenant_id IN (SELECT id FROM odca.tenants WHERE business_code IN ('12345678000195', 'ODCA-DEMO-LOCAL'));
            DELETE FROM odca.extraction_results WHERE tenant_id IN (SELECT id FROM odca.tenants WHERE business_code IN ('12345678000195', 'ODCA-DEMO-LOCAL'));
            DELETE FROM odca.extraction_reviews WHERE tenant_id IN (SELECT id FROM odca.tenants WHERE business_code IN ('12345678000195', 'ODCA-DEMO-LOCAL'));
            DELETE FROM odca.contract_imports WHERE tenant_id IN (SELECT id FROM odca.tenants WHERE business_code IN ('12345678000195', 'ODCA-DEMO-LOCAL'));
            DELETE FROM odca.extraction_jobs WHERE tenant_id IN (SELECT id FROM odca.tenants WHERE business_code IN ('12345678000195', 'ODCA-DEMO-LOCAL'));
            DELETE FROM odca.obligation_events WHERE tenant_id IN (SELECT id FROM odca.tenants WHERE business_code IN ('12345678000195', 'ODCA-DEMO-LOCAL'));
            DELETE FROM odca.obligation_evidence WHERE tenant_id IN (SELECT id FROM odca.tenants WHERE business_code IN ('12345678000195', 'ODCA-DEMO-LOCAL'));
            DELETE FROM odca.obligation_reminders WHERE tenant_id IN (SELECT id FROM odca.tenants WHERE business_code IN ('12345678000195', 'ODCA-DEMO-LOCAL'));
            DELETE FROM odca.user_notifications WHERE tenant_id IN (SELECT id FROM odca.tenants WHERE business_code IN ('12345678000195', 'ODCA-DEMO-LOCAL'));
            DELETE FROM odca.contract_obligations WHERE tenant_id IN (SELECT id FROM odca.tenants WHERE business_code IN ('12345678000195', 'ODCA-DEMO-LOCAL'));
            DELETE FROM odca.draft_save_receipts WHERE tenant_id IN (SELECT id FROM odca.tenants WHERE business_code IN ('12345678000195', 'ODCA-DEMO-LOCAL'));
            DELETE FROM odca.generated_contract_versions WHERE tenant_id IN (SELECT id FROM odca.tenants WHERE business_code IN ('12345678000195', 'ODCA-DEMO-LOCAL'));
            DELETE FROM odca.document_versions WHERE tenant_id IN (SELECT id FROM odca.tenants WHERE business_code IN ('12345678000195', 'ODCA-DEMO-LOCAL'));
            DELETE FROM odca.contract_drafts WHERE tenant_id IN (SELECT id FROM odca.tenants WHERE business_code IN ('12345678000195', 'ODCA-DEMO-LOCAL'));
            DELETE FROM odca.contract_documents WHERE tenant_id IN (SELECT id FROM odca.tenants WHERE business_code IN ('12345678000195', 'ODCA-DEMO-LOCAL'));
            DELETE FROM odca.contract_events WHERE tenant_id IN (SELECT id FROM odca.tenants WHERE business_code IN ('12345678000195', 'ODCA-DEMO-LOCAL'));
            DELETE FROM odca.contract_renewal_cycles WHERE tenant_id IN (SELECT id FROM odca.tenants WHERE business_code IN ('12345678000195', 'ODCA-DEMO-LOCAL'));
            DELETE FROM odca.invoice_payments WHERE tenant_id IN (SELECT id FROM odca.tenants WHERE business_code IN ('12345678000195', 'ODCA-DEMO-LOCAL'));
            DELETE FROM odca.obligation_series WHERE tenant_id IN (SELECT id FROM odca.tenants WHERE business_code IN ('12345678000195', 'ODCA-DEMO-LOCAL'));
            DELETE FROM odca.storage_capacity_grants WHERE tenant_id IN (SELECT id FROM odca.tenants WHERE business_code IN ('12345678000195', 'ODCA-DEMO-LOCAL'));
            DELETE FROM odca.patient_representatives WHERE tenant_id IN (SELECT id FROM odca.tenants WHERE business_code IN ('12345678000195', 'ODCA-DEMO-LOCAL'));
            DELETE FROM odca.privacy_legal_holds WHERE tenant_id IN (SELECT id FROM odca.tenants WHERE business_code IN ('12345678000195', 'ODCA-DEMO-LOCAL'));
            DELETE FROM odca.privacy_request_actions WHERE tenant_id IN (SELECT id FROM odca.tenants WHERE business_code IN ('12345678000195', 'ODCA-DEMO-LOCAL'));
            DELETE FROM odca.privacy_request_events WHERE privacy_request_id IN (SELECT id FROM odca.privacy_requests WHERE tenant_id IN (SELECT id FROM odca.tenants WHERE business_code IN ('12345678000195', 'ODCA-DEMO-LOCAL')))
                                                      OR actor_user_id IN (SELECT id FROM odca.users WHERE email_normalized IN ('ADMIN@ODCA.LOCAL', 'OPERADOR@ODCA.LOCAL', 'CLIENTE.TESTE@ODCA.LOCAL'));
            DELETE FROM odca.support_session_events WHERE session_id IN (SELECT id FROM odca.support_sessions WHERE tenant_id IN (SELECT id FROM odca.tenants WHERE business_code IN ('12345678000195', 'ODCA-DEMO-LOCAL')))
                                                     OR actor_user_id IN (SELECT id FROM odca.users WHERE email_normalized IN ('ADMIN@ODCA.LOCAL', 'OPERADOR@ODCA.LOCAL', 'CLIENTE.TESTE@ODCA.LOCAL'));
            DELETE FROM odca.notification_outbox WHERE tenant_id IN (SELECT id FROM odca.tenants WHERE business_code IN ('12345678000195', 'ODCA-DEMO-LOCAL'));
            DELETE FROM odca.additional_storage_requests WHERE tenant_id IN (SELECT id FROM odca.tenants WHERE business_code IN ('12345678000195', 'ODCA-DEMO-LOCAL'));
            DELETE FROM odca.patients WHERE tenant_id IN (SELECT id FROM odca.tenants WHERE business_code IN ('12345678000195', 'ODCA-DEMO-LOCAL'));
            DELETE FROM odca.privacy_requests WHERE tenant_id IN (SELECT id FROM odca.tenants WHERE business_code IN ('12345678000195', 'ODCA-DEMO-LOCAL'));
            DELETE FROM odca.privacy_contacts WHERE tenant_id IN (SELECT id FROM odca.tenants WHERE business_code IN ('12345678000195', 'ODCA-DEMO-LOCAL'));
            DELETE FROM odca.resource_movements WHERE tenant_id IN (SELECT id FROM odca.tenants WHERE business_code IN ('12345678000195', 'ODCA-DEMO-LOCAL'))
                                                 OR actor_user_id IN (SELECT id FROM odca.users WHERE email_normalized IN ('ADMIN@ODCA.LOCAL', 'OPERADOR@ODCA.LOCAL', 'CLIENTE.TESTE@ODCA.LOCAL'));
            DELETE FROM odca.support_sessions WHERE tenant_id IN (SELECT id FROM odca.tenants WHERE business_code IN ('12345678000195', 'ODCA-DEMO-LOCAL'));
            DELETE FROM odca.tenant_invitations WHERE tenant_id IN (SELECT id FROM odca.tenants WHERE business_code IN ('12345678000195', 'ODCA-DEMO-LOCAL'));
            DELETE FROM odca.saved_work_views WHERE tenant_id IN (SELECT id FROM odca.tenants WHERE business_code IN ('12345678000195', 'ODCA-DEMO-LOCAL'));
            DELETE FROM odca.organization_feature_blocks WHERE tenant_id IN (SELECT id FROM odca.tenants WHERE business_code IN ('12345678000195', 'ODCA-DEMO-LOCAL'));
            DELETE FROM odca.tenant_storage_usage WHERE tenant_id IN (SELECT id FROM odca.tenants WHERE business_code IN ('12345678000195', 'ODCA-DEMO-LOCAL'));
            DELETE FROM odca.storage_reservations WHERE tenant_id IN (SELECT id FROM odca.tenants WHERE business_code IN ('12345678000195', 'ODCA-DEMO-LOCAL'));
            DELETE FROM odca.contract_template_access WHERE tenant_id IN (SELECT id FROM odca.tenants WHERE business_code IN ('12345678000195', 'ODCA-DEMO-LOCAL'));
            DELETE FROM odca.contract_template_versions WHERE template_id IN (SELECT id FROM odca.contract_templates
                                                                               WHERE owner_tenant_id IN (SELECT id FROM odca.tenants WHERE business_code IN ('12345678000195', 'ODCA-DEMO-LOCAL'))
                                                                                  OR author_id IN (SELECT id FROM odca.users WHERE email_normalized IN ('ADMIN@ODCA.LOCAL', 'OPERADOR@ODCA.LOCAL', 'CLIENTE.TESTE@ODCA.LOCAL')));
            DELETE FROM odca.contracts WHERE tenant_id IN (SELECT id FROM odca.tenants WHERE business_code IN ('12345678000195', 'ODCA-DEMO-LOCAL'));
            DELETE FROM odca.contract_templates WHERE owner_tenant_id IN (SELECT id FROM odca.tenants WHERE business_code IN ('12345678000195', 'ODCA-DEMO-LOCAL'))
                                                OR author_id IN (SELECT id FROM odca.users WHERE email_normalized IN ('ADMIN@ODCA.LOCAL', 'OPERADOR@ODCA.LOCAL', 'CLIENTE.TESTE@ODCA.LOCAL'));
            DELETE FROM odca.invoices WHERE tenant_id IN (SELECT id FROM odca.tenants WHERE business_code IN ('12345678000195', 'ODCA-DEMO-LOCAL'));
            DELETE FROM odca.mfa_recovery_codes WHERE user_id IN (SELECT id FROM odca.users WHERE email_normalized IN ('ADMIN@ODCA.LOCAL', 'OPERADOR@ODCA.LOCAL', 'CLIENTE.TESTE@ODCA.LOCAL'));
            DELETE FROM odca.financial_audit_events WHERE tenant_id IN (SELECT id FROM odca.tenants WHERE business_code IN ('12345678000195', 'ODCA-DEMO-LOCAL'))
                                                   OR actor_user_id IN (SELECT id FROM odca.users WHERE email_normalized IN ('ADMIN@ODCA.LOCAL', 'OPERADOR@ODCA.LOCAL', 'CLIENTE.TESTE@ODCA.LOCAL'));
            DELETE FROM odca.audit_events WHERE actor_user_id IN (SELECT id FROM odca.users WHERE email_normalized IN ('ADMIN@ODCA.LOCAL', 'OPERADOR@ODCA.LOCAL', 'CLIENTE.TESTE@ODCA.LOCAL'))
                                          OR tenant_id IN (SELECT id FROM odca.tenants WHERE business_code IN ('12345678000195', 'ODCA-DEMO-LOCAL'));
            DELETE FROM odca.sessions WHERE user_id IN (SELECT id FROM odca.users WHERE email_normalized IN ('ADMIN@ODCA.LOCAL', 'OPERADOR@ODCA.LOCAL', 'CLIENTE.TESTE@ODCA.LOCAL'));
            DELETE FROM odca.member_roles WHERE tenant_id IN (SELECT id FROM odca.tenants WHERE business_code IN ('12345678000195', 'ODCA-DEMO-LOCAL'))
                                           OR user_id IN (SELECT id FROM odca.users WHERE email_normalized IN ('ADMIN@ODCA.LOCAL', 'OPERADOR@ODCA.LOCAL', 'CLIENTE.TESTE@ODCA.LOCAL'))
                                           OR assigned_by IN (SELECT id FROM odca.users WHERE email_normalized IN ('ADMIN@ODCA.LOCAL', 'OPERADOR@ODCA.LOCAL', 'CLIENTE.TESTE@ODCA.LOCAL'));
            DELETE FROM odca.memberships WHERE tenant_id IN (SELECT id FROM odca.tenants WHERE business_code IN ('12345678000195', 'ODCA-DEMO-LOCAL'))
                                           OR user_id IN (SELECT id FROM odca.users WHERE email_normalized IN ('ADMIN@ODCA.LOCAL', 'OPERADOR@ODCA.LOCAL', 'CLIENTE.TESTE@ODCA.LOCAL'));
            DELETE FROM odca.subscriptions WHERE tenant_id IN (SELECT id FROM odca.tenants WHERE business_code IN ('12345678000195', 'ODCA-DEMO-LOCAL'))
                                              OR created_by IN (SELECT id FROM odca.users WHERE email_normalized IN ('ADMIN@ODCA.LOCAL', 'OPERADOR@ODCA.LOCAL', 'CLIENTE.TESTE@ODCA.LOCAL'));
            DELETE FROM odca.role_permissions WHERE role_id IN (SELECT id FROM odca.roles WHERE tenant_id IN (SELECT id FROM odca.tenants WHERE business_code IN ('12345678000195', 'ODCA-DEMO-LOCAL')));
            DELETE FROM odca.roles WHERE tenant_id IN (SELECT id FROM odca.tenants WHERE business_code IN ('12345678000195', 'ODCA-DEMO-LOCAL'));
            DELETE FROM odca.storage_package_versions WHERE created_by IN (SELECT id FROM odca.users WHERE email_normalized IN ('ADMIN@ODCA.LOCAL', 'OPERADOR@ODCA.LOCAL', 'CLIENTE.TESTE@ODCA.LOCAL'));
            DELETE FROM odca.tenants WHERE business_code IN ('12345678000195', 'ODCA-DEMO-LOCAL');
            DELETE FROM odca.users WHERE email_normalized IN ('ADMIN@ODCA.LOCAL', 'OPERADOR@ODCA.LOCAL', 'CLIENTE.TESTE@ODCA.LOCAL');
        """);

    }
}
