using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Dapper;
using Npgsql;
using Odca.Application.Onboarding;
using Odca.Contracts.Administration;
using Odca.Contracts.Identity;
using Odca.Contracts.Onboarding;
using Odca.Contracts.Patients;

namespace Odca.IntegrationTests;

public sealed class OrganizationFeatureGateTests(DatabaseFixture database) : IClassFixture<DatabaseFixture>
{
    private static readonly Guid PlatformActorId = Guid.Parse("20000000-0000-0000-0000-000000000001");

    [Fact]
    public async Task AdministrativeBlockStopsDirectPatientCallAndReleasePreservesTheRecord()
    {
        await database.ResetAuthenticationScenarioAsync();
        var email = $"feature.gate.{Guid.NewGuid():N}@example.test";
        var document = NewCpf();
        await using var factory = database.CreateApi();
        using var client = factory.CreateClient();

        using var started = await client.PostAsJsonAsync("/api/v1/onboarding/registrations", new StartCustomerRegistrationRequest(
            "basic", document, "Cliente da funcionalidade", email, "Cliente!Password2026", true, true, false));
        Assert.Equal(HttpStatusCode.Accepted, started.StatusCode);
        var registration = await started.Content.ReadFromJsonAsync<StartCustomerRegistrationResponse>();
        Assert.NotNull(registration?.DevelopmentConfirmationToken);

        using var confirmed = await client.PostAsJsonAsync("/api/v1/onboarding/confirm-email", new ConfirmCustomerRegistrationRequest(
            registration.RegistrationId, registration.DevelopmentConfirmationToken));
        confirmed.EnsureSuccessStatusCode();

        using var loginResponse = await client.PostAsJsonAsync("/api/v1/auth/login", new LoginRequest(email, "Cliente!Password2026"));
        loginResponse.EnsureSuccessStatusCode();
        var login = await loginResponse.Content.ReadFromJsonAsync<LoginResponse>();
        Assert.NotNull(login);
        Assert.False(login.IsPlatformAdministrator);

        using var homeResponse = await client.SendAsync(Authorized(HttpMethod.Get, "/api/v1/onboarding/customer-home", login.AccessToken));
        homeResponse.EnsureSuccessStatusCode();
        var home = await homeResponse.Content.ReadFromJsonAsync<CustomerHomeResponse>();
        Assert.NotNull(home);

        var otherTenant = Guid.NewGuid();
        await using (var admin = new NpgsqlConnection(database.AdminConnectionString))
        {
            await admin.ExecuteAsync(
                "INSERT INTO odca.tenants(id, business_code, display_name, status) VALUES(@id, @code, 'Organização sem bloqueio', 'active')",
                new { id = otherTenant, code = $"ISO-{otherTenant:N}"[..20] });
            Assert.Equal("administratively_blocked", await SetFeatureAsync(admin, home.TenantId, blocked: true));
            Assert.Equal("allowed", await admin.ExecuteScalarAsync<string>(
                "SELECT odca.organization_feature_state(@tenant, 'patients')", new { tenant = otherTenant }));
            Assert.Equal("plan_restricted", await admin.ExecuteScalarAsync<string>(
                "SELECT odca.organization_feature_state(@tenant, 'signatures')", new { tenant = otherTenant }));
            Assert.Contains("organization_feature_state", await admin.ExecuteScalarAsync<string>(
                "SELECT pg_get_functiondef('odca.claim_document_scan()'::regprocedure)"), StringComparison.Ordinal);
            Assert.Contains("organization_feature_state", await admin.ExecuteScalarAsync<string>(
                "SELECT pg_get_functiondef('odca.claim_extraction_job(uuid)'::regprocedure)"), StringComparison.Ordinal);
        }

        using var blockedCreate = Authorized(HttpMethod.Post, $"/api/v1/organizations/{home.TenantId}/patients", login.AccessToken);
        blockedCreate.Content = JsonContent.Create(new SavePatientRequest("Paciente Preservado", null, null, null, null, null, null, null));
        using var blockedResponse = await client.SendAsync(blockedCreate);
        var blockedBody = await blockedResponse.Content.ReadAsStringAsync();
        Assert.Equal(HttpStatusCode.Forbidden, blockedResponse.StatusCode);
        Assert.Contains("feature_blocked", blockedBody, StringComparison.Ordinal);
        Assert.DoesNotContain("plan_restricted", blockedBody, StringComparison.Ordinal);

        await using (var admin = new NpgsqlConnection(database.AdminConnectionString))
        {
            Assert.Equal(0, await admin.ExecuteScalarAsync<int>(
                "SELECT count(*)::int FROM odca.patients WHERE tenant_id=@tenant AND full_name='Paciente Preservado'",
                new { tenant = home.TenantId }));
            Assert.Equal("allowed", await SetFeatureAsync(admin, home.TenantId, blocked: false));
        }

        using var createdRequest = Authorized(HttpMethod.Post, $"/api/v1/organizations/{home.TenantId}/patients", login.AccessToken);
        createdRequest.Content = JsonContent.Create(new SavePatientRequest("Paciente Preservado", null, null, null, null, null, null, null));
        using var createdResponse = await client.SendAsync(createdRequest);
        Assert.Equal(HttpStatusCode.Created, createdResponse.StatusCode);

        await using (var admin = new NpgsqlConnection(database.AdminConnectionString))
        {
            Assert.Equal("administratively_blocked", await SetFeatureAsync(admin, home.TenantId, blocked: true));
        }

        using var hidden = await client.SendAsync(Authorized(HttpMethod.Get, $"/api/v1/organizations/{home.TenantId}/patients", login.AccessToken));
        Assert.Equal(HttpStatusCode.Forbidden, hidden.StatusCode);
        Assert.Contains("feature_blocked", await hidden.Content.ReadAsStringAsync(), StringComparison.Ordinal);

        await using (var admin = new NpgsqlConnection(database.AdminConnectionString))
        {
            Assert.Equal("allowed", await SetFeatureAsync(admin, home.TenantId, blocked: false));
            var actions = (await admin.QueryAsync<string>(
                """
                SELECT action FROM odca.audit_events
                 WHERE tenant_id=@tenant AND action IN ('organization.feature_blocked','organization.feature_released')
                """,
                new { tenant = home.TenantId })).AsList();
            Assert.Contains("organization.feature_blocked", actions);
            Assert.Contains("organization.feature_released", actions);
            var metadata = await admin.ExecuteScalarAsync<string>(
                """
                SELECT metadata::text FROM odca.audit_events
                 WHERE tenant_id=@tenant AND action='organization.feature_blocked'
                 ORDER BY occurred_at DESC LIMIT 1
                """,
                new { tenant = home.TenantId });
            Assert.NotNull(metadata);
            Assert.Contains("patients", metadata, StringComparison.Ordinal);
            using var metadataDocument = JsonDocument.Parse(metadata);
            Assert.False(metadataDocument.RootElement.TryGetProperty("password", out _));
        }

        using var visible = await client.SendAsync(Authorized(HttpMethod.Get, $"/api/v1/organizations/{home.TenantId}/patients?search=Preservado", login.AccessToken));
        visible.EnsureSuccessStatusCode();
        var page = await visible.Content.ReadFromJsonAsync<PatientPage>();
        Assert.NotNull(page);
        Assert.Contains(page.Items, item => item.FullName == "Paciente Preservado");

        await using (var admin = new NpgsqlConnection(database.AdminConnectionString))
        {
            await admin.OpenAsync();
            await admin.ExecuteAsync("UPDATE odca.tenants SET status='suspended' WHERE id=@tenant", new { tenant = home.TenantId });
        }

        using var suspended = await client.SendAsync(Authorized(HttpMethod.Get, $"/api/v1/organizations/{home.TenantId}/patients", login.AccessToken));
        Assert.Equal(HttpStatusCode.Unauthorized, suspended.StatusCode);
        using var situation = await client.SendAsync(Authorized(HttpMethod.Get, $"/api/v1/organizations/{home.TenantId}/features", login.AccessToken));
        Assert.Equal(HttpStatusCode.OK, situation.StatusCode);
        var suspendedCatalog = await situation.Content.ReadFromJsonAsync<OrganizationFeatureCatalogResponse>();
        Assert.Equal("suspended", suspendedCatalog?.TenantStatus);

        await using (var admin = new NpgsqlConnection(database.AdminConnectionString))
        {
            await admin.OpenAsync();
            Assert.Equal(1, await admin.ExecuteScalarAsync<int>(
                "SELECT count(*)::int FROM odca.patients WHERE tenant_id=@tenant AND full_name='Paciente Preservado'",
                new { tenant = home.TenantId }));
            await admin.ExecuteAsync("UPDATE odca.tenants SET status='active' WHERE id=@tenant", new { tenant = home.TenantId });
        }

        using var restored = await client.SendAsync(Authorized(HttpMethod.Get, $"/api/v1/organizations/{home.TenantId}/patients?search=Preservado", login.AccessToken));
        restored.EnsureSuccessStatusCode();

        await using (var admin = new NpgsqlConnection(database.AdminConnectionString))
        {
            await admin.ExecuteAsync("DELETE FROM odca.tenants WHERE id=@id", new { id = otherTenant });
        }
    }

    private static async Task<string> SetFeatureAsync(NpgsqlConnection connection, Guid tenantId, bool blocked)
    {
        if (connection.State != System.Data.ConnectionState.Open)
        {
            await connection.OpenAsync();
        }

        await using var transaction = await connection.BeginTransactionAsync();
        await connection.ExecuteAsync(
            "SELECT set_config('odca.user_id', @actor, true)",
            new { actor = PlatformActorId.ToString() },
            transaction);
        var state = await connection.ExecuteScalarAsync<string>(
            "SELECT odca.set_organization_feature_block(@actor, @tenant, 'patients', @blocked, @reason)",
            new { actor = PlatformActorId, tenant = tenantId, blocked, reason = "Homologação do bloqueio administrativo" },
            transaction);
        await transaction.CommitAsync();
        return state ?? string.Empty;
    }

    private static HttpRequestMessage Authorized(HttpMethod method, string url, string token)
    {
        var request = new HttpRequestMessage(method, url);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return request;
    }

    private static string NewCpf()
    {
        Span<char> digits = stackalloc char[11];
        for (var index = 0; index < 9; index++)
        {
            digits[index] = (char)('0' + Random.Shared.Next(10));
        }

        if (digits[..9].ToArray().Distinct().Count() == 1)
        {
            digits[8] = digits[8] == '0' ? '1' : '0';
        }

        digits[9] = Digit(digits, 9);
        digits[10] = Digit(digits, 10);
        var value = new string(digits);
        Assert.Equal("cpf", BrazilianDocument.NormalizeAndValidate(value).Type);
        return value;
    }

    private static char Digit(Span<char> digits, int length)
    {
        var sum = 0;
        var weight = length + 1;
        for (var index = 0; index < length; index++)
        {
            sum += (digits[index] - '0') * weight--;
        }

        var remainder = sum % 11;
        return (char)('0' + (remainder < 2 ? 0 : 11 - remainder));
    }
}
