using System.Text;
using System.Text.Json.Nodes;
using Dapper;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Infrastructure;
using Microsoft.Extensions.Configuration;
using Npgsql;
using Odca.Api.Controllers;
using Odca.Application.Contracts;
using Odca.Contracts.Studio;

namespace Odca.IntegrationTests;

/// <summary>
/// D-OC32: prova de integridade da operação documental do estúdio — reupload idempotente por
/// hash de conteúdo (B.1), expiração de reservas com liberação de cota (B.2) e aplicação de
/// extração/OCR ao rascunho com mapeamento fechado e idempotência (B.3).
/// </summary>
public sealed class DocumentReuploadAndDraftExtractionTests(DatabaseFixture database) : IClassFixture<DatabaseFixture>
{
    private static readonly Guid TenantId = Guid.Parse("76000000-0000-0000-0000-000000000010");
    private static readonly Guid ActorId = Guid.Parse("76000000-0000-0000-0000-000000000001");
    private static readonly Guid OutsiderId = Guid.Parse("76000000-0000-0000-0000-000000000099");
    private static readonly Guid RoleId = Guid.Parse("76000000-0000-0000-0000-000000000011");
    private static readonly Guid TemplateId = Guid.Parse("76000000-0000-0000-0000-000000000021");
    private static readonly Guid TemplateVersionId = Guid.Parse("76000000-0000-0000-0000-000000000022");
    private static readonly Guid PlanVersionId = Guid.Parse("30000000-0000-0000-0000-000000000011");

    private const string Sha = "4fde553fc9d60cc62913d22de318396867b55314e831f83e509cc109f6e688f6";

    [Fact]
    public async Task ReuploadingTheSameContentToTheSameDocumentIsAHonestReplay()
    {
        var contractId = Guid.NewGuid();
        await using var admin = new NpgsqlConnection(database.AdminConnectionString);
        await admin.OpenAsync();
        await ResetTenantDataAsync(admin);
        await SeedTenantAndRoleAsync(admin);
        await admin.ExecuteAsync("""
            INSERT INTO odca.subscriptions(id, tenant_id, plan_version_id, commercial_state, status, created_by)
            VALUES (gen_random_uuid(), @TenantId, @PlanVersionId, 'active', 'active', @ActorId) ON CONFLICT DO NOTHING;
            INSERT INTO odca.contracts(id, tenant_id, title, reference) VALUES (@contractId, @TenantId, 'Contrato Reupload D-OC32', 'REF-D32-R1');
            """, new { TenantId, PlanVersionId, ActorId, contractId });

        var bytes = Encoding.UTF8.GetBytes("%PDF-1.7\n% ODCA D-OC32 reupload\ntrailer <<>>\n%%EOF");
        var root = Path.Combine(Path.GetTempPath(), "odca-d32-" + Guid.NewGuid().ToString("N"));
        try
        {
            var config = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?> { ["Documents:StoragePath"] = root })
                .Build();
            await using var dataSource = NpgsqlDataSource.Create(database.AppConnectionString);
            var controller = new ContractDocumentsController(dataSource, config)
            { ControllerContext = new ControllerContext { HttpContext = TestControllerContexts.Create(ActorId) } };

            var first = await controller.Upload(TenantId, contractId, new FormFile(new MemoryStream(bytes), 0, bytes.Length, "file", "contrato.pdf"), null, CancellationToken.None);
            var created = Assert.IsType<CreatedResult>(first);
            dynamic firstBody = created.Value!;
            Assert.False((bool)firstBody.replayed);
            var documentId = (Guid)firstBody.documentId;
            var versionId = (Guid)firstBody.versionId;
            Assert.Equal(1, (int)firstBody.version);
            Assert.Equal(1, await admin.ExecuteScalarAsync<int>("SELECT count(*)::int FROM odca.document_versions WHERE tenant_id=@tenantId AND document_id=@documentId", new { TenantId, documentId }));

            // Mesmo conteúdo, mesmo documento: replay honesto — sem nova versão, sem cota dupla.
            var second = await controller.Upload(TenantId, contractId, new FormFile(new MemoryStream(bytes), 0, bytes.Length, "file", "contrato.pdf"), documentId, CancellationToken.None);
            var ok = Assert.IsType<OkObjectResult>(second);
            dynamic secondBody = ok.Value!;
            Assert.Equal("replayed", (string)secondBody.status);
            Assert.True((bool)secondBody.replayed);
            Assert.Equal(documentId, (Guid)secondBody.documentId);
            Assert.Equal(versionId, (Guid)secondBody.versionId);
            Assert.Equal(1, (int)secondBody.version);

            Assert.Equal(1, await admin.ExecuteScalarAsync<int>("SELECT count(*)::int FROM odca.document_versions WHERE tenant_id=@tenantId AND document_id=@documentId", new { TenantId, documentId }));
            var usedBytes = await admin.ExecuteScalarAsync<long>("SELECT used_bytes FROM odca.tenant_storage_usage WHERE tenant_id=@tenantId", new { TenantId });
            var reservedBytes = await admin.ExecuteScalarAsync<long>("SELECT reserved_bytes FROM odca.tenant_storage_usage WHERE tenant_id=@tenantId", new { TenantId });
            Assert.Equal((long)bytes.Length, usedBytes);
            Assert.Equal(0L, reservedBytes);
            Assert.Equal(0, await admin.ExecuteScalarAsync<int>("SELECT count(*)::int FROM odca.storage_reservations WHERE tenant_id=@tenantId AND status='reserved'", new { TenantId }));
            Assert.Equal(1, await admin.ExecuteScalarAsync<int>("SELECT count(*)::int FROM odca.contract_events WHERE tenant_id=@tenantId AND event_type='document_uploaded'", new { TenantId }));
            Assert.Equal(1, await admin.ExecuteScalarAsync<int>("SELECT count(*)::int FROM odca.contract_events WHERE tenant_id=@tenantId AND event_type='document_reuploaded'", new { TenantId }));
            Assert.Single(Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories));
        }
        finally
        {
            try { Directory.Delete(root, true); } catch (IOException) { }
        }
    }

    [Fact]
    public async Task ExpiredReservationsAreFinalizedAndReleaseReservedQuota()
    {
        var expiredReservationId = Guid.NewGuid();
        var futureReservationId = Guid.NewGuid();
        await using var admin = new NpgsqlConnection(database.AdminConnectionString);
        await admin.OpenAsync();
        await ResetTenantDataAsync(admin);
        await SeedTenantAndRoleAsync(admin);
        await admin.ExecuteAsync("""
            INSERT INTO odca.tenant_storage_usage(tenant_id, used_bytes, reserved_bytes, quota_bytes) VALUES (@TenantId, 0, 1500, 1073741824);
            INSERT INTO odca.storage_reservations(id, tenant_id, operation_key, requested_bytes, status, created_by, expires_at) VALUES
                (@expiredReservationId, @TenantId, 'd32-upload-expired', 1000, 'reserved', @ActorId, now() - interval '1 hour'),
                (@futureReservationId, @TenantId, 'd32-upload-future', 500, 'reserved', @ActorId, now() + interval '1 day');
            """, new { TenantId, ActorId, expiredReservationId, futureReservationId });

        Assert.Equal(1, await admin.ExecuteScalarAsync<int>("SELECT odca.expire_storage_reservations(500)"));
        var expiredStatus = await admin.ExecuteScalarAsync<string?>(
            "SELECT status FROM odca.storage_reservations WHERE tenant_id=@tenantId AND id=@id", new { TenantId, id = expiredReservationId });
        Assert.Equal("expired", expiredStatus);
        Assert.True(await admin.ExecuteScalarAsync<bool>(
            "SELECT finalized_at IS NOT NULL FROM odca.storage_reservations WHERE tenant_id=@tenantId AND id=@id", new { TenantId, id = expiredReservationId }));
        Assert.Equal("reserved", await admin.ExecuteScalarAsync<string>("SELECT status FROM odca.storage_reservations WHERE tenant_id=@tenantId AND id=@id", new { TenantId, id = futureReservationId }));
        Assert.Equal(500L, await admin.ExecuteScalarAsync<long>("SELECT reserved_bytes FROM odca.tenant_storage_usage WHERE tenant_id=@tenantId", new { TenantId }));
        // Idempotente no próximo ciclo do worker: nada a expirar.
        Assert.Equal(0, await admin.ExecuteScalarAsync<int>("SELECT odca.expire_storage_reservations(500)"));
    }

    [Fact]
    public async Task ApplyExtractionToDraftMapsOnlySafeConceptsAndIsIdempotent()
    {
        var contractId = Guid.NewGuid();
        var draftId = Guid.NewGuid();
        var documentId = Guid.NewGuid();
        var versionId = Guid.NewGuid();
        var jobId = Guid.NewGuid();
        var resultId = Guid.NewGuid();
        var suggestionIds = Enumerable.Range(0, 5).Select(_ => Guid.NewGuid()).ToArray();
        var idempotencyKey = Guid.NewGuid();
        const string fields = """
            [
              {"id":"base_title","label":"Titulo","type":"ShortText","required":false},
              {"id":"base_reference","label":"Referencia","type":"ShortText","required":false},
              {"id":"start_date","label":"Inicio","type":"Date","required":false},
              {"id":"end_date","label":"Fim","type":"Date","required":false},
              {"id":"amount","label":"Valor","type":"Currency","required":false},
              {"id":"patient_document","label":"Documento da parte","type":"BrazilianDocument","required":false},
              {"id":"confirmed_field","label":"Ja confirmado","type":"ShortText","required":false}
            ]
            """;
        const string values = """
            [{"fieldId":"confirmed_field","value":"Mantido","confirmed":true,"source":"manual"}]
            """;

        await using var admin = new NpgsqlConnection(database.AdminConnectionString);
        await admin.OpenAsync();
        await ResetTenantDataAsync(admin);
        await SeedTenantAndRoleAsync(admin);
        await SeedTemplateAndDraftAsync(admin, contractId, draftId, fields, values, "REF-D32-X1");
        await SeedExtractionChainAsync(admin, contractId, documentId, versionId, jobId, resultId, suggestionIds, [
            ("title", "Contrato de Prestacao de Servicos", "Contrato de Prestacao de Servicos"),
            ("start_date", "15/01/2026", "2026-01-15"),
            ("value", "R$ 1.234,56", "1234.56"),
            ("party_document", "529.982.247-25", "52998224725"),
            ("renewal_notice_days", "30 dias", "30"),
        ]);

        await using var dataSource = NpgsqlDataSource.Create(database.AppConnectionString);
        var controller = CreateStudioController(dataSource, ActorId);
        var decisions = new[]
        {
            new DraftExtractionDecision(suggestionIds[0], "accepted", null),
            new DraftExtractionDecision(suggestionIds[1], "accepted", null),
            new DraftExtractionDecision(suggestionIds[2], "accepted", null),
            new DraftExtractionDecision(suggestionIds[3], "accepted", null),
            new DraftExtractionDecision(suggestionIds[4], "rejected", null),
        };
        var request = new ApplyDraftExtractionRequest(jobId, 1, idempotencyKey, decisions);

        var ok = Assert.IsType<OkObjectResult>(await controller.ApplyExtraction(TenantId, draftId, request, CancellationToken.None));
        dynamic body = ok.Value!;
        Assert.Equal("applied", (string)body.status);
        Assert.False((bool)body.repeated);
        Assert.Equal(2L, (long)body.version);
        var appliedFields = ((JsonArray)body.applied).Select(n => ((JsonObject)n!)["fieldId"]!.GetValue<string>())
            .OrderBy(x => x, StringComparer.Ordinal).ToArray();
        Assert.Equal(["amount", "base_title", "patient_document", "start_date"], appliedFields);
        Assert.Empty(((JsonArray)body.skipped).ToArray());

        var valuesAfterApply = await GetDraftValuesTextAsync(admin, draftId);
        var stored = DraftValuesMap(valuesAfterApply);
        Assert.Equal("Contrato de Prestacao de Servicos", stored["base_title"]["value"]?.GetValue<string>());
        Assert.Equal("extraction", stored["base_title"]["source"]?.GetValue<string>());
        Assert.False(stored["base_title"]["confirmed"]!.GetValue<bool>());
        Assert.Equal("2026-01-15", stored["start_date"]["value"]?.GetValue<string>());
        Assert.Equal("1234.56", stored["amount"]["value"]?.GetValue<string>());
        Assert.Equal("52998224725", stored["patient_document"]["value"]?.GetValue<string>());
        // Valor já confirmado nunca é sobrescrito por extração.
        Assert.Equal("Mantido", stored["confirmed_field"]["value"]?.GetValue<string>());
        Assert.True(stored["confirmed_field"]["confirmed"]!.GetValue<bool>());
        Assert.Equal("manual", stored["confirmed_field"]["source"]?.GetValue<string>());
        Assert.Equal(2L, await admin.ExecuteScalarAsync<long>("SELECT row_version FROM odca.contract_drafts WHERE tenant_id=@tenantId AND id=@draftId", new { TenantId, draftId }));
        Assert.Equal("applied", await admin.ExecuteScalarAsync<string>("SELECT status FROM odca.draft_extraction_reviews WHERE tenant_id=@tenantId AND idempotency_key=@key", new { TenantId, key = idempotencyKey }));
        Assert.Equal("accepted", await admin.ExecuteScalarAsync<string>("SELECT review_status FROM odca.extraction_suggestions WHERE tenant_id=@tenantId AND id=@id", new { TenantId, id = suggestionIds[0] }));
        Assert.Equal("rejected", await admin.ExecuteScalarAsync<string>("SELECT review_status FROM odca.extraction_suggestions WHERE tenant_id=@tenantId AND id=@id", new { TenantId, id = suggestionIds[4] }));
        Assert.Equal(1, await admin.ExecuteScalarAsync<int>("SELECT count(*)::int FROM odca.contract_events WHERE tenant_id=@tenantId AND event_type='draft.extraction_applied'", new { TenantId }));

        // Mesma chave + mesmo payload: repetição honesta, sem reprocessar nem incrementar.
        var repeated = Assert.IsType<OkObjectResult>(await controller.ApplyExtraction(TenantId, draftId, request, CancellationToken.None));
        dynamic repeatedBody = repeated.Value!;
        Assert.Equal("applied", (string)repeatedBody.status);
        Assert.True((bool)repeatedBody.repeated);
        Assert.Equal(valuesAfterApply, await GetDraftValuesTextAsync(admin, draftId));

        // Mesma chave + payload divergente: 409 de conflito de payload idempotente.
        var divergent = new ApplyDraftExtractionRequest(jobId, 2, idempotencyKey, [.. decisions[..4], new DraftExtractionDecision(suggestionIds[4], "accepted", null)]);
        var payloadConflict = Assert.IsType<ConflictObjectResult>(await controller.ApplyExtraction(TenantId, draftId, divergent, CancellationToken.None));
        dynamic payloadConflictBody = payloadConflict.Value!;
        Assert.Equal("idempotency.payload.conflict", (string)payloadConflictBody.code);

        // Chave nova + versão esperada obsoleta: 409 de conflito de sessão, rascunho intacto.
        var stale = new ApplyDraftExtractionRequest(jobId, 1, Guid.NewGuid(), decisions);
        var versionConflict = Assert.IsType<ConflictObjectResult>(await controller.ApplyExtraction(TenantId, draftId, stale, CancellationToken.None));
        dynamic versionConflictBody = versionConflict.Value!;
        Assert.Equal("draft.save.conflict", (string)versionConflictBody.code);
        Assert.Equal(2L, (long)versionConflictBody.currentVersion);
        Assert.Equal(valuesAfterApply, await GetDraftValuesTextAsync(admin, draftId));
    }

    [Fact]
    public async Task ApplyExtractionSkipsEveryUnsafeConceptWithAStableReason()
    {
        var contractId = Guid.NewGuid();
        var draftId = Guid.NewGuid();
        var documentId = Guid.NewGuid();
        var versionId = Guid.NewGuid();
        var jobId = Guid.NewGuid();
        var resultId = Guid.NewGuid();
        var suggestionIds = Enumerable.Range(0, 6).Select(_ => Guid.NewGuid()).ToArray();
        const string fields = """
            [
              {"id":"base_title","label":"Titulo","type":"ShortText","required":false},
              {"id":"base_reference","label":"Referencia","type":"ShortText","required":false},
              {"id":"start_date","label":"Inicio","type":"Date","required":false},
              {"id":"end_date","label":"Fim","type":"Date","required":false},
              {"id":"rent_amount","label":"Locacao","type":"Formula","formulaTerms":[{"quantityFieldId":"term_qty","unitFieldId":"term_unit"}]},
              {"id":"term_qty","label":"Qtd","type":"Number","required":false},
              {"id":"term_unit","label":"Unidade","type":"Currency","required":false},
              {"id":"amount","label":"Valor","type":"Currency","required":false},
              {"id":"unit_price","label":"Preco unitario","type":"Currency","required":false}
            ]
            """;
        const string values = """
            [{"fieldId":"start_date","value":"2026-01-01","confirmed":true,"source":"manual"}]
            """;

        await using var admin = new NpgsqlConnection(database.AdminConnectionString);
        await admin.OpenAsync();
        await ResetTenantDataAsync(admin);
        await SeedTenantAndRoleAsync(admin);
        await SeedTemplateAndDraftAsync(admin, contractId, draftId, fields, values, "REF-D32-X2");
        await SeedExtractionChainAsync(admin, contractId, documentId, versionId, jobId, resultId, suggestionIds, [
            ("title", "Minuta Locacao", "Minuta Locacao"),
            ("start_date", "02/02/2026", "2026-02-02"),      // campo já confirmado
            ("value", "R$ 999,99", "999.99"),                 // três candidatos monetários
            ("rent_amount", "R$ 500,00", "500.00"),           // match exato em campo Formula
            ("end_date", "31/32/2026", "31/32/2026"),         // data inválida para o campo
            ("reference", "(não localizada)", null),          // sem valor normalizado
        ]);

        await using var dataSource = NpgsqlDataSource.Create(database.AppConnectionString);
        var controller = CreateStudioController(dataSource, ActorId);
        var request = new ApplyDraftExtractionRequest(jobId, 1, Guid.NewGuid(),
            suggestionIds.Select(id => new DraftExtractionDecision(id, "accepted", null)).ToArray());

        var ok = Assert.IsType<OkObjectResult>(await controller.ApplyExtraction(TenantId, draftId, request, CancellationToken.None));
        dynamic body = ok.Value!;
        Assert.Equal("applied", (string)body.status);
        Assert.Equal(["base_title"], ((JsonArray)body.applied).Select(n => ((JsonObject)n!)["fieldId"]!.GetValue<string>()).ToArray());
        var skippedReasons = ((JsonArray)body.skipped).Select(n => ((JsonObject)n!)["reason"]!.GetValue<string>())
            .OrderBy(x => x, StringComparer.Ordinal).ToArray();
        Assert.Equal(["already_confirmed", "ambiguous_candidates", "calculated", "value_invalid_for_field", "value_missing"], skippedReasons);

        var stored = DraftValuesMap(await GetDraftValuesTextAsync(admin, draftId));
        Assert.Equal("Minuta Locacao", stored["base_title"]["value"]?.GetValue<string>());
        // Campo confirmado permanece intocado apesar da sugestão aceite.
        Assert.Equal("2026-01-01", stored["start_date"]["value"]?.GetValue<string>());
        Assert.True(stored["start_date"]["confirmed"]!.GetValue<bool>());
        Assert.Equal(2L, await admin.ExecuteScalarAsync<long>("SELECT row_version FROM odca.contract_drafts WHERE tenant_id=@tenantId AND id=@draftId", new { TenantId, draftId }));
    }

    [Fact]
    public async Task ApplyExtractionRequiresBothPermissions()
    {
        await using var admin = new NpgsqlConnection(database.AdminConnectionString);
        await admin.OpenAsync();
        await ResetTenantDataAsync(admin);
        await SeedTenantAndRoleAsync(admin);
        await using var dataSource = NpgsqlDataSource.Create(database.AppConnectionString);
        var controller = CreateStudioController(dataSource, OutsiderId);
        var request = new ApplyDraftExtractionRequest(Guid.NewGuid(), 1, Guid.NewGuid(), [new DraftExtractionDecision(Guid.NewGuid(), "accepted", null)]);
        Assert.IsType<ForbidResult>(await controller.ApplyExtraction(TenantId, Guid.NewGuid(), request, CancellationToken.None));
    }

    /// <summary>Deixa o tenant do teste limpo para execuções repetidas no mesmo banco (sem -FreshDb).</summary>
    private static async Task ResetTenantDataAsync(NpgsqlConnection admin)
    {
        await admin.ExecuteAsync("""
            DELETE FROM odca.contract_events WHERE tenant_id=@TenantId;
            DELETE FROM odca.resource_movements WHERE tenant_id=@TenantId;
            DELETE FROM odca.storage_reservations WHERE tenant_id=@TenantId;
            DELETE FROM odca.tenant_storage_usage WHERE tenant_id=@TenantId;
            DELETE FROM odca.draft_extraction_reviews WHERE tenant_id=@TenantId;
            DELETE FROM odca.extraction_suggestions WHERE result_id IN (SELECT id FROM odca.extraction_results WHERE tenant_id=@TenantId);
            DELETE FROM odca.extraction_results WHERE tenant_id=@TenantId;
            DELETE FROM odca.extraction_jobs WHERE tenant_id=@TenantId;
            DELETE FROM odca.generated_contract_versions WHERE tenant_id=@TenantId;
            DELETE FROM odca.document_versions WHERE tenant_id=@TenantId;
            DELETE FROM odca.contract_documents WHERE tenant_id=@TenantId;
            DELETE FROM odca.contract_drafts WHERE tenant_id=@TenantId;
            DELETE FROM odca.contracts WHERE tenant_id=@TenantId;
            DELETE FROM odca.subscriptions WHERE tenant_id=@TenantId;
            DELETE FROM odca.member_roles WHERE tenant_id=@TenantId;
            """, new { TenantId });
    }

    private static async Task SeedTenantAndRoleAsync(NpgsqlConnection admin)
    {
        await admin.ExecuteAsync("""
            INSERT INTO odca.tenants(id, business_code, display_name, status, timezone)
            VALUES (@TenantId, '76000000000011', 'Tenant D-OC32', 'active', 'America/Sao_Paulo')
            ON CONFLICT (id) DO NOTHING;
            INSERT INTO odca.users(id, email, email_normalized, login_normalized, display_name, password_hash, is_platform_administrator)
            VALUES
                (@ActorId, 'actor.d32@odca.local', 'ACTOR.D32@ODCA.LOCAL', 'ACTOR.D32@ODCA.LOCAL', 'Ator D-OC32', 'hash', false),
                (@OutsiderId, 'outsider.d32@odca.local', 'OUTSIDER.D32@ODCA.LOCAL', 'OUTSIDER.D32@ODCA.LOCAL', 'Fora D-OC32', 'hash', false)
            ON CONFLICT (id) DO NOTHING;
            INSERT INTO odca.memberships(tenant_id, user_id, status)
            VALUES (@TenantId, @ActorId, 'active'), (@TenantId, @OutsiderId, 'active')
            ON CONFLICT (tenant_id, user_id) DO UPDATE SET status = 'active';
            INSERT INTO odca.roles(id, scope_type, tenant_id, code, display_name, is_system)
            VALUES (@RoleId, 'tenant', @TenantId, 'd32-documents', 'D-OC32 Documentos', true)
            ON CONFLICT (id) DO NOTHING;
            INSERT INTO odca.role_permissions(role_id, permission_code)
            SELECT @RoleId, code FROM odca.permissions
             WHERE code IN (
                'tenant.documents.manage',
                'tenant.contract_drafts.read', 'tenant.contract_drafts.manage',
                'tenant.extractions.request', 'tenant.extractions.review', 'tenant.extractions.apply')
            ON CONFLICT DO NOTHING;
            INSERT INTO odca.member_roles(tenant_id, user_id, role_id, assigned_by)
            VALUES (@TenantId, @ActorId, @RoleId, @ActorId)
            ON CONFLICT DO NOTHING;
            """, new { TenantId, ActorId, OutsiderId, RoleId });
    }

    private static async Task SeedTemplateAndDraftAsync(NpgsqlConnection admin, Guid contractId, Guid draftId, string fields, string values, string reference)
    {
        await admin.ExecuteAsync("""
            INSERT INTO odca.contract_templates(id, owner_tenant_id, name, description, contract_type, scope, status, published_at, author_id, current_version)
            VALUES (@TemplateId, @TenantId, 'Modelo D-OC32', 'Estudio', 'service', 'private', 'published', now(), @ActorId, 1)
            ON CONFLICT (id) DO NOTHING;
            INSERT INTO odca.contract_template_versions(id, template_id, version_number, content, fields, created_by, published_at)
            VALUES (@TemplateVersionId, @TemplateId, 1, '{"type":"document","content":[]}'::jsonb, '[]'::jsonb, @ActorId, now())
            ON CONFLICT (template_id, version_number) DO NOTHING;
            INSERT INTO odca.contracts(id, tenant_id, title, reference)
            VALUES (@contractId, @TenantId, 'Contrato D-OC32', @reference);
            INSERT INTO odca.contract_drafts(id, tenant_id, contract_id, source_template_id, source_template_version_id, content, fields, values, created_by, updated_by)
            VALUES (@draftId, @TenantId, @contractId, @TemplateId, @TemplateVersionId, '{"type":"document","content":[]}'::jsonb, @fields::jsonb, @values::jsonb, @ActorId, @ActorId);
            """, new { TemplateId, TemplateVersionId, TenantId, ActorId, contractId, draftId, fields, values, reference });
    }

    private static async Task SeedExtractionChainAsync(NpgsqlConnection admin, Guid contractId, Guid documentId, Guid versionId, Guid jobId, Guid resultId, Guid[] suggestionIds, IReadOnlyList<(string Concept, string Extracted, string? Normalized)> concepts)
    {
        await admin.ExecuteAsync("""
            INSERT INTO odca.contract_documents(id, tenant_id, contract_id, title, created_by)
            VALUES (@documentId, @TenantId, @contractId, 'contrato.pdf', @ActorId);
            INSERT INTO odca.document_versions(id, tenant_id, contract_id, document_id, version_number, uploaded_by, display_name, detected_type, byte_size, sha256, storage_key, security_status, security_checked_at)
            VALUES (@versionId, @TenantId, @contractId, @documentId, 1, @ActorId, 'contrato.pdf', 'pdf', 2048, @sha, 'd32/' || CAST(@versionId AS text), 'safe', now());
            INSERT INTO odca.extraction_jobs(id, tenant_id, contract_id, version_id, requested_by, status, completed_at)
            VALUES (@jobId, @TenantId, @contractId, @versionId, @ActorId, 'ready_for_review', now());
            INSERT INTO odca.extraction_results(id, tenant_id, job_id, version_id, method, raw_text)
            VALUES (@resultId, @TenantId, @jobId, @versionId, 'docx-structure', 'conteudo D-OC32');
            """, new { documentId, versionId, jobId, resultId, TenantId, contractId, ActorId, sha = Sha });
        for (var i = 0; i < concepts.Count; i++)
        {
            var (concept, extracted, normalized) = concepts[i];
            await admin.ExecuteAsync("""
                INSERT INTO odca.extraction_suggestions(id, tenant_id, result_id, version_id, field_name, extracted_value, normalized_value, evidence, method)
                VALUES (@id, @TenantId, @resultId, @versionId, @concept, @extracted, @normalized, 'p.1', 'docx-structure');
                """, new { id = suggestionIds[i], TenantId, resultId, versionId, concept, extracted, normalized });
        }
    }

    private static ContractStudioController CreateStudioController(NpgsqlDataSource dataSource, Guid actorId)
    {
        var config = new ConfigurationBuilder().Build();
        var controller = new ContractStudioController(dataSource, config, new StubTemplateCatalogRepository());
        controller.ControllerContext = new ControllerContext { HttpContext = TestControllerContexts.Create(actorId) };
        return controller;
    }

    private static async Task<string> GetDraftValuesTextAsync(NpgsqlConnection admin, Guid draftId) =>
        await admin.ExecuteScalarAsync<string?>(
            "SELECT values::text FROM odca.contract_drafts WHERE tenant_id=@tenantId AND id=@draftId", new { TenantId, draftId })
        ?? throw new InvalidOperationException("Rascunho não localizado para leitura de valores.");

    private static Dictionary<string, JsonObject> DraftValuesMap(string valuesJson) =>
        JsonNode.Parse(valuesJson)!.AsArray()
            .Select(n => (JsonObject)n!)
            .ToDictionary(n => n["fieldId"]!.GetValue<string>(), StringComparer.Ordinal);

    private sealed class StubTemplateCatalogRepository : ITemplateCatalogRepository
    {
        public Task<TemplateCatalogPage> ListAsync(Guid tenantId, string? search, string? contractType, string? scope, string? documentPurpose, int page, int pageSize, CancellationToken cancellationToken) =>
            Task.FromResult(new TemplateCatalogPage([], page, pageSize, 0));
    }
}
