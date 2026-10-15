using System.Xml.Linq;
using Dapper;
using Microsoft.Extensions.Caching.Distributed;
using Npgsql;
using Odca.Infrastructure.Distributed;

namespace Odca.IntegrationTests;

/// <summary>
/// D-OC31 A.4: os stores compartilhados em PostgreSQL (v054) sustentam a operação
/// com múltiplas instâncias — o cache distribuído grava/lê/expira/remove entradas
/// e o repositório de chaves de Data Protection persiste chaves legíveis por um
/// segundo processo.
/// </summary>
public sealed class SharedStorageTests(DatabaseFixture database) : IClassFixture<DatabaseFixture>
{
    [Fact]
    public async Task PostgresDistributedCacheStoresRetrievesExpiresAndRemovesEntries()
    {
        await using var admin = new NpgsqlConnection(database.AdminConnectionString);
        await admin.OpenAsync();
        using var dataSource = NpgsqlDataSource.Create(database.AdminConnectionString);
        var cache = new PostgresDistributedCache(dataSource);
        var key = "odca:d31:test:" + Guid.NewGuid().ToString("N");
        var payload = new byte[] { 1, 2, 3, 250 };

        Assert.Null(await cache.GetAsync(key));
        await cache.SetAsync(key, payload, new DistributedCacheEntryOptions { AbsoluteExpiration = DateTimeOffset.UtcNow.AddMinutes(5) });
        var retrieved = await cache.GetAsync(key);
        Assert.NotNull(retrieved);
        Assert.True(payload.SequenceEqual(retrieved), "payload retornado difere do gravado");

        await cache.SetAsync(key, new byte[] { 9 }, new DistributedCacheEntryOptions { AbsoluteExpiration = DateTimeOffset.UtcNow.AddMinutes(5) });
        var overwritten = await cache.GetAsync(key);
        Assert.NotNull(overwritten);
        Assert.True(overwritten.SequenceEqual(new byte[] { 9 }), "substituicao nao sobrescreveu o valor");

        await admin.ExecuteAsync(
            "UPDATE odca.distributed_cache_entries SET expires_at = now() - interval '1 second' WHERE cache_key=@key",
            new { key });
        Assert.Null(await cache.GetAsync(key));
        await cache.RemoveAsync(key);
        Assert.Equal(0, await admin.ExecuteScalarAsync<int>(
            "SELECT count(*)::int FROM odca.distributed_cache_entries WHERE cache_key=@key", new { key }));
    }

    [Fact]
    public void PostgresKeysRepositoryPersistsKeysReadableByASecondProcess()
    {
        using var firstSource = NpgsqlDataSource.Create(database.AdminConnectionString);
        var element = XElement.Parse("<Key id='" + Guid.NewGuid() + "'>d31-test-key</Key>");
        new PostgresKeysRepository(firstSource).StoreElement(element, "d31-test");
        var firstRead = new PostgresKeysRepository(firstSource).GetAllElements();
        Assert.True(firstRead.Any(e => Matches(e, element)), "chave nao leida de volta pela primeira instancia");

        // Uma segunda instância (nova conexão) lê a mesma chave.
        using var secondSource = NpgsqlDataSource.Create(database.AdminConnectionString);
        var secondRead = new PostgresKeysRepository(secondSource).GetAllElements();
        Assert.True(secondRead.Any(e => Matches(e, element)), "chave nao leida de volta pela segunda instancia");
    }

    private static bool Matches(XElement stored, XElement expected) =>
        string.Equals(stored.ToString(SaveOptions.DisableFormatting), expected.ToString(SaveOptions.DisableFormatting), StringComparison.Ordinal);
}
