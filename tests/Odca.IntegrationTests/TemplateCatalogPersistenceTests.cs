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
