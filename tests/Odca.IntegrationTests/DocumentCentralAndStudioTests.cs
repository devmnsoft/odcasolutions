using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using Dapper;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Npgsql;
using Odca.Api.Controllers;
using Odca.Application.Contracts;
using Odca.Contracts.Reviews;
using Odca.Contracts.Studio;

namespace Odca.IntegrationTests;

public sealed class DocumentCentralAndStudioTests(DatabaseFixture database) : IClassFixture<DatabaseFixture>
{
    private static readonly Guid TenantId = Guid.Parse("73000000-0000-0000-0000-000000000010");
    private static readonly Guid ActorId = Guid.Parse("73000000-0000-0000-0000-000000000001");
    private static readonly Guid ReviewerId = Guid.Parse("73000000-0000-0000-0000-000000000002");
    private static readonly Guid TemplateAId = Guid.Parse("73000000-0000-0000-0000-000000000021");
    private static readonly Guid TemplateBId = Guid.Parse("73000000-0000-0000-0000-000000000022");
    private static readonly Guid ContractId = Guid.Parse("73000000-0000-0000-0000-000000000031");
    private static readonly Guid ChangeRequest1Id = Guid.Parse("73000000-0000-0000-0000-000000000041");
    private static readonly Guid ChangeRequest2Id = Guid.Parse("73000000-0000-0000-0000-000000000042");

    [Fact]
    public async Task ReviewRowVersionChangeDoesNotAlterDisplayedDocumentVersionNumber()
    {
        await SeedScenarioAsync();
        await using var dataSource = NpgsqlDataSource.Create(database.AppConnectionString);

        var controller = CreateReviewsController(dataSource, ActorId);

        // Fetch queue before row_version modification
        var resBefore = Assert.IsType<OkObjectResult>(await controller.List(TenantId, status: null, contractId: ContractId, ct: default));
        var pageBefore = Assert.IsType<ReviewQueuePage>(resBefore.Value);
        var itemBefore = Assert.Single(pageBefore.Items);
        Assert.Equal(1, itemBefore.DocumentVersionNumber);
        var initialRowVersion = itemBefore.Version;

        // Simulate review activity incrementing row_version of the review request
        await using (var adminConn = new NpgsqlConnection(database.AdminConnectionString))
        {
            await adminConn.ExecuteAsync("""
                UPDATE odca.contract_review_requests
                SET row_version = row_version + 1, updated_at = now()
                WHERE tenant_id = @TenantId AND contract_id = @ContractId;
                """, new { TenantId, ContractId });
        }

        // Fetch queue after row_version modification
        var resAfter = Assert.IsType<OkObjectResult>(await controller.List(TenantId, status: null, contractId: ContractId, ct: default));
        var pageAfter = Assert.IsType<ReviewQueuePage>(resAfter.Value);
        var itemAfter = Assert.Single(pageAfter.Items);

        // Concurrency control version changed, but DocumentVersionNumber remains exactly 1!
        Assert.Equal(initialRowVersion + 1, itemAfter.Version);
        Assert.Equal(1, itemAfter.DocumentVersionNumber);
    }

    [Fact]
    public async Task CreateDraftReturnsConflictWhenContractAlreadyHasDraftWithDifferentTemplate()
    {
        await SeedScenarioAsync();
        await using var dataSource = NpgsqlDataSource.Create(database.AppConnectionString);

        var controller = CreateStudioController(dataSource, ActorId);

        // Attempt to create draft for ContractId using Template B when it already has a draft with Template A
        var request = new CreateDraftRequest(
            TemplateId: TemplateBId,
            Title: "Contrato Conflitante",
            Reference: "REF-001",
            ContractId: ContractId);

        var result = await controller.CreateDraft(TenantId, request, default);

        var conflict = Assert.IsType<ConflictObjectResult>(result);
        dynamic val = conflict.Value!;
        Assert.Equal("draft_template_conflict", (string)val.code);
    }

    [Fact]
    public async Task CreateDraftWithExplicitChangeRequestLinksOnlyTheSelectedRequest()
    {
        await SeedScenarioAsync();
        await using var dataSource = NpgsqlDataSource.Create(database.AppConnectionString);

        // Create a new contract with two change requests
        var testContractId = Guid.NewGuid();
        var cr1 = Guid.NewGuid();
        var cr2 = Guid.NewGuid();

        await using (var adminConn = new NpgsqlConnection(database.AdminConnectionString))
        {
            await adminConn.ExecuteAsync("""
                DROP INDEX IF EXISTS odca.contract_change_one_active_uq;

                INSERT INTO odca.contracts(id, tenant_id, title, reference)
                VALUES (@testContractId, @TenantId, 'Contrato Aditivos', 'REF-ADITIVO');

                INSERT INTO odca.contract_change_requests(
                    id, tenant_id, contract_id, kind, author_id, responsible_id, reason,
                    effective_on, base_contract_version, idempotency_key, other_changes, status)
                VALUES
                    (@cr1, @TenantId, @testContractId, 'amendment', @ActorId, @ActorId, 'Aditivo 1',
                     CURRENT_DATE, 1, gen_random_uuid(), '[]'::jsonb, 'draft'),
                    (@cr2, @TenantId, @testContractId, 'amendment', @ActorId, @ActorId, 'Aditivo 2',
                     CURRENT_DATE, 1, gen_random_uuid(), '[]'::jsonb, 'draft');
                """, new { testContractId, TenantId, cr1, cr2, ActorId });
        }

        var controller = CreateStudioController(dataSource, ActorId);

        // Explicitly create draft for cr1
        var request = new CreateDraftRequest(
            TemplateId: TemplateAId,
            Title: "Minuta Aditivo 1",
            Reference: "REF-ADITIVO",
            ContractId: testContractId,
            ChangeRequestId: cr1);

        var result = await controller.CreateDraft(TenantId, request, default);
        var created = Assert.IsType<CreatedResult>(result);
        dynamic val = created.Value!;
        Guid draftId = val.id;

        // Verify in database: only cr1 is linked, cr2 is NOT touched
        await using (var adminConn = new NpgsqlConnection(database.AdminConnectionString))
        {
            var cr1DraftId = await adminConn.QuerySingleAsync<Guid?>(
                "SELECT draft_id FROM odca.contract_change_requests WHERE id = @cr1", new { cr1 });
            var cr2DraftId = await adminConn.QuerySingleAsync<Guid?>(
                "SELECT draft_id FROM odca.contract_change_requests WHERE id = @cr2", new { cr2 });

            Assert.Equal(draftId, cr1DraftId);
            Assert.Null(cr2DraftId);

            // Audit event was recorded
            var auditCount = await adminConn.ExecuteScalarAsync<int>("""
                SELECT count(*)::int FROM odca.audit_events
                WHERE tenant_id = @TenantId AND action = 'change_request.draft_linked' AND entity_id = @cr1;
                """, new { TenantId, cr1 });
            Assert.Equal(1, auditCount);
        }
    }

    [Fact]
    public async Task CreateDraftReturnsExistingDraftWhenSameTemplateAndPatient()
    {
        await SeedScenarioAsync();
        await using var dataSource = NpgsqlDataSource.Create(database.AppConnectionString);

        var controller = CreateStudioController(dataSource, ActorId);

        var request = new CreateDraftRequest(
            TemplateId: TemplateAId,
            Title: "Contrato Base Studio",
            Reference: "REF-BASE-001",
            ContractId: ContractId,
            PatientId: null);

        var result = await controller.CreateDraft(TenantId, request, default);

        var ok = Assert.IsType<OkObjectResult>(result);
        dynamic val = ok.Value!;
        Guid draftId = val.id;
        Assert.Equal(Guid.Parse("73000000-0000-0000-0000-000000000051"), draftId);
    }

    [Fact]
    public async Task CreateDraftAssociatesPatientWhenExistingDraftHasNoPatient()
    {
        await SeedScenarioAsync();
        var patientId = Guid.NewGuid();

        await using (var adminConn = new NpgsqlConnection(database.AdminConnectionString))
        {
            await adminConn.ExecuteAsync("""
                INSERT INTO odca.patients(id, tenant_id, full_name, created_by, updated_by)
                VALUES (@patientId, @TenantId, 'Paciente Novo Studio', @ActorId, @ActorId)
                ON CONFLICT (tenant_id, id) DO NOTHING;
                """, new { patientId, TenantId, ActorId });
        }

        await using var dataSource = NpgsqlDataSource.Create(database.AppConnectionString);
        var controller = CreateStudioController(dataSource, ActorId);

        var request = new CreateDraftRequest(
            TemplateId: TemplateAId,
            Title: "Contrato Base Studio",
            Reference: "REF-BASE-001",
            ContractId: ContractId,
            PatientId: patientId);

        var result = await controller.CreateDraft(TenantId, request, default);

        var ok = Assert.IsType<OkObjectResult>(result);
        dynamic val = ok.Value!;
        Guid draftId = val.id;
        Assert.Equal(Guid.Parse("73000000-0000-0000-0000-000000000051"), draftId);

        // Verify patient was canonically associated
        await using (var adminConn = new NpgsqlConnection(database.AdminConnectionString))
        {
            var pId = await adminConn.QuerySingleAsync<Guid?>(
                "SELECT patient_id FROM odca.contract_drafts WHERE id = @draftId", new { draftId });
            Assert.Equal(patientId, pId);

            var eventCount = await adminConn.ExecuteScalarAsync<int>("""
                SELECT count(*)::int FROM odca.contract_events
                WHERE tenant_id = @TenantId AND event_type = 'draft.patient_associated' AND contract_id = @ContractId;
                """, new { TenantId, ContractId });
            Assert.True(eventCount >= 1);
        }
    }

    [Fact]
    public async Task CreateDraftReturnsConflictWhenExistingDraftHasDifferentPatient()
    {
        await SeedScenarioAsync();
        var patientA = Guid.NewGuid();
        var patientB = Guid.NewGuid();

        await using (var adminConn = new NpgsqlConnection(database.AdminConnectionString))
        {
            await adminConn.ExecuteAsync("""
                INSERT INTO odca.patients(id, tenant_id, full_name, created_by, updated_by)
                VALUES
                    (@patientA, @TenantId, 'Paciente A', @ActorId, @ActorId),
                    (@patientB, @TenantId, 'Paciente B', @ActorId, @ActorId)
                ON CONFLICT (tenant_id, id) DO NOTHING;

                UPDATE odca.contract_drafts
                SET patient_id = @patientA,
                    patient_row_version = 1,
                    patient_selection_snapshot = odca.patient_document_snapshot(tenant_id, @patientA)
                WHERE id = '73000000-0000-0000-0000-000000000051';
                """, new { patientA, patientB, TenantId, ActorId });
        }

        await using var dataSource = NpgsqlDataSource.Create(database.AppConnectionString);
        var controller = CreateStudioController(dataSource, ActorId);

        // Attempt to resume with patientB
        var request = new CreateDraftRequest(
            TemplateId: TemplateAId,
            Title: "Contrato Base Studio",
            Reference: null,
            ContractId: ContractId,
            PatientId: patientB);

        var result = await controller.CreateDraft(TenantId, request, default);

        var conflict = Assert.IsType<ConflictObjectResult>(result);
        dynamic val = conflict.Value!;
        Assert.Equal("draft_patient_conflict", (string)val.code);
    }

    [Fact]
    public async Task CreateDraftReturnsConflictWhenExistingDraftHasPatientButRequestDoesNot()
    {
        await SeedScenarioAsync();
        var patientA = Guid.NewGuid();

        await using (var adminConn = new NpgsqlConnection(database.AdminConnectionString))
        {
            await adminConn.ExecuteAsync("""
                INSERT INTO odca.patients(id, tenant_id, full_name, created_by, updated_by)
                VALUES (@patientA, @TenantId, 'Paciente A Existente', @ActorId, @ActorId)
                ON CONFLICT (tenant_id, id) DO NOTHING;

                UPDATE odca.contract_drafts
                SET patient_id = @patientA,
                    patient_row_version = 1,
                    patient_selection_snapshot = odca.patient_document_snapshot(tenant_id, @patientA)
                WHERE id = '73000000-0000-0000-0000-000000000051';
                """, new { patientA, TenantId, ActorId });
        }

        await using var dataSource = NpgsqlDataSource.Create(database.AppConnectionString);
        var controller = CreateStudioController(dataSource, ActorId);

        // Attempt to resume with null patient
        var request = new CreateDraftRequest(
            TemplateId: TemplateAId,
            Title: "Contrato Base Studio",
            Reference: null,
            ContractId: ContractId,
            PatientId: null);

        var result = await controller.CreateDraft(TenantId, request, default);

        var conflict = Assert.IsType<ConflictObjectResult>(result);
        dynamic val = conflict.Value!;
        Assert.Equal("draft_patient_required", (string)val.code);
    }

    [Fact]
    public async Task CreateDraftRejectsChangeRequestWithoutContractId()
    {
        await SeedScenarioAsync();
        await using var dataSource = NpgsqlDataSource.Create(database.AppConnectionString);
        var controller = CreateStudioController(dataSource, ActorId);

        var request = new CreateDraftRequest(
            TemplateId: TemplateAId,
            Title: "Minuta sem contrato",
            Reference: null,
            ContractId: null,
            ChangeRequestId: Guid.NewGuid());

        var result = await controller.CreateDraft(TenantId, request, default);

        var badRequest = Assert.IsType<BadRequestObjectResult>(result);
        var problem = Assert.IsType<ValidationProblemDetails>(badRequest.Value);
        Assert.True(problem.Errors.ContainsKey("changeRequestId"));
    }

    [Fact]
    public async Task CreateDraftConcurrentRequestsHandleSavepointGracefully()
    {
        await SeedScenarioAsync();
        await using var dataSource = NpgsqlDataSource.Create(database.AppConnectionString);

        // Pre-create a contract for both tasks to target
        var targetContractId = Guid.NewGuid();
        await using (var adminConn = new NpgsqlConnection(database.AdminConnectionString))
        {
            await adminConn.ExecuteAsync("""
                INSERT INTO odca.contracts(id, tenant_id, title, reference)
                VALUES (@targetContractId, @TenantId, 'Contrato Concorrente', 'REF-CONC');
                """, new { targetContractId, TenantId });
        }

        var controller1 = CreateStudioController(dataSource, ActorId);
        var controller2 = CreateStudioController(dataSource, ActorId);

        var req1 = new CreateDraftRequest(
            TemplateId: TemplateAId,
            Title: "Minuta Concorrente 1",
            Reference: "REF-CONC",
            ContractId: targetContractId);

        var req2 = new CreateDraftRequest(
            TemplateId: TemplateAId,
            Title: "Minuta Concorrente 2",
            Reference: "REF-CONC",
            ContractId: targetContractId);

        // Run both concurrently
        var task1 = controller1.CreateDraft(TenantId, req1, default);
        var task2 = controller2.CreateDraft(TenantId, req2, default);

        var results = await Task.WhenAll(task1, task2);

        // Both should succeed (one Created 201, one Ok 200) without 23505 throwing an unhandled transaction aborted error!
        Assert.All(results, res => Assert.True(res is CreatedResult or OkObjectResult));

        // Exactly one draft exists for targetContractId in database
        await using (var adminConn = new NpgsqlConnection(database.AdminConnectionString))
        {
            var count = await adminConn.ExecuteScalarAsync<int>(
                "SELECT count(*)::int FROM odca.contract_drafts WHERE tenant_id = @TenantId AND contract_id = @targetContractId",
                new { TenantId, targetContractId });
            Assert.Equal(1, count);
        }
    }

    private static ReviewRequestsController CreateReviewsController(NpgsqlDataSource ds, Guid actorId)
    {
        var controller = new ReviewRequestsController(ds);
        var httpContext = new DefaultHttpContext();
        httpContext.User = new ClaimsPrincipal(new ClaimsIdentity([
            new Claim("sub", actorId.ToString()),
            new Claim(ClaimTypes.NameIdentifier, actorId.ToString())
        ], "test"));
        controller.ControllerContext = new ControllerContext { HttpContext = httpContext };
        return controller;
    }

    private static ContractStudioController CreateStudioController(NpgsqlDataSource ds, Guid actorId)
    {
        var config = new ConfigurationBuilder().Build();
        var catalog = new StubTemplateCatalogRepository();
        var controller = new ContractStudioController(ds, config, catalog);
        var httpContext = new DefaultHttpContext();
        httpContext.User = new ClaimsPrincipal(new ClaimsIdentity([
            new Claim("sub", actorId.ToString()),
            new Claim(ClaimTypes.NameIdentifier, actorId.ToString())
        ], "test"));
        controller.ControllerContext = new ControllerContext { HttpContext = httpContext };
        return controller;
    }

    private sealed class StubTemplateCatalogRepository : ITemplateCatalogRepository
    {
        public Task<TemplateCatalogPage> ListAsync(Guid tenantId, string? search, string? contractType, string? scope, int page, int pageSize, CancellationToken cancellationToken) =>
            Task.FromResult(new TemplateCatalogPage([], page, pageSize, 0));
    }

    private async Task SeedScenarioAsync()
    {
        await using var adminConn = new NpgsqlConnection(database.AdminConnectionString);
        await adminConn.OpenAsync();

        await adminConn.ExecuteAsync("""
            INSERT INTO odca.tenants(id, business_code, display_name, status, timezone)
            VALUES (@TenantId, '99999000000100', 'Tenant Teste Studio', 'active', 'America/Sao_Paulo')
            ON CONFLICT (id) DO UPDATE SET display_name = EXCLUDED.display_name, timezone = 'America/Sao_Paulo';

            INSERT INTO odca.users(id, email, email_normalized, login_normalized, display_name, password_hash, is_platform_administrator)
            VALUES
                (@ActorId, 'actor.studio@odca.local', 'ACTOR.STUDIO@ODCA.LOCAL', 'ACTOR.STUDIO@ODCA.LOCAL', 'Actor Studio', 'hash', false),
                (@ReviewerId, 'reviewer.studio@odca.local', 'REVIEWER.STUDIO@ODCA.LOCAL', 'REVIEWER.STUDIO@ODCA.LOCAL', 'Reviewer Studio', 'hash', false)
            ON CONFLICT (id) DO NOTHING;

            INSERT INTO odca.memberships(tenant_id, user_id, status)
            VALUES
                (@TenantId, @ActorId, 'active'),
                (@TenantId, @ReviewerId, 'active')
            ON CONFLICT (tenant_id, user_id) DO UPDATE SET status = 'active';

            INSERT INTO odca.roles(id, scope_type, tenant_id, code, display_name, is_system)
            VALUES
                ('73000000-0000-0000-0000-000000000011', 'tenant', @TenantId, 'studio-admin', 'Admin Studio', true)
            ON CONFLICT (id) DO NOTHING;

            INSERT INTO odca.role_permissions(role_id, permission_code)
            SELECT '73000000-0000-0000-0000-000000000011', code
            FROM odca.permissions
            WHERE code IN (
                'tenant.contracts.read', 'tenant.contracts.manage',
                'tenant.contract_drafts.read', 'tenant.contract_drafts.manage',
                'tenant.templates.read', 'tenant.templates.manage',
                'tenant.reviews.read', 'tenant.reviews.manage', 'tenant.reviews.request', 'tenant.reviews.decide')
            ON CONFLICT DO NOTHING;

            INSERT INTO odca.member_roles(tenant_id, user_id, role_id, assigned_by)
            VALUES
                (@TenantId, @ActorId, '73000000-0000-0000-0000-000000000011', @ActorId),
                (@TenantId, @ReviewerId, '73000000-0000-0000-0000-000000000011', @ActorId)
            ON CONFLICT DO NOTHING;

            -- Templates A and B
            INSERT INTO odca.contract_templates(id, owner_tenant_id, name, description, contract_type, scope, status, published_at, author_id, current_version)
            VALUES
                (@TemplateAId, @TenantId, 'Modelo A Prestação', 'Desc A', 'service', 'private', 'published', now(), @ActorId, 1),
                (@TemplateBId, @TenantId, 'Modelo B Consentimento', 'Desc B', 'consent', 'private', 'published', now(), @ActorId, 1)
            ON CONFLICT (id) DO UPDATE SET status = 'published', published_at = now();

            INSERT INTO odca.contract_template_versions(id, template_id, version_number, content, fields, created_by, published_at)
            VALUES
                (gen_random_uuid(), @TemplateAId, 1, '{"type":"document","content":[]}'::jsonb, '[]'::jsonb, @ActorId, now()),
                (gen_random_uuid(), @TemplateBId, 1, '{"type":"document","content":[]}'::jsonb, '[]'::jsonb, @ActorId, now())
            ON CONFLICT (template_id, version_number) DO UPDATE SET content = EXCLUDED.content, fields = EXCLUDED.fields, published_at = now();

            -- Base Contract
            INSERT INTO odca.contracts(id, tenant_id, title, reference)
            VALUES (@ContractId, @TenantId, 'Contrato Base Studio', 'REF-BASE-001')
            ON CONFLICT (tenant_id, id) DO NOTHING;

            -- Existing Draft for ContractId using TemplateA
            INSERT INTO odca.contract_drafts(id, tenant_id, contract_id, source_template_id, source_template_version_id, content, fields, created_by, updated_by)
            VALUES
                ('73000000-0000-0000-0000-000000000051', @TenantId, @ContractId, @TemplateAId, (SELECT id FROM odca.contract_template_versions WHERE template_id = @TemplateAId LIMIT 1), '{"type":"document","content":[]}'::jsonb, '[]'::jsonb, @ActorId, @ActorId)
            ON CONFLICT (tenant_id, contract_id) DO UPDATE SET content = EXCLUDED.content, fields = EXCLUDED.fields, patient_id = NULL, patient_row_version = NULL, patient_selection_snapshot = NULL;

            -- Generated contract version 1
            INSERT INTO odca.generated_contract_versions(
                id, tenant_id, contract_id, draft_id, version_number, content_schema_version,
                content, fields, values, source_template_id, source_template_version_id, canonical_sha256,
                storage_key, byte_size, created_by, review_status, idempotency_key, draft_row_version)
            VALUES
                ('73000000-0000-0000-0000-000000000061', @TenantId, @ContractId, '73000000-0000-0000-0000-000000000051',
                 1, 1, '{"type":"document","content":[]}'::jsonb, '[]'::jsonb, '[]'::jsonb, @TemplateAId,
                 (SELECT id FROM odca.contract_template_versions WHERE template_id = @TemplateAId LIMIT 1),
                 'e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855',
                 'studio/versions/test-v1.pdf', 1024, @ActorId, 'submitted',
                 '73000000-0000-0000-0000-000000000062', 1)
            ON CONFLICT (tenant_id, id) DO NOTHING;

            -- Review request pointing to generated_version 1
            INSERT INTO odca.contract_review_requests(
                id, tenant_id, contract_id, generated_version_id, requested_by, instructions,
                content_snapshot, document_sha256, idempotency_key, row_version, status)
            VALUES
                ('73000000-0000-0000-0000-000000000071', @TenantId, @ContractId, '73000000-0000-0000-0000-000000000061',
                 @ActorId, 'Revisar minuta', '{"type":"document","content":[]}'::jsonb,
                 'e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855',
                 gen_random_uuid(), 1, 'in_review')
            ON CONFLICT (tenant_id, id) DO NOTHING;

            INSERT INTO odca.contract_review_steps(tenant_id, review_id, sequence, reviewer_id, status)
            VALUES (@TenantId, '73000000-0000-0000-0000-000000000071', 1, @ReviewerId, 'current')
            ON CONFLICT DO NOTHING;
            """, new { TenantId, ActorId, ReviewerId, TemplateAId, TemplateBId, ContractId });
    }
}
