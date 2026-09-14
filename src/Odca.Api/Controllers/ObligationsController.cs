using System.Security.Claims;
using Dapper;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Npgsql;
using Odca.Contracts.Obligations;

namespace Odca.Api.Controllers;

[ApiController]
[Authorize(Policy="PasswordChanged")]
[Route("api/v1/organizations/{tenantId:guid}/obligations")]
public sealed class ObligationsController(NpgsqlDataSource dataSource) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> List(Guid tenantId,[FromQuery] string scope="mine",[FromQuery] Guid? contractId=null,[FromQuery] Guid? ownerId=null,[FromQuery] string? category=null,[FromQuery] string? status=null,[FromQuery] DateOnly? from=null,[FromQuery] DateOnly? to=null,[FromQuery] string? search=null,[FromQuery] int page=1,[FromQuery] int pageSize=20,CancellationToken ct=default)
    {
        if (from.HasValue && to.HasValue && from.Value > to.Value)
            return ValidationProblem(new ValidationProblemDetails(new Dictionary<string, string[]> { ["to"] = ["A data final deve ser igual ou posterior à data inicial."] }));
        var actor=Actor(); if(actor is null) return Unauthorized(); page=Math.Max(1,page); pageSize=Math.Clamp(pageSize,1,100);
        await using var c=await dataSource.OpenConnectionAsync(ct); if(!await Allowed(c,actor.Value,tenantId,"tenant.obligations.read",ct)) return Forbid();
        await using var tx=await c.BeginTransactionAsync(ct); await SetTenant(c,tenantId,tx,ct);
        var all=scope.Equals("organization",StringComparison.OrdinalIgnoreCase) && await Allowed(c,actor.Value,tenantId,"tenant.obligations.read_all",ct);
        var args=new {tenantId,actor,all,contractId,ownerId,category,status,from,to,search=string.IsNullOrWhiteSpace(search)?null:$"%{search.Trim()}%",offset=(page-1)*pageSize,pageSize};
        const string where="""
          o.tenant_id=@tenantId AND o.deleted_at IS NULL AND (@all OR o.owner_id=@actor)
          AND (@contractId IS NULL OR o.contract_id=@contractId) AND (@ownerId IS NULL OR o.owner_id=@ownerId)
          AND (@category IS NULL OR o.category=@category) AND (@status IS NULL OR o.status=@status)
          AND (@from IS NULL OR o.due_date>=@from) AND (@to IS NULL OR o.due_date<=@to)
          AND (@search IS NULL OR o.title ILIKE @search OR c.title ILIKE @search)
        """;
        var total=await c.ExecuteScalarAsync<int>(new CommandDefinition($"SELECT count(*)::int FROM odca.contract_obligations o JOIN odca.contracts c ON c.id=o.contract_id AND c.tenant_id=o.tenant_id WHERE {where}",args,tx,cancellationToken:ct));
        var rows=await c.QueryAsync<ObligationRow>(new CommandDefinition($"""
          SELECT o.id AS Id,o.contract_id AS ContractId,c.title AS Contract,o.title AS Title,o.category AS Category,
                 o.obligated_party AS ObligatedParty,o.owner_id AS OwnerId,u.display_name AS Owner,o.due_date AS DueDate,
                 o.priority AS Priority,o.status AS Status,(o.status IN('open','in_progress') AND o.due_date < (now() AT TIME ZONE t.timezone)::date) AS Overdue,
                 (m.status='blocked') AS OwnerBlocked,o.row_version AS Version,o.amount AS Amount,o.currency AS Currency
          FROM odca.contract_obligations o JOIN odca.contracts c ON c.id=o.contract_id AND c.tenant_id=o.tenant_id
          JOIN odca.users u ON u.id=o.owner_id JOIN odca.memberships m ON m.tenant_id=o.tenant_id AND m.user_id=o.owner_id JOIN odca.tenants t ON t.id=o.tenant_id
          WHERE {where} ORDER BY (o.status IN('open','in_progress') AND o.due_date < (now() AT TIME ZONE t.timezone)::date) DESC,o.due_date,o.id LIMIT @pageSize OFFSET @offset
          """,args,tx,cancellationToken:ct));
        await tx.CommitAsync(ct);
        return Ok(new ObligationPage(rows.Select(x=>new ObligationItem(x.Id,x.ContractId,x.Contract,x.Title,x.Category,x.ObligatedParty,x.OwnerId,x.Owner,x.DueDate,x.Priority,x.Status,x.Overdue,x.OwnerBlocked,x.Version,x.Amount,x.Currency,x.OwnerBlocked?"Reatribuir responsável":x.Status=="fulfilled"?"Consultar histórico":"Registrar andamento")).ToArray(),page,pageSize,total));
    }

    [HttpPost("contracts/{contractId:guid}")]
    public async Task<IActionResult> Create(Guid tenantId,Guid contractId,[FromBody] CreateObligationRequest request,CancellationToken ct)
    {
        var actor=Actor(); if(actor is null) return Unauthorized();
        if(string.IsNullOrWhiteSpace(request.Title)||string.IsNullOrWhiteSpace(request.ObligatedParty)||!Categories.Contains(request.Category)||!Priorities.Contains(request.Priority)||!Origins.Contains(request.Origin)) return ValidationProblem();
        if(request.Category=="financial" && (request.Amount is null or <=0 || request.Currency?.Length!=3)) return ValidationProblem(new ValidationProblemDetails(new Dictionary<string,string[]>{{"amount",["Obrigação financeira exige valor positivo e moeda explícita."]}}));
        if(request.Recurrence is { IntendedDay: <1 or >31 } || request.Recurrence is { OccurrenceCount: >120 }) return ValidationProblem();
        await using var c=await dataSource.OpenConnectionAsync(ct); if(!await Allowed(c,actor.Value,tenantId,"tenant.obligations.manage",ct)) return Forbid();
        await using var tx=await c.BeginTransactionAsync(ct); await SetTenant(c,tenantId,tx,ct);
        var contract=await c.QuerySingleOrDefaultAsync<ContractTerm>(new CommandDefinition("SELECT end_date AS EndDate FROM odca.contracts WHERE tenant_id=@tenantId AND id=@contractId",new{tenantId,contractId},tx,cancellationToken:ct)); if(contract is null)return NotFound();
        var ownerActive=await c.ExecuteScalarAsync<bool>(new CommandDefinition("SELECT EXISTS(SELECT 1 FROM odca.memberships WHERE tenant_id=@tenantId AND user_id=@ownerId AND status='active')",new{tenantId,request.OwnerId},tx,cancellationToken:ct)); if(!ownerActive)return ValidationProblem(new ValidationProblemDetails(new Dictionary<string,string[]>{{"ownerId",["O responsável precisa ter vínculo ativo."]}}));
        if(contract.EndDate is not null && request.DueDate>contract.EndDate && string.IsNullOrWhiteSpace(request.PostTermReason)) return ValidationProblem(new ValidationProblemDetails(new Dictionary<string,string[]>{{"postTermReason",["Justifique o dever posterior ao término da vigência."]}}));
        var seriesId=request.Recurrence is null?(Guid?)null:Guid.NewGuid();
        if(request.Recurrence is not null) await c.ExecuteAsync(new CommandDefinition("INSERT INTO odca.obligation_series(id,tenant_id,contract_id,base_date,intended_day,ends_on,occurrence_count,timezone,created_by) VALUES(@seriesId,@tenantId,@contractId,@baseDate,@day,@ends,@count,@timezone,@actor)",new{seriesId,tenantId,contractId,baseDate=request.Recurrence.BaseDate,day=request.Recurrence.IntendedDay,ends=request.Recurrence.EndsOn,count=request.Recurrence.OccurrenceCount,timezone=request.Recurrence.TimeZone,actor},tx,cancellationToken:ct));
        IReadOnlyList<DateOnly> dates=request.Recurrence is null?[request.DueDate]:Materialize(request.Recurrence);
        var ids=new List<Guid>(); for(var i=0;i<dates.Count;i++){var id=Guid.NewGuid();ids.Add(id);await c.ExecuteAsync(new CommandDefinition("""
          INSERT INTO odca.contract_obligations(id,tenant_id,contract_id,title,description,category,obligated_party,owner_id,due_date,priority,origin,amount,currency,clause_document_version_id,evidence_required,post_term_reason,series_id,occurrence_index,created_by)
          VALUES(@id,@tenantId,@contractId,@title,@description,@category,@party,@ownerId,@due,@priority,@origin,@amount,@currency,@documentVersion,@evidenceRequired,@postTermReason,@seriesId,@index,@actor);
          INSERT INTO odca.obligation_events(tenant_id,obligation_id,actor_id,event_type,details) VALUES(@tenantId,@id,@actor,'created',jsonb_build_object('dueDate',@due));
          """,new{id,tenantId,contractId,title=request.Title.Trim(),request.Description,category=request.Category,party=request.ObligatedParty.Trim(),request.OwnerId,due=dates[i],priority=request.Priority,origin=request.Origin,request.Amount,currency=request.Currency?.ToUpperInvariant(),documentVersion=request.ClauseDocumentVersionId,request.EvidenceRequired,request.PostTermReason,seriesId,index=i,actor},tx,cancellationToken:ct));
          foreach(var days in (request.ReminderDays??[7,1,0]).Distinct().Where(x=>x is >=0 and <=365)) await c.ExecuteAsync(new CommandDefinition("INSERT INTO odca.obligation_reminders(tenant_id,obligation_id,days_before,deduplication_key,scheduled_for) VALUES(@tenantId,@id,@days,@key,@scheduled)",new{tenantId,id,days,key=$"{id:N}:v1:d{days}",scheduled=dates[i].AddDays(-days)},tx,cancellationToken:ct));}
        await tx.CommitAsync(ct); return Created($"/api/v1/organizations/{tenantId}/obligations/{ids[0]}",new{ids,seriesId,pastDue=dates.Any(x=>x<DateOnly.FromDateTime(DateTime.UtcNow)),postTerm=contract.EndDate is not null&&dates.Any(x=>x>contract.EndDate)});
    }

    [HttpPost("{id:guid}/{action}")]
    public async Task<IActionResult> Action(Guid tenantId,Guid id,string action,[FromBody] ObligationActionRequest request,CancellationToken ct)
    {
        var actor=Actor(); if(actor is null)return Unauthorized(); var permission=action switch{"fulfill"=>"tenant.obligations.fulfill","reopen"=>"tenant.obligations.reopen","cancel"=>"tenant.obligations.cancel","reassign"=>"tenant.obligations.assign",_=>"tenant.obligations.manage"};
        await using var c=await dataSource.OpenConnectionAsync(ct);if(!await Allowed(c,actor.Value,tenantId,permission,ct))return Forbid();await using var tx=await c.BeginTransactionAsync(ct);await SetTenant(c,tenantId,tx,ct);
        var item=await c.QuerySingleOrDefaultAsync<ActionRow>(new CommandDefinition("SELECT status AS Status,row_version AS Version,due_date AS DueDate,evidence_required AS EvidenceRequired,owner_id AS OwnerId FROM odca.contract_obligations WHERE tenant_id=@tenantId AND id=@id AND deleted_at IS NULL FOR UPDATE",new{tenantId,id},tx,cancellationToken:ct));if(item is null)return NotFound();if(item.Version!=request.Version)return Conflict(new{title="A obrigação foi alterada em outra sessão.",currentVersion=item.Version});
        string status=item.Status; DateOnly due=item.DueDate; Guid owner=item.OwnerId; DateTimeOffset? fulfilled=null;
        switch(action){case "start" when status=="open":status="in_progress";break;case "fulfill" when status is "open" or "in_progress":if(request.EffectiveAt is null||item.EvidenceRequired&&(!request.EvidenceDocumentVersionId.HasValue||request.EvidenceDocumentVersionId.Value==Guid.Empty))return ValidationProblem();fulfilled=request.EffectiveAt;break;case "cancel" when status is "open" or "in_progress":if(string.IsNullOrWhiteSpace(request.Reason))return ValidationProblem();status="cancelled";break;case "reopen" when status is "fulfilled" or "cancelled":if(string.IsNullOrWhiteSpace(request.Reason))return ValidationProblem();status="open";break;case "reschedule" when status is "open" or "in_progress":if(request.DueDate is null||string.IsNullOrWhiteSpace(request.Reason))return ValidationProblem();due=request.DueDate.Value;break;case "reassign" when status is "open" or "in_progress":if(!request.OwnerId.HasValue||request.OwnerId.Value==Guid.Empty)return ValidationProblem();var active=await c.ExecuteScalarAsync<bool>(new CommandDefinition("SELECT EXISTS(SELECT 1 FROM odca.memberships WHERE tenant_id=@tenantId AND user_id=@owner AND status='active')",new{tenantId,owner=request.OwnerId},tx,cancellationToken:ct));if(!active)return ValidationProblem();owner=request.OwnerId.Value;break;default:return Conflict(new{title="Transição de estado não permitida."});}
        if(action=="fulfill")status="fulfilled";
        await c.ExecuteAsync(new CommandDefinition("""
          UPDATE odca.contract_obligations SET status=@status,due_date=@due,owner_id=@owner,row_version=row_version+1,
            fulfilled_at=CASE WHEN @action='fulfill' THEN @fulfilled WHEN @action='reopen' THEN NULL ELSE fulfilled_at END,
            fulfilled_by=CASE WHEN @action='fulfill' THEN @actor WHEN @action='reopen' THEN NULL ELSE fulfilled_by END,
            fulfillment_note=CASE WHEN @action='fulfill' THEN @note WHEN @action='reopen' THEN NULL ELSE fulfillment_note END,
            updated_at=now() WHERE tenant_id=@tenantId AND id=@id;
          INSERT INTO odca.obligation_events(tenant_id,obligation_id,actor_id,event_type,details)
          VALUES(@tenantId,@id,@actor,@action,jsonb_strip_nulls(jsonb_build_object(
            'reason',@reason,'effectiveAt',@fulfilled,'note',@note,'evidenceDocumentVersionId',@evidence,
            'previousDueDate',@previousDue,'dueDate',@due,'previousOwner',@previousOwner,'owner',@owner)));
          UPDATE odca.obligation_reminders SET status='obsolete' WHERE tenant_id=@tenantId AND obligation_id=@id AND status='pending';
          """,new{status,due,owner,fulfilled,actor,note=request.Note,evidence=request.EvidenceDocumentVersionId,tenantId,id,action,reason=request.Reason,previousDue=item.DueDate,previousOwner=item.OwnerId},tx,cancellationToken:ct));
        if(request.EvidenceDocumentVersionId is Guid evidence){var valid=await c.ExecuteScalarAsync<bool>(new CommandDefinition("SELECT EXISTS(SELECT 1 FROM odca.document_versions v JOIN odca.contract_obligations o ON o.contract_id=v.contract_id AND o.tenant_id=v.tenant_id WHERE o.id=@id AND v.id=@evidence AND v.tenant_id=@tenantId)",new{tenantId,id,evidence},tx,cancellationToken:ct));if(!valid)return ValidationProblem();await c.ExecuteAsync(new CommandDefinition("INSERT INTO odca.obligation_evidence(tenant_id,obligation_id,document_version_id,linked_by) VALUES(@tenantId,@id,@evidence,@actor) ON CONFLICT DO NOTHING",new{tenantId,id,evidence,actor},tx,cancellationToken:ct));}
        if(action=="reschedule")foreach(var days in new[]{7,1,0})await c.ExecuteAsync(new CommandDefinition("INSERT INTO odca.obligation_reminders(tenant_id,obligation_id,days_before,deduplication_key,scheduled_for) VALUES(@tenantId,@id,@days,@key,@scheduled) ON CONFLICT DO NOTHING",new{tenantId,id,days,key=$"{id:N}:v{item.Version+1}:d{days}",scheduled=due.AddDays(-days)},tx,cancellationToken:ct));
        await tx.CommitAsync(ct);return Ok(new{status,version=item.Version+1});
    }

    private Guid? Actor()=>Guid.TryParse(User.FindFirstValue("sub"),out var id)?id:null;
    private static Task<bool> Allowed(NpgsqlConnection c,Guid actor,Guid tenant,string permission,CancellationToken ct)=>c.ExecuteScalarAsync<bool>(new CommandDefinition("SELECT odca.tenant_actor_has_permission(@actor,@tenant,@permission)",new{actor,tenant,permission},cancellationToken:ct));
    private static Task<int> SetTenant(NpgsqlConnection c,Guid tenant,NpgsqlTransaction tx,CancellationToken ct)=>c.ExecuteAsync(new CommandDefinition("SELECT set_config('odca.tenant_id',@value,true)",new{value=tenant.ToString()},tx,cancellationToken:ct));
    private static List<DateOnly> Materialize(MonthlyRecurrenceRequest recurrence){var count=Math.Min(recurrence.OccurrenceCount??24,24);var result=new List<DateOnly>();for(var i=0;i<count;i++){var month=recurrence.BaseDate.AddMonths(i);var date=new DateOnly(month.Year,month.Month,Math.Min(recurrence.IntendedDay,DateTime.DaysInMonth(month.Year,month.Month)));if(recurrence.EndsOn is not null&&date>recurrence.EndsOn)break;result.Add(date);}return result;}
    private static readonly HashSet<string> Categories=["delivery","document","renewal","communication","financial","other"]; private static readonly HashSet<string> Priorities=["low","normal","high","critical"];private static readonly HashSet<string> Origins=["manual","reviewed_suggestion"];
    private sealed record ContractTerm(DateOnly? EndDate);private sealed record ActionRow(string Status,long Version,DateOnly DueDate,bool EvidenceRequired,Guid OwnerId);private sealed record ObligationRow(Guid Id,Guid ContractId,string Contract,string Title,string Category,string ObligatedParty,Guid OwnerId,string Owner,DateOnly DueDate,string Priority,string Status,bool Overdue,bool OwnerBlocked,long Version,decimal? Amount,string? Currency);
}
