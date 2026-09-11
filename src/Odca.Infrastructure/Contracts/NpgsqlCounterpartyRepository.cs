using Dapper;
using Npgsql;
using Odca.Application.Contracts;
using Odca.Application.Tenancy;

namespace Odca.Infrastructure.Contracts;

public sealed class NpgsqlCounterpartyRepository(NpgsqlDataSource dataSource) : ICounterpartyRepository
{
    public async Task<QueryAccess<TenantPage<CounterpartyRecord>>> ListAsync(
        Guid actorId,
        Guid tenantId,
        string? search,
        string? status,
        int page,
        int pageSize,
        CancellationToken cancellationToken)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var tx = await TenantSql.BeginAuthorizedAsync(connection, actorId, tenantId, "tenant.counterparties.read", cancellationToken);
        if (tx is null)
        {
            return new QueryAccess<TenantPage<CounterpartyRecord>>(QueryAccessStatus.Forbidden, null);
        }

        var normalizedSearch = string.IsNullOrWhiteSpace(search) ? null : search.Trim();
        var total = await connection.ExecuteScalarAsync<int>(new CommandDefinition(
            """
            SELECT count(*)::int
              FROM odca.counterparties
             WHERE tenant_id = @tenantId
               AND NOT is_deleted
               AND (@status IS NULL OR status = @status)
               AND (
                    @search IS NULL
                    OR display_name ILIKE '%' || @search || '%'
                    OR legal_name ILIKE '%' || @search || '%'
                    OR coalesce(document_normalized, '') ILIKE '%' || @search || '%'
                    OR coalesce(document_display, '') ILIKE '%' || @search || '%'
               );
            """,
            new { tenantId, status, search = normalizedSearch },
            tx,
            cancellationToken: cancellationToken));

        var rows = await connection.QueryAsync<CounterpartyRecord>(new CommandDefinition(
            $"""
            {SelectColumns}
              FROM odca.counterparties
             WHERE tenant_id = @tenantId
               AND NOT is_deleted
               AND (@status IS NULL OR status = @status)
               AND (
                    @search IS NULL
                    OR display_name ILIKE '%' || @search || '%'
                    OR legal_name ILIKE '%' || @search || '%'
                    OR coalesce(document_normalized, '') ILIKE '%' || @search || '%'
                    OR coalesce(document_display, '') ILIKE '%' || @search || '%'
               )
             ORDER BY display_name
             OFFSET @offset LIMIT @pageSize;
            """,
            new { tenantId, status, search = normalizedSearch, offset = Pagination.Offset(page, pageSize), pageSize },
            tx,
            cancellationToken: cancellationToken));

        await tx.CommitAsync(cancellationToken);
        return new QueryAccess<TenantPage<CounterpartyRecord>>(QueryAccessStatus.Ok, new TenantPage<CounterpartyRecord>(rows.AsList(), total, page, pageSize));
    }

    public async Task<QueryAccess<CounterpartyRecord?>> GetAsync(
        Guid actorId,
        Guid tenantId,
        Guid id,
        CancellationToken cancellationToken)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var tx = await TenantSql.BeginAuthorizedAsync(connection, actorId, tenantId, "tenant.counterparties.read", cancellationToken);
        if (tx is null)
        {
            return new QueryAccess<CounterpartyRecord?>(QueryAccessStatus.Forbidden, null);
        }

        var row = await connection.QuerySingleOrDefaultAsync<CounterpartyRecord>(new CommandDefinition(
            $"""
            {SelectColumns}
              FROM odca.counterparties
             WHERE tenant_id = @tenantId AND id = @id AND NOT is_deleted;
            """,
            new { tenantId, id },
            tx,
            cancellationToken: cancellationToken));
        await tx.CommitAsync(cancellationToken);
        return new QueryAccess<CounterpartyRecord?>(QueryAccessStatus.Ok, row);
    }

    public async Task<MutationResult<CounterpartyRecord>> CreateAsync(
        Guid actorId,
        Guid tenantId,
        CounterpartyWriteModel model,
        CancellationToken cancellationToken)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var tx = await TenantSql.BeginAuthorizedAsync(connection, actorId, tenantId, "tenant.counterparties.manage", cancellationToken);
        if (tx is null)
        {
            return new MutationResult<CounterpartyRecord>(MutationStatus.Forbidden);
        }

        try
        {
            var id = await connection.ExecuteScalarAsync<Guid>(new CommandDefinition(
                """
                INSERT INTO odca.counterparties (
                    tenant_id, person_type, legal_name, display_name, document_type, document_normalized, document_display,
                    email, phone, is_client, is_supplier, is_partner, is_provider,
                    address_line1, address_line2, address_city, address_state, address_postal_code, address_country,
                    created_by)
                VALUES (
                    @tenantId, @PersonType, @LegalName, @DisplayName, @DocumentType, @DocumentNormalized, @DocumentDisplay,
                    @Email, @Phone, @IsClient, @IsSupplier, @IsPartner, @IsProvider,
                    @AddressLine1, @AddressLine2, @AddressCity, @AddressState, @AddressPostalCode, @AddressCountry,
                    @actorId)
                RETURNING id;
                """,
                new
                {
                    tenantId,
                    actorId,
                    model.PersonType,
                    model.LegalName,
                    model.DisplayName,
                    model.DocumentType,
                    model.DocumentNormalized,
                    model.DocumentDisplay,
                    model.Email,
                    model.Phone,
                    model.IsClient,
                    model.IsSupplier,
                    model.IsPartner,
                    model.IsProvider,
                    model.AddressLine1,
                    model.AddressLine2,
                    model.AddressCity,
                    model.AddressState,
                    model.AddressPostalCode,
                    model.AddressCountry
                },
                tx,
                cancellationToken: cancellationToken));

            var row = await LoadAsync(connection, tx, tenantId, id, cancellationToken);
            await tx.CommitAsync(cancellationToken);
            return new MutationResult<CounterpartyRecord>(MutationStatus.Succeeded, row);
        }
        catch (PostgresException ex) when (ex.SqlState == PostgresErrorCodes.UniqueViolation)
        {
            await tx.RollbackAsync(cancellationToken);
            return new MutationResult<CounterpartyRecord>(MutationStatus.Conflict, ErrorCode: "document_conflict");
        }
    }

    public async Task<MutationResult<CounterpartyRecord>> UpdateAsync(
        Guid actorId,
        Guid tenantId,
        Guid id,
        long version,
        CounterpartyWriteModel model,
        CancellationToken cancellationToken)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var tx = await TenantSql.BeginAuthorizedAsync(connection, actorId, tenantId, "tenant.counterparties.manage", cancellationToken);
        if (tx is null)
        {
            return new MutationResult<CounterpartyRecord>(MutationStatus.Forbidden);
        }

        try
        {
            var updated = await connection.ExecuteAsync(new CommandDefinition(
                """
                UPDATE odca.counterparties
                   SET person_type = @PersonType,
                       legal_name = @LegalName,
                       display_name = @DisplayName,
                       document_type = @DocumentType,
                       document_normalized = @DocumentNormalized,
                       document_display = @DocumentDisplay,
                       email = @Email,
                       phone = @Phone,
                       is_client = @IsClient,
                       is_supplier = @IsSupplier,
                       is_partner = @IsPartner,
                       is_provider = @IsProvider,
                       address_line1 = @AddressLine1,
                       address_line2 = @AddressLine2,
                       address_city = @AddressCity,
                       address_state = @AddressState,
                       address_postal_code = @AddressPostalCode,
                       address_country = @AddressCountry,
                       version = version + 1,
                       updated_at = now()
                 WHERE tenant_id = @tenantId
                   AND id = @id
                   AND version = @version
                   AND NOT is_deleted
                   AND status = 'active';
                """,
                new
                {
                    tenantId,
                    id,
                    version,
                    model.PersonType,
                    model.LegalName,
                    model.DisplayName,
                    model.DocumentType,
                    model.DocumentNormalized,
                    model.DocumentDisplay,
                    model.Email,
                    model.Phone,
                    model.IsClient,
                    model.IsSupplier,
                    model.IsPartner,
                    model.IsProvider,
                    model.AddressLine1,
                    model.AddressLine2,
                    model.AddressCity,
                    model.AddressState,
                    model.AddressPostalCode,
                    model.AddressCountry
                },
                tx,
                cancellationToken: cancellationToken));

            if (updated == 0)
            {
                await tx.RollbackAsync(cancellationToken);
                return new MutationResult<CounterpartyRecord>(MutationStatus.Conflict, ErrorCode: "version_conflict");
            }

            var row = await LoadAsync(connection, tx, tenantId, id, cancellationToken);
            await tx.CommitAsync(cancellationToken);
            return new MutationResult<CounterpartyRecord>(MutationStatus.Succeeded, row);
        }
        catch (PostgresException ex) when (ex.SqlState == PostgresErrorCodes.UniqueViolation)
        {
            await tx.RollbackAsync(cancellationToken);
            return new MutationResult<CounterpartyRecord>(MutationStatus.Conflict, ErrorCode: "document_conflict");
        }
    }

    public async Task<MutationResult> InactivateAsync(
        Guid actorId,
        Guid tenantId,
        Guid id,
        long version,
        CancellationToken cancellationToken)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var tx = await TenantSql.BeginAuthorizedAsync(connection, actorId, tenantId, "tenant.counterparties.manage", cancellationToken);
        if (tx is null)
        {
            return new MutationResult(MutationStatus.Forbidden);
        }

        var updated = await connection.ExecuteAsync(new CommandDefinition(
            """
            UPDATE odca.counterparties
               SET status = 'inactive',
                   inactivated_at = now(),
                   inactivated_by = @actorId,
                   version = version + 1,
                   updated_at = now()
             WHERE tenant_id = @tenantId
               AND id = @id
               AND version = @version
               AND NOT is_deleted
               AND status = 'active';
            """,
            new { tenantId, id, version, actorId },
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

    private static async Task<CounterpartyRecord> LoadAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction tx,
        Guid tenantId,
        Guid id,
        CancellationToken cancellationToken)
    {
        return await connection.QuerySingleAsync<CounterpartyRecord>(new CommandDefinition(
            $"""
            {SelectColumns}
              FROM odca.counterparties
             WHERE tenant_id = @tenantId AND id = @id;
            """,
            new { tenantId, id },
            tx,
            cancellationToken: cancellationToken));
    }

    private const string SelectColumns = """
        SELECT id AS "Id",
               person_type AS "PersonType",
               legal_name AS "LegalName",
               display_name AS "DisplayName",
               document_type AS "DocumentType",
               document_normalized AS "DocumentNormalized",
               document_display AS "DocumentDisplay",
               email AS "Email",
               phone AS "Phone",
               is_client AS "IsClient",
               is_supplier AS "IsSupplier",
               is_partner AS "IsPartner",
               is_provider AS "IsProvider",
               status AS "Status",
               address_line1 AS "AddressLine1",
               address_line2 AS "AddressLine2",
               address_city AS "AddressCity",
               address_state AS "AddressState",
               address_postal_code AS "AddressPostalCode",
               address_country AS "AddressCountry",
               version AS "Version",
               created_at AS "CreatedAt",
               updated_at AS "UpdatedAt"
        """;
}
