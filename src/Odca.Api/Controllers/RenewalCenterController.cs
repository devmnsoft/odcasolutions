using System.Security.Claims;
using Dapper;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Npgsql;
using Odca.Contracts.Renewals;

namespace Odca.Api.Controllers;

[ApiController]
[Authorize(Policy = "PasswordChanged")]
[Route("api/v1/organizations/{tenantId:guid}/renewals")]
public sealed class RenewalCenterController(NpgsqlDataSource dataSource) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> List(Guid tenantId, [FromQuery] DateOnly? from, [FromQuery] DateOnly? to,
        [FromQuery] Guid? ownerId, [FromQuery] string? contractType, [FromQuery] string? counterparty,
        [FromQuery] string? status, [FromQuery] bool mine = false, [FromQuery] bool withoutOwner = false,
        [FromQuery] int page = 1, [FromQuery] int pageSize = 20, CancellationToken ct = default)
    {
        var actor = Actor(); if (actor is null) return Unauthorized();
        if (from.HasValue && to.HasValue && from > to) return ValidationProblem();
        page = Math.Max(1, page); pageSize = Math.Clamp(pageSize, 1, 100);
        await using var c = await dataSource.OpenConnectionAsync(ct);
        if (!await Allowed(c, actor.Value, tenantId, "tenant.renewals.read", ct)) return Forbid();
        await using var tx = await c.BeginTransactionAsync(ct); await SetTenant(c, tenantId, actor.Value, tx, ct);
        var today = await c.ExecuteScalarAsync<DateOnly>(new CommandDefinition("SELECT (now() AT TIME ZONE timezone)::date FROM odca.tenants WHERE id=@tenantId", new { tenantId }, tx, cancellationToken: ct));
        var args = new { tenantId, from, to, ownerId, contractType, counterparty, status, mine, actor, withoutOwner, today, windowEnd = today.AddMonths(3), offset = (page - 1) * pageSize, pageSize };
        const string projection = """
            WITH latest AS (SELECT DISTINCT ON(contract_id) * FROM odca.contract_change_requests WHERE tenant_id=@tenantId ORDER BY contract_id,created_at DESC),
            projected AS (SELECT c.id AS ContractId,r.id AS RequestId,c.title AS Name,c.counterparty AS Counterparty,u.display_name AS OwnerName,c.end_date AS EndDate,
              CASE WHEN c.renewal_notice_amount IS NULL OR c.end_date IS NULL THEN NULL WHEN c.renewal_notice_unit='calendar_months' THEN c.end_date-(c.renewal_notice_amount||' months')::interval ELSE c.end_date-c.renewal_notice_amount END::date AS DecisionDueOn,
              CASE WHEN r.status IS NOT NULL THEN r.status WHEN c.end_date<@today THEN 'expired' WHEN c.end_date<=@windowEnd THEN 'expiring' ELSE 'outside_window' END AS Status,
              CASE WHEN r.status='formalized' AND r.application_status='scheduled' THEN 'Aguardar aplicação programada' WHEN r.status='in_review' THEN 'Acompanhar revisão' WHEN r.status='awaiting_formalization' THEN 'Registrar formalização' WHEN r.status='draft' THEN 'Concluir proposta' WHEN c.end_date<@today THEN 'Analisar contrato vencido' ELSE 'Decidir renovação' END AS NextAction,
              CASE WHEN c.end_date IS NULL THEN NULL ELSE c.end_date-@today END AS DaysRemaining,c.version AS ContractVersion,c.owner_id AS OwnerId,c.contract_type AS ContractType
            FROM odca.contracts c LEFT JOIN latest r ON r.contract_id=c.id LEFT JOIN odca.users u ON u.id=c.owner_id WHERE c.tenant_id=@tenantId)
            """;
        const string filter = """
            WHERE (@from IS NULL OR EndDate>=@from) AND (@to IS NULL OR EndDate<=@to) AND (@ownerId IS NULL OR OwnerId=@ownerId)
              AND (@contractType IS NULL OR ContractType=@contractType) AND (@counterparty IS NULL OR Counterparty=@counterparty)
              AND (@status IS NULL OR Status=@status) AND (NOT @mine OR OwnerId=@actor) AND (NOT @withoutOwner OR OwnerId IS NULL)
              AND (@status IS NOT NULL OR Status<>'outside_window')
            """;
        var total = await c.ExecuteScalarAsync<int>(new CommandDefinition(projection + "SELECT count(*)::int FROM projected " + filter, args, tx, cancellationToken: ct));
        var items = await c.QueryAsync<RenewalListItem>(new CommandDefinition(projection + "SELECT ContractId,RequestId,Name,Counterparty,OwnerName,EndDate,DecisionDueOn,Status,NextAction,DaysRemaining,ContractVersion FROM projected " + filter + " ORDER BY COALESCE(DecisionDueOn,EndDate),Name LIMIT @pageSize OFFSET @offset", args, tx, cancellationToken: ct));
        var counts = await c.QuerySingleAsync<CountRow>(new CommandDefinition(projection + "SELECT count(*) FILTER(WHERE Status='expiring')::int AS Expiring,count(*) FILTER(WHERE Status='expired')::int AS Expired,count(*) FILTER(WHERE Status='draft')::int AS Preparing,count(*) FILTER(WHERE Status='in_review')::int AS InReview,count(*) FILTER(WHERE Status='formalized')::int AS Scheduled,count(*) FILTER(WHERE Status='cancelled')::int AS NotRenewing FROM projected " + filter, args, tx, cancellationToken: ct));
        await tx.CommitAsync(ct);
        return Ok(new RenewalPage(items.AsList(), new(counts.Expiring, counts.Expired, counts.Preparing, counts.InReview, counts.Scheduled, counts.NotRenewing), page, pageSize, total, Today: today));
    }

    [HttpPost("contracts/{contractId:guid}")]
    public async Task<IActionResult> Create(Guid tenantId, Guid contractId, [FromBody] CreateRenewalRequest request, CancellationToken ct)
    {
        var actor = Actor(); if (actor is null) return Unauthorized();
        if (request.Kind is not ("renewal" or "amendment") || string.IsNullOrWhiteSpace(request.Reason)) return ValidationProblem();
        if (request.ProposedEndDate < request.ProposedStartDate || request.ProposedValue < 0 || (request.ProposedValue.HasValue && string.IsNullOrWhiteSpace(request.Currency))) return ValidationProblem();
        await using var c = await dataSource.OpenConnectionAsync(ct); if (!await Allowed(c, actor.Value, tenantId, "tenant.renewals.prepare", ct)) return Forbid();
        await using var tx = await c.BeginTransactionAsync(ct); await SetTenant(c, tenantId, actor.Value, tx, ct);
        var organizationToday=await OrganizationToday(c,tenantId,tx,ct);if(request.EffectiveOn<organizationToday)return ValidationProblem(new ValidationProblemDetails(new Dictionary<string,string[]>{{"effectiveOn",["Datas retroativas não são aceitas."]}}));
        var contract = await c.QuerySingleOrDefaultAsync<ContractBase>(new CommandDefinition("SELECT start_date AS StartDate,end_date AS EndDate,value AS Value,currency AS Currency,owner_id AS OwnerId,version AS Version FROM odca.contracts WHERE tenant_id=@tenantId AND id=@contractId FOR UPDATE", new { tenantId, contractId }, tx, cancellationToken: ct));
        if (contract is null) return NotFound(); if (contract.Version != request.ContractVersion) return Conflict(new { title = "O contrato mudou. Faça uma nova análise.", currentVersion = contract.Version });
        var id = Guid.NewGuid();
        var inserted = await c.ExecuteScalarAsync<Guid?>(new CommandDefinition("""
                WITH created AS (INSERT INTO odca.contract_change_requests(id,tenant_id,contract_id,kind,author_id,responsible_id,reason,current_start_date,current_end_date,proposed_start_date,proposed_end_date,current_value,proposed_value,currency,current_operational_owner_id,effective_on,source_document_version_id,base_contract_version,idempotency_key)
                VALUES(@id,@tenantId,@contractId,@kind,@actor,@responsibleId,@reason,@currentStart,@currentEnd,@proposedStart,@proposedEnd,@currentValue,@proposedValue,@currency,@currentOwner,@effectiveOn,@sourceDocument,@baseVersion,@idempotencyKey)
                ON CONFLICT DO NOTHING RETURNING id)
                INSERT INTO odca.contract_change_events(tenant_id,request_id,actor_id,event_type,details)
                SELECT @tenantId,id,@actor,'created',jsonb_build_object('baseContractVersion',@baseVersion) FROM created RETURNING request_id;
                """, new { id, tenantId, contractId, kind=request.Kind, actor, request.ResponsibleId, reason=request.Reason.Trim(), currentStart=contract.StartDate, currentEnd=contract.EndDate, proposedStart=request.ProposedStartDate, proposedEnd=request.ProposedEndDate, currentValue=contract.Value, proposedValue=request.ProposedValue, currency=request.Currency?.Trim().ToUpperInvariant() ?? contract.Currency, currentOwner=contract.OwnerId, effectiveOn=request.EffectiveOn, sourceDocument=request.SourceDocumentVersionId, baseVersion=contract.Version, request.IdempotencyKey }, tx, cancellationToken: ct));
        if (!inserted.HasValue)
        {
            var existing=await c.ExecuteScalarAsync<Guid?>(new CommandDefinition("SELECT id FROM odca.contract_change_requests WHERE tenant_id=@tenantId AND idempotency_key=@key",new{tenantId,key=request.IdempotencyKey},tx,cancellationToken:ct));
            if(existing.HasValue){await tx.CommitAsync(ct);return Ok(new{id=existing.Value,replayed=true});}
            return Conflict(new{title="Já existe uma alteração ativa para este contrato."});
        }
        await tx.CommitAsync(ct); return Created($"/api/v1/organizations/{tenantId}/renewals/{id}", new { id, replayed=false });
    }

    [HttpPost("{id:guid}/formalization")]
    public async Task<IActionResult> Formalize(Guid tenantId, Guid id, [FromBody] FormalizeRenewalRequest request, CancellationToken ct)
    {
        var actor=Actor(); if(actor is null)return Unauthorized(); if(string.IsNullOrWhiteSpace(request.Justification))return ValidationProblem();
        await using var c=await dataSource.OpenConnectionAsync(ct);if(!await Allowed(c,actor.Value,tenantId,"tenant.renewals.formalize",ct))return Forbid();await using var tx=await c.BeginTransactionAsync(ct);await SetTenant(c,tenantId,actor.Value,tx,ct);
        var organizationToday=await OrganizationToday(c,tenantId,tx,ct);if(request.FormalizedOn>organizationToday)return ValidationProblem();
        var changed=await c.ExecuteAsync(new CommandDefinition("""
            UPDATE odca.contract_change_requests r SET status='formalized',application_status=CASE WHEN effective_on>@today THEN 'scheduled' ELSE 'not_applied' END,
              evidence_version_id=@evidence,formalized_on=@date,formalization_justification=@reason,formalized_by=@actor,formalized_at=now(),row_version=row_version+1,updated_at=now()
            WHERE r.tenant_id=@tenantId AND r.id=@id AND r.row_version=@version AND r.status IN('internally_approved','awaiting_formalization')
              AND EXISTS(SELECT 1 FROM odca.document_versions v WHERE v.tenant_id=r.tenant_id AND v.contract_id=r.contract_id AND v.id=@evidence AND v.security_status='safe')
            """,new{tenantId,id,version=request.RowVersion,evidence=request.EvidenceVersionId,date=request.FormalizedOn,reason=request.Justification.Trim(),actor,today=organizationToday},tx,cancellationToken:ct));
        if(changed!=1)return Conflict(new{title="Estado, versão ou evidência segura inválidos."});await c.ExecuteAsync(new CommandDefinition("INSERT INTO odca.contract_change_events(tenant_id,request_id,actor_id,event_type,details) VALUES(@tenantId,@id,@actor,'manual_formalization',jsonb_build_object('evidenceVersionId',@evidence))",new{tenantId,id,actor,evidence=request.EvidenceVersionId},tx,cancellationToken:ct));await tx.CommitAsync(ct);return Ok(new{manualFormalization=true});
    }

    [HttpPost("{id:guid}/apply")]
    public async Task<IActionResult> Apply(Guid tenantId,Guid id,[FromBody] ApplyRenewalRequest request,CancellationToken ct)
    {var actor=Actor();if(actor is null)return Unauthorized();await using var c=await dataSource.OpenConnectionAsync(ct);if(!await Allowed(c,actor.Value,tenantId,"tenant.renewals.apply",ct))return Forbid();await using var tx=await c.BeginTransactionAsync(ct);await SetTenant(c,tenantId,actor.Value,tx,ct);var today=await OrganizationToday(c,tenantId,tx,ct);var outcome=await ApplyCore(c,tx,tenantId,id,actor.Value,request.RowVersion,today,ct);if(outcome is null){await tx.CommitAsync(ct);return Conflict(new{title="A proposta está desatualizada, não está formalizada ou ainda tem efeito futuro."});}await tx.CommitAsync(ct);return Ok(outcome);}

    internal static async Task<object?> ApplyCore(NpgsqlConnection c,NpgsqlTransaction tx,Guid tenantId,Guid id,Guid actor,long rowVersion,DateOnly today,CancellationToken ct)
    {
        var row=await c.QuerySingleOrDefaultAsync<ApplyRow>(new CommandDefinition("SELECT r.contract_id AS ContractId,r.base_contract_version AS BaseVersion,r.proposed_start_date AS StartDate,r.proposed_end_date AS EndDate,r.proposed_value AS Value,r.currency AS Currency,r.effective_on AS EffectiveOn,c.version AS CurrentVersion,c.start_date AS OldStart,c.end_date AS OldEnd,c.value AS OldValue,c.currency AS OldCurrency FROM odca.contract_change_requests r JOIN odca.contracts c ON c.tenant_id=r.tenant_id AND c.id=r.contract_id WHERE r.tenant_id=@tenantId AND r.id=@id AND (r.row_version=@rowVersion OR (r.base_contract_version=@rowVersion AND c.version=@rowVersion)) AND r.status='formalized' AND r.application_status IN('not_applied','scheduled','failed') FOR UPDATE OF r,c",new{tenantId,id,rowVersion},tx,cancellationToken:ct));
        if(row is null||row.EffectiveOn>today)return null;if(row.CurrentVersion!=row.BaseVersion){await c.ExecuteAsync(new CommandDefinition("UPDATE odca.contract_change_requests SET status='conflict',application_status='failed',row_version=row_version+1,updated_at=now() WHERE tenant_id=@tenantId AND id=@id",new{tenantId,id},tx,cancellationToken:ct));return null;}
        var changed=await c.ExecuteAsync(new CommandDefinition("UPDATE odca.contracts SET start_date=COALESCE(@start,start_date),end_date=COALESCE(@end,end_date),value=COALESCE(@value,value),currency=CASE WHEN @value IS NULL THEN currency ELSE @currency END,version=version+1,updated_at=now() WHERE tenant_id=@tenantId AND id=@contractId AND version=@baseVersion",new{start=row.StartDate,end=row.EndDate,value=row.Value,currency=row.Currency,tenantId,contractId=row.ContractId,baseVersion=row.BaseVersion},tx,cancellationToken:ct));if(changed!=1)return null;
        await c.ExecuteAsync(new CommandDefinition("INSERT INTO odca.contract_change_applications(tenant_id,request_id,contract_id,applied_by,before_data,after_data) VALUES(@tenantId,@id,@contractId,@actor,jsonb_build_object('startDate',@oldStart,'endDate',@oldEnd,'value',@oldValue,'currency',@oldCurrency),jsonb_build_object('startDate',COALESCE(@start,@oldStart),'endDate',COALESCE(@end,@oldEnd),'value',COALESCE(@value,@oldValue),'currency',COALESCE(@currency,@oldCurrency))) ON CONFLICT(tenant_id,request_id) DO NOTHING; UPDATE odca.contract_change_requests SET application_status='applied',row_version=row_version+1,updated_at=now() WHERE tenant_id=@tenantId AND id=@id",new{tenantId,id,contractId=row.ContractId,actor,oldStart=row.OldStart,oldEnd=row.OldEnd,oldValue=row.OldValue,oldCurrency=row.OldCurrency,start=row.StartDate,end=row.EndDate,value=row.Value,currency=row.Currency},tx,cancellationToken:ct));
        return new ApplyRenewalResponse(true, row.CurrentVersion + 1, true, row.EndDate ?? row.OldEnd);
    }

    private Guid? Actor()=>Guid.TryParse(User.FindFirstValue("sub"),out var id)?id:null;
    private static Task<DateOnly> OrganizationToday(NpgsqlConnection c,Guid tenant,NpgsqlTransaction tx,CancellationToken ct)=>c.ExecuteScalarAsync<DateOnly>(new CommandDefinition("SELECT (now() AT TIME ZONE timezone)::date FROM odca.tenants WHERE id=@tenant",new{tenant},tx,cancellationToken:ct));
    private static Task<bool> Allowed(NpgsqlConnection c,Guid actor,Guid tenant,string permission,CancellationToken ct)=>c.ExecuteScalarAsync<bool>(new CommandDefinition("SELECT odca.tenant_actor_has_permission(@actor,@tenant,@permission)",new{actor,tenant,permission},cancellationToken:ct));
    private static Task<int> SetTenant(NpgsqlConnection c,Guid tenant,Guid actor,NpgsqlTransaction tx,CancellationToken ct)=>c.ExecuteAsync(new CommandDefinition("SELECT set_config('odca.tenant_id',@tenant,true),set_config('odca.actor_id',@actor,true)",new{tenant=tenant.ToString(),actor=actor.ToString()},tx,cancellationToken:ct));
    private sealed record CountRow(int Expiring,int Expired,int Preparing,int InReview,int Scheduled,int NotRenewing);
    private sealed record ContractBase(DateOnly? StartDate,DateOnly? EndDate,decimal? Value,string? Currency,Guid? OwnerId,long Version);
    private sealed record ApplyRow(Guid ContractId,long BaseVersion,DateOnly? StartDate,DateOnly? EndDate,decimal? Value,string? Currency,DateOnly EffectiveOn,long CurrentVersion,DateOnly? OldStart,DateOnly? OldEnd,decimal? OldValue,string? OldCurrency);
}
