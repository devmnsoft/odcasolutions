using System.Text.Json;
using System.Text.Json.Serialization;
using Dapper;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;
using Odca.Api.Controllers;
using Odca.Application.Contracts;
using Odca.Application.Signatures;
using Odca.Contracts.Studio;
using Odca.Infrastructure.Signatures;
using Odca.Worker;

namespace Odca.IntegrationTests;

/// <summary>
/// D-OC37 (Sprint 3/A-1): contrato do provedor plugável de envelopes externos de assinatura,
/// exercitado pelo transporte local-file de referência (pasta de drop). Apenas dados sintéticos:
/// tenant/ator/paciente aleatórios por teste, plano oficial basic v2 (signature_envelopes_monthly=10)
/// e envelope materializado como JSON em pasta temporária. Nenhum fornecedor externo foi escolhido.
/// </summary>
public sealed class SignatureEnvelopeTests(DatabaseFixture database) : IClassFixture<DatabaseFixture>
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    [Fact]
    public async Task SendIsRejectedWhenNoProviderIsConfigured()
    {
        var seed = await SeedAsync(confirmed: true);
        await using var ds = NpgsqlDataSource.Create(database.AppConnectionString);
        var controller = CreateController(ds, seed.Actor, new DisabledSignatureEnvelopeProvider());

        var result = await controller.SendSignatureEnvelope(seed.Tenant, seed.Version, CancellationToken.None);

        AssertConflict(result, "signatures.provider_unconfigured");
        await using var admin = new NpgsqlConnection(database.AdminConnectionString);
        await admin.OpenAsync();
        Assert.Equal(0, await admin.ExecuteScalarAsync<int>(
            "SELECT count(*)::int FROM odca.signature_envelopes WHERE generated_version_id=@version", new { seed.Version }));
        Assert.Equal(0, await admin.ExecuteScalarAsync<int>(
            "SELECT count(*)::int FROM odca.resource_movements WHERE tenant_id=@tenant AND resource_type='signature_credit'", new { seed.Tenant }));
    }

    [Fact]
    public async Task SendConsumesOneQuotaUnitAndReplayDoesNotConsumeAgain()
    {
        var seed = await SeedAsync(confirmed: true);
        var dir = TempDirectory();
        try
        {
            await using var ds = NpgsqlDataSource.Create(database.AppConnectionString);
            var controller = CreateController(ds, seed.Actor, new LocalFileSignatureEnvelopeProvider(dir));

            var first = await controller.SendSignatureEnvelope(seed.Tenant, seed.Version, CancellationToken.None);
            var firstJson = Json(((OkObjectResult)first).Value);
            Assert.Contains("\"status\":\"sent\"", firstJson);
            Assert.Contains("\"replayed\":false", firstJson);

            // O transporte de referência materializa o envelope na pasta de drop com os
            // metadados dos participantes e o hash do PDF da versão imutável.
            // (nomes com acento podem sair codificados em unicode; asserções em valores ASCII)
            var envelopePath = Path.Combine(dir, $"env-{seed.Version:N}.envelope.json");
            var payload = await File.ReadAllTextAsync(envelopePath);
            Assert.Contains($"\"clientId\":\"{seed.Client:D}\"", payload);
            Assert.Contains("\"participantType\":\"patient\"", payload);
            Assert.Contains(seed.PdfSha256, payload);

            var replay = await controller.SendSignatureEnvelope(seed.Tenant, seed.Version, CancellationToken.None);
            var replayJson = Json(((OkObjectResult)replay).Value);
            Assert.Contains("\"replayed\":true", replayJson);

            // Replay por chave natural não reconsome a franquia nem duplica o evento de envio.
            await using var admin = new NpgsqlConnection(database.AdminConnectionString);
            await admin.OpenAsync();
            Assert.Equal(1, await admin.ExecuteScalarAsync<int>(
                "SELECT count(*)::int FROM odca.resource_movements WHERE tenant_id=@tenant AND resource_type='signature_credit'", new { seed.Tenant }));
            var envelopeId = await admin.ExecuteScalarAsync<Guid>("SELECT id FROM odca.signature_envelopes WHERE generated_version_id=@version", new { seed.Version });
            Assert.Equal(1, await admin.ExecuteScalarAsync<int>(
                "SELECT count(*)::int FROM odca.signature_envelope_events WHERE envelope_id=@envelope", new { envelope = envelopeId }));
        }
        finally { CleanupDirectory(dir); }
    }

    [Fact]
    public async Task SyncTransitionsToAcceptedOnceWithDeduplicatedEvents()
    {
        var seed = await SeedAsync(confirmed: true);
        var dir = TempDirectory();
        try
        {
            await using var ds = NpgsqlDataSource.Create(database.AppConnectionString);
            var controller = CreateController(ds, seed.Actor, new LocalFileSignatureEnvelopeProvider(dir));
            Assert.IsType<OkObjectResult>(await controller.SendSignatureEnvelope(seed.Tenant, seed.Version, CancellationToken.None));

            // Sem estado externo ainda: o sync reporta que nada mudou.
            var pending = Json(((OkObjectResult)await controller.SyncSignatureEnvelope(seed.Tenant, seed.Version, CancellationToken.None)).Value);
            Assert.Contains("\"updated\":false", pending);
            Assert.Contains("\"status\":\"sent\"", pending);

            // A contraparte escreve o estado aceito na pasta de drop (instantâneo fixo => dedup determinística).
            File.WriteAllText(Path.Combine(dir, $"env-{seed.Version:N}.state.json"),
                """{"status":"accepted","occurred_at":"2026-10-17T12:00:00Z"}""");

            var applied = Json(((OkObjectResult)await controller.SyncSignatureEnvelope(seed.Tenant, seed.Version, CancellationToken.None)).Value);
            Assert.Contains("\"updated\":true", applied);
            Assert.Contains("\"status\":\"accepted\"", applied);

            var repeated = Json(((OkObjectResult)await controller.SyncSignatureEnvelope(seed.Tenant, seed.Version, CancellationToken.None)).Value);
            Assert.Contains("\"updated\":false", repeated);

            // A tabela de eventos é auditoria deduplicada: exatamente sent + accepted.
            await using var admin = new NpgsqlConnection(database.AdminConnectionString);
            await admin.OpenAsync();
            var envelopeId = await admin.ExecuteScalarAsync<Guid>("SELECT id FROM odca.signature_envelopes WHERE generated_version_id=@version", new { seed.Version });
            var types = (await admin.QueryAsync<string>(
                "SELECT event_type FROM odca.signature_envelope_events WHERE envelope_id=@envelope ORDER BY received_at,id", new { envelope = envelopeId })).ToList();
            Assert.Equal(["sent", "accepted"], types);

            // A visão pública expõe o estado atual e a auditoria de eventos.
            var view = Json(((OkObjectResult)await controller.GetSignatureEnvelope(seed.Tenant, seed.Version, CancellationToken.None)).Value);
            Assert.Contains("\"status\":\"accepted\"", view);
            Assert.Contains("\"eventType\":\"accepted\"", view);
        }
        finally { CleanupDirectory(dir); }
    }

    [Fact]
    public async Task DeclinedEnvelopeCanBeResentConsumingANewQuotaUnit()
    {
        var seed = await SeedAsync(confirmed: true);
        var dir = TempDirectory();
        try
        {
            await using var ds = NpgsqlDataSource.Create(database.AppConnectionString);
            var controller = CreateController(ds, seed.Actor, new LocalFileSignatureEnvelopeProvider(dir));
            Assert.IsType<OkObjectResult>(await controller.SendSignatureEnvelope(seed.Tenant, seed.Version, CancellationToken.None));

            File.WriteAllText(Path.Combine(dir, $"env-{seed.Version:N}.state.json"),
                """{"status":"declined","occurred_at":"2026-10-17T13:00:00Z"}""");
            var declined = Json(((OkObjectResult)await controller.SyncSignatureEnvelope(seed.Tenant, seed.Version, CancellationToken.None)).Value);
            Assert.Contains("\"updated\":true", declined);
            Assert.Contains("\"status\":\"declined\"", declined);

            // Novo envio sobre a mesma versão: nova tentativa consome nova unidade (chave v2)
            // e reseta o estado para sent sem duplicar o registro do envelope.
            var resent = Json(((OkObjectResult)await controller.SendSignatureEnvelope(seed.Tenant, seed.Version, CancellationToken.None)).Value);
            Assert.Contains("\"replayed\":false", resent);
            Assert.Contains("\"status\":\"sent\"", resent);

            await using var admin = new NpgsqlConnection(database.AdminConnectionString);
            await admin.OpenAsync();
            Assert.Equal(1, await admin.ExecuteScalarAsync<int>(
                "SELECT count(*)::int FROM odca.signature_envelopes WHERE generated_version_id=@version", new { seed.Version }));
            Assert.Equal(3, await admin.ExecuteScalarAsync<int>(
                "SELECT row_version::int FROM odca.signature_envelopes WHERE generated_version_id=@version", new { seed.Version }));
            Assert.Equal(2, await admin.ExecuteScalarAsync<int>(
                "SELECT count(*)::int FROM odca.resource_movements WHERE tenant_id=@tenant AND resource_type='signature_credit'", new { seed.Tenant }));
            var envelopeId = await admin.ExecuteScalarAsync<Guid>("SELECT id FROM odca.signature_envelopes WHERE generated_version_id=@version", new { seed.Version });
            var types = (await admin.QueryAsync<string>(
                "SELECT event_type FROM odca.signature_envelope_events WHERE envelope_id=@envelope ORDER BY event_type,received_at", new { envelope = envelopeId })).ToList();
            Assert.Equal(["declined", "sent", "sent"], types);
        }
        finally { CleanupDirectory(dir); }
    }

    [Fact]
    public async Task SendIsRejectedWhenTheFranchiseIsExhausted()
    {
        var seed = await SeedAsync(confirmed: true);

        // Esgota a franquia mensal do tenant drenando unidade por unidade (plano basic v2: 10).
        await using var admin = new NpgsqlConnection(database.AdminConnectionString);
        await admin.OpenAsync();
        await using var drainTx = await admin.BeginTransactionAsync();
        var drained = 0;
        string? drain = "";
        for (var i = 1; i <= 50 && drain != "exhausted"; i++)
        {
            drain = await admin.ExecuteScalarAsync<string>(
                "SELECT odca.consume_monthly_franchise(@tenant,'signature_credit',1,@key,'signature_envelope',@source,'studio-api')",
                new { tenant = seed.Tenant, key = $"drain:{i}", source = Guid.NewGuid() }, drainTx);
            if (drain == "consumed") drained++;
        }
        await drainTx.CommitAsync();
        Assert.Equal("exhausted", drain);
        Assert.True(drained > 0, "a franquia deveria ter sido drenada antes do envio");

        await using var ds = NpgsqlDataSource.Create(database.AppConnectionString);
        var controller = CreateController(ds, seed.Actor, new LocalFileSignatureEnvelopeProvider(TempDirectory()));

        var result = await controller.SendSignatureEnvelope(seed.Tenant, seed.Version, CancellationToken.None);

        AssertConflict(result, "signatures.franchise_exhausted");
        Assert.Equal(0, await admin.ExecuteScalarAsync<int>(
            "SELECT count(*)::int FROM odca.signature_envelopes WHERE generated_version_id=@version", new { seed.Version }));
    }

    [Fact]
    public async Task UnconfirmedPreparationRejectsSend()
    {
        var seed = await SeedAsync(confirmed: false);
        await using var ds = NpgsqlDataSource.Create(database.AppConnectionString);
        var controller = CreateController(ds, seed.Actor, new LocalFileSignatureEnvelopeProvider(TempDirectory()));

        var result = await controller.SendSignatureEnvelope(seed.Tenant, seed.Version, CancellationToken.None);

        AssertConflict(result, "preparation.not_confirmed");
        await using var admin = new NpgsqlConnection(database.AdminConnectionString);
        await admin.OpenAsync();
        Assert.Equal(0, await admin.ExecuteScalarAsync<int>(
            "SELECT count(*)::int FROM odca.signature_envelopes WHERE generated_version_id=@version", new { seed.Version }));
    }

    [Fact]
    public async Task VersionDetailDerivesSignatureStatusAndMaterializesConfirmedParticipant()
    {
        var seed = await SeedAsync(confirmed: true);
        await using var ds = NpgsqlDataSource.Create(database.AppConnectionString);
        var controller = CreateController(ds, seed.Actor, new DisabledSignatureEnvelopeProvider());

        // Detalhe da versão (superficie F-1): status de assinatura derivado da preparacao e
        // participante confirmado materializado (source_id/phone nulos, ainda nao assinado).
        var result = await controller.Version(seed.Tenant, seed.Version, CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result);
        var json = Json(ok.Value);
        Assert.Contains("\"signatureStatus\":\"confirmed\"", json);
        Assert.Contains("\"status\":\"confirmed\"", json);
        // O participant serializa o id do cliente na propriedade "id" do registro publico.
        Assert.Contains(seed.Client.ToString(), json);
        Assert.Contains("\"participantType\":\"patient\"", json);
    }

    [Fact]
    public async Task WorkerSweepAppliesProviderStateAndTenantRowsStayIsolated()
    {
        var seed = await SeedAsync(confirmed: true);
        var dir = TempDirectory();
        try
        {
            var provider = new LocalFileSignatureEnvelopeProvider(dir);
            await using var ds = NpgsqlDataSource.Create(database.AppConnectionString);
            var controller = CreateController(ds, seed.Actor, provider);
            Assert.IsType<OkObjectResult>(await controller.SendSignatureEnvelope(seed.Tenant, seed.Version, CancellationToken.None));

            File.WriteAllText(Path.Combine(dir, $"env-{seed.Version:N}.state.json"),
                """{"status":"accepted","occurred_at":"2026-10-17T12:30:00Z"}""");

            // O worker varre cross-tenant via função SECURITY DEFINER e aplica a transição
            // com evento deduplicado — executado com o papel de aplicação.
            var worker = new SignatureEnvelopeWorker(ds, new ConfigurationBuilder().Build(), provider, NullLogger<SignatureEnvelopeWorker>.Instance);
            await worker.SweepOnceAsync(CancellationToken.None);

            await using var admin = new NpgsqlConnection(database.AdminConnectionString);
            await admin.OpenAsync();
            Assert.Equal("accepted", await admin.ExecuteScalarAsync<string>(
                "SELECT status FROM odca.signature_envelopes WHERE generated_version_id=@version", new { seed.Version }));
            var envelopeId = await admin.ExecuteScalarAsync<Guid>("SELECT id FROM odca.signature_envelopes WHERE generated_version_id=@version", new { seed.Version });
            Assert.Equal(2, await admin.ExecuteScalarAsync<int>(
                "SELECT count(*)::int FROM odca.signature_envelope_events WHERE envelope_id=@envelope", new { envelope = envelopeId }));

            // RLS: sob o papel de aplicação a linha só é visível dentro do tenant dono.
            await using var app = new NpgsqlConnection(database.AppConnectionString);
            await app.OpenAsync();
            await using var tx = await app.BeginTransactionAsync();
            await app.ExecuteAsync("SELECT set_config('odca.tenant_id', @tenant, true)", new { tenant = Guid.NewGuid().ToString() }, tx);
            Assert.Equal(0, await app.ExecuteScalarAsync<int>(
                "SELECT count(*)::int FROM odca.signature_envelopes WHERE generated_version_id=@version", new { seed.Version }, tx));
            await app.ExecuteAsync("SELECT set_config('odca.tenant_id', @tenant, true)", new { tenant = seed.Tenant.ToString() }, tx);
            Assert.Equal(1, await app.ExecuteScalarAsync<int>(
                "SELECT count(*)::int FROM odca.signature_envelopes WHERE generated_version_id=@version", new { seed.Version }, tx));
        }
        finally { CleanupDirectory(dir); }
    }

    private sealed record Seed(Guid Tenant, Guid Actor, Guid Client, Guid Version, Guid Preparation, string PdfSha256);

    private async Task<Seed> SeedAsync(bool confirmed)
    {
        var tenant = Guid.NewGuid();
        var actor = Guid.NewGuid();
        var client = Guid.NewGuid();
        var role = Guid.NewGuid();
        var subscription = Guid.NewGuid();
        var template = Guid.NewGuid();
        var contract = Guid.NewGuid();
        var draft = Guid.NewGuid();
        var version = Guid.NewGuid();
        var preparation = Guid.NewGuid();
        var pdfSha256 = "e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855";
        var storageKey = $"studio/envelope-{version:N}.pdf";
        var actorEmail = $"{tenant:N}-actor@envelope.test";
        var clientEmail = $"{tenant:N}-client@envelope.test";

        await using var admin = new NpgsqlConnection(database.AdminConnectionString);
        await admin.OpenAsync();
        await admin.ExecuteAsync("""
            INSERT INTO odca.tenants(id, business_code, display_name, status, timezone)
            VALUES (@tenant, @businessCode, 'Organização do envelope', 'active', 'America/Sao_Paulo');
            INSERT INTO odca.users(id, email, email_normalized, login_normalized, display_name, password_hash, is_platform_administrator)
            VALUES
                (@actor, @actorEmail, @actorEmailUpper, @actorEmailUpper, 'Ator do envelope', 'hash', false),
                (@client, @clientEmail, @clientEmailUpper, @clientEmailUpper, 'Paciente sintético', 'hash', false);
            INSERT INTO odca.memberships(tenant_id, user_id, status) VALUES (@tenant, @actor, 'active');
            INSERT INTO odca.roles(id, scope_type, tenant_id, code, display_name, is_system)
            VALUES (@role, 'tenant', @tenant, 'envelope-studio', 'Studio do envelope', true);
            INSERT INTO odca.role_permissions(role_id, permission_code)
            SELECT @role, code FROM odca.permissions
            WHERE code IN ('tenant.contract_drafts.read', 'tenant.contract_drafts.manage');
            INSERT INTO odca.member_roles(tenant_id, user_id, role_id, assigned_by) VALUES (@tenant, @actor, @role, @actor);
            INSERT INTO odca.subscriptions(id, tenant_id, plan_version_id, commercial_state, status, created_by)
            VALUES (@subscription, @tenant, '30000000-0000-0000-0000-000000000011', 'active', 'active', @actor);
            INSERT INTO odca.contract_templates(id, owner_tenant_id, name, description, contract_type, scope, status, published_at, author_id, current_version)
            VALUES (@template, @tenant, 'Modelo do envelope', 'Modelo sintético de teste', 'service', 'private', 'published', now(), @actor, 1);
            INSERT INTO odca.contract_template_versions(id, template_id, version_number, content, fields, created_by, published_at)
            VALUES (gen_random_uuid(), @template, 1, '{"type":"document","content":[]}'::jsonb, '[]'::jsonb, @actor, now());
            INSERT INTO odca.contracts(id, tenant_id, title, reference) VALUES (@contract, @tenant, 'Contrato do envelope', @contractReference);
            INSERT INTO odca.contract_drafts(id, tenant_id, contract_id, source_template_id, source_template_version_id, content, fields, created_by, updated_by)
            VALUES (@draft, @tenant, @contract, @template,
                    (SELECT id FROM odca.contract_template_versions WHERE template_id=@template LIMIT 1),
                    '{"type":"document","content":[]}'::jsonb, '[]'::jsonb, @actor, @actor);
            INSERT INTO odca.generated_contract_versions(id, tenant_id, contract_id, draft_id, version_number, content_schema_version, content, fields, values, source_template_id, source_template_version_id, canonical_sha256, storage_key, byte_size, created_by, review_status, idempotency_key, draft_row_version, pdf_status, pdf_storage_key, pdf_sha256, pdf_byte_size, pdf_renderer_version, pdf_completed_at)
            VALUES (@version, @tenant, @contract, @draft, 1, 1,
                    '{"type":"document","content":[]}'::jsonb, '[]'::jsonb, '[]'::jsonb, @template,
                    (SELECT id FROM odca.contract_template_versions WHERE template_id=@template LIMIT 1),
                    @pdfSha256, @storageKey, 1024, @actor, 'internally_approved', gen_random_uuid(), 1,
                    'completed', @storageKey, @pdfSha256, 1024, 'odca-pdf-3', now());
            INSERT INTO odca.signature_preparations(id, tenant_id, generated_version_id, status, row_version, created_by, updated_by, composition_revision, confirmed_revision, confirmed_pdf_storage_key, confirmed_pdf_sha256, confirmed_at, confirmed_by, confirmation_operation_id)
            VALUES (@preparation, @tenant, @version, @status, 1, @actor, @actor, 1,
                    @confirmedRevision, @confirmedKey, @confirmedSha, @confirmedAt, @confirmedBy, @confirmationOperationId);
            INSERT INTO odca.signature_participants(id, client_id, tenant_id, preparation_id, composition_revision, participant_type, role, name, email, position, recorded_by)
            VALUES (gen_random_uuid(), @client, @tenant, @preparation, 1, 'patient', 'Paciente', 'Paciente Sintético', @clientEmail, 1, @actor);
            """, new
        {
            tenant, actor, client, role, subscription, template, contract, draft, version, preparation,
            pdfSha256, storageKey,
            businessCode = tenant.ToString("N")[..20],
            contractReference = $"ENV-{tenant.ToString("N")[..8].ToUpperInvariant()}",
            actorEmail, actorEmailUpper = actorEmail.ToUpperInvariant(),
            clientEmail, clientEmailUpper = clientEmail.ToUpperInvariant(),
            status = confirmed ? "confirmed" : "draft",
            confirmedRevision = confirmed ? 1 : (int?)null,
            confirmedKey = confirmed ? storageKey : null,
            confirmedSha = confirmed ? pdfSha256 : null,
            confirmedAt = confirmed ? DateTimeOffset.UtcNow : (DateTimeOffset?)null,
            confirmedBy = confirmed ? actor : (Guid?)null,
            confirmationOperationId = confirmed ? Guid.NewGuid() : (Guid?)null,
        });

        return new Seed(tenant, actor, client, version, preparation, pdfSha256);
    }

    private static ContractStudioController CreateController(NpgsqlDataSource ds, Guid actor, ISignatureEnvelopeProvider provider)
    {
        var controller = new ContractStudioController(ds, new ConfigurationBuilder().Build(), new StubTemplateCatalogRepository(), provider);
        controller.ControllerContext = new ControllerContext { HttpContext = TestControllerContexts.Create(actor) };
        return controller;
    }

    private static string Json(object? value) => JsonSerializer.Serialize(value, JsonOptions);

    private static void AssertConflict(IActionResult result, string code)
    {
        var conflict = Assert.IsType<ConflictObjectResult>(result);
        Assert.Contains($"\"code\":\"{code}\"", Json(conflict.Value));
    }

    private static string TempDirectory()
    {
        var dir = Path.Combine(Path.GetTempPath(), $"odca-envelope-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        return dir;
    }

    private static void CleanupDirectory(string dir)
    {
        try { Directory.Delete(dir, recursive: true); }
        catch (IOException) { /* pasta temporária; limpeza deixada para o sistema */ }
    }

    private sealed class StubTemplateCatalogRepository : ITemplateCatalogRepository
    {
        public Task<TemplateCatalogPage> ListAsync(Guid tenantId, string? search, string? contractType, string? scope, string? documentPurpose, int page, int pageSize, CancellationToken cancellationToken) =>
            Task.FromResult(new TemplateCatalogPage([], page, pageSize, 0));
    }
}
