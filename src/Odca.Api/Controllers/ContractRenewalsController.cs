using System.Security.Claims;
using Dapper;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Npgsql;
using Odca.Contracts.Obligations;

namespace Odca.Api.Controllers;

[ApiController]
[Authorize(Policy="PasswordChanged")]
[Route("api/v1/organizations/{tenantId:guid}/contracts/{contractId:guid}/renewals")]
public sealed class ContractRenewalsController(NpgsqlDataSource dataSource) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> History(Guid tenantId,Guid contractId,CancellationToken ct)
    {
        var actor=Actor();if(actor is null)return Unauthorized();await using var c=await dataSource.OpenConnectionAsync(ct);if(!await Allowed(c,actor.Value,tenantId,"tenant.obligations.read",ct))return Forbid();await using var tx=await c.BeginTransactionAsync(ct);await SetTenant(c,tenantId,tx,ct);
        var contract=await c.QuerySingleOrDefaultAsync(new CommandDefinition("SELECT title,reference,start_date,end_date,renewal_notice_days,version FROM odca.contracts WHERE tenant_id=@tenantId AND id=@contractId",new{tenantId,contractId},tx,cancellationToken:ct));if(contract is null)return NotFound();
        var cycles=await c.QueryAsync(new CommandDefinition("SELECT id,cycle_number,starts_on,ends_on,notice_due_on,decision_owner_id,decision,justification,decided_by,decided_at,row_version FROM odca.contract_renewal_cycles WHERE tenant_id=@tenantId AND contract_id=@contractId ORDER BY cycle_number DESC",new{tenantId,contractId},tx,cancellationToken:ct));
        var pending=await c.ExecuteScalarAsync<int>(new CommandDefinition("SELECT count(*)::int FROM odca.contract_obligations WHERE tenant_id=@tenantId AND contract_id=@contractId AND deleted_at IS NULL AND status IN('open','in_progress')",new{tenantId,contractId},tx,cancellationToken:ct));await tx.CommitAsync(ct);return Ok(new{contract,pendingObligations=pending,cycles});
    }

    [HttpPost("decision")]
    public async Task<IActionResult> Decide(Guid tenantId,Guid contractId,[FromBody] RenewalDecisionRequest request,CancellationToken ct)
    {
        var actor=Actor();if(actor is null)return Unauthorized();var allowed=new[]{"intent_to_renew","negotiating","not_renewing"};if(request.Decision=="renewed")return Conflict(new{title="Use a Central de Renovações para revisar, formalizar e aplicar a nova vigência."});if(!allowed.Contains(request.Decision)||string.IsNullOrWhiteSpace(request.Justification))return ValidationProblem();
        var permission=request.Decision=="renewed"?"tenant.renewals.register":"tenant.renewals.decide";await using var c=await dataSource.OpenConnectionAsync(ct);if(!await Allowed(c,actor.Value,tenantId,permission,ct))return Forbid();await using var tx=await c.BeginTransactionAsync(ct);await SetTenant(c,tenantId,tx,ct);
        var contract=await c.QuerySingleOrDefaultAsync<Term>(new CommandDefinition("SELECT start_date AS StartDate,end_date AS EndDate,renewal_notice_days AS NoticeDays,version AS Version FROM odca.contracts WHERE tenant_id=@tenantId AND id=@contractId FOR UPDATE",new{tenantId,contractId},tx,cancellationToken:ct));if(contract is null)return NotFound();if(contract.Version!=request.ContractVersion)return Conflict(new{title="O contrato foi alterado em outra sessão.",currentVersion=contract.Version});
        var previous=await c.ExecuteScalarAsync<Guid?>(new CommandDefinition("SELECT id FROM odca.contract_renewal_cycles WHERE tenant_id=@tenantId AND contract_id=@contractId ORDER BY cycle_number DESC LIMIT 1",new{tenantId,contractId},tx,cancellationToken:ct));
        var id=Guid.NewGuid();var cycle=await c.ExecuteScalarAsync<int>(new CommandDefinition("SELECT COALESCE(max(cycle_number),0)+1 FROM odca.contract_renewal_cycles WHERE tenant_id=@tenantId AND contract_id=@contractId",new{tenantId,contractId},tx,cancellationToken:ct));
        await c.ExecuteAsync(new CommandDefinition("INSERT INTO odca.contract_renewal_cycles(id,tenant_id,contract_id,cycle_number,starts_on,ends_on,notice_due_on,decision,justification,decided_by,decided_at,previous_cycle_id) VALUES(@id,@tenantId,@contractId,@cycle,@starts,@ends,@notice,@decision,@justification,@actor,now(),@previous)",new{id,tenantId,contractId,cycle,starts=request.Decision=="renewed"?request.NewStartDate:contract.StartDate,ends=request.Decision=="renewed"?request.NewEndDate:contract.EndDate,notice=(request.Decision=="renewed"?request.NewEndDate:contract.EndDate)?.AddDays(-(contract.NoticeDays??0)),decision=request.Decision,justification=request.Justification.Trim(),actor,previous},tx,cancellationToken:ct));
        await c.ExecuteAsync(new CommandDefinition("INSERT INTO odca.contract_events(tenant_id,contract_id,actor_id,event_type,details) VALUES(@tenantId,@contractId,@actor,'renewal_decision',jsonb_build_object('decision',@decision,'cycle',@cycle,'previousCycle',@previous))",new{tenantId,contractId,actor,decision=request.Decision,cycle,previous},tx,cancellationToken:ct));await tx.CommitAsync(ct);
        return Ok(new{id,cycle,decision=request.Decision,contractVersion=contract.Version,obligationsPreserved=true});
    }
    private Guid? Actor()=>Guid.TryParse(User.FindFirstValue("sub"),out var id)?id:null;private static Task<bool> Allowed(NpgsqlConnection c,Guid actor,Guid tenant,string permission,CancellationToken ct)=>c.ExecuteScalarAsync<bool>(new CommandDefinition("SELECT odca.tenant_actor_has_permission(@actor,@tenant,@permission)",new{actor,tenant,permission},cancellationToken:ct));private static Task<int> SetTenant(NpgsqlConnection c,Guid tenant,NpgsqlTransaction tx,CancellationToken ct)=>c.ExecuteAsync(new CommandDefinition("SELECT set_config('odca.tenant_id',@value,true)",new{value=tenant.ToString()},tx,cancellationToken:ct));private sealed record Term(DateOnly? StartDate,DateOnly? EndDate,int? NoticeDays,long Version);
}
