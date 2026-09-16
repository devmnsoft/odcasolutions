using Dapper;
using Npgsql;
using Odca.Application.Contracts;
using Odca.Contracts.Studio;

namespace Odca.Infrastructure.Contracts;

public sealed class NpgsqlTemplateCatalogRepository(NpgsqlDataSource dataSource) : ITemplateCatalogRepository
{
    private const string VisibleFilter = """
        t.status = 'published'
        AND (t.scope = 'global' OR t.owner_tenant_id = @tenantId OR EXISTS (
            SELECT 1
              FROM odca.contract_template_access a
             WHERE a.template_id = t.id
               AND a.tenant_id = @tenantId
               AND a.revoked_at IS NULL))
        AND (@search IS NULL OR t.name ILIKE @search OR t.description ILIKE @search)
        AND (@contractType IS NULL OR t.contract_type = @contractType)
        AND (@scope IS NULL OR t.scope = @scope)
        """;

    public async Task<TemplateCatalogPage> ListAsync(
        Guid tenantId,
        string? search,
        string? contractType,
        string? scope,
        int page,
        int pageSize,
        CancellationToken cancellationToken)
    {
        var parameters = new
        {
            tenantId,
            search = string.IsNullOrWhiteSpace(search) ? null : $"%{search.Trim()}%",
            contractType,
            scope,
            offset = (page - 1) * pageSize,
            pageSize
        };

        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        var total = await connection.ExecuteScalarAsync<int>(new CommandDefinition(
            $"SELECT count(*)::integer FROM odca.contract_templates t WHERE {VisibleFilter}",
            parameters,
            cancellationToken: cancellationToken));
        var rows = await connection.QueryAsync<TemplateCatalogRow>(new CommandDefinition($"""
            SELECT t.id AS Id,
                   t.name AS Name,
                   t.description AS Description,
                   t.contract_type AS ContractType,
                   t.scope AS Scope,
                   t.status AS Status,
                   t.current_version AS Version,
                   u.display_name AS Author,
                   t.created_at AS CreatedAt,
                   t.published_at AS PublishedAt
              FROM odca.contract_templates t
              JOIN odca.users u ON u.id = t.author_id
             WHERE {VisibleFilter}
             ORDER BY t.name, t.id
             LIMIT @pageSize OFFSET @offset
            """, parameters, cancellationToken: cancellationToken));

        return new TemplateCatalogPage(rows.Select(ToContract).ToList(), page, pageSize, total);
    }

    private static TemplateCatalogItem ToContract(TemplateCatalogRow row) => new(
        row.Id,
        row.Name,
        row.Description,
        row.ContractType,
        row.Scope,
        row.Status,
        row.Version,
        row.Author,
        AsOffset(row.CreatedAt, nameof(row.CreatedAt)),
        row.PublishedAt is null ? null : AsOffset(row.PublishedAt.Value, nameof(row.PublishedAt)));

    private static DateTimeOffset AsOffset(DateTime value, string column) => value.Kind switch
    {
        DateTimeKind.Utc => new DateTimeOffset(value),
        _ => throw new InvalidOperationException(
            $"A coluna timestamptz {column} não foi materializada como DateTime UTC.")
    };

    // Npgsql exposes PostgreSQL timestamptz as UTC DateTime. Keeping this read model
    // internal prevents Dapper from requiring the public record's DateTimeOffset constructor.
    private sealed class TemplateCatalogRow
    {
        public Guid Id { get; init; }
        public string Name { get; init; } = null!;
        public string? Description { get; init; }
        public string ContractType { get; init; } = null!;
        public string Scope { get; init; } = null!;
        public string Status { get; init; } = null!;
        public int Version { get; init; }
        public string Author { get; init; } = null!;
        public DateTime CreatedAt { get; init; }
        public DateTime? PublishedAt { get; init; }
    }
}
