using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Odca.Contracts.Identity;
using Odca.Infrastructure.Database;

namespace Odca.IntegrationTests;

public sealed class S00FlowTests(DatabaseFixture database) : IClassFixture<DatabaseFixture>
{
    [Fact]
    public async Task CanonicalSqlReappliesWithoutDuplicatingMigration()
    {
        Assert.Equal(1, await database.MigrationCountAsync());
    }

    [Fact]
    public async Task BootstrapLoginRequiresPasswordChangeThenDashboardAndLogoutWork()
    {
        await using var factory = database.CreateApi();
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

        using var wrong = await client.PostAsJsonAsync(
            "/api/v1/auth/login",
            new LoginRequest(DatabaseFixture.Email, "wrong-password"));
        Assert.True(
            wrong.StatusCode == HttpStatusCode.Unauthorized,
            $"Esperado 401; recebido {(int)wrong.StatusCode}: {await wrong.Content.ReadAsStringAsync()}");

        var first = await LoginAsync(client, DatabaseFixture.InitialPassword);
        Assert.True(first.MustChangePassword);

        using var deniedDashboard = Authorized(HttpMethod.Get, "/api/v1/platform/dashboard", first.AccessToken);
        using var deniedResponse = await client.SendAsync(deniedDashboard);
        Assert.Equal(HttpStatusCode.Forbidden, deniedResponse.StatusCode);

        using var change = Authorized(HttpMethod.Post, "/api/v1/auth/change-password", first.AccessToken);
        change.Content = JsonContent.Create(new ChangePasswordRequest(
            DatabaseFixture.InitialPassword,
            DatabaseFixture.ChangedPassword));
        using var changeResponse = await client.SendAsync(change);
        changeResponse.EnsureSuccessStatusCode();
        var changed = await changeResponse.Content.ReadFromJsonAsync<LoginResponse>();
        Assert.NotNull(changed);
        Assert.False(changed.MustChangePassword);

        using var dashboard = Authorized(HttpMethod.Get, "/api/v1/platform/dashboard", changed.AccessToken);
        using var dashboardResponse = await client.SendAsync(dashboard);
        dashboardResponse.EnsureSuccessStatusCode();
        var data = await dashboardResponse.Content.ReadFromJsonAsync<DashboardResponse>();
        Assert.NotNull(data);
        Assert.True(data.ActiveUsers >= 1);
        Assert.True(data.PendingPrivacyItems >= 1);

        using var logout = Authorized(HttpMethod.Post, "/api/v1/auth/logout", changed.AccessToken);
        using var logoutResponse = await client.SendAsync(logout);
        Assert.Equal(HttpStatusCode.NoContent, logoutResponse.StatusCode);

        using var revoked = Authorized(HttpMethod.Get, "/api/v1/platform/dashboard", changed.AccessToken);
        using var revokedResponse = await client.SendAsync(revoked);
        Assert.Equal(HttpStatusCode.Unauthorized, revokedResponse.StatusCode);
    }

    private static async Task<LoginResponse> LoginAsync(HttpClient client, string password)
    {
        using var response = await client.PostAsJsonAsync(
            "/api/v1/auth/login",
            new LoginRequest(DatabaseFixture.Email, password));
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<LoginResponse>())!;
    }

    private static HttpRequestMessage Authorized(HttpMethod method, string path, string token)
    {
        var request = new HttpRequestMessage(method, path);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return request;
    }

}
