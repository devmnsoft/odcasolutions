using Dapper;
using Npgsql;
using Odca.Application.Contracts;
using Odca.Application.Tenancy;

namespace Odca.Infrastructure.Contracts;

public sealed class NpgsqlContractTypeRepository(NpgsqlDataSource dataSource) : IContractTypeRepository
{
    public async Task<QueryAccess<IReadOnlyList<ContractTypeRecord>>> ListAsync(
        Guid actorId,
        Guid tenantId,
        string? status,
        CancellationToken cancellationToken)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var tx = await TenantSql.BeginAuthorizedAsync(connection, actorId, tenantId, "tenant.contract_types.read", cancellationToken);
        if (tx is null)
        {
            return new QueryAccess<IReadOnlyList<ContractTypeRecord>>(QueryAccessStatus.Forbidden, null);
        }

        var rows = await connection.QueryAsync<ContractTypeRecord>(new CommandDefinition(
            $"""
            {SelectColumns}
              FROM odca.contract_types
             WHERE tenant_id = @tenantId
               AND (@status IS NULL OR status = @status)
             ORDER BY name;
            """,
            new { tenantId, status },
            tx,
            cancellationToken: cancellationToken));
        await tx.CommitAsync(cancellationToken);
        return new QueryAccess<IReadOnlyList<ContractTypeRecord>>(QueryAccessStatus.Ok, rows.AsList());
    }

    public async Task<QueryAccess<ContractTypeRecord?>> GetAsync(
        Guid actorId,
        Guid tenantId,
        Guid id,
        CancellationToken cancellationToken)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var tx = await TenantSql.BeginAuthorizedAsync(connection, actorId, tenantId, "tenant.contract_types.read", cancellationToken);
        if (tx is null)
        {
            return new QueryAccess<ContractTypeRecord?>(QueryAccessStatus.Forbidden, null);
        }

        var row = await LoadAsync(connection, tx, tenantId, id, cancellationToken);
        await tx.CommitAsync(cancellationToken);
        return new QueryAccess<ContractTypeRecord?>(QueryAccessStatus.Ok, row);
    }

    public async Task<MutationResult<ContractTypeRecord>> CreateAsync(
        Guid actorId,
        Guid tenantId,
        ContractTypeWriteModel model,
        CancellationToken cancellationToken)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var tx = await TenantSql.BeginAuthorizedAsync(connection, actorId, tenantId, "tenant.contract_types.manage", cancellationToken);
        if (tx is null)
        {
            return new MutationResult<ContractTypeRecord>(MutationStatus.Forbidden);
        }

        try
        {
            var id = await connection.ExecuteScalarAsync<Guid>(new CommandDefinition(
                """
                INSERT INTO odca.contract_types (tenant_id, code, name, description, guidance, created_by)
                VALUES (@tenantId, @Code, @Name, @Description, @Guidance, @actorId)
                RETURNING id;
                """,
                new { tenantId, actorId, model.Code, model.Name, model.Description, model.Guidance },
                tx,
                cancellationToken: cancellationToken));
            var row = await LoadAsync(connection, tx, tenantId, id, cancellationToken);
            await tx.CommitAsync(cancellationToken);
            return new MutationResult<ContractTypeRecord>(MutationStatus.Succeeded, row);
        }
        catch (PostgresException ex) when (ex.SqlState == PostgresErrorCodes.UniqueViolation)
        {
            await tx.RollbackAsync(cancellationToken);
            return new MutationResult<ContractTypeRecord>(MutationStatus.Conflict, ErrorCode: "code_conflict");
        }
    }

    public async Task<MutationResult<ContractTypeRecord>> UpdateAsync(
        Guid actorId,
        Guid tenantId,
        Guid id,
        long version,
        ContractTypeWriteModel model,
        CancellationToken cancellationToken)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var tx = await TenantSql.BeginAuthorizedAsync(connection, actorId, tenantId, "tenant.contract_types.manage", cancellationToken);
        if (tx is null)
        {
            return new MutationResult<ContractTypeRecord>(MutationStatus.Forbidden);
        }

        try
        {
            var updated = await connection.ExecuteAsync(new CommandDefinition(
                """
                UPDATE odca.contract_types
                   SET code = @Code,
                       name = @Name,
                       description = @Description,
                       guidance = @Guidance,
                       version = version + 1,
                       updated_at = now()
                 WHERE tenant_id = @tenantId
                   AND id = @id
                   AND version = @version;
                """,
                new { tenantId, id, version, model.Code, model.Name, model.Description, model.Guidance },
                tx,
                cancellationToken: cancellationToken));
            if (updated == 0)
            {
                await tx.RollbackAsync(cancellationToken);
                return new MutationResult<ContractTypeRecord>(MutationStatus.Conflict, ErrorCode: "version_conflict");
            }

            var row = await LoadAsync(connection, tx, tenantId, id, cancellationToken);
            await tx.CommitAsync(cancellationToken);
            return new MutationResult<ContractTypeRecord>(MutationStatus.Succeeded, row);
        }
        catch (PostgresException ex) when (ex.SqlState == PostgresErrorCodes.UniqueViolation)
        {
            await tx.RollbackAsync(cancellationToken);
            return new MutationResult<ContractTypeRecord>(MutationStatus.Conflict, ErrorCode: "code_conflict");
        }
    }

    public async Task<MutationResult> SetStatusAsync(
        Guid actorId,
        Guid tenantId,
        Guid id,
        long version,
        string status,
        CancellationToken cancellationToken)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var tx = await TenantSql.BeginAuthorizedAsync(connection, actorId, tenantId, "tenant.contract_types.manage", cancellationToken);
        if (tx is null)
        {
            return new MutationResult(MutationStatus.Forbidden);
        }

        var updated = await connection.ExecuteAsync(new CommandDefinition(
            """
            UPDATE odca.contract_types
               SET status = @status,
                   version = version + 1,
                   updated_at = now()
             WHERE tenant_id = @tenantId
               AND id = @id
               AND version = @version;
            """,
            new { tenantId, id, version, status },
            tx,
            cancellationToken: cancellationToken));
        if (updated == 0)
        {
            await tx.RollbackAsync(cancellationToken);
            return new MutationResult(MutationStatus.Conflict);
        }

        await tx.CommitAsync(cancellationToken);
        return new MutationResult(MutationStatus.Succeeded);
    }

    private static Task<ContractTypeRecord?> LoadAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction tx,
        Guid tenantId,
        Guid id,
        CancellationToken cancellationToken)
        => connection.QuerySingleOrDefaultAsync<ContractTypeRecord>(new CommandDefinition(
            $"""
            {SelectColumns}
              FROM odca.contract_types
             WHERE tenant_id = @tenantId AND id = @id;
            """,
            new { tenantId, id },
            tx,
            cancellationToken: cancellationToken));

    private const string SelectColumns = """
        SELECT id AS "Id",
               code AS "Code",
               name AS "Name",
               description AS "Description",
               guidance AS "Guidance",
               status AS "Status",
               is_system_demo AS "IsSystemDemo",
               version AS "Version",
               created_at AS "CreatedAt",
               updated_at AS "UpdatedAt"
        """;
}
