using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Dapper;
using Microsoft.AspNetCore.Mvc.Testing;
using Npgsql;
using Odca.Application.Identity;
using Odca.Contracts.Identity;
using Odca.Contracts.Onboarding;
using Odca.Contracts.Plans;
using Odca.Contracts.Privacy;
using Odca.Infrastructure.Database;
using Odca.Infrastructure.Common;

namespace Odca.IntegrationTests;

public sealed class S00FlowTests(DatabaseFixture database) : IClassFixture<DatabaseFixture>
{
    [Fact]
    public async Task CanonicalSqlReappliesWithoutDuplicatingMigration()
    {
        Assert.Equal(3, await database.MigrationCountAsync());
        Assert.True(await database.IsolationFixesArePresentAsync());
    }

    [Fact]
    public async Task RuntimeRoleIsolatesTenantsAndRejectsCrossTenantRole()
    {
        await database.SeedTenantIsolationScenarioAsync();
        await using var connection = new NpgsqlConnection(database.AppConnectionString);
        await connection.OpenAsync();

        Assert.Equal(0, await connection.ExecuteScalarAsync<int>("SELECT count(*)::integer FROM odca.tenants;"));

        await using (var tenantATransaction = await connection.BeginTransactionAsync())
        {
            await connection.ExecuteAsync(
                "SELECT set_config('odca.tenant_id', @tenantId, true);",
                new { tenantId = "50000000-0000-0000-0000-000000000001" },
                tenantATransaction);
            Assert.Equal("TEST-A", await connection.QuerySingleAsync<string>(
                "SELECT business_code FROM odca.tenants;",
                transaction: tenantATransaction));
            Assert.Equal(1, await connection.ExecuteScalarAsync<int>(
                "SELECT count(*)::integer FROM odca.role_permissions;",
                transaction: tenantATransaction));
            Assert.Equal(1, await connection.ExecuteScalarAsync<int>(
                "SELECT count(*)::integer FROM odca.audit_events WHERE action = 'test.isolation';",
                transaction: tenantATransaction));
            await tenantATransaction.CommitAsync();
        }

        Assert.Equal(0, await connection.ExecuteScalarAsync<int>("SELECT count(*)::integer FROM odca.tenants;"));

        await using var invalidTransaction = await connection.BeginTransactionAsync();
        await connection.ExecuteAsync(
            "SELECT set_config('odca.tenant_id', @tenantId, true);",
            new { tenantId = "50000000-0000-0000-0000-000000000001" },
            invalidTransaction);
        var exception = await Assert.ThrowsAsync<PostgresException>(() => connection.ExecuteAsync(
            """
            INSERT INTO odca.member_roles (tenant_id, user_id, role_id, assigned_by)
            VALUES (
                '50000000-0000-0000-0000-000000000001',
                '20000000-0000-0000-0000-000000000001',
                '60000000-0000-0000-0000-000000000002',
                '20000000-0000-0000-0000-000000000001');
            """,
            transaction: invalidTransaction));
        Assert.Equal(PostgresErrorCodes.ForeignKeyViolation, exception.SqlState);
        await invalidTransaction.RollbackAsync();
    }

    [Fact]
    public async Task PublicPrivacyRequestReturnsOpaqueProtocol()
    {
        await using var factory = database.CreateApi();
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

        using var privacyResponse = await client.PostAsJsonAsync(
            "/api/v1/privacy/requests",
            new CreatePrivacyRequest("subject@example.test", "access", "Synthetic integration request"));
        Assert.Equal(HttpStatusCode.Accepted, privacyResponse.StatusCode);
        var privacy = await privacyResponse.Content.ReadFromJsonAsync<PrivacyRequestCreated>();
        Assert.NotNull(privacy);
        Assert.Matches("^[A-F0-9]{24}$", privacy.Protocol);
        Assert.DoesNotContain("subject@example.test", privacy.Message, StringComparison.OrdinalIgnoreCase);
        Assert.True(await database.PrivacyRequestExistsAsync(privacy.Protocol));
    }

    [Fact]
    public async Task CustomerRegistrationConfirmsEmailAndShowsCommercialPendingHome()
    {
        await using var factory = database.CreateApi();
        using var client = factory.CreateClient();
        var request = new StartCustomerRegistrationRequest(
            "basic",
            "12.345.678/0001-90",
            "Cliente Sintético",
            "cliente.sintetico@example.test",
            "Cliente!Password2026",
            true,
            true,
            false);

        using var firstResponse = await client.PostAsJsonAsync("/api/v1/onboarding/registrations", request);
        Assert.Equal(HttpStatusCode.Accepted, firstResponse.StatusCode);
        var first = await firstResponse.Content.ReadFromJsonAsync<StartCustomerRegistrationResponse>();
        Assert.NotNull(first);
        Assert.NotNull(first.DevelopmentConfirmationToken);

        using var repeatedResponse = await client.PostAsJsonAsync("/api/v1/onboarding/registrations", request);
        Assert.Equal(HttpStatusCode.Accepted, repeatedResponse.StatusCode);
        var repeated = await repeatedResponse.Content.ReadFromJsonAsync<StartCustomerRegistrationResponse>();
        Assert.NotNull(repeated);
        Assert.Equal(first.RegistrationId, repeated.RegistrationId);
        Assert.NotEqual(first.DevelopmentConfirmationToken, repeated.DevelopmentConfirmationToken);

        using var confirmResponse = await client.PostAsJsonAsync(
            "/api/v1/onboarding/confirm-email",
            new ConfirmCustomerRegistrationRequest(repeated.RegistrationId, repeated.DevelopmentConfirmationToken!));
        confirmResponse.EnsureSuccessStatusCode();

        using var reusedConfirmResponse = await client.PostAsJsonAsync(
            "/api/v1/onboarding/confirm-email",
            new ConfirmCustomerRegistrationRequest(repeated.RegistrationId, repeated.DevelopmentConfirmationToken!));
        Assert.Equal(HttpStatusCode.BadRequest, reusedConfirmResponse.StatusCode);

        var login = await LoginAsync(client, "Cliente!Password2026", "cliente.sintetico@example.test");
        Assert.False(login.MustChangePassword);
        Assert.False(login.RequiresMfaChallenge);

        using var home = Authorized(HttpMethod.Get, "/api/v1/onboarding/customer-home", login.AccessToken);
        using var homeResponse = await client.SendAsync(home);
        homeResponse.EnsureSuccessStatusCode();
        var data = await homeResponse.Content.ReadFromJsonAsync<CustomerHomeResponse>();
        Assert.NotNull(data);
        Assert.Equal(("basic", 1, "commercial_pending"), (data.PlanCode, data.PlanVersion, data.CommercialState));
    }

    [Fact]
    public async Task InvalidPasswordIsRejected()
    {
        await database.ResetAuthenticationScenarioAsync();
        await using var factory = database.CreateApi();
        using var client = factory.CreateClient();

        using var wrong = await client.PostAsJsonAsync(
            "/api/v1/auth/login",
            new LoginRequest(DatabaseFixture.Email, "wrong-password"));
        Assert.True(
            wrong.StatusCode == HttpStatusCode.Unauthorized,
            $"Esperado 401; recebido {(int)wrong.StatusCode}: {await wrong.Content.ReadAsStringAsync()}");
    }

    [Fact]
    public async Task InitialPasswordOnlyAllowsPasswordChange()
    {
        await database.ResetAuthenticationScenarioAsync();
        await using var factory = database.CreateApi();
        using var client = factory.CreateClient();

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
        Assert.True(changed.RequiresMfaEnrollment);
        Assert.False(changed.MfaVerified);
    }

    [Fact]
    public async Task PasswordChangedSessionCannotReadDashboardWithoutMfa()
    {
        await database.ResetAuthenticationScenarioAsync();
        await using var factory = database.CreateApi();
        using var client = factory.CreateClient();
        var changed = await ChangeInitialPasswordAsync(client);

        using var dashboard = Authorized(HttpMethod.Get, "/api/v1/platform/dashboard", changed.AccessToken);
        using var dashboardResponse = await client.SendAsync(dashboard);
        Assert.Equal(HttpStatusCode.Forbidden, dashboardResponse.StatusCode);
    }

    [Fact]
    public async Task SuperAdministratorCanReadDashboardAndPlansAfterMfaEnrollment()
    {
        await database.ResetAuthenticationScenarioAsync();
        await using var factory = database.CreateApi();
        using var client = factory.CreateClient();
        var verified = await EnrollMfaAsync(client);

        using var dashboard = Authorized(HttpMethod.Get, "/api/v1/platform/dashboard", verified.AccessToken);
        using var dashboardResponse = await client.SendAsync(dashboard);
        dashboardResponse.EnsureSuccessStatusCode();
        var data = await dashboardResponse.Content.ReadFromJsonAsync<DashboardResponse>();
        Assert.NotNull(data);
        Assert.True(data.ActiveUsers >= 1);
        Assert.True(data.PendingPrivacyItems >= 1);

        using var plans = Authorized(HttpMethod.Get, "/api/v1/platform/plans", verified.AccessToken);
        using var plansResponse = await client.SendAsync(plans);
        plansResponse.EnsureSuccessStatusCode();
        var catalog = await plansResponse.Content.ReadFromJsonAsync<PlanCatalogResponse[]>();
        Assert.NotNull(catalog);
        Assert.Collection(
            catalog,
            basic => Assert.Equal(("basic", 3, 10_000_000_000), (basic.Code, basic.ActiveSeats, basic.StorageBytes)),
            intermediate => Assert.Equal(("intermediate", 10, 100_000_000_000), (intermediate.Code, intermediate.ActiveSeats, intermediate.StorageBytes)),
            enterprise => Assert.Equal(("enterprise", 30, 500_000_000_000), (enterprise.Code, enterprise.ActiveSeats, enterprise.StorageBytes)));
    }

    [Fact]
    public async Task InvalidAndReusedMfaCodesAreRejected()
    {
        await database.ResetAuthenticationScenarioAsync();
        await using var factory = database.CreateApi();
        using var client = factory.CreateClient();
        var verified = await EnrollMfaAsync(client);
        var recoveryCode = verified.RecoveryCodes[0];

        var passwordOnly = await LoginAsync(client, DatabaseFixture.ChangedPassword);
        Assert.True(passwordOnly.RequiresMfaChallenge);

        using var reusedTotp = Authorized(HttpMethod.Post, "/api/v1/auth/mfa/challenge", passwordOnly.AccessToken);
        reusedTotp.Content = JsonContent.Create(new MfaChallengeRequest(verified.LastTotpCode));
        using var reusedTotpResponse = await client.SendAsync(reusedTotp);
        Assert.Equal(HttpStatusCode.BadRequest, reusedTotpResponse.StatusCode);

        using var recovery = Authorized(HttpMethod.Post, "/api/v1/auth/mfa/challenge", passwordOnly.AccessToken);
        recovery.Content = JsonContent.Create(new MfaChallengeRequest(recoveryCode));
        using var recoveryResponse = await client.SendAsync(recovery);
        recoveryResponse.EnsureSuccessStatusCode();

        var secondPasswordOnly = await LoginAsync(client, DatabaseFixture.ChangedPassword);
        using var reusedRecovery = Authorized(HttpMethod.Post, "/api/v1/auth/mfa/challenge", secondPasswordOnly.AccessToken);
        reusedRecovery.Content = JsonContent.Create(new MfaChallengeRequest(recoveryCode));
        using var reusedRecoveryResponse = await client.SendAsync(reusedRecovery);
        Assert.Equal(HttpStatusCode.BadRequest, reusedRecoveryResponse.StatusCode);
    }

    [Fact]
    public async Task LogoutRevokesPasswordChangedSession()
    {
        await database.ResetAuthenticationScenarioAsync();
        await using var factory = database.CreateApi();
        using var client = factory.CreateClient();
        var changed = await EnrollMfaAsync(client);

        using var logout = Authorized(HttpMethod.Post, "/api/v1/auth/logout", changed.AccessToken);
        using var logoutResponse = await client.SendAsync(logout);
        Assert.Equal(HttpStatusCode.NoContent, logoutResponse.StatusCode);

        using var revoked = Authorized(HttpMethod.Get, "/api/v1/platform/dashboard", changed.AccessToken);
        using var revokedResponse = await client.SendAsync(revoked);
        Assert.Equal(HttpStatusCode.Unauthorized, revokedResponse.StatusCode);
    }

    private static async Task<LoginResponse> ChangeInitialPasswordAsync(HttpClient client)
    {
        var first = await LoginAsync(client, DatabaseFixture.InitialPassword);
        using var change = Authorized(HttpMethod.Post, "/api/v1/auth/change-password", first.AccessToken);
        change.Content = JsonContent.Create(new ChangePasswordRequest(
            DatabaseFixture.InitialPassword,
            DatabaseFixture.ChangedPassword));
        using var response = await client.SendAsync(change);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<LoginResponse>())!;
    }

    private static async Task<MfaTestSession> EnrollMfaAsync(HttpClient client)
    {
        var changed = await ChangeInitialPasswordAsync(client);
        using var start = Authorized(HttpMethod.Post, "/api/v1/auth/mfa/enrollment", changed.AccessToken);
        using var startResponse = await client.SendAsync(start);
        startResponse.EnsureSuccessStatusCode();
        var enrollment = await startResponse.Content.ReadFromJsonAsync<MfaEnrollmentResponse>();
        Assert.NotNull(enrollment);
        var code = new TotpService(new SystemClock()).GenerateCode(enrollment.ManualKey);

        using var confirm = Authorized(HttpMethod.Post, "/api/v1/auth/mfa/enrollment/confirm", changed.AccessToken);
        confirm.Content = JsonContent.Create(new ConfirmMfaEnrollmentRequest(code));
        using var confirmResponse = await client.SendAsync(confirm);
        confirmResponse.EnsureSuccessStatusCode();
        var verified = await confirmResponse.Content.ReadFromJsonAsync<MfaVerificationResponse>();
        Assert.NotNull(verified);
        Assert.True(verified.MfaVerified);
        Assert.NotNull(verified.RecoveryCodes);
        Assert.Equal(8, verified.RecoveryCodes.Length);
        return new MfaTestSession(verified.AccessToken, verified.RecoveryCodes, code);
    }

    private static async Task<LoginResponse> LoginAsync(HttpClient client, string password)
        => await LoginAsync(client, password, DatabaseFixture.Email);

    private static async Task<LoginResponse> LoginAsync(HttpClient client, string password, string login)
    {
        using var response = await client.PostAsJsonAsync(
            "/api/v1/auth/login",
            new LoginRequest(login, password));
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<LoginResponse>())!;
    }

    private static HttpRequestMessage Authorized(HttpMethod method, string path, string token)
    {
        var request = new HttpRequestMessage(method, path);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return request;
    }

    private sealed record MfaTestSession(string AccessToken, string[] RecoveryCodes, string LastTotpCode);
}
