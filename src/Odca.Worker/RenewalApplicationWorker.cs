using Dapper;
using Npgsql;

namespace Odca.Worker;

public sealed class RenewalApplicationWorker(NpgsqlDataSource dataSource,ILogger<RenewalApplicationWorker> logger):BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer=new PeriodicTimer(TimeSpan.FromMinutes(1));
        try{do{try{await ApplyOne(stoppingToken);}catch(Exception ex) when(ex is not OperationCanceledException){logger.LogError(ex,"Falha controlada ao aplicar alteração contratual agendada.");}}while(await timer.WaitForNextTickAsync(stoppingToken));}
        catch(OperationCanceledException) when(stoppingToken.IsCancellationRequested){logger.LogInformation("Worker de renovações encerrado.");}
    }

    private async Task ApplyOne(CancellationToken ct)
    {
        await using var c=await dataSource.OpenConnectionAsync(ct);
        var candidate=await c.QuerySingleOrDefaultAsync<Candidate>(new CommandDefinition("SELECT * FROM odca.claim_due_contract_change()",cancellationToken:ct));if(candidate is null)return;
        await using var tx=await c.BeginTransactionAsync(ct);await c.ExecuteAsync(new CommandDefinition("SELECT set_config('odca.tenant_id',@tenant,true)",new{tenant=candidate.TenantId.ToString()},tx,cancellationToken:ct));
        var row=await c.QuerySingleOrDefaultAsync<Change>(new CommandDefinition("SELECT r.contract_id AS ContractId,r.base_contract_version AS BaseVersion,r.proposed_start_date AS StartDate,r.proposed_end_date AS EndDate,r.proposed_value AS Value,r.currency AS Currency,c.version AS CurrentVersion,c.start_date AS OldStart,c.end_date AS OldEnd,c.value AS OldValue,c.currency AS OldCurrency FROM odca.contract_change_requests r JOIN odca.contracts c ON c.tenant_id=r.tenant_id AND c.id=r.contract_id WHERE r.tenant_id=@tenantId AND r.id=@id AND r.row_version=@version AND r.status='formalized' AND r.application_status='scheduled' AND r.effective_on<=@today FOR UPDATE OF r,c",new{tenantId=candidate.TenantId,id=candidate.Id,version=candidate.RowVersion,today=candidate.Today},tx,cancellationToken:ct));
        if(row is null){await tx.RollbackAsync(ct);return;}if(row.CurrentVersion!=row.BaseVersion){await c.ExecuteAsync(new CommandDefinition("UPDATE odca.contract_change_requests SET status='conflict',application_status='failed',row_version=row_version+1 WHERE tenant_id=@tenantId AND id=@id",new{tenantId=candidate.TenantId,id=candidate.Id},tx,cancellationToken:ct));await tx.CommitAsync(ct);return;}
        var applied=await c.ExecuteAsync(new CommandDefinition("UPDATE odca.contracts SET start_date=COALESCE(@start,start_date),end_date=COALESCE(@end,end_date),value=COALESCE(@value,value),currency=CASE WHEN @value IS NULL THEN currency ELSE @currency END,version=version+1,updated_at=now() WHERE tenant_id=@tenantId AND id=@contractId AND version=@base",new{start=row.StartDate,end=row.EndDate,value=row.Value,currency=row.Currency,tenantId=candidate.TenantId,contractId=row.ContractId,base=row.BaseVersion},tx,cancellationToken:ct));if(applied!=1){await tx.RollbackAsync(ct);return;}
        await c.ExecuteAsync(new CommandDefinition("INSERT INTO odca.contract_change_applications(tenant_id,request_id,contract_id,before_data,after_data) VALUES(@tenantId,@id,@contractId,jsonb_build_object('startDate',@oldStart,'endDate',@oldEnd,'value',@oldValue,'currency',@oldCurrency),jsonb_build_object('startDate',COALESCE(@start,@oldStart),'endDate',COALESCE(@end,@oldEnd),'value',COALESCE(@value,@oldValue),'currency',COALESCE(@currency,@oldCurrency))) ON CONFLICT(tenant_id,request_id) DO NOTHING; UPDATE odca.contract_change_requests SET application_status='applied',row_version=row_version+1,updated_at=now() WHERE tenant_id=@tenantId AND id=@id",new{tenantId=candidate.TenantId,id=candidate.Id,contractId=row.ContractId,oldStart=row.OldStart,oldEnd=row.OldEnd,oldValue=row.OldValue,oldCurrency=row.OldCurrency,start=row.StartDate,end=row.EndDate,value=row.Value,currency=row.Currency},tx,cancellationToken:ct));await tx.CommitAsync(ct);
        logger.LogInformation("Alteração contratual agendada aplicada request={RequestId} tenant={TenantId}",candidate.Id,candidate.TenantId);
    }
    private sealed record Candidate(Guid Id,Guid TenantId,long RowVersion,DateOnly Today);
    private sealed record Change(Guid ContractId,long BaseVersion,DateOnly? StartDate,DateOnly? EndDate,decimal? Value,string? Currency,long CurrentVersion,DateOnly? OldStart,DateOnly? OldEnd,decimal? OldValue,string? OldCurrency);
}
