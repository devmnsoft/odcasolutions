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
        var seed = FindRepositoryFile("database", "development", "seed-test-access.sql");
        var provisioner = new TestAccessProvisioner(new AspNetPasswordService(), seed);
        var generated = new Queue<string>([
            "Admin!Development2026-Unique",
            "Operator!Development2026-Unique",
            "Client!Development2026-Unique"
        ]);
        var first = await provisioner.ProvisionAsync(database.AdminConnectionString,
            "unused-admin", null, true, true, generated.Dequeue,
            operatorPassword: null, rotateOperator: true);

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
            operatorPassword: first.OperatorPassword, rotateOperator: false);
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
                operatorPassword: first.OperatorPassword, rotateOperator: false)));
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
        var seed = FindRepositoryFile("database", "development", "seed-test-access.sql");
        var provisioner = new TestAccessProvisioner(new AspNetPasswordService(), seed);
        var provisioned = await provisioner.ProvisionAsync(
            database.AdminConnectionString,
            TestAccessProvisioner.AdministratorInitialPassword,
            TestAccessProvisioner.ClientInitialPassword,
            false,
            false,
            () => throw new InvalidOperationException("Credenciais reservadas devem ser reutilizadas."),
            requestedAdministratorPassword: TestAccessProvisioner.AdministratorInitialPassword,
            requestedClientPassword: TestAccessProvisioner.ClientInitialPassword,
            operatorPassword: null,
            rotateOperator: false);

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
}
