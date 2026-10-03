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
    public async Task CatalogMaterializesPostgresTimestampsAndNullableFieldsWhileHidingDrafts()
    {
        await SeedAsync();
        await using var dataSource = NpgsqlDataSource.Create(database.AppConnectionString);
        var repository = new NpgsqlTemplateCatalogRepository(dataSource);

        var page = await repository.ListAsync(TenantId, "integração catálogo", null, null, 1, 12, CancellationToken.None);

        var item = Assert.Single(page.Items);
        Assert.Equal(PublishedId, item.Id);
        Assert.Null(item.Description);
        Assert.Equal("published", item.Status);
        Assert.Equal(7, item.Version);
        Assert.Equal(TimeSpan.Zero, item.CreatedAt.Offset);
        Assert.Equal(new DateTimeOffset(2026, 9, 16, 12, 30, 0, TimeSpan.Zero), item.PublishedAt);
        Assert.DoesNotContain(page.Items, candidate => candidate.Id == DraftId);
    }

    [Fact]
    public async Task CatalogCanBeTrulyEmpty()
    {
        await using var dataSource = NpgsqlDataSource.Create(database.AppConnectionString);
        var repository = new NpgsqlTemplateCatalogRepository(dataSource);

        var page = await repository.ListAsync(Guid.NewGuid(), Guid.NewGuid().ToString("N"), null, null, 1, 12, CancellationToken.None);

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

    private async Task SeedAsync()
    {
        const string sql = """
            INSERT INTO odca.tenants(id,business_code,display_name,timezone)
            VALUES(@TenantId,'CATALOG-TEST','Catálogo Teste','America/Sao_Paulo')
            ON CONFLICT(id) DO NOTHING;

            INSERT INTO odca.contract_templates
                (id,owner_tenant_id,name,description,contract_type,scope,status,current_version,author_id,created_at,published_at)
            VALUES
                (@PublishedId,NULL,'Integração catálogo publicado',NULL,'service','global','published',7,
                 '20000000-0000-0000-0000-000000000001','2026-09-16 09:30:00-03','2026-09-16 09:30:00-03'),
                (@DraftId,@TenantId,'Integração catálogo rascunho',NULL,'service','private','draft',1,
                 '20000000-0000-0000-0000-000000000001','2026-09-16 09:30:00-03',NULL)
            ON CONFLICT(id) DO UPDATE SET
                name=EXCLUDED.name,description=EXCLUDED.description,status=EXCLUDED.status,
                current_version=EXCLUDED.current_version,created_at=EXCLUDED.created_at,published_at=EXCLUDED.published_at;
            """;
        await using var connection = new NpgsqlConnection(database.AdminConnectionString);
        await connection.ExecuteAsync(sql, new { TenantId, PublishedId, DraftId });
    }
}
