using Dapper;
using Npgsql;
using Odca.Infrastructure.Contracts;

namespace Odca.IntegrationTests;

public sealed class TemplateCatalogPersistenceTests(DatabaseFixture database) : IClassFixture<DatabaseFixture>
{
    private static readonly Guid TenantId = Guid.Parse("71000000-0000-0000-0000-000000000001");
    private static readonly Guid PublishedId = Guid.Parse("71000000-0000-0000-0000-000000000002");
    private static readonly Guid DraftId = Guid.Parse("71000000-0000-0000-0000-000000000003");

    [Fact]
    public async Task CatalogMaterializesPostgresTimestampsAndNullableFieldsWhileScopingDraftsToOwner()
    {
        await SeedAsync();
        await using var dataSource = NpgsqlDataSource.Create(database.AppConnectionString);
        var repository = new NpgsqlTemplateCatalogRepository(dataSource);

        var page = await repository.ListAsync(TenantId, "integração catálogo", null, null, null, 1, 12, CancellationToken.None);

        var item = Assert.Single(page.Items, x => x.Id == PublishedId);
        Assert.Null(item.Description);
        Assert.Equal("published", item.Status);
        Assert.Equal(7, item.Version);
        Assert.Equal(TimeSpan.Zero, item.CreatedAt.Offset);
        Assert.Equal(new DateTimeOffset(2026, 9, 16, 12, 30, 0, TimeSpan.Zero), item.PublishedAt);
        var draft = Assert.Single(page.Items, x => x.Id == DraftId);
        Assert.Equal("draft", draft.Status);
        Assert.Equal(2, page.Total);

        // Filtro por finalidade documental (categorias de descoberta): só expõe linhas com o valor pedido;
        // linhas sem finalidade (rascunho) não aparecem em nenhum filtro de finalidade.
        var byPurpose = await repository.ListAsync(TenantId, "integração catálogo", null, null, "confidentiality", 1, 12, CancellationToken.None);
        var byPurposeItem = Assert.Single(byPurpose.Items, x => x.Id == PublishedId);
        Assert.Equal("confidentiality", byPurposeItem.DocumentPurpose);
        var otherPurpose = await repository.ListAsync(TenantId, "integração catálogo", null, null, "lease", 1, 12, CancellationToken.None);
        Assert.Empty(otherPurpose.Items);
        Assert.Equal(0, otherPurpose.Total);

        // Rascunhos só são visíveis ao tenant proprietário: outro tenant vê apenas o publicado global.
        var other = await repository.ListAsync(Guid.NewGuid(), "integração catálogo", null, null, null, 1, 12, CancellationToken.None);
        var otherItem = Assert.Single(other.Items);
        Assert.Equal(PublishedId, otherItem.Id);
    }

    [Fact]
    public async Task CatalogCanBeTrulyEmpty()
    {
        await using var dataSource = NpgsqlDataSource.Create(database.AppConnectionString);
        var repository = new NpgsqlTemplateCatalogRepository(dataSource);

        var page = await repository.ListAsync(Guid.NewGuid(), Guid.NewGuid().ToString("N"), null, null, null, 1, 12, CancellationToken.None);

        Assert.Empty(page.Items);
        Assert.Equal(0, page.Total);
    }

    [Fact]
    public async Task OfficialCatalogMultipleTherapiesAndGenericServicesCoexistAndAreIdempotent()
    {
        var testTenantId = Guid.NewGuid();
        var testUserId = Guid.Parse("20000000-0000-0000-0000-000000000001");
        await using var adminConn = new NpgsqlConnection(database.AdminConnectionString);
        await adminConn.OpenAsync();

        await adminConn.ExecuteAsync("""
            INSERT INTO odca.tenants(id, business_code, display_name, timezone)
            VALUES (@testTenantId, @code, 'Clínica Múltiplas Terapias Teste', 'America/Sao_Paulo');

            INSERT INTO odca.memberships(tenant_id, user_id, status)
            VALUES (@testTenantId, @testUserId, 'active');

            INSERT INTO odca.roles(scope_type, tenant_id, code, display_name, is_system)
            VALUES ('tenant', @testTenantId, 'tenant-administrator', 'Administrador', true)
            ON CONFLICT DO NOTHING;

            INSERT INTO odca.role_permissions(role_id, permission_code)
            SELECT r.id, p.code FROM odca.roles r CROSS JOIN odca.permissions p
            WHERE r.tenant_id = @testTenantId AND r.code = 'tenant-administrator' AND p.code LIKE 'tenant.%'
            ON CONFLICT DO NOTHING;

            INSERT INTO odca.member_roles(tenant_id, user_id, role_id, assigned_by)
            SELECT @testTenantId, @testUserId, r.id, @testUserId FROM odca.roles r
            WHERE r.tenant_id = @testTenantId AND r.code = 'tenant-administrator'
            ON CONFLICT DO NOTHING;
            """, new { testTenantId, testUserId, code = "TEST-ORG-" + Guid.NewGuid().ToString("N")[..8] });

        await using var dataSource = NpgsqlDataSource.Create(database.AppConnectionString);
        var controller = new Odca.Api.Controllers.ContractStudioCatalogController(dataSource);
        var httpContext = new Microsoft.AspNetCore.Http.DefaultHttpContext();
        httpContext.User = new System.Security.Claims.ClaimsPrincipal(new System.Security.Claims.ClaimsIdentity([
            new System.Security.Claims.Claim("sub", testUserId.ToString()),
            new System.Security.Claims.Claim(System.Security.Claims.ClaimTypes.NameIdentifier, testUserId.ToString())
        ], "test"));
        controller.ControllerContext = new Microsoft.AspNetCore.Mvc.ControllerContext { HttpContext = httpContext };

        // 1. First installation
        var installResult1 = await controller.InstallOfficial(testTenantId, default);
        var okResult1 = Assert.IsType<Microsoft.AspNetCore.Mvc.OkObjectResult>(installResult1);
        var installResp1 = Assert.IsType<Odca.Contracts.Studio.OfficialTemplateInstallResponse>(okResult1.Value);
        Assert.True(installResp1.Installed >= 2);
        Assert.Contains(installResp1.Names, n => n.Contains("Múltiplas Terapias", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(installResp1.Names, n => n.Contains("Prestação de Serviços", StringComparison.OrdinalIgnoreCase));

        // 2. Query multiple-therapies official template
        var mtResult = await controller.OfficialTemplate(testTenantId, "multiple-therapies", default);
        var mtOk = Assert.IsType<Microsoft.AspNetCore.Mvc.OkObjectResult>(mtResult);
        var mtPreview = Assert.IsType<Odca.Contracts.Studio.TemplatePreview>(mtOk.Value);
        Assert.Contains("Múltiplas Terapias", mtPreview.Name, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(mtPreview.FieldLabels, l => l.Contains("Neuropsicologia", StringComparison.OrdinalIgnoreCase));

        // 3. Query generic services official template
        var srvResult = await controller.OfficialTemplate(testTenantId, "services-agreement", default);
        var srvOk = Assert.IsType<Microsoft.AspNetCore.Mvc.OkObjectResult>(srvResult);
        var srvPreview = Assert.IsType<Odca.Contracts.Studio.TemplatePreview>(srvOk.Value);
        Assert.DoesNotContain("Múltiplas Terapias", srvPreview.Name, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(srvPreview.FieldLabels, l => l.Contains("Terapias Selecionadas", StringComparison.OrdinalIgnoreCase));

        // 4. Second installation is idempotent: 0 new templates installed
        var installResult2 = await controller.InstallOfficial(testTenantId, default);
        var okResult2 = Assert.IsType<Microsoft.AspNetCore.Mvc.OkObjectResult>(installResult2);
        var installResp2 = Assert.IsType<Odca.Contracts.Studio.OfficialTemplateInstallResponse>(okResult2.Value);
        Assert.Equal(0, installResp2.Installed);
        Assert.True(installResp2.AlreadyPresent >= 2);
    }

    private sealed class StubTemplateCatalogRepository : Odca.Application.Contracts.ITemplateCatalogRepository
    {
        public Task<Odca.Contracts.Studio.TemplateCatalogPage> ListAsync(Guid tenantId, string? search, string? contractType, string? scope, string? documentPurpose, int page, int pageSize, CancellationToken cancellationToken) =>
            Task.FromResult(new Odca.Contracts.Studio.TemplateCatalogPage([], page, pageSize, 0));
    }

    [Fact]
    public async Task ArchiveRequiresReasonPersistsItAndKeepsPublishedVersionImmutable()
    {
        // D-OC34 B.5: arquivar exige motivo (5..500), o motivo fica gravado na linha e o
        // conteúdo da versão vigente não muda com o arquivamento (imutabilidade preservada).
        var tenantId = Guid.NewGuid();
        var userId = Guid.Parse("20000000-0000-0000-0000-000000000001");
        var templateId = Guid.NewGuid();
        var versionId = Guid.NewGuid();
        await using var adminConn = new NpgsqlConnection(database.AdminConnectionString);
        await adminConn.OpenAsync();
        await adminConn.ExecuteAsync("""
            INSERT INTO odca.tenants(id, business_code, display_name, timezone)
            VALUES (@TenantId, @Code, 'Modelo Arquivo Teste', 'America/Sao_Paulo');

            INSERT INTO odca.memberships(tenant_id, user_id, status)
            VALUES (@TenantId, @UserId, 'active');

            INSERT INTO odca.roles(scope_type, tenant_id, code, display_name, is_system)
            VALUES ('tenant', @TenantId, 'tenant-administrator', 'Administrador', true)
            ON CONFLICT DO NOTHING;

            INSERT INTO odca.role_permissions(role_id, permission_code)
            SELECT r.id, p.code FROM odca.roles r CROSS JOIN odca.permissions p
            WHERE r.tenant_id = @TenantId AND r.code = 'tenant-administrator' AND p.code LIKE 'tenant.%'
            ON CONFLICT DO NOTHING;

            INSERT INTO odca.member_roles(tenant_id, user_id, role_id, assigned_by)
            SELECT @TenantId, @UserId, r.id, @UserId FROM odca.roles r
            WHERE r.tenant_id = @TenantId AND r.code = 'tenant-administrator'
            ON CONFLICT DO NOTHING;

            INSERT INTO odca.contract_templates(id, owner_tenant_id, name, contract_type, scope, status, current_version, author_id, published_at)
            VALUES (@TemplateId, @TenantId, 'Modelo arquivável', 'service', 'private', 'published', 1, @UserId, now());

            INSERT INTO odca.contract_template_versions(id, template_id, version_number, content, fields, created_by, published_at)
            VALUES (@VersionId, @TemplateId, 1, '{"type":"document","content":[]}', '[]', @UserId, now());
            """, new { TenantId = tenantId, UserId = userId, TemplateId = templateId, VersionId = versionId, Code = "ARCHIVE-" + Guid.NewGuid().ToString("N")[..8] });

        var versionBefore = await adminConn.QuerySingleAsync<string>(new CommandDefinition("""
            SELECT t.current_version::text || '|' || v.content::text || '|' || v.fields::text
              FROM odca.contract_templates t
              JOIN odca.contract_template_versions v ON v.template_id=t.id AND v.version_number=t.current_version
             WHERE t.id=@TemplateId
            """, new { TemplateId = templateId }, cancellationToken: CancellationToken.None));

        await using var dataSource = NpgsqlDataSource.Create(database.AppConnectionString);
        var config = new Microsoft.Extensions.Configuration.ConfigurationBuilder().Build();
        var controller = new Odca.Api.Controllers.ContractStudioController(dataSource, config, new StubTemplateCatalogRepository());
        controller.ControllerContext = new Microsoft.AspNetCore.Mvc.ControllerContext { HttpContext = TestControllerContexts.Create(userId) };

        // Sem motivo (ou motivo curto demais) o arquivamento é recusado antes de qualquer efeito.
        var invalid = await controller.ArchiveTemplate(tenantId, templateId, expectedVersion: 1, reason: "   ", CancellationToken.None);
        Assert.IsType<Microsoft.AspNetCore.Mvc.BadRequestObjectResult>(invalid);
        var stillPublished = await adminConn.ExecuteScalarAsync<string>(new CommandDefinition(
            "SELECT status FROM odca.contract_templates WHERE id=@TemplateId", new { TemplateId = templateId }, cancellationToken: CancellationToken.None));
        Assert.Equal("published", stillPublished);

        var ok = await controller.ArchiveTemplate(tenantId, templateId, expectedVersion: 1, reason: "Substituído pelo modelo de serviços v2", CancellationToken.None);
        Assert.IsType<Microsoft.AspNetCore.Mvc.NoContentResult>(ok);

        var stateAfter = await adminConn.QuerySingleAsync<string>(new CommandDefinition("""
            SELECT t.status || '|' || coalesce(t.archive_reason,'') || '|' || t.row_version::text || '|' ||
                   (t.archived_at IS NOT NULL)::text
              FROM odca.contract_templates t WHERE t.id=@TemplateId
            """, new { TemplateId = templateId }, cancellationToken: CancellationToken.None));
        Assert.Equal("archived|Substituído pelo modelo de serviços v2|2|true", stateAfter);

        // A versão vigente permanece intacta: mesmo conteúdo, campos e número de versão.
        var versionAfter = await adminConn.QuerySingleAsync<string>(new CommandDefinition("""
            SELECT t.current_version::text || '|' || v.content::text || '|' || v.fields::text
              FROM odca.contract_templates t
              JOIN odca.contract_template_versions v ON v.template_id=t.id AND v.version_number=t.current_version
             WHERE t.id=@TemplateId
            """, new { TemplateId = templateId }, cancellationToken: CancellationToken.None));
        Assert.Equal(versionBefore, versionAfter);

        // Segunda tentativa sobre a linha já arquivada é conflito estável.
        var stale = await controller.ArchiveTemplate(tenantId, templateId, expectedVersion: 2, reason: "Segundo arquivamento", CancellationToken.None);
        Assert.IsType<Microsoft.AspNetCore.Mvc.ConflictObjectResult>(stale);
    }

    private async Task SeedAsync()
    {
        const string sql = """
            INSERT INTO odca.tenants(id,business_code,display_name,timezone)
            VALUES(@TenantId,'CATALOG-TEST','Catálogo Teste','America/Sao_Paulo')
            ON CONFLICT(id) DO NOTHING;

            INSERT INTO odca.contract_templates
                (id,owner_tenant_id,name,description,contract_type,scope,status,current_version,author_id,created_at,published_at,document_purpose)
            VALUES
                (@PublishedId,NULL,'Integração catálogo publicado',NULL,'service','global','published',7,
                 '20000000-0000-0000-0000-000000000001','2026-09-16 09:30:00-03','2026-09-16 09:30:00-03','confidentiality'),
                (@DraftId,@TenantId,'Integração catálogo rascunho',NULL,'service','private','draft',1,
                 '20000000-0000-0000-0000-000000000001','2026-09-16 09:30:00-03',NULL,NULL)
            ON CONFLICT(id) DO UPDATE SET
                name=EXCLUDED.name,description=EXCLUDED.description,status=EXCLUDED.status,
                current_version=EXCLUDED.current_version,created_at=EXCLUDED.created_at,published_at=EXCLUDED.published_at,
                document_purpose=EXCLUDED.document_purpose;
            """;
        await using var connection = new NpgsqlConnection(database.AdminConnectionString);
        await connection.ExecuteAsync(sql, new { TenantId, PublishedId, DraftId });
    }
}
