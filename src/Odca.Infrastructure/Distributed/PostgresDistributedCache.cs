using Dapper;
using Microsoft.Extensions.Caching.Distributed;
using Npgsql;

namespace Odca.Infrastructure.Distributed;

/// <summary>
/// D-OC31 A.4: IDistributedCache sobre PostgreSQL (tabela
/// <c>odca.distributed_cache_entries</c>, v054) para compartilhar os tickets de
/// sessão do BFF entre múltiplas instâncias. A AbsoluteExpiration é gravada em
/// <c>expires_at</c>; a SlidingExpiration é aceita e materializada de forma
/// conservadora como expiração absoluta — o BFF não renova o ticket em leitura
/// (opção de cookie SlidingExpiration=false), então estender o prazo ao ler
/// manteria a sessão viva além da vida prevista.
/// </summary>
public sealed class PostgresDistributedCache(NpgsqlDataSource dataSource) : IDistributedCache
{
    private static readonly TimeSpan DefaultAbsoluteExpiration = TimeSpan.FromHours(1);

    public byte[]? Get(string key) => GetAsync(key).GetAwaiter().GetResult();

    public void Set(string key, byte[] value, DistributedCacheEntryOptions options) => SetAsync(key, value, options).GetAwaiter().GetResult();

    public void Refresh(string key) => RefreshAsync(key).GetAwaiter().GetResult();

    public void Remove(string key) => RemoveAsync(key).GetAwaiter().GetResult();

    public async Task<byte[]?> GetAsync(string key, CancellationToken token = default)
    {
        await using var connection = await dataSource.OpenConnectionAsync(token);
        return await connection.QuerySingleOrDefaultAsync<byte[]?>(new CommandDefinition(
            """
            SELECT value
              FROM odca.distributed_cache_entries
             WHERE cache_key = @key AND expires_at > now()
            """,
            new { key },
            cancellationToken: token));
    }

    public async Task RefreshAsync(string key, CancellationToken token = default)
    {
        await using var connection = await dataSource.OpenConnectionAsync(token);
        await connection.ExecuteAsync(new CommandDefinition(
            """
            UPDATE odca.distributed_cache_entries
               SET expires_at = now() + @window, updated_at = now()
             WHERE cache_key = @key AND expires_at > now()
            """,
            new { key, window = DefaultAbsoluteExpiration },
            cancellationToken: token));
    }

    public async Task SetAsync(string key, byte[] value, DistributedCacheEntryOptions options, CancellationToken token = default)
    {
        var now = DateTimeOffset.UtcNow;
        DateTimeOffset? expiresAt = options.AbsoluteExpiration;
        if (expiresAt is null && options.AbsoluteExpirationRelativeToNow.HasValue) expiresAt = now.Add(options.AbsoluteExpirationRelativeToNow.Value);
        expiresAt ??= now.Add(options.SlidingExpiration ?? DefaultAbsoluteExpiration);
        await using var connection = await dataSource.OpenConnectionAsync(token);
        await connection.ExecuteAsync(new CommandDefinition(
            """
            INSERT INTO odca.distributed_cache_entries(cache_key, value, expires_at, updated_at)
            VALUES(@key, @value, @expiresAt, now())
            ON CONFLICT(cache_key) DO UPDATE
               SET value = excluded.value, expires_at = excluded.expires_at, updated_at = now()
            """,
            new { key, value, expiresAt = expiresAt.Value.UtcDateTime },
            cancellationToken: token));
    }

    public async Task RemoveAsync(string key, CancellationToken token = default)
    {
        await using var connection = await dataSource.OpenConnectionAsync(token);
        await connection.ExecuteAsync(new CommandDefinition(
            "DELETE FROM odca.distributed_cache_entries WHERE cache_key = @key",
            new { key },
            cancellationToken: token));
    }
}
