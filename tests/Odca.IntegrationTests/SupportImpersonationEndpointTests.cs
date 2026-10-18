using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Json;
using Dapper;
using Microsoft.AspNetCore.Mvc.Testing;
using Npgsql;
using Odca.Application.Identity;
using Odca.Contracts.Identity;
using Odca.Contracts.Support;
using Odca.Infrastructure.Common;
using Odca.Infrastructure.Identity;

namespace Odca.IntegrationTests;

/// <summary>
/// D-OC43 (Sprint 9/F-1): visão do cliente (impersonação profunda) dentro da janela de
/// suporte — ponta a ponta sobre HTTP real: o token emitido é do usuário-alvo na sua
/// sessão dedicada (sub/sid/mfa), carrega imp_op/imp_grant e é revalidado por request no
/// middleware (concessão encerrada/expirada ou janela fora do ar invalidam imediatamente).
/// O encerramento vindo da própria visão do cliente resolve o ator pelo vínculo imp_op.
/// Apenas dados sintéticos.
/// </summary>
public sealed class SupportImpersonationEndpointTests(DatabaseFixture database) : IClassFixture<DatabaseFixture>
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private static readonly Guid OperatorUser = Guid.Parse("20000000-0000-0000-0000-000000000001");

    [Fact]
    public async Task ClientViewIssuesTargetScopedTokenValidatedPerRequestAndEndsFromTheView()
    {
        await database.ResetAuthenticationScenarioAsync();
        await using var factory = database.CreateApi();
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        var verified = await ChangeInitialPasswordAndEnrollMfaAsync(client);

        var tenant = Guid.NewGuid();
        await InsertTenantAsync(tenant);
        var target = Guid.NewGuid();
        await InsertTargetUserAsync(target, tenant);

        using var open = Authorized(HttpMethod.Post, "/api/v1/platform/support-sessions", verified.AccessToken);
        open.Content = JsonContent.Create(new OpenSupportSessionRequest(tenant, "Janela para a visao do cliente", 30));
        using var openResponse = await client.SendAsync(open);
        Assert.Equal(HttpStatusCode.Created, openResponse.StatusCode);
        var session = (await openResponse.Content.ReadFromJsonAsync<SupportSessionResponse>())!;

        using var membersRequest = Authorized(HttpMethod.Get, $"/api/v1/platform/support-sessions/{session.Id}/members", verified.AccessToken);
        using var membersResponse = await client.SendAsync(membersRequest);
        Assert.Equal(HttpStatusCode.OK, membersResponse.StatusCode);
        var members = (await membersResponse.Content.ReadFromJsonAsync<SupportSessionMemberResponse[]>())!
            .ToList();
        Assert.Contains(members, m => m.UserId == target && m.DisplayName == "Cliente alvo sintético");

        using var start = Authorized(HttpMethod.Post, $"/api/v1/platform/support-sessions/{session.Id}/impersonation", verified.AccessToken);
        start.Content = JsonContent.Create(new StartSupportImpersonationRequest(target, 10));
        using var startResponse = await client.SendAsync(start);
        Assert.Equal(HttpStatusCode.OK, startResponse.StatusCode);
        var imp = (await startResponse.Content.ReadFromJsonAsync<StartSupportImpersonationResponse>())!;
        Assert.NotEqual(Guid.Empty, imp.GrantId);
        Assert.InRange(imp.ExpiresAt, DateTimeOffset.UtcNow.AddMinutes(8), DateTimeOffset.UtcNow.AddMinutes(12));

        // O bearer emitido pertence ao ALVO, na sua sessao dedicada, com o vinculo ao operador.
        var jwt = new JwtSecurityTokenHandler().ReadJwtToken(imp.AccessToken);
        Assert.Equal(target.ToString(), jwt.Claims.First(c => c.Type == "sub").Value);
        Assert.Equal(OperatorUser.ToString(), jwt.Claims.First(c => c.Type == "imp_op").Value);
        Assert.Equal(imp.GrantId.ToString(), jwt.Claims.First(c => c.Type == "imp_grant").Value);
        Assert.Equal("true", jwt.Claims.First(c => c.Type == "mfa_verified").Value);
        Assert.Equal("false", jwt.Claims.First(c => c.Type == "must_change_password").Value);
        Assert.True(jwt.Claims.All(c => c.Type != ClaimTypes.Role || c.Value != "SuperAdministrator"),
            "o alvo nao pode herdar papel de plataforma");
        var targetSessionId = Guid.Parse(jwt.Claims.First(c => c.Type == "sid").Value);

        var dedicatedSession = await TargetDedicatedSessionAsync(targetSessionId, target);
        Assert.True(dedicatedSession.Alive);
        Assert.Equal("mfa", dedicatedSession.AuthenticationLevel);

        // Com a concessao viva o middleware aceita o token, mas o alvo nao tem papel de
        // plataforma: 403 puro (e nao 401) na rota de plataforma.
        using var platformList = Authorized(HttpMethod.Get, "/api/v1/platform/support-sessions", imp.AccessToken);
        using var platformListResponse = await client.SendAsync(platformList);
        Assert.Equal(HttpStatusCode.Forbidden, platformListResponse.StatusCode);

        // O operador continua operando normalmente com o proprio token.
        using var operatorMembers = Authorized(HttpMethod.Get, $"/api/v1/platform/support-sessions/{session.Id}/members", verified.AccessToken);
        using var operatorMembersResponse = await client.SendAsync(operatorMembers);
        Assert.Equal(HttpStatusCode.OK, operatorMembersResponse.StatusCode);

        // Saida VINDO da visao do cliente: o ator e resolvido pelo vinculo imp_op (42501
        // protegeria caso o operador nao fosse o dono da concessao).
        using var end = Authorized(HttpMethod.Post, $"/api/v1/platform/support-sessions/{session.Id}/impersonation/{imp.GrantId}/end", imp.AccessToken);
        using var endResponse = await client.SendAsync(end);
        Assert.Equal(HttpStatusCode.NoContent, endResponse.StatusCode);

        var grant = await GrantRowAsync(imp.GrantId);
        Assert.Equal("ended", grant.EndType);
        Assert.Equal(OperatorUser, grant.EndedBy);
        Assert.False((await TargetDedicatedSessionAsync(targetSessionId, target)).Alive,
            "a sessao dedicada do alvo precisa ser revogada para matar tokens em voo");

        // apos o encerramento, o token da visao falha na revalidacao por request (401, nao 403):
        using var afterEnd = Authorized(HttpMethod.Get, "/api/v1/platform/support-sessions", imp.AccessToken);
        using var afterEndResponse = await client.SendAsync(afterEnd);
        Assert.Equal(HttpStatusCode.Unauthorized, afterEndResponse.StatusCode);

        // Rastreabilidade com o ator real: eventos da janela + auditoria da plataforma.
        Assert.Equal(1, await CountEventAsync(session.Id, "impersonation_started", OperatorUser));
        Assert.Equal(1, await CountEventAsync(session.Id, "impersonation_ended", OperatorUser));
        Assert.Equal(1, await AuditCountAsync("superadmin.support_impersonation.started", imp.GrantId));
        Assert.Equal(1, await AuditCountAsync("superadmin.support_impersonation.ended", imp.GrantId));
    }

    [Fact]
    public async Task StartValidatesWindowTargetAndDurationAndReapsNaturallyExpiredGrants()
    {
        await database.ResetAuthenticationScenarioAsync();
        await using var factory = database.CreateApi();
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        var verified = await ChangeInitialPasswordAndEnrollMfaAsync(client);

        var tenant = Guid.NewGuid();
        await InsertTenantAsync(tenant);
        var target = Guid.NewGuid();
        await InsertTargetUserAsync(target, tenant);

        using var open = Authorized(HttpMethod.Post, "/api/v1/platform/support-sessions", verified.AccessToken);
        open.Content = JsonContent.Create(new OpenSupportSessionRequest(tenant, "Janela de validacao da visao", 60));
        using var openResponse = await client.SendAsync(open);
        Assert.Equal(HttpStatusCode.Created, openResponse.StatusCode);
        var session = (await openResponse.Content.ReadFromJsonAsync<SupportSessionResponse>())!;

        // Validacao do controller: duracao fora de 5..60 minutos.
        using var shortStart = Authorized(HttpMethod.Post, $"/api/v1/platform/support-sessions/{session.Id}/impersonation", verified.AccessToken);
        shortStart.Content = JsonContent.Create(new StartSupportImpersonationRequest(target, 4));
        using var shortResponse = await client.SendAsync(shortStart);
        Assert.Equal(HttpStatusCode.BadRequest, shortResponse.StatusCode);

        // Validacao do SQL: alvo sem membership ativa no tenant da janela.
        using var strangerStart = Authorized(HttpMethod.Post, $"/api/v1/platform/support-sessions/{session.Id}/impersonation", verified.AccessToken);
        strangerStart.Content = JsonContent.Create(new StartSupportImpersonationRequest(Guid.NewGuid(), 10));
        using var strangerResponse = await client.SendAsync(strangerStart);
        Assert.Equal(HttpStatusCode.BadRequest, strangerResponse.StatusCode);

        // Concorrencia: uma unica concessao ativa por janela.
        var first = await StartAsync(client, verified.AccessToken, session.Id, target, 10);
        using var second = Authorized(HttpMethod.Post, $"/api/v1/platform/support-sessions/{session.Id}/impersonation", verified.AccessToken);
        second.Content = JsonContent.Create(new StartSupportImpersonationRequest(target, 10));
        using var secondResponse = await client.SendAsync(second);
        Assert.Equal(HttpStatusCode.Conflict, secondResponse.StatusCode);
        var conflictBody = await secondResponse.Content.ReadAsStringAsync();
        Assert.Contains("\"code\":\"support_impersonation.active_exists\"", conflictBody, StringComparison.Ordinal);

        // Encerramento pelo lado da plataforma (operador direto, sem imp_op no token).
        using var operatorEnd = Authorized(HttpMethod.Post, $"/api/v1/platform/support-sessions/{session.Id}/impersonation/{first.GrantId}/end", verified.AccessToken);
        using var operatorEndResponse = await client.SendAsync(operatorEnd);
        Assert.Equal(HttpStatusCode.NoContent, operatorEndResponse.StatusCode);

        // Expiracao natural da concessao: o proximo inicio colhe o registro antigo antes
        // de abrir outro (revogando a sessao dedicada), sem bloquear a janela para sempre.
        var stale = await StartAsync(client, verified.AccessToken, session.Id, target, 5);
        await ExpireGrantAsync(stale.GrantId);
        var renewed = await StartAsync(client, verified.AccessToken, session.Id, target, 5);
        Assert.NotEqual(stale.GrantId, renewed.GrantId);
        var staleRow = await GrantRowAsync(stale.GrantId);
        Assert.Equal("ended", staleRow.EndType);
        Assert.Equal(OperatorUser, staleRow.EndedBy);
        Assert.Equal("ended", (await EndGrantAsync(client, verified.AccessToken, session.Id, renewed.GrantId)).EndType);
    }

    private static async Task<StartSupportImpersonationResponse> StartAsync(
        HttpClient client, string token, Guid sessionId, Guid target, int minutes)
    {
        using var request = Authorized(HttpMethod.Post, $"/api/v1/platform/support-sessions/{sessionId}/impersonation", token);
        request.Content = JsonContent.Create(new StartSupportImpersonationRequest(target, minutes));
        using var response = await client.SendAsync(request);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<StartSupportImpersonationResponse>())!;
    }

    private async Task<GrantRow> EndGrantAsync(
        HttpClient client, string token, Guid sessionId, Guid grantId)
    {
        using var request = Authorized(HttpMethod.Post, $"/api/v1/platform/support-sessions/{sessionId}/impersonation/{grantId}/end", token);
        using var response = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        return await GrantRowAsync(grantId);
    }

    private sealed record GrantRow(string EndType, Guid EndedBy);

    private async Task<GrantRow> GrantRowAsync(Guid grantId)
    {
        await using var connection = new NpgsqlConnection(database.AdminConnectionString);
        return await connection.QuerySingleAsync<GrantRow>($"""
            SELECT end_type::text AS "EndType", ended_by AS "EndedBy"
              FROM odca.support_impersonations WHERE id = @id;
            """, new { id = grantId });
    }

    private sealed record DedicatedSession(bool Alive, string AuthenticationLevel);

    private async Task<DedicatedSession> TargetDedicatedSessionAsync(Guid sessionId, Guid userId)
    {
        await using var connection = new NpgsqlConnection(database.AdminConnectionString);
        return await connection.QuerySingleAsync<DedicatedSession>("""
            SELECT (revoked_at IS NULL AND expires_at > now()) AS "Alive",
                   authentication_level::text AS "AuthenticationLevel"
              FROM odca.sessions WHERE id = @id AND user_id = @user;
            """, new { id = sessionId, user = userId });
    }

    private async Task ExpireGrantAsync(Guid grantId)
    {
        await using var connection = new NpgsqlConnection(database.AdminConnectionString);
        await connection.ExecuteAsync("""
            UPDATE odca.support_impersonations
               SET started_at = now() - interval '10 minutes',
                   expires_at = now() - interval '1 minute'
             WHERE id = @id;
            """, new { id = grantId });
    }

    private async Task<int> AuditCountAsync(string action, Guid entityId)
    {
        await using var connection = new NpgsqlConnection(database.AdminConnectionString);
        return await connection.ExecuteScalarAsync<int>(
            """
            SELECT count(*)::int FROM odca.audit_events
             WHERE action = @action AND entity_id = @entity AND result = 'success'
               AND scope_type = 'platform' AND actor_user_id = @actor;
            """, new { action, entity = entityId, actor = OperatorUser });
    }

    private async Task<int> CountEventAsync(Guid sessionId, string eventType, Guid actor)
    {
        await using var connection = new NpgsqlConnection(database.AdminConnectionString);
        return await connection.ExecuteScalarAsync<int>(
            """
            SELECT count(*)::int FROM odca.support_session_events
             WHERE session_id = @session AND event_type = @type AND actor_user_id = @actor;
            """, new { session = sessionId, type = eventType, actor });
    }

    private async Task InsertTenantAsync(Guid tenant)
    {
        await using var connection = new NpgsqlConnection(database.AdminConnectionString);
        await connection.ExecuteAsync("""
            INSERT INTO odca.tenants(id, business_code, display_name, status, timezone)
            VALUES (@tenant, @businessCode, 'Organização sintética de visão', 'active', 'America/Sao_Paulo');
            """, new { tenant, businessCode = tenant.ToString("N")[..20] });
    }

    private async Task InsertTargetUserAsync(Guid userId, Guid tenant)
    {
        var email = $"{userId:N}@visao.test";
        await using var connection = new NpgsqlConnection(database.AdminConnectionString);
        await connection.ExecuteAsync("""
            INSERT INTO odca.users(id, email, email_normalized, login_normalized, display_name, password_hash, must_change_password, is_platform_administrator, email_verified_at)
            VALUES (@id, @email, @emailUpper, @loginUpper, 'Cliente alvo sintético', 'hash', false, false, now());
            INSERT INTO odca.memberships(tenant_id, user_id, status) VALUES (@tenant, @id, 'active');
            """, new
        {
            id = userId,
            email,
            emailUpper = email.ToUpperInvariant(),
            loginUpper = email.ToUpperInvariant(),
            tenant
        });
    }

    private static HttpRequestMessage Authorized(HttpMethod method, string path, string token)
    {
        var request = new HttpRequestMessage(method, path);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return request;
    }

    private static async Task<MfaVerificationResponse> ChangeInitialPasswordAndEnrollMfaAsync(HttpClient client)
    {
        using var firstResponse = await client.PostAsJsonAsync(
            "/api/v1/auth/login", new LoginRequest(DatabaseFixture.Email, DatabaseFixture.InitialPassword));
        firstResponse.EnsureSuccessStatusCode();
        var first = (await firstResponse.Content.ReadFromJsonAsync<LoginResponse>())!;
        Assert.True(first.MustChangePassword);

        using var change = Authorized(HttpMethod.Post, "/api/v1/auth/change-password", first.AccessToken);
        change.Content = JsonContent.Create(new ChangePasswordRequest(
            DatabaseFixture.InitialPassword, DatabaseFixture.ChangedPassword));
        using var changeResponse = await client.SendAsync(change);
        changeResponse.EnsureSuccessStatusCode();
        var changed = (await changeResponse.Content.ReadFromJsonAsync<LoginResponse>())!;

        using var start = Authorized(HttpMethod.Post, "/api/v1/auth/mfa/enrollment", changed.AccessToken);
        using var startResponse = await client.SendAsync(start);
        startResponse.EnsureSuccessStatusCode();
        var enrollment = (await startResponse.Content.ReadFromJsonAsync<MfaEnrollmentResponse>())!;
        var code = new TotpService(new SystemClock()).GenerateCode(enrollment.ManualKey);

        using var confirm = Authorized(HttpMethod.Post, "/api/v1/auth/mfa/enrollment/confirm", changed.AccessToken);
        confirm.Content = JsonContent.Create(new ConfirmMfaEnrollmentRequest(code));
        using var confirmResponse = await client.SendAsync(confirm);
        confirmResponse.EnsureSuccessStatusCode();
        var verified = (await confirmResponse.Content.ReadFromJsonAsync<MfaVerificationResponse>())!;
        Assert.True(verified.MfaVerified);
        return verified;
    }
}
