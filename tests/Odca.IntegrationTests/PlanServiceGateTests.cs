using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Dapper;
using Npgsql;
using Odca.Application.Onboarding;
using Odca.Contracts.Administration;
using Odca.Contracts.Identity;
using Odca.Contracts.Onboarding;
using Odca.Contracts.Solicitations;

namespace Odca.IntegrationTests;

/// <summary>
/// D-OC31 A.3: regra canônica de planos (6/6/7 após v038) e os quatro serviços da
/// Central de solicitações diferenciados individualmente. Em base fresca o onboarding
/// atribui as versões v2 dos códigos (basic/intermediate/enterprise); a ativação por
/// SQL representa a concessão de desenvolvimento e não confunde a regra comercial:
/// os serviços documentais exigem Enterprise enquanto suporte_tecnico aceita qualquer
/// assinatura ativa (409 solicitations.plan.required nos demais casos).
/// </summary>
public sealed class PlanServiceGateTests(DatabaseFixture database) : IClassFixture<DatabaseFixture>
{
    private static readonly (string Code, Guid VersionId)[] Plans =
    {
        ("basic", Guid.Parse("30000000-0000-0000-0000-000000000011")),
        ("intermediate", Guid.Parse("30000000-0000-0000-0000-000000000012")),
        ("enterprise", Guid.Parse("30000000-0000-0000-0000-000000000013")),
    };

    private static readonly string[] Services = { "revisao", "adaptacao", "esclarecimento", "suporte_tecnico" };

    [Fact]
    public async Task PlanGateDifferentiatesTheFourServicesPerPlan()
    {
        await database.ResetAuthenticationScenarioAsync();
        await using var factory = database.CreateApi();
        using var client = factory.CreateClient();

        foreach (var (code, versionId) in Plans)
        {
            var email = $"plan.gate.{code}.{Guid.NewGuid():N}@example.test";
            using var started = await client.PostAsJsonAsync("/api/v1/onboarding/registrations", new StartCustomerRegistrationRequest(
                code, NewCpf(), $"Cliente gate {code}", email, "Cliente!Password2026", true, true, false));
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

            using var homeResponse = await client.SendAsync(Authorized(HttpMethod.Get, "/api/v1/onboarding/customer-home", login.AccessToken));
            homeResponse.EnsureSuccessStatusCode();
            var home = await homeResponse.Content.ReadFromJsonAsync<CustomerHomeResponse>();
            Assert.NotNull(home);
            var tenantId = home.TenantId;

            await using var admin = new NpgsqlConnection(database.AdminConnectionString);
            await admin.OpenAsync();

            // Catálogo canônico 6/6/7: v038 desativa module.reviews em basic-v2 e intermediate-v2.
            var enabledModules = await admin.ExecuteScalarAsync<int>(
                """
                SELECT count(*)::int FROM odca.plan_entitlements
                 WHERE plan_version_id=@v AND entitlement_code LIKE 'module.%' AND enabled
                """, new { v = versionId });
            var expectedModules = code == "enterprise" ? 7 : 6;
            Assert.True(enabledModules == expectedModules, $"{code}: modulos habilitados={enabledModules} esperado={expectedModules}");
            var reviewsEnabled = await admin.ExecuteScalarAsync<bool>(
                "SELECT enabled FROM odca.plan_entitlements WHERE plan_version_id=@v AND entitlement_code='module.reviews'",
                new { v = versionId });
            Assert.True(reviewsEnabled == (code == "enterprise"), $"{code}: module.reviews enabled={reviewsEnabled}");

            // Ativação de desenvolvimento: organizacao/membros ativos e assinatura ativa.
            await admin.ExecuteAsync(
                """
                UPDATE odca.tenants SET status='active' WHERE id=@tenant;
                UPDATE odca.memberships SET status='active' WHERE tenant_id=@tenant;
                UPDATE odca.subscriptions SET commercial_state='active', status='active' WHERE tenant_id=@tenant;
                """, new { tenant = tenantId });
            var subscribedVersion = await admin.ExecuteScalarAsync<Guid>(
                "SELECT plan_version_id FROM odca.subscriptions WHERE tenant_id=@tenant AND status='active'",
                new { tenant = tenantId });
            Assert.True(subscribedVersion == versionId, $"{code}: plan_version_id={subscribedVersion} esperado={versionId}");

            foreach (var service in Services)
            {
                using var request = new HttpRequestMessage(HttpMethod.Post, $"/api/v1/organizations/{tenantId}/solicitations");
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", login.AccessToken);
                request.Content = JsonContent.Create(new OpenSolicitationRequest(
                    service, null, $"Gate plano {code} servico {service}",
                    "Descricao de homologacao do gate de plano por servico individual.", Guid.NewGuid()));
                using var response = await client.SendAsync(request);
                var body = await response.Content.ReadAsStringAsync();
                var allowed = code == "enterprise" || service == "suporte_tecnico";
                if (allowed)
                {
                    Assert.True(response.StatusCode is HttpStatusCode.OK or HttpStatusCode.Created,
                        $"{code}/{service} -> {(int)response.StatusCode} {body}");
                }
                else
                {
                    Assert.True(response.StatusCode == HttpStatusCode.Conflict, $"{code}/{service} -> {(int)response.StatusCode} {body}");
                    Assert.Contains("solicitations.plan.required", body, StringComparison.Ordinal);
                }
            }

            var created = await admin.ExecuteScalarAsync<int>(
                "SELECT count(*)::int FROM odca.solicitations WHERE tenant_id=@tenant", new { tenant = tenantId });
            var expectedCreated = code == "enterprise" ? 4 : 1;
            Assert.True(created == expectedCreated, $"{code}: solicitacoes criadas={created} esperado={expectedCreated}");
        }
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
