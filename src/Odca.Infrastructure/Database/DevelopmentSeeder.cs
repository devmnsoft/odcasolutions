using Dapper;
using Npgsql;
using Odca.Application.Identity;

namespace Odca.Infrastructure.Database;

public sealed class DevelopmentSeeder(IPasswordService passwordService)
{
    public async Task SeedSuperAdministratorAsync(
        string adminConnectionString,
        string email,
        string password,
        CancellationToken cancellationToken = default)
    {
        var normalizedEmail = email.Trim().ToUpperInvariant();
        var template = new UserCredential(
            Guid.NewGuid(),
            email.Trim(),
            "Administrador da plataforma",
            string.Empty,
            1,
            true,
            true,
            null,
            false,
            null);
        var hash = passwordService.Hash(template, password);

        const string sql = """
            INSERT INTO odca.users
                (id, email, email_normalized, login_normalized, display_name, password_hash,
                 must_change_password, is_platform_administrator, email_verified_at)
            VALUES
                (@Id, @Email, @NormalizedEmail, @NormalizedEmail, @DisplayName, @Hash,
                 true, true, now())
            ON CONFLICT (email_normalized) DO NOTHING;
            """;

        await using var connection = new NpgsqlConnection(adminConnectionString);
        await connection.OpenAsync(cancellationToken);
        await connection.ExecuteAsync(new CommandDefinition(
            sql,
            new
            {
                template.Id,
                template.Email,
                NormalizedEmail = normalizedEmail,
                template.DisplayName,
                Hash = hash
            },
            cancellationToken: cancellationToken));
    }

    /// <summary>
    /// Explicit demo only. Seeds two organizations with representative contract scenarios.
    /// Does not run during migrate.
    /// </summary>
    public static async Task SeedContractsDemoAsync(
        string adminConnectionString,
        DateOnly? referenceDate = null,
        CancellationToken cancellationToken = default)
    {
        var today = referenceDate ?? DateOnly.FromDateTime(DateTime.UtcNow.Date);
        await using var connection = new NpgsqlConnection(adminConnectionString);
        await connection.OpenAsync(cancellationToken);
        await using var tx = await connection.BeginTransactionAsync(cancellationToken);

        var actorId = await connection.ExecuteScalarAsync<Guid?>(new CommandDefinition(
            """
            SELECT id FROM odca.users
             WHERE is_platform_administrator AND NOT is_deleted
             ORDER BY created_at
             LIMIT 1;
            """,
            transaction: tx,
            cancellationToken: cancellationToken));
        if (actorId is null)
        {
            throw new InvalidOperationException("É necessário um administrador de plataforma antes do seed de contratos.");
        }

        var orgA = await EnsureDemoTenantAsync(connection, tx, "ODCA-DEMO-CONTRACTS-A", "ODCA Contratos Demo A", actorId.Value, cancellationToken);
        var orgB = await EnsureDemoTenantAsync(connection, tx, "ODCA-DEMO-CONTRACTS-B", "ODCA Contratos Demo B", actorId.Value, cancellationToken);

        await SeedTenantScenarioAsync(connection, tx, orgA, actorId.Value, today, "A", cancellationToken);
        await SeedTenantScenarioAsync(connection, tx, orgB, actorId.Value, today, "B", cancellationToken);
        await tx.CommitAsync(cancellationToken);
    }

    private static async Task<Guid> EnsureDemoTenantAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction tx,
        string code,
        string name,
        Guid actorId,
        CancellationToken cancellationToken)
    {
        var tenantId = await connection.ExecuteScalarAsync<Guid>(new CommandDefinition(
            """
            INSERT INTO odca.tenants (business_code, display_name, status)
            VALUES (@code, @name, 'active')
            ON CONFLICT (business_code) DO UPDATE
              SET display_name = EXCLUDED.display_name,
                  updated_at = now()
            RETURNING id;
            """,
            new { code, name },
            tx,
            cancellationToken: cancellationToken));

        await connection.ExecuteAsync(new CommandDefinition(
            """
            INSERT INTO odca.memberships (tenant_id, user_id, status)
            VALUES (@tenantId, @actorId, 'active')
            ON CONFLICT (tenant_id, user_id) DO UPDATE SET status = 'active', updated_at = now();
            """,
            new { tenantId, actorId },
            tx,
            cancellationToken: cancellationToken));

        var roleId = await connection.ExecuteScalarAsync<Guid?>(new CommandDefinition(
            """
            SELECT id FROM odca.roles WHERE tenant_id = @tenantId AND code = 'tenant-administrator';
            """,
            new { tenantId },
            tx,
            cancellationToken: cancellationToken));
        if (roleId is null)
        {
            roleId = await connection.ExecuteScalarAsync<Guid>(new CommandDefinition(
                """
                INSERT INTO odca.roles (scope_type, tenant_id, code, display_name, is_system)
                VALUES ('tenant', @tenantId, 'tenant-administrator', 'Administrador', true)
                RETURNING id;
                """,
                new { tenantId },
                tx,
                cancellationToken: cancellationToken));
        }

        await connection.ExecuteAsync(new CommandDefinition(
            """
            INSERT INTO odca.role_permissions (role_id, permission_code)
            SELECT @roleId, p.code FROM odca.permissions p WHERE p.code LIKE 'tenant.%'
            ON CONFLICT DO NOTHING;
            """,
            new { roleId = roleId.Value },
            tx,
            cancellationToken: cancellationToken));

        await connection.ExecuteAsync(new CommandDefinition(
            """
            INSERT INTO odca.member_roles (tenant_id, user_id, role_id, assigned_by)
            VALUES (@tenantId, @actorId, @roleId, @actorId)
            ON CONFLICT DO NOTHING;
            """,
            new { tenantId, actorId, roleId = roleId.Value },
            tx,
            cancellationToken: cancellationToken));

        return tenantId;
    }

    private static async Task SeedTenantScenarioAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction tx,
        Guid tenantId,
        Guid actorId,
        DateOnly today,
        string suffix,
        CancellationToken cancellationToken)
    {
        await connection.ExecuteAsync(new CommandDefinition(
            "SELECT set_config('odca.tenant_id', @tenantId, true);",
            new { tenantId = tenantId.ToString() },
            tx,
            cancellationToken: cancellationToken));

        var typeId = await connection.ExecuteScalarAsync<Guid>(new CommandDefinition(
            """
            INSERT INTO odca.contract_types (tenant_id, code, name, description, guidance, is_system_demo, created_by)
            VALUES (@tenantId, @code, @name, 'Tipo demonstrativo', 'Uso exclusivo de demonstração.', true, @actorId)
            ON CONFLICT (tenant_id, code) DO UPDATE
              SET name = EXCLUDED.name,
                  updated_at = now(),
                  version = odca.contract_types.version + 1
            RETURNING id;
            """,
            new { tenantId, actorId, code = $"demo-service-{suffix.ToLowerInvariant()}", name = $"Prestação de serviços {suffix}" },
            tx,
            cancellationToken: cancellationToken));

        var counterpartyDoc = suffix == "A" ? ("cnpj", "11222333000181") : ("cpf", "39053344705");
        var counterpartyId = await connection.ExecuteScalarAsync<Guid?>(new CommandDefinition(
            """
            SELECT id FROM odca.counterparties
             WHERE tenant_id = @tenantId
               AND document_type = @documentType
               AND document_normalized = @documentNormalized
               AND NOT is_deleted
             LIMIT 1;
            """,
            new { tenantId, documentType = counterpartyDoc.Item1, documentNormalized = counterpartyDoc.Item2 },
            tx,
            cancellationToken: cancellationToken));
        if (counterpartyId is null)
        {
            counterpartyId = await connection.ExecuteScalarAsync<Guid>(new CommandDefinition(
                """
                INSERT INTO odca.counterparties (
                    tenant_id, person_type, legal_name, display_name, document_type, document_normalized, document_display,
                    is_client, is_supplier, created_by)
                VALUES (
                    @tenantId, @personType, @legalName, @displayName, @documentType, @documentNormalized, @documentDisplay,
                    true, false, @actorId)
                RETURNING id;
                """,
                new
                {
                    tenantId,
                    actorId,
                    personType = counterpartyDoc.Item1 == "cnpj" ? "organization" : "individual",
                    legalName = $"Contraparte Demo {suffix}",
                    displayName = $"Contraparte Demo {suffix}",
                    documentType = counterpartyDoc.Item1,
                    documentNormalized = counterpartyDoc.Item2,
                    documentDisplay = counterpartyDoc.Item2
                },
                tx,
                cancellationToken: cancellationToken));
        }

        var cp = counterpartyId.Value;
        await UpsertContractAsync(connection, tx, tenantId, actorId, typeId, cp, $"draft-{suffix}", "Rascunho demo", "draft", today.AddDays(10), today.AddMonths(6), false, null, cancellationToken);
        await UpsertContractAsync(connection, tx, tenantId, actorId, typeId, cp, $"active-{suffix}", "Ativo demo", "active", today.AddMonths(-2), today.AddMonths(4), false, actorId, cancellationToken);
        await UpsertContractAsync(connection, tx, tenantId, actorId, typeId, cp, $"approaching-{suffix}", "Aproximando fim demo", "active", today.AddMonths(-10), today.AddDays(20), false, actorId, cancellationToken);
        await UpsertContractAsync(connection, tx, tenantId, actorId, typeId, cp, $"expired-{suffix}", "Expirado demo", "active", today.AddYears(-1), today.AddDays(-5), false, actorId, cancellationToken);
        await UpsertContractAsync(connection, tx, tenantId, actorId, typeId, cp, $"indefinite-{suffix}", "Indeterminado demo", "active", today.AddMonths(-1), null, true, actorId, cancellationToken);
        await UpsertContractAsync(connection, tx, tenantId, actorId, typeId, cp, $"closed-{suffix}", "Encerrado demo", "closed", today.AddMonths(-8), today.AddMonths(-1), false, actorId, cancellationToken);
        await UpsertContractAsync(connection, tx, tenantId, actorId, typeId, cp, $"inactive-{suffix}", "Inativo demo", "inactive", today.AddMonths(-3), today.AddMonths(3), false, actorId, cancellationToken);

        var renewedId = await UpsertContractAsync(connection, tx, tenantId, actorId, typeId, cp, $"renewed-{suffix}", "Renovado demo", "active", today.AddMonths(-14), today.AddMonths(6), false, actorId, cancellationToken, termCycle: 2);
        await connection.ExecuteAsync(new CommandDefinition(
            """
            INSERT INTO odca.contract_renewals (
                tenant_id, contract_id, previous_end_date, new_end_date, previous_term_cycle, new_term_cycle, reason, created_by)
            SELECT @tenantId, @contractId, @previousEnd, @newEnd, 1, 2, 'Renovação demonstrativa', @actorId
             WHERE NOT EXISTS (
               SELECT 1 FROM odca.contract_renewals
                WHERE tenant_id = @tenantId AND contract_id = @contractId AND new_term_cycle = 2);
            """,
            new
            {
                tenantId,
                contractId = renewedId,
                previousEnd = today.AddMonths(-2),
                newEnd = today.AddMonths(6),
                actorId
            },
            tx,
            cancellationToken: cancellationToken));
    }

    private static async Task<Guid> UpsertContractAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction tx,
        Guid tenantId,
        Guid actorId,
        Guid typeId,
        Guid counterpartyId,
        string reference,
        string title,
        string status,
        DateOnly start,
        DateOnly? end,
        bool indefinite,
        Guid? ownerId,
        CancellationToken cancellationToken,
        int termCycle = 1)
    {
        var id = await connection.ExecuteScalarAsync<Guid?>(new CommandDefinition(
            """
            SELECT id FROM odca.contracts
             WHERE tenant_id = @tenantId AND reference_number = @reference AND NOT is_deleted;
            """,
            new { tenantId, reference },
            tx,
            cancellationToken: cancellationToken));

        if (id is null)
        {
            id = Guid.NewGuid();
            await connection.ExecuteAsync(new CommandDefinition(
                """
                INSERT INTO odca.contracts (
                    id, tenant_id, reference_number, title, summary, type_id, primary_counterparty_id, owner_user_id,
                    start_date, end_date, is_indefinite, renewal_notice_days, operational_status, term_cycle, created_by,
                    closed_at, closed_by)
                VALUES (
                    @id, @tenantId, @reference, @title, @summary, @typeId, @counterpartyId, @ownerId,
                    @start, @end, @indefinite, 30, @status, @termCycle, @actorId,
                    CASE WHEN @status = 'closed' THEN now() ELSE NULL END,
                    CASE WHEN @status = 'closed' THEN @actorId ELSE NULL END);
                """,
                new
                {
                    id,
                    tenantId,
                    reference,
                    title,
                    summary = $"Cenário {reference}",
                    typeId,
                    counterpartyId,
                    ownerId,
                    start,
                    end,
                    indefinite,
                    status,
                    termCycle,
                    actorId
                },
                tx,
                cancellationToken: cancellationToken));

            await connection.ExecuteAsync(new CommandDefinition(
                """
                INSERT INTO odca.contract_parties (contract_id, tenant_id, counterparty_id, role)
                VALUES (@id, @tenantId, @counterpartyId, 'primary')
                ON CONFLICT DO NOTHING;
                """,
                new { id, tenantId, counterpartyId },
                tx,
                cancellationToken: cancellationToken));

            await connection.ExecuteAsync(new CommandDefinition(
                """
                INSERT INTO odca.contract_events (tenant_id, contract_id, action, actor_user_id, metadata)
                VALUES (@tenantId, @id, 'contract.seeded', @actorId, jsonb_build_object('reference', @reference));
                """,
                new { tenantId, id, actorId, reference },
                tx,
                cancellationToken: cancellationToken));
        }

        return id.Value;
    }
}
