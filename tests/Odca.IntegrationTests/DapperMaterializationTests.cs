using System.Security.Claims;
using Dapper;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;
using Odca.Api.Controllers;
using Odca.Application.Contracts;
using Odca.Contracts.DocumentImports;
using Odca.Contracts.Patients;
using Odca.Contracts.Studio;
using Odca.Infrastructure.Operations;
using Odca.Infrastructure.Patients;

namespace Odca.IntegrationTests;

public sealed class DapperMaterializationTests(DatabaseFixture database) : IClassFixture<DatabaseFixture>
{
    private static readonly Guid TenantId = Guid.Parse("74000000-0000-0000-0000-000000000010");
    private static readonly Guid EmptyTenantId = Guid.Parse("74000000-0000-0000-0000-000000000099");
    private static readonly Guid ActorId = Guid.Parse("74000000-0000-0000-0000-000000000001");
    private static readonly Guid Patient1Id = Guid.Parse("74000000-0000-0000-0000-000000000101");
    private static readonly Guid Patient2Id = Guid.Parse("74000000-0000-0000-0000-000000000102");
    private static readonly Guid Patient3Id = Guid.Parse("74000000-0000-0000-0000-000000000103");
    private static readonly Guid Contract1Id = Guid.Parse("74000000-0000-0000-0000-000000000201");
    private static readonly Guid Contract2Id = Guid.Parse("74000000-0000-0000-0000-000000000202");
    private static readonly Guid TemplateId = Guid.Parse("74000000-0000-0000-0000-000000000301");
    private static readonly Guid Import1Id = Guid.Parse("74000000-0000-0000-0000-000000000401");
    private static readonly Guid Import2Id = Guid.Parse("74000000-0000-0000-0000-000000000402");

    [Fact]
    public async Task PatientSummaryMaterializesCorrectlyFromRealPostgreSql()
    {
        await SeedAsync();
        await using var dataSource = NpgsqlDataSource.Create(database.AppConnectionString);
        var repo = new NpgsqlPatientRepository(dataSource);

        // 1. Empty list for tenant with no patients
        var emptyPage = await repo.ListAsync(ActorId, EmptyTenantId, search: null, includeInactive: true, page: 1, pageSize: 20, default);
        Assert.NotNull(emptyPage);
        Assert.Empty(emptyPage.Items);
        Assert.Equal(0, emptyPage.Total);

        // 2. Active patients only (Patient 1 with null optional fields, Patient 2 with filled optional fields)
        var activePage = await repo.ListAsync(ActorId, TenantId, search: null, includeInactive: false, page: 1, pageSize: 20, default);
        Assert.NotNull(activePage);
        Assert.Equal(2, activePage.Total);
        Assert.Equal(2, activePage.Items.Count);

        var p1 = Assert.Single(activePage.Items, x => x.Id == Patient1Id);
        Assert.Equal("Paciente Sem Opcionais", p1.FullName);
        Assert.Null(p1.PreferredName);
        Assert.Null(p1.MaskedIdentifier);
        Assert.True(p1.Active);
        Assert.Equal(TimeSpan.Zero, p1.UpdatedAt.Offset);

        var p2 = Assert.Single(activePage.Items, x => x.Id == Patient2Id);
        Assert.Equal("Paciente Com Opcionais", p2.FullName);
        Assert.Equal("Nome Social", p2.PreferredName);
        Assert.NotNull(p2.MaskedIdentifier);
        Assert.True(p2.Active);
        Assert.Equal(TimeSpan.Zero, p2.UpdatedAt.Offset);

        // 3. Include inactive patients (Patient 3 is inactive)
        var allPage = await repo.ListAsync(ActorId, TenantId, search: null, includeInactive: true, page: 1, pageSize: 20, default);
        Assert.NotNull(allPage);
        Assert.Equal(3, allPage.Total);
        var p3 = Assert.Single(allPage.Items, x => x.Id == Patient3Id);
        Assert.False(p3.Active);
        Assert.Equal(TimeSpan.Zero, p3.UpdatedAt.Offset);

        // 4. Filter and Pagination
        var searchPage = await repo.ListAsync(ActorId, TenantId, search: "Opcionais", includeInactive: true, page: 1, pageSize: 1, default);
        Assert.NotNull(searchPage);
        Assert.Single(searchPage.Items);
        Assert.True(searchPage.Total >= 2);
    }

    [Fact]
    public async Task PatientArchiveMaterializesDocumentsCorrectlyFromRealPostgreSql()
    {
        await SeedAsync();
        await using var dataSource = NpgsqlDataSource.Create(database.AppConnectionString);
        var repo = new NpgsqlPatientRepository(dataSource);

        // Archive for Patient 1 has 1 generated version
        var archive = await repo.ArchiveAsync(ActorId, TenantId, Patient1Id, page: 1, pageSize: 10, default);
        Assert.NotNull(archive);
        Assert.Single(archive.Items);
        Assert.Equal(1, archive.Total);

        var doc = archive.Items[0];
        Assert.Equal(Contract1Id, doc.ContractId);
        Assert.Equal(TimeSpan.Zero, doc.CreatedAt.Offset);
        Assert.NotEqual(Guid.Empty, doc.DraftId);
        Assert.Equal("generated", doc.DocumentStatus);
    }

    [Fact]
    public async Task ContractImportsMaterializeCorrectlyFromRealPostgreSql()
    {
        await SeedAsync();
        await using var dataSource = NpgsqlDataSource.Create(database.AppConnectionString);
        var controller = CreateImportsController(dataSource, ActorId);

        // 1. Empty list for empty tenant
        var emptyRes = Assert.IsType<OkObjectResult>(await controller.List(EmptyTenantId, status: null, requester: null, mine: false, awaitingReview: false, from: null, to: null, ct: default));
        var emptyPage = Assert.IsType<ContractImportPage>(emptyRes.Value);
        Assert.Empty(emptyPage.Items);
        Assert.Equal(0, emptyPage.Total);

        // 2. Real items with and without result contract
        var res = Assert.IsType<OkObjectResult>(await controller.List(TenantId, status: null, requester: null, mine: false, awaitingReview: false, from: null, to: null, ct: default));
        var page = Assert.IsType<ContractImportPage>(res.Value);
        Assert.Equal(2, page.Total);

        var imp1 = Assert.Single(page.Items, x => x.Id == Import1Id);
        Assert.Null(imp1.ResultContractId);
        Assert.Null(imp1.DiagnosticCode);
        Assert.Equal(TimeSpan.Zero, imp1.CreatedAt.Offset);

        var imp2 = Assert.Single(page.Items, x => x.Id == Import2Id);
        Assert.Equal(Contract2Id, imp2.ResultContractId);
        Assert.Equal("DIAG_OK", imp2.DiagnosticCode);
        Assert.Equal(TimeSpan.Zero, imp2.CreatedAt.Offset);
    }

    [Fact]
    public async Task StudioDocumentListMaterializesCorrectlyFromRealPostgreSql()
    {
        await SeedAsync();
        await using var dataSource = NpgsqlDataSource.Create(database.AppConnectionString);
        var controller = CreateStudioController(dataSource, ActorId);

        // 1. Empty list for empty tenant
        var emptyRes = Assert.IsType<OkObjectResult>(await controller.ListDocuments(EmptyTenantId, search: null, type: null, stage: null, patientId: null, page: 1, pageSize: 20, ct: default));
        var emptyPage = Assert.IsType<StudioDocumentPage>(emptyRes.Value);
        Assert.Empty(emptyPage.Items);

        // 2. Real documents list
        var res = Assert.IsType<OkObjectResult>(await controller.ListDocuments(TenantId, search: null, type: null, stage: null, patientId: null, page: 1, pageSize: 20, ct: default));
        var docPage = Assert.IsType<StudioDocumentPage>(res.Value);
        Assert.True(docPage.Items.Count >= 2);

        // Document 1 has draft, patient, generated version and completed PDF
        var doc1 = Assert.Single(docPage.Items, x => x.ContractId == Contract1Id);
        Assert.Equal(Patient1Id, doc1.PatientId);
        Assert.Equal("Paciente Sem Opcionais", doc1.PatientName);
        Assert.NotNull(doc1.LatestVersionId);
        Assert.Equal(1, doc1.LatestVersionNumber);
        Assert.Equal("completed", doc1.PdfStatus);
        Assert.True(doc1.CanDownloadPdf);
        Assert.Equal(TimeSpan.Zero, doc1.UpdatedAt.Offset);

        // Document 2 is B2B without patient
        var doc2 = Assert.Single(docPage.Items, x => x.ContractId == Contract2Id);
        Assert.Null(doc2.PatientId);
        Assert.Null(doc2.PatientName);
        Assert.Equal(TimeSpan.Zero, doc2.UpdatedAt.Offset);
    }

    [Fact]
    public async Task ContractSheetMaterializesDocumentsAndReviewCorrectlyFromRealPostgreSql()
    {
        await SeedAsync();
        await using var dataSource = NpgsqlDataSource.Create(database.AppConnectionString);
        var repo = new ContractSheetRepository(dataSource);

        var sheet = await repo.GetAsync(
            TenantId,
            Contract1Id,
            ActorId,
            purposeAuthorized: true,
            canReadTenant: true,
            today: new DateOnly(2026, 9, 28),
            cancellationToken: default);

        Assert.NotNull(sheet);
        Assert.Equal(Contract1Id, sheet.ContractId);
        Assert.NotNull(sheet.CurrentReview);
        Assert.Equal("in_review", sheet.CurrentReview.Status);
        if (sheet.CurrentReview.DueAt.HasValue)
        {
            Assert.Equal(TimeSpan.Zero, sheet.CurrentReview.DueAt.Value.Offset);
        }

        Assert.NotEmpty(sheet.Documents);
        var firstDoc = sheet.Documents[0];
        Assert.Equal(TimeSpan.Zero, firstDoc.UpdatedAt.Offset);
    }

    private static ContractImportsController CreateImportsController(NpgsqlDataSource ds, Guid actorId)
    {
        var config = new ConfigurationBuilder().Build();
        var controller = new ContractImportsController(ds, config, NullLogger<ContractImportsController>.Instance);
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

    private async Task SeedAsync()
    {
        await using var admin = new NpgsqlConnection(database.AdminConnectionString);
        await admin.OpenAsync();

        await admin.ExecuteAsync("""
            INSERT INTO odca.tenants(id, business_code, display_name, status, timezone)
            VALUES (@TenantId, '74000000000100', 'Tenant Materialização', 'active', 'America/Sao_Paulo'),
                   (@EmptyTenantId, '74000000000199', 'Tenant Vazio', 'active', 'America/Sao_Paulo')
            ON CONFLICT (id) DO UPDATE SET display_name = EXCLUDED.display_name, timezone = 'America/Sao_Paulo';

            INSERT INTO odca.users(id, email, email_normalized, login_normalized, display_name, password_hash, is_platform_administrator)
            VALUES (@ActorId, 'actor.mat@odca.local', 'ACTOR.MAT@ODCA.LOCAL', 'ACTOR.MAT@ODCA.LOCAL', 'Actor Materialização', 'hash', false)
            ON CONFLICT (id) DO NOTHING;

            INSERT INTO odca.memberships(tenant_id, user_id, status)
            VALUES (@TenantId, @ActorId, 'active'), (@EmptyTenantId, @ActorId, 'active')
            ON CONFLICT (tenant_id, user_id) DO UPDATE SET status = 'active';

            INSERT INTO odca.roles(id, scope_type, tenant_id, code, display_name, is_system)
            VALUES ('74000000-0000-0000-0000-000000000011', 'tenant', @TenantId, 'mat-admin', 'Admin Materialização', true),
                   ('74000000-0000-0000-0000-000000000012', 'tenant', @EmptyTenantId, 'mat-admin-empty', 'Admin Vazio', true)
            ON CONFLICT (id) DO NOTHING;

            INSERT INTO odca.role_permissions(role_id, permission_code)
            SELECT '74000000-0000-0000-0000-000000000011', code FROM odca.permissions
            ON CONFLICT DO NOTHING;

            INSERT INTO odca.role_permissions(role_id, permission_code)
            SELECT '74000000-0000-0000-0000-000000000012', code FROM odca.permissions
            ON CONFLICT DO NOTHING;

            INSERT INTO odca.member_roles(tenant_id, user_id, role_id, assigned_by)
            VALUES (@TenantId, @ActorId, '74000000-0000-0000-0000-000000000011', @ActorId),
                   (@EmptyTenantId, @ActorId, '74000000-0000-0000-0000-000000000012', @ActorId)
            ON CONFLICT DO NOTHING;

            -- Patients
            INSERT INTO odca.patients(id, tenant_id, full_name, preferred_name, birth_date, email, phone, address, identifier_type, identifier_value, identifier_normalized, created_by, updated_by, inactive_at)
            VALUES
                (@Patient1Id, @TenantId, 'Paciente Sem Opcionais', null, null, null, null, null, null, null, null, @ActorId, @ActorId, null),
                (@Patient2Id, @TenantId, 'Paciente Com Opcionais', 'Nome Social', '1990-01-01', 'com@odca.local', '11999998888', 'Rua Teste, 1', 'cpf', '123.456.789-00', '12345678900', @ActorId, @ActorId, null),
                (@Patient3Id, @TenantId, 'Paciente Inativo', null, null, null, null, null, null, null, null, @ActorId, @ActorId, now())
            ON CONFLICT (tenant_id, id) DO UPDATE SET
                full_name = EXCLUDED.full_name,
                preferred_name = EXCLUDED.preferred_name,
                birth_date = EXCLUDED.birth_date,
                email = EXCLUDED.email,
                phone = EXCLUDED.phone,
                address = EXCLUDED.address,
                identifier_type = EXCLUDED.identifier_type,
                identifier_value = EXCLUDED.identifier_value,
                identifier_normalized = EXCLUDED.identifier_normalized,
                inactive_at = EXCLUDED.inactive_at;

            -- Templates
            INSERT INTO odca.contract_templates(id, owner_tenant_id, name, description, contract_type, scope, status, published_at, author_id, current_version)
            VALUES (@TemplateId, @TenantId, 'Modelo Geral Mat', 'Desc', 'service', 'private', 'published', now(), @ActorId, 1)
            ON CONFLICT (id) DO UPDATE SET status = 'published';

            INSERT INTO odca.contract_template_versions(id, template_id, version_number, content, fields, created_by, published_at)
            VALUES (gen_random_uuid(), @TemplateId, 1, '{"type":"document","content":[]}'::jsonb, '[]'::jsonb, @ActorId, now())
            ON CONFLICT (template_id, version_number) DO NOTHING;

            -- Contracts
            INSERT INTO odca.contracts(id, tenant_id, title, reference, contract_type)
            VALUES (@Contract1Id, @TenantId, 'Contrato Mat 1', 'REF-MAT-01', 'service'),
                   (@Contract2Id, @TenantId, 'Contrato Mat 2 B2B', 'REF-MAT-02', 'service')
            ON CONFLICT (tenant_id, id) DO NOTHING;

            -- Contract Documents & Versions for Contract 1
            INSERT INTO odca.contract_documents(id, tenant_id, contract_id, title, created_by)
            VALUES ('74000000-0000-0000-0000-000000000211', @TenantId, @Contract1Id, 'Documento Mat 1', @ActorId)
            ON CONFLICT (tenant_id, id) DO NOTHING;

            INSERT INTO odca.document_versions(id, tenant_id, contract_id, document_id, version_number, display_name, detected_type, byte_size, sha256, storage_key, security_status, uploaded_by)
            VALUES ('74000000-0000-0000-0000-000000000221', @TenantId, @Contract1Id, '74000000-0000-0000-0000-000000000211', 1, 'doc1.pdf', 'pdf', 1024, 'e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855', 'test/key1.pdf', 'safe', @ActorId)
            ON CONFLICT (tenant_id, id) DO NOTHING;

            -- Contract Documents & Versions for Contract 2 (used for import 1 document version)
            INSERT INTO odca.contract_documents(id, tenant_id, contract_id, title, created_by)
            VALUES ('74000000-0000-0000-0000-000000000212', @TenantId, @Contract2Id, 'Documento Mat 2', @ActorId)
            ON CONFLICT (tenant_id, id) DO NOTHING;

            INSERT INTO odca.document_versions(id, tenant_id, contract_id, document_id, version_number, display_name, detected_type, byte_size, sha256, storage_key, security_status, uploaded_by)
            VALUES ('74000000-0000-0000-0000-000000000222', @TenantId, @Contract2Id, '74000000-0000-0000-0000-000000000212', 1, 'doc2.pdf', 'pdf', 2048, 'e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855', 'test/key2.pdf', 'safe', @ActorId)
            ON CONFLICT (tenant_id, id) DO NOTHING;

            -- Draft for Contract 1
            INSERT INTO odca.contract_drafts(id, tenant_id, contract_id, source_template_id, source_template_version_id, content, fields, patient_id, patient_selection_snapshot, patient_row_version, created_by, updated_by)
            VALUES ('74000000-0000-0000-0000-000000000501', @TenantId, @Contract1Id, @TemplateId, (SELECT id FROM odca.contract_template_versions WHERE template_id = @TemplateId LIMIT 1), '{"type":"document","content":[]}'::jsonb, '[]'::jsonb, @Patient1Id, odca.patient_document_snapshot(@TenantId, @Patient1Id), 1, @ActorId, @ActorId)
            ON CONFLICT (tenant_id, contract_id) DO UPDATE SET patient_id = @Patient1Id, patient_selection_snapshot = odca.patient_document_snapshot(@TenantId, @Patient1Id), patient_row_version = 1;

            -- Generated version for Draft 1
            INSERT INTO odca.generated_contract_versions(
                id, tenant_id, contract_id, draft_id, patient_id, patient_snapshot, version_number, content_schema_version,
                content, fields, values, source_template_id, source_template_version_id, canonical_sha256,
                storage_key, byte_size, created_by, review_status, idempotency_key, draft_row_version,
                pdf_status, pdf_byte_size, pdf_storage_key, pdf_sha256, pdf_renderer_version, pdf_completed_at)
            VALUES (
                '74000000-0000-0000-0000-000000000601', @TenantId, @Contract1Id, '74000000-0000-0000-0000-000000000501', @Patient1Id, odca.patient_document_snapshot(@TenantId, @Patient1Id),
                1, 1, '{"type":"document","content":[]}'::jsonb, '[]'::jsonb, '[]'::jsonb, @TemplateId,
                (SELECT id FROM odca.contract_template_versions WHERE template_id = @TemplateId LIMIT 1),
                'e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855',
                'studio/versions/test-mat-v1.pdf', 1024, @ActorId, 'submitted', gen_random_uuid(), 1,
                'completed', 1024, 'studio/pdfs/test-mat-v1.pdf', 'e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855', 1, now())
            ON CONFLICT (tenant_id, id) DO NOTHING;

            -- Review request for Contract 1
            INSERT INTO odca.contract_review_requests(
                id, tenant_id, contract_id, generated_version_id, requested_by, instructions,
                content_snapshot, document_sha256, idempotency_key, row_version, status, due_at)
            VALUES (
                '74000000-0000-0000-0000-000000000701', @TenantId, @Contract1Id, '74000000-0000-0000-0000-000000000601',
                @ActorId, 'Revisar minuta mat', '{"type":"document","content":[]}'::jsonb,
                'e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855',
                gen_random_uuid(), 1, 'in_review', now() + interval '5 days')
            ON CONFLICT (tenant_id, id) DO NOTHING;

            INSERT INTO odca.contract_review_steps(tenant_id, review_id, sequence, reviewer_id, status)
            VALUES (@TenantId, '74000000-0000-0000-0000-000000000701', 1, @ActorId, 'current')
            ON CONFLICT DO NOTHING;

            -- Contract Imports
            INSERT INTO odca.contract_imports(id, tenant_id, requested_by, document_version_id, file_sha256, status, current_step, result_contract_id, safe_diagnostic_code, confirmed_by, confirmed_at)
            VALUES
                (@Import1Id, @TenantId, @ActorId, '74000000-0000-0000-0000-000000000221', 'e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855', 'awaiting_review', 'document_inspection', null, null, null, null),
                (@Import2Id, @TenantId, @ActorId, '74000000-0000-0000-0000-000000000222', 'e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855', 'confirmed', 'confirmation', @Contract2Id, 'DIAG_OK', @ActorId, now())
            ON CONFLICT (tenant_id, id) DO UPDATE SET
                status = EXCLUDED.status,
                current_step = EXCLUDED.current_step,
                result_contract_id = EXCLUDED.result_contract_id,
                safe_diagnostic_code = EXCLUDED.safe_diagnostic_code,
                confirmed_by = EXCLUDED.confirmed_by,
                confirmed_at = EXCLUDED.confirmed_at;
            """, new { TenantId, EmptyTenantId, ActorId, Patient1Id, Patient2Id, Patient3Id, Contract1Id, Contract2Id, TemplateId, Import1Id, Import2Id });
    }
}
