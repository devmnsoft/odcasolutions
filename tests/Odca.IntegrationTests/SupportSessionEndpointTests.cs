using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Dapper;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Options;
using Npgsql;
using Odca.Api.Controllers;
using Odca.Application.Identity;
using Odca.Contracts.Identity;
using Odca.Contracts.Support;
using Odca.Infrastructure.Common;
using Odca.Infrastructure.Identity;
using Odca.Infrastructure.Support;

namespace Odca.IntegrationTests;

/// <summary>
/// D-OC39 (Sprint 5/F-2): sessao temporaria de suporte ponta a ponta — abertura com motivo e
/// duracao limitados, extensao sem retroceder a expiracao (v060), encerramento/revogacao
/// terminais, eventos e auditoria com o ator real. A defesa profunda no SQL (42501 para quem
/// nao e superadministrador) e a reautenticacao MFA recente (F-1) sao exercitadas; o pipeline
/// HTTP emite o 403 puro da plataforma. Apenas dados sintéticos.
/// </summary>
public sealed class SupportSessionEndpointTests(DatabaseFixture database) : IClassFixture<DatabaseFixture>
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private static readonly Guid AdminUser = Guid.Parse("20000000-0000-0000-0000-000000000001");

    private const string JwtKey = "test-only-key-with-more-than-thirty-two-bytes-2026";

    [Fact]
    public async Task OpenCreatesAuditedWindowWithRealActor()
    {
        var seed = await SeedAsync();
        var controller = CreateController(seed.Admin);
        var repo = NpgsqlSupportSessionRepositoryFactory();

        var created = await controller.Open(
            new OpenSupportSessionRequest(seed.Tenant, "Validacao de sessao de suporte", 60), CancellationToken.None);
        Assert.IsAssignableFrom<ObjectResult>(created);
        Assert.Equal(StatusCodes.Status201Created, ((ObjectResult)created).StatusCode);
        var id = SessionIdFromOpen(created);

        var info = (await repo.GetAsync(seed.Admin, id, CancellationToken.None))!;
        Assert.Equal("active", info.Status);
        Assert.True(info.Valid);
        Assert.Equal(seed.TenantName, info.TenantName);
        Assert.Equal(seed.AdminName, info.SuperadminName);
        Assert.Equal("Validacao de sessao de suporte", info.Reason);
        Assert.InRange(info.ExpiresAt, DateTimeOffset.UtcNow.AddMinutes(55), DateTimeOffset.UtcNow.AddMinutes(65));

        var events = await repo.EventsAsync(id, CancellationToken.None);
        Assert.Contains(events, e => e.EventType == "started" && e.ActorUserId == seed.Admin);

        Assert.Equal(1, await AuditCountAsync("superadmin.support_session.opened", seed.Admin, id));
    }

    [Fact]
    public async Task SecondActiveSessionForSameAdminAndTenantConflictsAndReopensAfterClose()
    {
        var seed = await SeedAsync();
        var controller = CreateController(seed.Admin);

        var first = await controller.Open(
            new OpenSupportSessionRequest(seed.Tenant, "Primeira janela de atendimento", 60), CancellationToken.None);
        Assert.IsAssignableFrom<ObjectResult>(first);
        var id = SessionIdFromOpen(first);

        var conflict = await controller.Open(
            new OpenSupportSessionRequest(seed.Tenant, "Segunda janela do mesmo admin", 30), CancellationToken.None);
        var conflictBody = Assert.IsType<ConflictObjectResult>(conflict);
        var json = JsonSerializer.Serialize(conflictBody.Value, JsonOptions);
        Assert.Contains("\"code\":\"support_session.active_exists\"", json, StringComparison.Ordinal);

        var ended = await controller.End(id, CancellationToken.None);
        Assert.Equal("ended", ((SupportSessionResponse)Assert.IsType<OkObjectResult>(ended).Value!).Status);

        var reopened = await controller.Open(
            new OpenSupportSessionRequest(seed.Tenant, "Janela reaberta apos encerramento", 30), CancellationToken.None);
        Assert.IsAssignableFrom<ObjectResult>(reopened);
        Assert.Equal(StatusCodes.Status201Created, ((ObjectResult)reopened).StatusCode);
    }

    [Fact]
    public async Task ExtendAddsMinutesAndRecordsAuditedEventWithoutRewritingHistory()
    {
        var seed = await SeedAsync();
        var controller = CreateController(seed.Admin);
        var opened = await controller.Open(
            new OpenSupportSessionRequest(seed.Tenant, "Janela base para extensao", 60), CancellationToken.None);
        var id = SessionIdFromOpen(opened);

        var extended = await controller.Extend(id, new ExtendSupportSessionRequest(30), CancellationToken.None);
        var body = Assert.IsType<OkObjectResult>(extended);
        var session = (SupportSessionResponse)body.Value!;
        Assert.True(session.Valid);
        Assert.InRange(session.ExpiresAt, DateTimeOffset.UtcNow.AddMinutes(85), DateTimeOffset.UtcNow.AddMinutes(95));

        var events = await CreateController(seed.Admin).Events(id, CancellationToken.None);
        var list = Assert.IsType<OkObjectResult>(events);
        var json = JsonSerializer.Serialize(list.Value, JsonOptions);
        Assert.Contains("\"eventType\":\"extended\"", json, StringComparison.Ordinal);
        Assert.Equal(30, await ExtendedEventMinutesAsync(id, seed.Admin));
        Assert.Equal(1, await AuditCountAsync("superadmin.support_session.extended", seed.Admin, id));

        var invalidMinutes = await controller.Extend(id, new ExtendSupportSessionRequest(0), CancellationToken.None);
        var invalid = Assert.IsAssignableFrom<ObjectResult>(invalidMinutes);
        Assert.Equal(StatusCodes.Status400BadRequest, invalid.StatusCode);

        var ended = await controller.End(id, CancellationToken.None);
        Assert.IsType<OkObjectResult>(ended);
        var staleExtend = await controller.Extend(id, new ExtendSupportSessionRequest(15), CancellationToken.None);
        var notFound = Assert.IsType<NotFoundObjectResult>(staleExtend);
        var notFoundJson = JsonSerializer.Serialize(notFound.Value, JsonOptions);
        Assert.Contains("\"code\":\"support_session.not_found\"", notFoundJson, StringComparison.Ordinal);
    }

    [Fact]
    public async Task EndByOwnerAndRevokeByAnyAdminAreTerminalWithRealActor()
    {
        var seed = await SeedAsync();
        var owner = CreateController(seed.Admin);
        var other = CreateController(seed.OtherAdmin);

        var first = await owner.Open(
            new OpenSupportSessionRequest(seed.Tenant, "Janela encerrada por outro admin", 60), CancellationToken.None);
        var firstId = SessionIdFromOpen(first);
        var endedByOther = await other.End(firstId, CancellationToken.None);
        var endedInfo = (SupportSessionResponse)Assert.IsType<OkObjectResult>(endedByOther).Value!;
        Assert.Equal("ended", endedInfo.Status);
        Assert.False(endedInfo.Valid);
        Assert.NotNull(endedInfo.EndedAt);
        Assert.Equal(seed.OtherAdmin, await EndedByAsync(firstId));
        Assert.Equal(1, await CountEventAsync(firstId, "ended", seed.OtherAdmin));

        var second = await owner.Open(
            new OpenSupportSessionRequest(seed.Tenant, "Janela revogada por outro admin", 60), CancellationToken.None);
        var secondId = SessionIdFromOpen(second);
        var revoked = await other.Revoke(secondId, CancellationToken.None);
        Assert.Equal("revoked", ((SupportSessionResponse)Assert.IsType<OkObjectResult>(revoked).Value!).Status);
        Assert.Equal(1, await CountEventAsync(secondId, "revoked", seed.OtherAdmin));

        var afterRevoke = await other.End(secondId, CancellationToken.None);
        Assert.IsType<NotFoundObjectResult>(afterRevoke);
    }

    [Fact]
    public async Task ExpiredActiveSessionIsReportedInvalidAndCannotBeExtended()
    {
        var seed = await SeedAsync();
        var controller = CreateController(seed.Admin);
        var repo = NpgsqlSupportSessionRepositoryFactory();

        var opened = await controller.Open(
            new OpenSupportSessionRequest(seed.Tenant, "Janela que expira antes da consulta", 1), CancellationToken.None);
        var id = SessionIdFromOpen(opened);
        await ExpireAsync(id);

        var info = (await repo.GetAsync(seed.Admin, id, CancellationToken.None))!;
        Assert.Equal("active", info.Status);
        Assert.False(info.Valid);

        var extend = await controller.Extend(id, new ExtendSupportSessionRequest(15), CancellationToken.None);
        var notFound = Assert.IsType<NotFoundObjectResult>(extend);
        var json = JsonSerializer.Serialize(notFound.Value, JsonOptions);
        Assert.Contains("\"code\":\"support_session.not_found\"", json, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RegularUserCannotOpenBecauseOfSqlGuardEvenWithoutPolicyLayer()
    {
        var seed = await SeedAsync();
        var controller = CreateController(seed.RegularUser);

        var result = await controller.Open(
            new OpenSupportSessionRequest(seed.Tenant, "Tentativa de um membro comum", 30), CancellationToken.None);
        var unavailable = Assert.IsAssignableFrom<ObjectResult>(result);
        Assert.Equal(StatusCodes.Status503ServiceUnavailable, unavailable.StatusCode);

        var rows = await ListCountAsync(seed.Tenant);
        Assert.Equal(0, rows);
    }

    [Fact]
    public async Task StaleRecentMfaBlocksOpenWhileFreshSessionOpensThroughHttp()
    {
        await database.ResetAuthenticationScenarioAsync();
        await using var factory = database.CreateApi();
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        var verified = await ChangeInitialPasswordAndEnrollMfaAsync(client);

        var tenant = Guid.NewGuid();
        await InsertTenantAsync(tenant);
        var securityVersion = await CurrentSecurityVersionAsync(AdminUser);

        // A sessao da inscricao MFA (mfa_verified_at = agora) e fresca; uma sessao sintetica
        // com MFA verificado ha duas horas e obsoleta. As duas passam na plataforma; apenas a
        // antiga falha na janela RecentMfa.
        var staleSession = Guid.NewGuid();
        await InsertSessionAsync(staleSession, securityVersion, AdminUser, mfaCompletedAt: DateTimeOffset.UtcNow.AddHours(-2));
        var staleToken = CraftToken(securityVersion, staleSession, AdminUser, mfaCompletedAt: DateTimeOffset.UtcNow.AddHours(-2), platformAdmin: true);

        using var staleList = Authorized(HttpMethod.Get, "/api/v1/platform/support-sessions", staleToken);
        using var staleListResponse = await client.SendAsync(staleList);
        Assert.Equal(HttpStatusCode.OK, staleListResponse.StatusCode);

        using var staleOpen = Authorized(HttpMethod.Post, "/api/v1/platform/support-sessions", staleToken);
        staleOpen.Content = JsonContent.Create(new OpenSupportSessionRequest(tenant, "Abertura com MFA obsoleto", 45));
        using var staleOpenResponse = await client.SendAsync(staleOpen);
        Assert.Equal(HttpStatusCode.Forbidden, staleOpenResponse.StatusCode);

        using var freshOpen = Authorized(HttpMethod.Post, "/api/v1/platform/support-sessions", verified.AccessToken);
        freshOpen.Content = JsonContent.Create(new OpenSupportSessionRequest(tenant, "Abertura com MFA recente", 45));
        using var freshOpenResponse = await client.SendAsync(freshOpen);
        Assert.Equal(HttpStatusCode.Created, freshOpenResponse.StatusCode);
        var created = await freshOpenResponse.Content.ReadFromJsonAsync<SupportSessionResponse>();
        Assert.NotNull(created);
        Assert.Equal("active", created!.Status);
        Assert.Equal("Abertura com MFA recente", created.Reason);

        Assert.Equal(1, await ListCountAsync(tenant));
    }

    [Fact]
    public async Task RegularUserGetsForbiddenOnSupportSessionsOverHttp()
    {
        await database.ResetAuthenticationScenarioAsync();
        await using var factory = database.CreateApi();
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

        var tenant = Guid.NewGuid();
        await InsertTenantAsync(tenant);
        var userId = Guid.NewGuid();
        await InsertRegularUserAsync(userId, tenant);
        var securityVersion = await CurrentSecurityVersionAsync(userId);
        var sessionId = Guid.NewGuid();
        await InsertSessionAsync(sessionId, securityVersion, userId, mfaCompletedAt: null);
        var token = CraftToken(securityVersion, sessionId, userId, mfaCompletedAt: null, platformAdmin: false);

        using var open = Authorized(HttpMethod.Post, "/api/v1/platform/support-sessions", token);
        open.Content = JsonContent.Create(new OpenSupportSessionRequest(tenant, "Area restrita a superadministradores", 30));
        using var openResponse = await client.SendAsync(open);
        Assert.Equal(HttpStatusCode.Forbidden, openResponse.StatusCode);
    }

    private SupportSessionsController CreateController(Guid actor) =>
        new(NpgsqlSupportSessionRepositoryFactory()) { ControllerContext = new ControllerContext { HttpContext = TestControllerContexts.Create(actor) } };

    private NpgsqlSupportSessionRepository NpgsqlSupportSessionRepositoryFactory() =>
        new(NpgsqlDataSource.Create(database.AppConnectionString));

    private static Guid SessionIdFromOpen(IActionResult result) => ((SupportSessionResponse)((ObjectResult)result).Value!).Id;

    private async Task<int> AuditCountAsync(string action, Guid actor, Guid entityId)
    {
        await using var connection = new NpgsqlConnection(database.AdminConnectionString);
        return await connection.ExecuteScalarAsync<int>("""
            SELECT count(*)::int FROM odca.audit_events
             WHERE action = @action AND actor_user_id = @actor AND entity_id = @entity AND result = 'success';
            """, new { action, actor, entity = entityId });
    }

    private async Task<int> CountEventAsync(Guid sessionId, string eventType, Guid actor)
    {
        await using var connection = new NpgsqlConnection(database.AdminConnectionString);
        return await connection.ExecuteScalarAsync<int>("""
            SELECT count(*)::int FROM odca.support_session_events
             WHERE session_id = @session AND event_type = @type AND actor_user_id = @actor;
            """, new { session = sessionId, type = eventType, actor });
    }

    private async Task<int> ExtendedEventMinutesAsync(Guid sessionId, Guid actor)
    {
        await using var connection = new NpgsqlConnection(database.AdminConnectionString);
        return await connection.ExecuteScalarAsync<int>("""
            SELECT (details->>'minutes')::int FROM odca.support_session_events
             WHERE session_id = @session AND event_type = 'extended' AND actor_user_id = @actor
             ORDER BY occurred_at DESC LIMIT 1;
            """, new { session = sessionId, actor });
    }

    private async Task<int> ListCountAsync(Guid tenant)
    {
        await using var connection = new NpgsqlConnection(database.AdminConnectionString);
        return await connection.ExecuteScalarAsync<int>(
            "SELECT count(*)::int FROM odca.support_sessions WHERE tenant_id = @tenant;", new { tenant });
    }

    private async Task ExpireAsync(Guid sessionId)
    {
        await using var connection = new NpgsqlConnection(database.AdminConnectionString);
        await connection.ExecuteAsync(
            """
            UPDATE odca.support_sessions
               SET started_at = now() - interval '2 minutes',
                   expires_at = now() - interval '30 seconds'
             WHERE id = @id;
            """,
            new { id = sessionId });
    }

    private async Task InsertTenantAsync(Guid tenant)
    {
        await using var connection = new NpgsqlConnection(database.AdminConnectionString);
        await connection.ExecuteAsync("""
            INSERT INTO odca.tenants(id, business_code, display_name, status, timezone)
            VALUES (@tenant, @businessCode, 'Organização sintética de suporte', 'active', 'America/Sao_Paulo');
            """, new { tenant, businessCode = tenant.ToString("N")[..20] });
    }

    private async Task InsertRegularUserAsync(Guid userId, Guid tenant)
    {
        var email = $"{userId:N}@suporte.test";
        await using var connection = new NpgsqlConnection(database.AdminConnectionString);
        await connection.ExecuteAsync("""
            INSERT INTO odca.users(id, email, email_normalized, login_normalized, display_name, password_hash, must_change_password, is_platform_administrator, email_verified_at)
            VALUES (@id, @email, @emailUpper, @loginUpper, 'Usuario comum sintético', 'hash', false, false, now());
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

    private async Task<int> CurrentSecurityVersionAsync(Guid userId)
    {
        await using var connection = new NpgsqlConnection(database.AdminConnectionString);
        return await connection.ExecuteScalarAsync<int>(
            "SELECT security_version FROM odca.users WHERE id = @id;", new { id = userId });
    }

    private async Task InsertSessionAsync(Guid sessionId, int securityVersion, Guid userId, DateTimeOffset? mfaCompletedAt)
    {
        await using var connection = new NpgsqlConnection(database.AdminConnectionString);
        await connection.ExecuteAsync("""
            INSERT INTO odca.sessions(id, user_id, security_version, created_at, expires_at, authentication_level, mfa_completed_at)
            VALUES (@id, @user, @secver, now() - interval '5 minutes', now() + interval '14 minutes',
                    CASE WHEN @completed IS NULL THEN 'password' ELSE 'mfa' END, @completed);
            """, new { id = sessionId, user = userId, secver = securityVersion, completed = mfaCompletedAt });
    }

    private static string CraftToken(int securityVersion, Guid sessionId, Guid userId, DateTimeOffset? mfaCompletedAt, bool platformAdmin)
    {
        var options = new JwtOptions
        {
            Issuer = "odca-api-tests",
            Audience = "odca-api-tests",
            SigningKey = JwtKey,
            AccessTokenMinutes = 15
        };
        var service = new JwtTokenService(Options.Create(options));
        var user = new UserCredential(
            userId,
            platformAdmin ? DatabaseFixture.Email : $"{userId:N}@suporte.test",
            platformAdmin ? "Administrador de integração" : "Usuario comum sintético",
            "hash",
            securityVersion,
            false,
            platformAdmin,
            platformAdmin ? DateTimeOffset.UtcNow : null,
            false,
            null);
        var session = new SessionRecord(
            sessionId, userId, securityVersion, DateTimeOffset.UtcNow.AddMinutes(14),
            mfaCompletedAt is not null ? "mfa" : "password", mfaCompletedAt);
        return service.Issue(user, session).AccessToken;
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

    private async Task<Guid> EndedByAsync(Guid sessionId)
    {
        await using var connection = new NpgsqlConnection(database.AdminConnectionString);
        return await connection.ExecuteScalarAsync<Guid>(
            "SELECT ended_by FROM odca.support_sessions WHERE id = @id;", new { id = sessionId });
    }

    private sealed record Seed(Guid Tenant, string TenantName, Guid Admin, string AdminName, Guid OtherAdmin, Guid RegularUser);

    private async Task<Seed> SeedAsync()
    {
        var tenant = Guid.NewGuid();
        var admin = Guid.NewGuid();
        var otherAdmin = Guid.NewGuid();
        var regular = Guid.NewGuid();
        var tenantName = "Organização de suporte sintética";

        await using var connection = new NpgsqlConnection(database.AdminConnectionString);
        await connection.ExecuteAsync("""
            INSERT INTO odca.tenants(id, business_code, display_name, status, timezone)
            VALUES (@tenant, @businessCode, @name, 'active', 'America/Sao_Paulo');
            INSERT INTO odca.users(id, email, email_normalized, login_normalized, display_name, password_hash, is_platform_administrator)
            VALUES
                (@admin, @adminEmail, @adminEmailUpper, @adminEmailUpper, 'Superadmin de suporte', 'hash', true),
                (@otherAdmin, @otherEmail, @otherEmailUpper, @otherEmailUpper, 'Outro superadmin', 'hash', true),
                (@regular, @regularEmail, @regularEmailUpper, @regularEmailUpper, 'Membro comum', 'hash', false);
            """, new
        {
            tenant,
            name = tenantName,
            businessCode = tenant.ToString("N")[..20],
            admin,
            adminEmail = $"{tenant:N}-admin@suporte.test",
            adminEmailUpper = $"{tenant:N}-admin@suporte.test".ToUpperInvariant(),
            otherAdmin,
            otherEmail = $"{tenant:N}-other@suporte.test",
            otherEmailUpper = $"{tenant:N}-other@suporte.test".ToUpperInvariant(),
            regular,
            regularEmail = $"{tenant:N}-regular@suporte.test",
            regularEmailUpper = $"{tenant:N}-regular@suporte.test".ToUpperInvariant()
        });

        return new Seed(tenant, tenantName, admin, "Superadmin de suporte", otherAdmin, regular);
    }
}
