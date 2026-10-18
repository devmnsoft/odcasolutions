using System.Text.Json;
using System.Text.Json.Serialization;
using Dapper;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;
using Odca.Api;
using Odca.Api.Controllers;
using Odca.Application.Contracts;
using Odca.Contracts.Studio;
using Odca.Infrastructure.Signatures;

namespace Odca.IntegrationTests;

/// <summary>
/// D-OC38 (Sprint 4/F-1): o sino (odca.user_notifications) passa a materializar os eventos de
/// negócio que antes iam só para a outbox de e-mail — revisão atribuída, assinatura pendente
/// (lembrete/envio de envelope) e assinatura concluída — com deduplicação determinística por
/// source_id (v059) e respeito às preferências por canal. Apenas dados sintéticos: tenant/atores
/// aleatórios por teste. O canal e-mail segue desabilitado por padrão: nenhum fato espera linha
/// em notification_outbox sem preferência channel_email explícita.
/// </summary>
public sealed class NotificationFeedTests(DatabaseFixture database) : IClassFixture<DatabaseFixture>
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    [Fact]
    public async Task InAppRowIsDeduplicatedBySourceAndRespectsChannelPreference()
    {
        var seed = await SeedAsync();
        await using var app = new NpgsqlConnection(database.AppConnectionString);
        await app.OpenAsync();
        await using var tx = await app.BeginTransactionAsync();
        await SetGuc(app, tx, seed);

        var source = NotificationEnqueue.DeterministicSource("feed.test", "a");
        Assert.True(await NotificationEnqueue.EnqueueInAppAsync(app, tx, seed.Tenant, seed.Client, "signature_pending", "T1", "B1", source, NullLogger.Instance));
        // Replay do mesmo evento (mesma origem determinística): nenhuma linha nova.
        Assert.True(await NotificationEnqueue.EnqueueInAppAsync(app, tx, seed.Tenant, seed.Client, "signature_pending", "T1", "B1", source, NullLogger.Instance));
        Assert.Equal(1, await CountInAppRows(app, tx, seed));

        // Preferência por canal: channel_in_app desligado para o tipo não materializa linha nova.
        await app.ExecuteAsync(new CommandDefinition(
            """
            INSERT INTO odca.user_notification_preferences(tenant_id,user_id,kind,enabled,channel_in_app,channel_email)
            VALUES(@tenant,@user,'signature_pending',true,false,false)
            """, new { tenant = seed.Tenant, user = seed.Client }, tx));
        var otherSource = NotificationEnqueue.DeterministicSource("feed.test", "b");
        Assert.False(await NotificationEnqueue.EnqueueInAppAsync(app, tx, seed.Tenant, seed.Client, "signature_pending", "T2", "B2", otherSource, NullLogger.Instance));
        Assert.Equal(1, await CountInAppRows(app, tx, seed));

        await tx.CommitAsync();
    }

    [Fact]
    public async Task NonMemberRecipientDoesNotMaterializeInAppRow()
    {
        var seed = await SeedAsync();
        await using var app = new NpgsqlConnection(database.AppConnectionString);
        await app.OpenAsync();
        await using var tx = await app.BeginTransactionAsync();
        await SetGuc(app, tx, seed);

        // Evento de plataforma endereçado a usuário fora do tenant: sem membership, sem sino.
        var source = NotificationEnqueue.DeterministicSource("feed.test.nonmember", seed.NonMember);
        Assert.False(await NotificationEnqueue.EnqueueInAppAsync(app, tx, seed.Tenant, seed.NonMember, "solicitation_opened", "T", "B", source, NullLogger.Instance));
        Assert.Equal(0, await CountInAppRows(app, tx, seed));

        await tx.CommitAsync();
    }

    [Fact]
    public async Task ReviewAssignedCreatesSingleInAppRowAcrossIdempotentReplay()
    {
        var seed = await SeedAsync();
        await using var ds = NpgsqlDataSource.Create(database.AppConnectionString);
        var controller = CreateController(ds, seed.Actor);
        var idempotencyKey = Guid.NewGuid();

        var first = await controller.Submit(seed.Tenant, new SubmitReviewRequest(seed.Version, seed.Reviewer, null, null, idempotencyKey), CancellationToken.None);
        Assert.IsType<OkObjectResult>(first);
        var replay = await controller.Submit(seed.Tenant, new SubmitReviewRequest(seed.Version, seed.Reviewer, null, null, idempotencyKey), CancellationToken.None);
        Assert.IsType<OkObjectResult>(replay);

        await using var admin = new NpgsqlConnection(database.AdminConnectionString);
        await admin.OpenAsync();
        Assert.Equal(1, await admin.ExecuteScalarAsync<int>(
            """
            SELECT count(*)::int FROM odca.user_notifications
            WHERE tenant_id=@tenant AND user_id=@user AND kind='review_assigned'
              AND channel='in_app' AND source_id IS NOT NULL
            """, new { tenant = seed.Tenant, user = seed.Reviewer }));
        // Canal e-mail desabilitado por padrão: nenhum lado efeito na outbox sem preferência.
        Assert.Equal(0, await admin.ExecuteScalarAsync<int>(
            "SELECT count(*)::int FROM odca.notification_outbox WHERE tenant_id=@tenant", new { seed.Tenant }));
        Assert.Equal("submitted", await admin.ExecuteScalarAsync<string>(
            "SELECT review_status FROM odca.generated_contract_versions WHERE id=@version", new { seed.Version }));
    }

    [Fact]
    public async Task SignaturePendingRemindMaterialsInAppRowForLinkedParticipant()
    {
        var seed = await SeedAsync();
        await using var ds = NpgsqlDataSource.Create(database.AppConnectionString);
        var controller = CreateController(ds, seed.Actor);

        var first = Json(((OkObjectResult)await controller.RemindPreparation(seed.Tenant, seed.Version, seed.Preparation, CancellationToken.None)).Value);
        Assert.Contains("remindedAt", first, StringComparison.Ordinal);

        await using var admin = new NpgsqlConnection(database.AdminConnectionString);
        await admin.OpenAsync();
        var rows = await admin.ExecuteScalarAsync<int>(
            """
            SELECT count(*)::int FROM odca.user_notifications
            WHERE tenant_id=@tenant AND user_id=@user AND kind='signature_pending'
              AND channel='in_app' AND source_id IS NOT NULL AND title='Assinatura pendente'
            """, new { tenant = seed.Tenant, user = seed.Client });
        Assert.Equal(1, rows);
        Assert.Equal(0, await admin.ExecuteScalarAsync<int>(
            "SELECT count(*)::int FROM odca.notification_outbox WHERE tenant_id=@tenant", new { seed.Tenant }));

        // Segundo lembrete no cooldown de 60s: conflito explícito, sem duplicar o sino.
        var tooSoon = await controller.RemindPreparation(seed.Tenant, seed.Version, seed.Preparation, CancellationToken.None);
        Assert.Contains("reminder.too_soon", Json(((Microsoft.AspNetCore.Mvc.ConflictObjectResult)tooSoon).Value), StringComparison.Ordinal);
        Assert.Equal(1, await admin.ExecuteScalarAsync<int>(
            "SELECT count(*)::int FROM odca.user_notifications WHERE tenant_id=@tenant AND user_id=@user AND kind='signature_pending'",
            new { tenant = seed.Tenant, user = seed.Client }));
    }

    [Fact]
    public async Task SignatureCompletedNotifiesPreparerInAppOnce()
    {
        var seed = await SeedAsync();
        await using var ds = NpgsqlDataSource.Create(database.AppConnectionString);
        var controller = CreateController(ds, seed.Actor);

        var result = await controller.SignParticipant(seed.Tenant, seed.Version, seed.Preparation, seed.Client,
            new SignParticipantRequest("evidence", "Evidência sintética do fato de notificação"), CancellationToken.None);
        var signed = Json(((OkObjectResult)result).Value);
        Assert.Contains("\"allSigned\":true", signed, StringComparison.Ordinal);

        await using var admin = new NpgsqlConnection(database.AdminConnectionString);
        await admin.OpenAsync();
        Assert.Equal(1, await admin.ExecuteScalarAsync<int>(
            """
            SELECT count(*)::int FROM odca.user_notifications
            WHERE tenant_id=@tenant AND user_id=@user AND kind='signature_completed'
              AND channel='in_app' AND source_id IS NOT NULL
            """, new { tenant = seed.Tenant, user = seed.Actor }));
        Assert.Equal("externally_signed", await admin.ExecuteScalarAsync<string>(
            "SELECT review_status FROM odca.generated_contract_versions WHERE id=@version", new { seed.Version }));

        // Replay do ato pelo mesmo ator: estado real devolvido, sem segunda linha no sino.
        var replay = Json(((OkObjectResult)await controller.SignParticipant(seed.Tenant, seed.Version, seed.Preparation, seed.Client,
            new SignParticipantRequest("evidence", "Evidência sintética do fato de notificação"), CancellationToken.None)).Value);
        Assert.Contains("\"replayed\":true", replay, StringComparison.Ordinal);
        Assert.Equal(1, await admin.ExecuteScalarAsync<int>(
            "SELECT count(*)::int FROM odca.user_notifications WHERE tenant_id=@tenant AND user_id=@user AND kind='signature_completed'",
            new { tenant = seed.Tenant, user = seed.Actor }));
    }

    [Fact]
    public async Task NotificationContentFollowsRecipientPreferredLanguage()
    {
        var seed = await SeedAsync();
        await using var admin = new NpgsqlConnection(database.AdminConnectionString);
        await admin.OpenAsync();
        // D-OC46 (Sprint 12/F-1): preferência de idioma do destinatário + canal de e-mail
        // habilitado para o tipo — sem a segunda linha não haveria mensagem na outbox.
        await admin.ExecuteAsync("""
            INSERT INTO odca.user_notification_settings(tenant_id,user_id,preferred_language)
            VALUES(@tenant,@user,'en-US');
            INSERT INTO odca.user_notification_preferences(tenant_id,user_id,kind,channel_email)
            VALUES(@tenant,@user,'review_assigned',true);
            """, new { seed.Tenant, user = seed.Reviewer });

        await using var ds = NpgsqlDataSource.Create(database.AppConnectionString);
        var controller = CreateController(ds, seed.Actor);
        var idempotencyKey = Guid.NewGuid();

        var result = await controller.Submit(seed.Tenant, new SubmitReviewRequest(seed.Version, seed.Reviewer, null, null, idempotencyKey), CancellationToken.None);
        Assert.IsType<OkObjectResult>(result);

        // O conteúdo armazenado segue o idioma do DESTINATÁRIO (en-US), não do ator.
        var title = await admin.ExecuteScalarAsync<string>(
            """
            SELECT title FROM odca.user_notifications
            WHERE tenant_id=@tenant AND user_id=@user AND kind='review_assigned' AND channel='in_app'
            """, new { seed.Tenant, user = seed.Reviewer });
        Assert.Equal("You received a review request", title);
        // Canal e-mail ativo: a outbox preserva o idioma do destinatário (v062) para o transporte.
        var language = await admin.ExecuteScalarAsync<string>(
            "SELECT language FROM odca.notification_outbox WHERE tenant_id=@tenant AND kind='review_assigned'",
            new { seed.Tenant });
        Assert.Equal("en-US", language);
    }

    private sealed record Seed(Guid Tenant, Guid Actor, Guid Reviewer, Guid Client, Guid NonMember, Guid Version, Guid Preparation, Guid ClientMembership);

    private async Task<Seed> SeedAsync()
    {
        var tenant = Guid.NewGuid();
        var actor = Guid.NewGuid();
        var reviewer = Guid.NewGuid();
        var client = Guid.NewGuid();
        var nonMember = Guid.NewGuid();
        var role = Guid.NewGuid();
        var subscription = Guid.NewGuid();
        var template = Guid.NewGuid();
        var contract = Guid.NewGuid();
        var draft = Guid.NewGuid();
        var version = Guid.NewGuid();
        var preparation = Guid.NewGuid();
        var clientMembership = Guid.NewGuid();
        var pdfSha256 = "e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855";
        var storageKey = $"studio/feed-{version:N}.pdf";

        await using var admin = new NpgsqlConnection(database.AdminConnectionString);
        await admin.OpenAsync();
        await admin.ExecuteAsync("""
            INSERT INTO odca.tenants(id, business_code, display_name, status, timezone)
            VALUES (@tenant, @businessCode, 'Organização do feed', 'active', 'America/Sao_Paulo');
            INSERT INTO odca.users(id, email, email_normalized, login_normalized, display_name, password_hash, is_platform_administrator)
            VALUES
                (@actor, @actorEmail, @actorEmailUpper, @actorEmailUpper, 'Ator do feed', 'hash', false),
                (@reviewer, @reviewerEmail, @reviewerEmailUpper, @reviewerEmailUpper, 'Revisor do feed', 'hash', false),
                (@client, @clientEmail, @clientEmailUpper, @clientEmailUpper, 'Paciente sintético', 'hash', false),
                (@nonMember, @nonMemberEmail, @nonMemberEmailUpper, @nonMemberEmailUpper, 'Fora do tenant', 'hash', false);
            INSERT INTO odca.memberships(id, tenant_id, user_id, status)
            VALUES
                (@actorMembership, @tenant, @actor, 'active'),
                (@clientMembership, @tenant, @client, 'active'),
                (@reviewerMembership, @tenant, @reviewer, 'active');
            INSERT INTO odca.roles(id, scope_type, tenant_id, code, display_name, is_system)
            VALUES (@role, 'tenant', @tenant, 'notif-feed', 'Feed de notificação', true);
            INSERT INTO odca.role_permissions(role_id, permission_code)
            SELECT @role, code FROM odca.permissions
            WHERE code IN ('tenant.contract_drafts.read','tenant.contract_drafts.manage','tenant.signature_participants.record','tenant.reviews.request','tenant.reviews.decide');
            INSERT INTO odca.member_roles(tenant_id, user_id, role_id, assigned_by)
            VALUES (@tenant, @actor, @role, @actor), (@tenant, @reviewer, @role, @actor), (@tenant, @client, @role, @actor);
            INSERT INTO odca.subscriptions(id, tenant_id, plan_version_id, commercial_state, status, created_by)
            VALUES (@subscription, @tenant, '30000000-0000-0000-0000-000000000011', 'active', 'active', @actor);
            INSERT INTO odca.contract_templates(id, owner_tenant_id, name, description, contract_type, scope, status, published_at, author_id, current_version)
            VALUES (@template, @tenant, 'Modelo do feed', 'Modelo sintético de teste', 'service', 'private', 'published', now(), @actor, 1);
            INSERT INTO odca.contract_template_versions(id, template_id, version_number, content, fields, created_by, published_at)
            VALUES (gen_random_uuid(), @template, 1, '{"type":"document","content":[]}'::jsonb, '[]'::jsonb, @actor, now());
            INSERT INTO odca.contracts(id, tenant_id, title, reference) VALUES (@contract, @tenant, 'Contrato do feed', @contractReference);
            INSERT INTO odca.contract_drafts(id, tenant_id, contract_id, source_template_id, source_template_version_id, content, fields, created_by, updated_by)
            VALUES (@draft, @tenant, @contract, @template,
                    (SELECT id FROM odca.contract_template_versions WHERE template_id=@template LIMIT 1),
                    '{"type":"document","content":[]}'::jsonb, '[]'::jsonb, @actor, @actor);
            INSERT INTO odca.generated_contract_versions(id, tenant_id, contract_id, draft_id, version_number, content_schema_version, content, fields, values, source_template_id, source_template_version_id, canonical_sha256, storage_key, byte_size, created_by, review_status, idempotency_key, draft_row_version, pdf_status, pdf_storage_key, pdf_sha256, pdf_byte_size, pdf_renderer_version, pdf_completed_at)
            VALUES (@version, @tenant, @contract, @draft, 1, 1,
                    '{"type":"document","content":[]}'::jsonb, '[]'::jsonb, '[]'::jsonb, @template,
                    (SELECT id FROM odca.contract_template_versions WHERE template_id=@template LIMIT 1),
                    @pdfSha256, @storageKey, 1024, @actor, 'generated', gen_random_uuid(), 1,
                    'completed', @storageKey, @pdfSha256, 1024, 'odca-pdf-3', now());
            INSERT INTO odca.signature_preparations(id, tenant_id, generated_version_id, status, row_version, created_by, updated_by, composition_revision, confirmed_revision, confirmed_pdf_storage_key, confirmed_pdf_sha256, confirmed_at, confirmed_by, confirmation_operation_id)
            VALUES (@preparation, @tenant, @version, 'confirmed', 1, @actor, @actor, 1, 1, @storageKey, @pdfSha256, now(), @actor, @confirmationOperationId);
            INSERT INTO odca.signature_participants(id, client_id, tenant_id, preparation_id, composition_revision, participant_type, role, name, email, position, recorded_by, identity_membership_id)
            VALUES (gen_random_uuid(), @client, @tenant, @preparation, 1, 'patient', 'Paciente', 'Paciente Sintético', @clientEmail, 1, @actor, @clientMembership);
            """, new
        {
            tenant, actor, reviewer, client, nonMember, role, subscription, template, contract, draft, version, preparation,
            pdfSha256, storageKey,
            businessCode = tenant.ToString("N")[..20],
            contractReference = $"FEED-{tenant.ToString("N")[..8].ToUpperInvariant()}",
            actorEmail = $"{tenant:N}-actor@feed.test", actorEmailUpper = $"{tenant:N}-ACTOR@FEED.TEST".ToUpperInvariant(),
            reviewerEmail = $"{tenant:N}-reviewer@feed.test", reviewerEmailUpper = $"{tenant:N}-REVIEWER@FEED.TEST".ToUpperInvariant(),
            clientEmail = $"{tenant:N}-client@feed.test", clientEmailUpper = $"{tenant:N}-CLIENT@FEED.TEST".ToUpperInvariant(),
            nonMemberEmail = $"{tenant:N}-nonmember@feed.test", nonMemberEmailUpper = $"{tenant:N}-NONMEMBER@FEED.TEST".ToUpperInvariant(),
            actorMembership = Guid.NewGuid(), reviewerMembership = Guid.NewGuid(), clientMembership,
            confirmationOperationId = Guid.NewGuid(),
        });

        return new Seed(tenant, actor, reviewer, client, nonMember, version, preparation, clientMembership);
    }

    private static ContractStudioController CreateController(NpgsqlDataSource ds, Guid actor)
    {
        var controller = new ContractStudioController(ds, new ConfigurationBuilder().Build(), new StubTemplateCatalogRepository(), new DisabledSignatureEnvelopeProvider());
        controller.ControllerContext = new ControllerContext { HttpContext = TestControllerContexts.Create(actor) };
        return controller;
    }

    private static async Task SetGuc(NpgsqlConnection connection, NpgsqlTransaction tx, Seed seed) =>
        await connection.ExecuteAsync(new CommandDefinition(
            "SELECT set_config('odca.tenant_id',@tenant,true),set_config('odca.actor_id',@actor,true)",
            new { tenant = seed.Tenant.ToString(), actor = seed.Actor.ToString() }, transaction: tx));

    private static Task<int> CountInAppRows(NpgsqlConnection connection, NpgsqlTransaction tx, Seed seed) =>
        connection.ExecuteScalarAsync<int>(new CommandDefinition(
            "SELECT count(*)::int FROM odca.user_notifications WHERE tenant_id=@tenant AND user_id=@user",
            new { tenant = seed.Tenant, user = seed.Client }, tx));

    private static string Json(object? value) => JsonSerializer.Serialize(value, JsonOptions);

    private sealed class StubTemplateCatalogRepository : ITemplateCatalogRepository
    {
        public Task<TemplateCatalogPage> ListAsync(Guid tenantId, string? search, string? contractType, string? scope, string? documentPurpose, int page, int pageSize, CancellationToken cancellationToken) =>
            Task.FromResult(new TemplateCatalogPage([], page, pageSize, 0));
    }
}
