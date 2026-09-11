using System.Text.Json;
using Dapper;
using Npgsql;
using Odca.Application.Contracts;
using Odca.Application.Tenancy;
using Odca.Domain.Contracts;

namespace Odca.Infrastructure.Contracts;

public sealed class NpgsqlContractRepository(NpgsqlDataSource dataSource) : IContractRepository
{
    public async Task<QueryAccess<TenantPage<ContractListItem>>> ListAsync(
        Guid actorId,
        Guid tenantId,
        ContractListFilter filter,
        int page,
        int pageSize,
        CancellationToken cancellationToken)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var tx = await TenantSql.BeginAuthorizedAsync(connection, actorId, tenantId, "tenant.contracts.read", cancellationToken);
        if (tx is null)
        {
            return new QueryAccess<TenantPage<ContractListItem>>(QueryAccessStatus.Forbidden, null);
        }

        var rows = (await connection.QueryAsync<ContractListRow>(new CommandDefinition(
            """
            SELECT c.id AS "Id",
                   c.reference_number AS "ReferenceNumber",
                   c.title AS "Title",
                   c.type_id AS "TypeId",
                   t.name AS "TypeName",
                   c.primary_counterparty_id AS "PrimaryCounterpartyId",
                   cp.display_name AS "PrimaryCounterpartyName",
                   c.owner_user_id AS "OwnerUserId",
                   u.display_name AS "OwnerName",
                   c.start_date AS "StartDate",
                   c.end_date AS "EndDate",
                   c.is_indefinite AS "IsIndefinite",
                   c.operational_status AS "OperationalStatus",
                   c.term_cycle AS "TermCycle",
                   c.version AS "Version",
                   c.updated_at AS "UpdatedAt"
              FROM odca.contracts c
              JOIN odca.contract_types t ON t.tenant_id = c.tenant_id AND t.id = c.type_id
              JOIN odca.counterparties cp ON cp.tenant_id = c.tenant_id AND cp.id = c.primary_counterparty_id
              LEFT JOIN odca.users u ON u.id = c.owner_user_id
             WHERE c.tenant_id = @tenantId
               AND NOT c.is_deleted
               AND (@title IS NULL OR c.title ILIKE '%' || @title || '%')
               AND (@counterpartyId IS NULL OR c.primary_counterparty_id = @counterpartyId
                    OR EXISTS (
                        SELECT 1 FROM odca.contract_parties p
                         WHERE p.tenant_id = c.tenant_id AND p.contract_id = c.id AND p.counterparty_id = @counterpartyId))
               AND (@typeId IS NULL OR c.type_id = @typeId)
               AND (@ownerUserId IS NULL OR c.owner_user_id = @ownerUserId)
               AND (@operationalStatus IS NULL OR c.operational_status = @operationalStatus)
               AND (@endFrom IS NULL OR c.end_date >= @endFrom)
               AND (@endTo IS NULL OR c.end_date <= @endTo)
               AND (NOT @mine OR c.owner_user_id = @actorId)
               AND (NOT @unassigned OR c.owner_user_id IS NULL)
             ORDER BY c.updated_at DESC, c.title;
            """,
            new
            {
                tenantId,
                actorId,
                title = string.IsNullOrWhiteSpace(filter.Title) ? null : filter.Title.Trim(),
                counterpartyId = filter.CounterpartyId,
                typeId = filter.TypeId,
                ownerUserId = filter.OwnerUserId,
                operationalStatus = filter.OperationalStatus,
                endFrom = filter.EndFrom,
                endTo = filter.EndTo,
                mine = filter.Mine,
                unassigned = filter.Unassigned
            },
            tx,
            cancellationToken: cancellationToken))).AsList();

        var projected = rows
            .Select(row =>
            {
                var temporal = ContractLifecycle.DeriveTemporalStatus(
                    ParseOperational(row.OperationalStatus),
                    row.StartDate,
                    row.EndDate,
                    row.IsIndefinite,
                    filter.ReferenceDate);
                return new ContractListItem(
                    row.Id,
                    row.ReferenceNumber,
                    row.Title,
                    row.TypeId,
                    row.TypeName,
                    row.PrimaryCounterpartyId,
                    row.PrimaryCounterpartyName,
                    row.OwnerUserId,
                    row.OwnerName,
                    row.StartDate,
                    row.EndDate,
                    row.IsIndefinite,
                    row.OperationalStatus,
                    temporal.ToString(),
                    row.TermCycle,
                    row.Version,
                    row.UpdatedAt);
            })
            .Where(item =>
            {
                if (!string.IsNullOrWhiteSpace(filter.TemporalStatus) &&
                    !string.Equals(item.TemporalStatus, filter.TemporalStatus, StringComparison.OrdinalIgnoreCase))
                {
                    return false;
                }

                if (filter.Approaching &&
                    !string.Equals(item.TemporalStatus, nameof(ContractTemporalStatus.ApproachingEnd), StringComparison.OrdinalIgnoreCase))
                {
                    return false;
                }

                return true;
            })
            .ToList();

        var total = projected.Count;
        var pageItems = projected.Skip(Pagination.Offset(page, pageSize)).Take(pageSize).ToList();
        await tx.CommitAsync(cancellationToken);
        return new QueryAccess<TenantPage<ContractListItem>>(QueryAccessStatus.Ok, new TenantPage<ContractListItem>(pageItems, total, page, pageSize));
    }

    public async Task<QueryAccess<ContractDetail?>> GetAsync(
        Guid actorId,
        Guid tenantId,
        Guid id,
        DateOnly referenceDate,
        CancellationToken cancellationToken)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var tx = await TenantSql.BeginAuthorizedAsync(connection, actorId, tenantId, "tenant.contracts.read", cancellationToken);
        if (tx is null)
        {
            return new QueryAccess<ContractDetail?>(QueryAccessStatus.Forbidden, null);
        }

        var detail = await LoadDetailAsync(connection, tx, tenantId, id, referenceDate, cancellationToken);
        await tx.CommitAsync(cancellationToken);
        return new QueryAccess<ContractDetail?>(QueryAccessStatus.Ok, detail);
    }

    public async Task<QueryAccess<ContractOverviewMetrics>> OverviewAsync(
        Guid actorId,
        Guid tenantId,
        DateOnly referenceDate,
        CancellationToken cancellationToken)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var tx = await TenantSql.BeginAuthorizedAsync(connection, actorId, tenantId, "tenant.contracts.read", cancellationToken);
        if (tx is null)
        {
            return new QueryAccess<ContractOverviewMetrics>(QueryAccessStatus.Forbidden, null);
        }

        var rows = (await connection.QueryAsync<ContractListRow>(new CommandDefinition(
            """
            SELECT c.id AS "Id",
                   c.reference_number AS "ReferenceNumber",
                   c.title AS "Title",
                   c.type_id AS "TypeId",
                   '' AS "TypeName",
                   c.primary_counterparty_id AS "PrimaryCounterpartyId",
                   '' AS "PrimaryCounterpartyName",
                   c.owner_user_id AS "OwnerUserId",
                   NULL::text AS "OwnerName",
                   c.start_date AS "StartDate",
                   c.end_date AS "EndDate",
                   c.is_indefinite AS "IsIndefinite",
                   c.operational_status AS "OperationalStatus",
                   c.term_cycle AS "TermCycle",
                   c.version AS "Version",
                   c.updated_at AS "UpdatedAt"
              FROM odca.contracts c
             WHERE c.tenant_id = @tenantId
               AND NOT c.is_deleted;
            """,
            new { tenantId },
            tx,
            cancellationToken: cancellationToken))).AsList();

        var draft = 0;
        var active = 0;
        var approaching = 0;
        var expired = 0;
        var indefinite = 0;
        var closed = 0;
        var unassigned = 0;
        foreach (var row in rows)
        {
            if (row.OwnerUserId is null)
            {
                unassigned++;
            }

            switch (row.OperationalStatus)
            {
                case "draft":
                    draft++;
                    break;
                case "closed":
                    closed++;
                    break;
            }

            if (row.OperationalStatus == "active")
            {
                active++;
                var temporal = ContractLifecycle.DeriveTemporalStatus(
                    ContractOperationalStatus.Active,
                    row.StartDate,
                    row.EndDate,
                    row.IsIndefinite,
                    referenceDate);
                switch (temporal)
                {
                    case ContractTemporalStatus.ApproachingEnd:
                        approaching++;
                        break;
                    case ContractTemporalStatus.Expired:
                        expired++;
                        break;
                    case ContractTemporalStatus.Indefinite:
                        indefinite++;
                        break;
                }
            }
        }

        await tx.CommitAsync(cancellationToken);
        return new QueryAccess<ContractOverviewMetrics>(
            QueryAccessStatus.Ok,
            new ContractOverviewMetrics(draft, active, approaching, expired, indefinite, closed, unassigned));
    }

    public async Task<MutationResult<ContractDetail>> CreateDraftAsync(
        Guid actorId,
        Guid tenantId,
        ContractWriteModel model,
        DateOnly referenceDate,
        CancellationToken cancellationToken)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var tx = await TenantSql.BeginAuthorizedAsync(connection, actorId, tenantId, "tenant.contracts.manage", cancellationToken);
        if (tx is null)
        {
            return new MutationResult<ContractDetail>(MutationStatus.Forbidden);
        }

        var refsOk = await ValidateReferencesAsync(connection, tx, tenantId, model, cancellationToken);
        if (refsOk is not null)
        {
            await tx.RollbackAsync(cancellationToken);
            return new MutationResult<ContractDetail>(MutationStatus.ValidationFailed, ErrorCode: refsOk);
        }

        var id = Guid.NewGuid();
        await connection.ExecuteAsync(new CommandDefinition(
            """
            INSERT INTO odca.contracts (
                id, tenant_id, reference_number, title, summary, type_id, primary_counterparty_id, owner_user_id,
                start_date, end_date, is_indefinite, amount, currency, amount_periodicity, renewal_notice_days,
                renewal_decision, operational_status, created_by)
            VALUES (
                @id, @tenantId, @ReferenceNumber, @Title, @Summary, @TypeId, @PrimaryCounterpartyId, @OwnerUserId,
                @StartDate, @EndDate, @IsIndefinite, @Amount, @Currency, @AmountPeriodicity, @RenewalNoticeDays,
                @RenewalDecision, 'draft', @actorId);
            """,
            Bind(model, id, tenantId, actorId),
            tx,
            cancellationToken: cancellationToken));

        await ReplacePartiesAsync(connection, tx, tenantId, id, model, cancellationToken);
        await InsertEventAsync(connection, tx, tenantId, id, actorId, "contract.created", new { status = "draft" }, cancellationToken);

        var detail = await LoadDetailAsync(connection, tx, tenantId, id, referenceDate, cancellationToken);
        await tx.CommitAsync(cancellationToken);
        return new MutationResult<ContractDetail>(MutationStatus.Succeeded, detail);
    }

    public async Task<MutationResult<ContractDetail>> UpdateAsync(
        Guid actorId,
        Guid tenantId,
        Guid id,
        long version,
        ContractWriteModel model,
        DateOnly referenceDate,
        CancellationToken cancellationToken)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var tx = await TenantSql.BeginAuthorizedAsync(connection, actorId, tenantId, "tenant.contracts.manage", cancellationToken);
        if (tx is null)
        {
            return new MutationResult<ContractDetail>(MutationStatus.Forbidden);
        }

        var current = await connection.QuerySingleOrDefaultAsync<(string? Status, bool IsDeleted)>(new CommandDefinition(
            "SELECT operational_status AS Status, is_deleted AS IsDeleted FROM odca.contracts WHERE tenant_id=@tenantId AND id=@id;",
            new { tenantId, id },
            tx,
            cancellationToken: cancellationToken));
        if (current.Status is null)
        {
            await tx.RollbackAsync(cancellationToken);
            return new MutationResult<ContractDetail>(MutationStatus.NotFound);
        }

        if (current.IsDeleted || !ContractLifecycle.CanEditContent(ParseOperational(current.Status)))
        {
            await tx.RollbackAsync(cancellationToken);
            return new MutationResult<ContractDetail>(MutationStatus.Conflict, ErrorCode: "not_editable");
        }

        var refsOk = await ValidateReferencesAsync(connection, tx, tenantId, model, cancellationToken);
        if (refsOk is not null)
        {
            await tx.RollbackAsync(cancellationToken);
            return new MutationResult<ContractDetail>(MutationStatus.ValidationFailed, ErrorCode: refsOk);
        }

        var updated = await connection.ExecuteAsync(new CommandDefinition(
            """
            UPDATE odca.contracts
               SET reference_number = @ReferenceNumber,
                   title = @Title,
                   summary = @Summary,
                   type_id = @TypeId,
                   primary_counterparty_id = @PrimaryCounterpartyId,
                   owner_user_id = @OwnerUserId,
                   start_date = @StartDate,
                   end_date = @EndDate,
                   is_indefinite = @IsIndefinite,
                   amount = @Amount,
                   currency = @Currency,
                   amount_periodicity = @AmountPeriodicity,
                   renewal_notice_days = @RenewalNoticeDays,
                   renewal_decision = @RenewalDecision,
                   version = version + 1,
                   updated_at = now()
             WHERE tenant_id = @tenantId
               AND id = @id
               AND version = @version
               AND NOT is_deleted;
            """,
            Bind(model, id, tenantId, actorId, version),
            tx,
            cancellationToken: cancellationToken));
        if (updated == 0)
        {
            await tx.RollbackAsync(cancellationToken);
            return new MutationResult<ContractDetail>(MutationStatus.Conflict, ErrorCode: "version_conflict");
        }

        await ReplacePartiesAsync(connection, tx, tenantId, id, model, cancellationToken);
        await InsertEventAsync(connection, tx, tenantId, id, actorId, "contract.updated", new { version = version + 1 }, cancellationToken);
        var detail = await LoadDetailAsync(connection, tx, tenantId, id, referenceDate, cancellationToken);
        await tx.CommitAsync(cancellationToken);
        return new MutationResult<ContractDetail>(MutationStatus.Succeeded, detail);
    }

    public Task<MutationResult> ActivateAsync(Guid actorId, Guid tenantId, Guid id, long version, CancellationToken cancellationToken)
        => TransitionAsync(actorId, tenantId, id, version, "tenant.contracts.lifecycle", ContractOperationalStatus.Active, "contract.activated", cancellationToken);

    public async Task<MutationResult> RenewAsync(
        Guid actorId,
        Guid tenantId,
        Guid id,
        long version,
        DateOnly newEndDate,
        string? reason,
        CancellationToken cancellationToken)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var tx = await TenantSql.BeginAuthorizedAsync(connection, actorId, tenantId, "tenant.contracts.lifecycle", cancellationToken);
        if (tx is null)
        {
            return new MutationResult(MutationStatus.Forbidden);
        }

        var current = await connection.QuerySingleOrDefaultAsync<RenewalState>(new CommandDefinition(
            """
            SELECT operational_status AS "OperationalStatus",
                   end_date AS "EndDate",
                   is_indefinite AS "IsIndefinite",
                   term_cycle AS "TermCycle",
                   is_deleted AS "IsDeleted"
              FROM odca.contracts
             WHERE tenant_id = @tenantId AND id = @id;
            """,
            new { tenantId, id },
            tx,
            cancellationToken: cancellationToken));
        if (current is null || current.IsDeleted)
        {
            await tx.RollbackAsync(cancellationToken);
            return new MutationResult(MutationStatus.NotFound);
        }

        if (!ContractLifecycle.CanRenew(ParseOperational(current.OperationalStatus), current.IsIndefinite) ||
            current.EndDate is null ||
            newEndDate <= current.EndDate)
        {
            await tx.RollbackAsync(cancellationToken);
            return new MutationResult(MutationStatus.Conflict, "renewal_invalid");
        }

        var newCycle = current.TermCycle + 1;
        var updated = await connection.ExecuteAsync(new CommandDefinition(
            """
            UPDATE odca.contracts
               SET end_date = @newEndDate,
                   term_cycle = @newCycle,
                   version = version + 1,
                   updated_at = now()
             WHERE tenant_id = @tenantId
               AND id = @id
               AND version = @version
               AND NOT is_deleted
               AND operational_status = 'active';
            """,
            new { tenantId, id, version, newEndDate, newCycle },
            tx,
            cancellationToken: cancellationToken));
        if (updated == 0)
        {
            await tx.RollbackAsync(cancellationToken);
            return new MutationResult(MutationStatus.Conflict, "version_conflict");
        }

        await connection.ExecuteAsync(new CommandDefinition(
            """
            INSERT INTO odca.contract_renewals (
                tenant_id, contract_id, previous_end_date, new_end_date, previous_term_cycle, new_term_cycle, reason, created_by)
            VALUES (@tenantId, @id, @previousEnd, @newEndDate, @previousCycle, @newCycle, @reason, @actorId);
            """,
            new
            {
                tenantId,
                id,
                previousEnd = current.EndDate,
                newEndDate,
                previousCycle = current.TermCycle,
                newCycle,
                reason = string.IsNullOrWhiteSpace(reason) ? null : reason.Trim(),
                actorId
            },
            tx,
            cancellationToken: cancellationToken));

        await connection.ExecuteAsync(new CommandDefinition(
            """
            UPDATE odca.user_notifications
               SET status = 'obsolete',
                   is_obsolete = true
             WHERE tenant_id = @tenantId
               AND resource_type = 'contract'
               AND resource_id = @id
               AND status = 'open'
               AND event_key LIKE @prefix;
            """,
            new { tenantId, id, prefix = $"{id:D}:{current.TermCycle}:%" },
            tx,
            cancellationToken: cancellationToken));

        await InsertEventAsync(connection, tx, tenantId, id, actorId, "contract.renewed", new { previousCycle = current.TermCycle, newCycle, newEndDate }, cancellationToken);
        await tx.CommitAsync(cancellationToken);
        return new MutationResult(MutationStatus.Succeeded);
    }

    public Task<MutationResult> CloseAsync(Guid actorId, Guid tenantId, Guid id, long version, CancellationToken cancellationToken)
        => TransitionAsync(actorId, tenantId, id, version, "tenant.contracts.lifecycle", ContractOperationalStatus.Closed, "contract.closed", cancellationToken, markClosed: true);

    public Task<MutationResult> CancelAsync(Guid actorId, Guid tenantId, Guid id, long version, CancellationToken cancellationToken)
        => TransitionAsync(actorId, tenantId, id, version, "tenant.contracts.lifecycle", ContractOperationalStatus.Cancelled, "contract.cancelled", cancellationToken, markCancelled: true);

    public async Task<MutationResult> SoftDeleteAsync(
        Guid actorId,
        Guid tenantId,
        Guid id,
        long version,
        string reason,
        CancellationToken cancellationToken)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var tx = await TenantSql.BeginAuthorizedAsync(connection, actorId, tenantId, "tenant.contracts.manage", cancellationToken);
        if (tx is null)
        {
            return new MutationResult(MutationStatus.Forbidden);
        }

        var updated = await connection.ExecuteAsync(new CommandDefinition(
            """
            UPDATE odca.contracts
               SET is_deleted = true,
                   deleted_at = now(),
                   deleted_by = @actorId,
                   deletion_reason = @reason,
                   version = version + 1,
                   updated_at = now()
             WHERE tenant_id = @tenantId
               AND id = @id
               AND version = @version
               AND NOT is_deleted;
            """,
            new { tenantId, id, version, actorId, reason },
            tx,
            cancellationToken: cancellationToken));
        if (updated == 0)
        {
            await tx.RollbackAsync(cancellationToken);
            return new MutationResult(MutationStatus.Conflict);
        }

        await ObsoleteNotificationsAsync(connection, tx, tenantId, id, cancellationToken);
        await InsertEventAsync(connection, tx, tenantId, id, actorId, "contract.deleted", new { reason }, cancellationToken);
        await tx.CommitAsync(cancellationToken);
        return new MutationResult(MutationStatus.Succeeded);
    }

    public async Task<MutationResult> RestoreAsync(
        Guid actorId,
        Guid tenantId,
        Guid id,
        long version,
        CancellationToken cancellationToken)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var tx = await TenantSql.BeginAuthorizedAsync(connection, actorId, tenantId, "tenant.contracts.manage", cancellationToken);
        if (tx is null)
        {
            return new MutationResult(MutationStatus.Forbidden);
        }

        var updated = await connection.ExecuteAsync(new CommandDefinition(
            """
            UPDATE odca.contracts
               SET is_deleted = false,
                   deleted_at = NULL,
                   deleted_by = NULL,
                   deletion_reason = NULL,
                   version = version + 1,
                   updated_at = now()
             WHERE tenant_id = @tenantId
               AND id = @id
               AND version = @version
               AND is_deleted;
            """,
            new { tenantId, id, version },
            tx,
            cancellationToken: cancellationToken));
        if (updated == 0)
        {
            await tx.RollbackAsync(cancellationToken);
            return new MutationResult(MutationStatus.Conflict);
        }

        await InsertEventAsync(connection, tx, tenantId, id, actorId, "contract.restored", new { }, cancellationToken);
        await tx.CommitAsync(cancellationToken);
        return new MutationResult(MutationStatus.Succeeded);
    }

    private async Task<MutationResult> TransitionAsync(
        Guid actorId,
        Guid tenantId,
        Guid id,
        long version,
        string permission,
        ContractOperationalStatus target,
        string eventAction,
        CancellationToken cancellationToken,
        bool markClosed = false,
        bool markCancelled = false)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var tx = await TenantSql.BeginAuthorizedAsync(connection, actorId, tenantId, permission, cancellationToken);
        if (tx is null)
        {
            return new MutationResult(MutationStatus.Forbidden);
        }

        var current = await connection.QuerySingleOrDefaultAsync<(string? Status, bool IsDeleted)>(new CommandDefinition(
            "SELECT operational_status AS Status, is_deleted AS IsDeleted FROM odca.contracts WHERE tenant_id=@tenantId AND id=@id;",
            new { tenantId, id },
            tx,
            cancellationToken: cancellationToken));
        if (current.Status is null || current.IsDeleted)
        {
            await tx.RollbackAsync(cancellationToken);
            return new MutationResult(MutationStatus.NotFound);
        }

        if (!ContractLifecycle.CanTransition(ParseOperational(current.Status), target))
        {
            await tx.RollbackAsync(cancellationToken);
            return new MutationResult(MutationStatus.Conflict, "transition_invalid");
        }

        var updated = await connection.ExecuteAsync(new CommandDefinition(
            """
            UPDATE odca.contracts
               SET operational_status = @status,
                   closed_at = CASE WHEN @markClosed THEN now() ELSE closed_at END,
                   closed_by = CASE WHEN @markClosed THEN @actorId ELSE closed_by END,
                   cancelled_at = CASE WHEN @markCancelled THEN now() ELSE cancelled_at END,
                   cancelled_by = CASE WHEN @markCancelled THEN @actorId ELSE cancelled_by END,
                   version = version + 1,
                   updated_at = now()
             WHERE tenant_id = @tenantId
               AND id = @id
               AND version = @version
               AND NOT is_deleted;
            """,
            new
            {
                tenantId,
                id,
                version,
                actorId,
                status = ContractOperationalStatusParser.ToStorage(target),
                markClosed,
                markCancelled
            },
            tx,
            cancellationToken: cancellationToken));
        if (updated == 0)
        {
            await tx.RollbackAsync(cancellationToken);
            return new MutationResult(MutationStatus.Conflict, "version_conflict");
        }

        if (!ContractLifecycle.IsMonitoredForAlerts(target))
        {
            await ObsoleteNotificationsAsync(connection, tx, tenantId, id, cancellationToken);
        }

        await InsertEventAsync(connection, tx, tenantId, id, actorId, eventAction, new { status = ContractOperationalStatusParser.ToStorage(target) }, cancellationToken);
        await tx.CommitAsync(cancellationToken);
        return new MutationResult(MutationStatus.Succeeded);
    }

    private static async Task<string?> ValidateReferencesAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction tx,
        Guid tenantId,
        ContractWriteModel model,
        CancellationToken cancellationToken)
    {
        var typeOk = await connection.ExecuteScalarAsync<bool>(new CommandDefinition(
            """
            SELECT EXISTS(
              SELECT 1 FROM odca.contract_types
               WHERE tenant_id = @tenantId AND id = @typeId AND status = 'active');
            """,
            new { tenantId, typeId = model.TypeId },
            tx,
            cancellationToken: cancellationToken));
        if (!typeOk)
        {
            return "type_invalid";
        }

        var counterpartyOk = await connection.ExecuteScalarAsync<bool>(new CommandDefinition(
            """
            SELECT EXISTS(
              SELECT 1 FROM odca.counterparties
               WHERE tenant_id = @tenantId AND id = @id AND NOT is_deleted AND status = 'active');
            """,
            new { tenantId, id = model.PrimaryCounterpartyId },
            tx,
            cancellationToken: cancellationToken));
        if (!counterpartyOk)
        {
            return "counterparty_invalid";
        }

        if (model.OwnerUserId is Guid ownerId)
        {
            var ownerOk = await connection.ExecuteScalarAsync<bool>(new CommandDefinition(
                """
                SELECT EXISTS(
                  SELECT 1 FROM odca.memberships
                   WHERE tenant_id = @tenantId AND user_id = @ownerId AND status = 'active');
                """,
                new { tenantId, ownerId },
                tx,
                cancellationToken: cancellationToken));
            if (!ownerOk)
            {
                return "owner_invalid";
            }
        }

        if (model.AdditionalCounterpartyIds is { Count: > 0 })
        {
            var count = await connection.ExecuteScalarAsync<int>(new CommandDefinition(
                """
                SELECT count(*)::int
                  FROM odca.counterparties
                 WHERE tenant_id = @tenantId
                   AND id = ANY(@ids)
                   AND NOT is_deleted
                   AND status = 'active';
                """,
                new { tenantId, ids = model.AdditionalCounterpartyIds.ToArray() },
                tx,
                cancellationToken: cancellationToken));
            if (count != model.AdditionalCounterpartyIds.Count)
            {
                return "additional_counterparty_invalid";
            }
        }

        return null;
    }

    private static async Task ReplacePartiesAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction tx,
        Guid tenantId,
        Guid contractId,
        ContractWriteModel model,
        CancellationToken cancellationToken)
    {
        await connection.ExecuteAsync(new CommandDefinition(
            "DELETE FROM odca.contract_parties WHERE tenant_id = @tenantId AND contract_id = @contractId;",
            new { tenantId, contractId },
            tx,
            cancellationToken: cancellationToken));

        await connection.ExecuteAsync(new CommandDefinition(
            """
            INSERT INTO odca.contract_parties (contract_id, tenant_id, counterparty_id, role)
            VALUES (@contractId, @tenantId, @counterpartyId, 'primary');
            """,
            new { contractId, tenantId, counterpartyId = model.PrimaryCounterpartyId },
            tx,
            cancellationToken: cancellationToken));

        if (model.AdditionalCounterpartyIds is null)
        {
            return;
        }

        foreach (var additional in model.AdditionalCounterpartyIds)
        {
            await connection.ExecuteAsync(new CommandDefinition(
                """
                INSERT INTO odca.contract_parties (contract_id, tenant_id, counterparty_id, role)
                VALUES (@contractId, @tenantId, @counterpartyId, 'additional');
                """,
                new { contractId, tenantId, counterpartyId = additional },
                tx,
                cancellationToken: cancellationToken));
        }
    }

    private static async Task InsertEventAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction tx,
        Guid tenantId,
        Guid contractId,
        Guid actorId,
        string action,
        object metadata,
        CancellationToken cancellationToken)
    {
        await connection.ExecuteAsync(new CommandDefinition(
            """
            INSERT INTO odca.contract_events (tenant_id, contract_id, action, actor_user_id, metadata)
            VALUES (@tenantId, @contractId, @action, @actorId, CAST(@metadata AS jsonb));
            """,
            new
            {
                tenantId,
                contractId,
                action,
                actorId,
                metadata = JsonSerializer.Serialize(metadata)
            },
            tx,
            cancellationToken: cancellationToken));
    }

    private static async Task ObsoleteNotificationsAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction tx,
        Guid tenantId,
        Guid contractId,
        CancellationToken cancellationToken)
    {
        _ = await connection.ExecuteAsync(new CommandDefinition(
            """
            UPDATE odca.user_notifications
               SET status = 'obsolete',
                   is_obsolete = true
             WHERE tenant_id = @tenantId
               AND resource_type = 'contract'
               AND resource_id = @contractId
               AND status = 'open';
            """,
            new { tenantId, contractId },
            tx,
            cancellationToken: cancellationToken));
    }

    private static async Task<ContractDetail?> LoadDetailAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction tx,
        Guid tenantId,
        Guid id,
        DateOnly referenceDate,
        CancellationToken cancellationToken)
    {
        var header = await connection.QuerySingleOrDefaultAsync<ContractHeaderRow>(new CommandDefinition(
            """
            SELECT c.id AS "Id",
                   c.reference_number AS "ReferenceNumber",
                   c.title AS "Title",
                   c.summary AS "Summary",
                   c.type_id AS "TypeId",
                   t.name AS "TypeName",
                   c.primary_counterparty_id AS "PrimaryCounterpartyId",
                   cp.display_name AS "PrimaryCounterpartyName",
                   c.owner_user_id AS "OwnerUserId",
                   u.display_name AS "OwnerName",
                   c.start_date AS "StartDate",
                   c.end_date AS "EndDate",
                   c.is_indefinite AS "IsIndefinite",
                   c.amount AS "Amount",
                   c.currency AS "Currency",
                   c.amount_periodicity AS "AmountPeriodicity",
                   c.renewal_notice_days AS "RenewalNoticeDays",
                   c.renewal_decision AS "RenewalDecision",
                   c.operational_status AS "OperationalStatus",
                   c.term_cycle AS "TermCycle",
                   c.version AS "Version",
                   c.created_at AS "CreatedAt",
                   c.updated_at AS "UpdatedAt",
                   c.closed_at AS "ClosedAt",
                   c.cancelled_at AS "CancelledAt",
                   c.is_deleted AS "IsDeleted"
              FROM odca.contracts c
              JOIN odca.contract_types t ON t.tenant_id = c.tenant_id AND t.id = c.type_id
              JOIN odca.counterparties cp ON cp.tenant_id = c.tenant_id AND cp.id = c.primary_counterparty_id
              LEFT JOIN odca.users u ON u.id = c.owner_user_id
             WHERE c.tenant_id = @tenantId AND c.id = @id;
            """,
            new { tenantId, id },
            tx,
            cancellationToken: cancellationToken));
        if (header is null || header.IsDeleted)
        {
            return null;
        }

        var parties = (await connection.QueryAsync<ContractPartyRecord>(new CommandDefinition(
            """
            SELECT p.counterparty_id AS "CounterpartyId",
                   c.display_name AS "CounterpartyName",
                   p.role AS "Role"
              FROM odca.contract_parties p
              JOIN odca.counterparties c ON c.tenant_id = p.tenant_id AND c.id = p.counterparty_id
             WHERE p.tenant_id = @tenantId AND p.contract_id = @id
             ORDER BY CASE p.role WHEN 'primary' THEN 0 WHEN 'additional' THEN 1 WHEN 'guarantor' THEN 2 ELSE 3 END, c.display_name;
            """,
            new { tenantId, id },
            tx,
            cancellationToken: cancellationToken))).AsList();

        var renewals = (await connection.QueryAsync<ContractRenewalRecord>(new CommandDefinition(
            """
            SELECT id AS "Id",
                   previous_end_date AS "PreviousEndDate",
                   new_end_date AS "NewEndDate",
                   previous_term_cycle AS "PreviousTermCycle",
                   new_term_cycle AS "NewTermCycle",
                   reason AS "Reason",
                   created_by AS "CreatedBy",
                   created_at AS "CreatedAt"
              FROM odca.contract_renewals
             WHERE tenant_id = @tenantId AND contract_id = @id
             ORDER BY created_at DESC;
            """,
            new { tenantId, id },
            tx,
            cancellationToken: cancellationToken))).AsList();

        var events = (await connection.QueryAsync<ContractEventRecord>(new CommandDefinition(
            """
            SELECT id AS "Id",
                   action AS "Action",
                   actor_user_id AS "ActorUserId",
                   occurred_at AS "OccurredAt",
                   metadata::text AS "MetadataJson"
              FROM odca.contract_events
             WHERE tenant_id = @tenantId AND contract_id = @id
             ORDER BY occurred_at DESC, id DESC
             LIMIT 100;
            """,
            new { tenantId, id },
            tx,
            cancellationToken: cancellationToken))).AsList();

        var temporal = ContractLifecycle.DeriveTemporalStatus(
            ParseOperational(header.OperationalStatus),
            header.StartDate,
            header.EndDate,
            header.IsIndefinite,
            referenceDate);

        return new ContractDetail(
            header.Id,
            header.ReferenceNumber,
            header.Title,
            header.Summary,
            header.TypeId,
            header.TypeName,
            header.PrimaryCounterpartyId,
            header.PrimaryCounterpartyName,
            header.OwnerUserId,
            header.OwnerName,
            header.StartDate,
            header.EndDate,
            header.IsIndefinite,
            header.Amount,
            header.Currency,
            header.AmountPeriodicity,
            header.RenewalNoticeDays,
            header.RenewalDecision,
            header.OperationalStatus,
            temporal.ToString(),
            header.TermCycle,
            header.Version,
            header.CreatedAt,
            header.UpdatedAt,
            header.ClosedAt,
            header.CancelledAt,
            parties,
            renewals,
            events);
    }

    private static object Bind(ContractWriteModel model, Guid id, Guid tenantId, Guid actorId, long? version = null)
        => new
        {
            id,
            tenantId,
            actorId,
            version,
            model.ReferenceNumber,
            model.Title,
            model.Summary,
            model.TypeId,
            model.PrimaryCounterpartyId,
            model.OwnerUserId,
            model.StartDate,
            model.EndDate,
            model.IsIndefinite,
            model.Amount,
            model.Currency,
            model.AmountPeriodicity,
            model.RenewalNoticeDays,
            RenewalDecision = model.RenewalDecision ?? "pending"
        };

    private static ContractOperationalStatus ParseOperational(string value)
        => ContractOperationalStatusParser.TryParse(value, out var status)
            ? status
            : throw new InvalidDataException($"Estado operacional inválido: {value}");

    private sealed record ContractListRow(
        Guid Id,
        string? ReferenceNumber,
        string Title,
        Guid TypeId,
        string TypeName,
        Guid PrimaryCounterpartyId,
        string PrimaryCounterpartyName,
        Guid? OwnerUserId,
        string? OwnerName,
        DateOnly StartDate,
        DateOnly? EndDate,
        bool IsIndefinite,
        string OperationalStatus,
        int TermCycle,
        long Version,
        DateTimeOffset UpdatedAt);

    private sealed record ContractHeaderRow(
        Guid Id,
        string? ReferenceNumber,
        string Title,
        string? Summary,
        Guid TypeId,
        string TypeName,
        Guid PrimaryCounterpartyId,
        string PrimaryCounterpartyName,
        Guid? OwnerUserId,
        string? OwnerName,
        DateOnly StartDate,
        DateOnly? EndDate,
        bool IsIndefinite,
        decimal? Amount,
        string? Currency,
        string? AmountPeriodicity,
        int? RenewalNoticeDays,
        string RenewalDecision,
        string OperationalStatus,
        int TermCycle,
        long Version,
        DateTimeOffset CreatedAt,
        DateTimeOffset UpdatedAt,
        DateTimeOffset? ClosedAt,
        DateTimeOffset? CancelledAt,
        bool IsDeleted);

    private sealed record RenewalState(
        string OperationalStatus,
        DateOnly? EndDate,
        bool IsIndefinite,
        int TermCycle,
        bool IsDeleted);
}
