using System.Net;
using System.Net.Http.Json;
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
