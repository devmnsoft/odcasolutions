using Dapper;
using Npgsql;

namespace Odca.Worker;

public sealed class RenewalApplicationWorker(
    NpgsqlDataSource dataSource,
    ILogger<RenewalApplicationWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromMinutes(1));
        try
        {
            do
            {
                try
                {
                    await ApplyOne(stoppingToken);
                }
                catch (Exception exception) when (exception is not OperationCanceledException)
                {
                    logger.LogError(exception, "Falha controlada ao aplicar alteração contratual agendada.");
                }
            }
            while (await timer.WaitForNextTickAsync(stoppingToken));
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            logger.LogInformation("Worker de renovações encerrado.");
        }
    }

    private async Task ApplyOne(CancellationToken cancellationToken)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

        // The transaction-scoped lock makes candidate selection and application one
        // indivisible claim across worker instances, without introducing a stale lease.
        var ownsClaim = await connection.ExecuteScalarAsync<bool>(new CommandDefinition(
            "SELECT pg_try_advisory_xact_lock(hashtext('odca.renewal-application-worker'))",
            transaction: transaction,
            cancellationToken: cancellationToken));
        if (!ownsClaim)
        {
            await transaction.RollbackAsync(cancellationToken);
            return;
        }

        var candidate = await connection.QuerySingleOrDefaultAsync<Candidate>(new CommandDefinition(
            "SELECT * FROM odca.claim_due_contract_change()",
            transaction: transaction,
            cancellationToken: cancellationToken));
        if (candidate is null)
        {
            await transaction.RollbackAsync(cancellationToken);
            return;
        }

        await connection.ExecuteAsync(new CommandDefinition(
            "SELECT set_config('odca.tenant_id',@tenant,true)",
            new { tenant = candidate.TenantId.ToString() }, transaction,
            cancellationToken: cancellationToken));
        var change = await connection.QuerySingleOrDefaultAsync<Change>(new CommandDefinition(
            """
            SELECT r.contract_id AS ContractId,r.base_contract_version AS BaseVersion,
                   r.proposed_start_date AS StartDate,r.proposed_end_date AS EndDate,
                   r.proposed_value AS Value,r.currency AS Currency,c.version AS CurrentVersion,
                   c.start_date AS OldStart,c.end_date AS OldEnd,c.value AS OldValue,c.currency AS OldCurrency
              FROM odca.contract_change_requests r
              JOIN odca.contracts c ON c.tenant_id=r.tenant_id AND c.id=r.contract_id
             WHERE r.tenant_id=@tenantId AND r.id=@id AND r.row_version=@version
               AND r.status='formalized' AND r.application_status='scheduled' AND r.effective_on<=@today
             FOR UPDATE OF r,c
            """,
            new { tenantId = candidate.TenantId, id = candidate.Id, version = candidate.RowVersion, today = candidate.Today },
            transaction, cancellationToken: cancellationToken));
        if (change is null)
        {
            await transaction.RollbackAsync(cancellationToken);
            return;
        }

        if (change.CurrentVersion != change.BaseVersion)
        {
            var conflicts = await connection.ExecuteAsync(new CommandDefinition(
                "UPDATE odca.contract_change_requests SET status='conflict',application_status='failed',row_version=row_version+1,updated_at=now() WHERE tenant_id=@tenantId AND id=@id AND row_version=@version AND application_status='scheduled'",
                new { tenantId = candidate.TenantId, id = candidate.Id, version = candidate.RowVersion },
                transaction, cancellationToken: cancellationToken));
            if (conflicts == 1) await transaction.CommitAsync(cancellationToken);
            else await transaction.RollbackAsync(cancellationToken);
            return;
        }

        var updatedContracts = await connection.ExecuteAsync(new CommandDefinition(
            "UPDATE odca.contracts SET start_date=COALESCE(@start,start_date),end_date=COALESCE(@end,end_date),value=COALESCE(@value,value),currency=CASE WHEN @value IS NULL THEN currency ELSE @currency END,version=version+1,updated_at=now() WHERE tenant_id=@tenantId AND id=@contractId AND version=@expectedVersion",
            new { start = change.StartDate, end = change.EndDate, value = change.Value, currency = change.Currency, tenantId = candidate.TenantId, contractId = change.ContractId, expectedVersion = change.BaseVersion },
            transaction, cancellationToken: cancellationToken));
        if (updatedContracts != 1)
        {
            await transaction.RollbackAsync(cancellationToken);
            return;
        }

        var recordedApplications = await connection.ExecuteAsync(new CommandDefinition(
            """
            INSERT INTO odca.contract_change_applications(tenant_id,request_id,contract_id,before_data,after_data)
            VALUES(@tenantId,@id,@contractId,
              jsonb_build_object('startDate',@oldStart,'endDate',@oldEnd,'value',@oldValue,'currency',@oldCurrency),
              jsonb_build_object('startDate',COALESCE(@start,@oldStart),'endDate',COALESCE(@end,@oldEnd),'value',COALESCE(@value,@oldValue),'currency',COALESCE(@currency,@oldCurrency)))
            ON CONFLICT(tenant_id,request_id) DO NOTHING
            """,
            new { tenantId = candidate.TenantId, id = candidate.Id, contractId = change.ContractId, oldStart = change.OldStart, oldEnd = change.OldEnd, oldValue = change.OldValue, oldCurrency = change.OldCurrency, start = change.StartDate, end = change.EndDate, value = change.Value, currency = change.Currency },
            transaction, cancellationToken: cancellationToken));
        var completedRequests = recordedApplications == 1
            ? await connection.ExecuteAsync(new CommandDefinition(
                "UPDATE odca.contract_change_requests SET application_status='applied',row_version=row_version+1,updated_at=now() WHERE tenant_id=@tenantId AND id=@id AND row_version=@version AND application_status='scheduled'",
                new { tenantId = candidate.TenantId, id = candidate.Id, version = candidate.RowVersion },
                transaction, cancellationToken: cancellationToken))
            : 0;
        if (recordedApplications != 1 || completedRequests != 1)
        {
            await transaction.RollbackAsync(cancellationToken);
            return;
        }

        await transaction.CommitAsync(cancellationToken);
        logger.LogInformation("Alteração contratual agendada aplicada request={RequestId} tenant={TenantId}", candidate.Id, candidate.TenantId);
    }

    private sealed record Candidate(Guid Id, Guid TenantId, long RowVersion, DateOnly Today);
    private sealed record Change(Guid ContractId, long BaseVersion, DateOnly? StartDate, DateOnly? EndDate, decimal? Value, string? Currency, long CurrentVersion, DateOnly? OldStart, DateOnly? OldEnd, decimal? OldValue, string? OldCurrency);
}
