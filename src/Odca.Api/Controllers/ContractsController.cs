using System.Security.Claims;
using Dapper;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Npgsql;

namespace Odca.Api.Controllers;

[ApiController]
[Authorize(Policy="PasswordChanged")]
[Route("api/v1/organizations/{tenantId:guid}/contracts")]
public sealed class ContractsController(NpgsqlDataSource dataSource) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> List(Guid tenantId,[FromQuery] string? q,[FromQuery] string? status,[FromQuery] int page = 1,[FromQuery] int pageSize = 20,CancellationToken ct = default)
    {
        var actor=Actor();if(actor is null)return Unauthorized();
        page=Math.Max(page,1);pageSize=Math.Clamp(pageSize,1,50);
        var state=status?.Trim().ToLowerInvariant();
        if(state is not (null or "" or "active" or "archived" or "closed" or "all"))
            return ValidationProblem(new ValidationProblemDetails(new Dictionary<string,string[]>{{"status",["Use active, archived, closed ou all."]}}));
        state ??= "active";
        await using var c=await dataSource.OpenConnectionAsync(ct);
        if(!await Allowed(c,actor.Value,tenantId,"tenant.contracts.read","tenant.contract_drafts.read",ct))return Forbid();
        await using var tx=await c.BeginTransactionAsync(ct);await SetTenant(c,tenantId,actor.Value,tx,ct);
        var term=string.IsNullOrWhiteSpace(q)?null:$"%{q.Trim()}%";
        var filter=state switch
        {
            "active" => "c.archived_at IS NULL",
            "archived" => "c.archived_at IS NOT NULL AND c.closed_at IS NULL",
            "closed" => "c.closed_at IS NOT NULL",
            _ => "true"
        };
        var search="(@term IS NULL OR c.title ILIKE @term OR c.reference ILIKE @term OR c.counterparty ILIKE @term)";
        var total=await c.ExecuteScalarAsync<int>(new CommandDefinition($"SELECT count(*)::int FROM odca.contracts c WHERE c.tenant_id=@tenantId AND {filter} AND {search}",new{tenantId,term},tx,cancellationToken:ct));
        var items=await c.QueryAsync<ContractListItem>(new CommandDefinition($"""
            SELECT c.id AS Id,c.title AS Title,c.reference AS Reference,c.contract_type AS ContractType,c.counterparty AS Counterparty,
              c.start_date AS StartDate,c.end_date AS EndDate,c.value AS Value,c.currency AS Currency,c.renewal_notice_days AS RenewalNoticeDays,
              c.owner_id AS OwnerId,u.display_name AS Owner,c.version AS Version,
              latest.Number AS LatestVersionNumber,latest.Status AS LatestReviewStatus,
              c.archived_at AS ArchivedAt,c.archive_reason AS ArchiveReason,c.closed_at AS ClosedAt,c.closed_on AS ClosedOn,c.closure_reason AS ClosureReason,c.updated_at AS UpdatedAt
            FROM odca.contracts c
            LEFT JOIN LATERAL (SELECT v.version_number AS Number,v.review_status AS Status FROM odca.generated_contract_versions v
              WHERE v.tenant_id=c.tenant_id AND v.contract_id=c.id ORDER BY v.version_number DESC LIMIT 1) latest ON true
            LEFT JOIN odca.memberships m ON (m.tenant_id,m.user_id)=(c.tenant_id,c.owner_id)
            LEFT JOIN odca.users u ON u.id=m.user_id
            WHERE c.tenant_id=@tenantId AND {filter} AND {search}
            ORDER BY c.updated_at DESC,c.id DESC LIMIT @pageSize OFFSET @offset
            """,new{tenantId,term,pageSize,offset=(page-1)*pageSize},tx,cancellationToken:ct));
        await tx.CommitAsync(ct);
        return Ok(new{items=items.AsList(),page,pageSize,total});
    }

    [HttpGet("{contractId:guid}")]
    public async Task<IActionResult> Detail(Guid tenantId,Guid contractId,CancellationToken ct)
    {
        var actor=Actor();if(actor is null)return Unauthorized();
        await using var c=await dataSource.OpenConnectionAsync(ct);
        if(!await Allowed(c,actor.Value,tenantId,"tenant.contracts.read","tenant.contract_drafts.read",ct))return Forbid();
        await using var tx=await c.BeginTransactionAsync(ct);await SetTenant(c,tenantId,actor.Value,tx,ct);
        var item=await c.QuerySingleOrDefaultAsync<ContractListItem>(new CommandDefinition("""
            SELECT c.id AS Id,c.title AS Title,c.reference AS Reference,c.contract_type AS ContractType,c.counterparty AS Counterparty,
              c.start_date AS StartDate,c.end_date AS EndDate,c.value AS Value,c.currency AS Currency,c.renewal_notice_days AS RenewalNoticeDays,
              c.owner_id AS OwnerId,u.display_name AS Owner,c.version AS Version,
              latest.Number AS LatestVersionNumber,latest.Status AS LatestReviewStatus,
              c.archived_at AS ArchivedAt,c.archive_reason AS ArchiveReason,c.closed_at AS ClosedAt,c.closed_on AS ClosedOn,c.closure_reason AS ClosureReason,c.updated_at AS UpdatedAt
            FROM odca.contracts c
            LEFT JOIN LATERAL (SELECT v.version_number AS Number,v.review_status AS Status FROM odca.generated_contract_versions v
              WHERE v.tenant_id=c.tenant_id AND v.contract_id=c.id ORDER BY v.version_number DESC LIMIT 1) latest ON true
            LEFT JOIN odca.memberships m ON (m.tenant_id,m.user_id)=(c.tenant_id,c.owner_id)
            LEFT JOIN odca.users u ON u.id=m.user_id
            WHERE c.tenant_id=@tenantId AND c.id=@contractId
            """,new{tenantId,contractId},tx,cancellationToken:ct));
        if(item is null)return NotFound();
        await tx.CommitAsync(ct);return Ok(item);
    }

    [HttpGet("{contractId:guid}/events")]
    public async Task<IActionResult> History(Guid tenantId,Guid contractId,[FromQuery] int page = 1,[FromQuery] int pageSize = 50,CancellationToken ct = default)
    {
        var actor=Actor();if(actor is null)return Unauthorized();
        page=Math.Max(page,1);pageSize=Math.Clamp(pageSize,1,100);
        await using var c=await dataSource.OpenConnectionAsync(ct);
        if(!await Allowed(c,actor.Value,tenantId,"tenant.contracts.history","tenant.contracts.read",ct))return Forbid();
        await using var tx=await c.BeginTransactionAsync(ct);await SetTenant(c,tenantId,actor.Value,tx,ct);
        var exists=await c.ExecuteScalarAsync<bool>(new CommandDefinition("SELECT EXISTS(SELECT 1 FROM odca.contracts WHERE tenant_id=@tenantId AND id=@contractId)",new{tenantId,contractId},tx,cancellationToken:ct));
        if(!exists)return NotFound();
        var total=await c.ExecuteScalarAsync<int>(new CommandDefinition("SELECT count(*)::int FROM odca.contract_events WHERE tenant_id=@tenantId AND contract_id=@contractId",new{tenantId,contractId},tx,cancellationToken:ct));
        var items=await c.QueryAsync<ContractEventItem>(new CommandDefinition("""
            SELECT e.id AS Id,e.event_type AS Type,e.occurred_at AS OccurredAt,e.actor_id AS ActorId,u.display_name AS Actor,e.details::text AS Details
            FROM odca.contract_events e LEFT JOIN odca.users u ON u.id=e.actor_id
            WHERE e.tenant_id=@tenantId AND e.contract_id=@contractId ORDER BY e.occurred_at DESC,e.id DESC LIMIT @pageSize OFFSET @offset
            """,new{tenantId,contractId,pageSize,offset=(page-1)*pageSize},tx,cancellationToken:ct));
        await tx.CommitAsync(ct);
        return Ok(new{items=items.AsList(),page,pageSize,total});
    }

    [HttpPatch("{contractId:guid}")]
    public async Task<IActionResult> Update(Guid tenantId,Guid contractId,[FromQuery] long version,[FromBody] UpdateContractRequest request,CancellationToken ct)
    {
        var actor=Actor();if(actor is null)return Unauthorized();
        bool hasTitle=request.Title is not null,hasReference=request.Reference is not null;
        if(hasTitle&&request.Title!.Trim().Length is < 2 or > 160)
            return ValidationProblem(new ValidationProblemDetails(new Dictionary<string,string[]>{{"title",["O título precisa ter de 2 a 160 caracteres."]}}));
        if(hasReference&&request.Reference!.Trim().Length>160)
            return ValidationProblem(new ValidationProblemDetails(new Dictionary<string,string[]>{{"reference",["A referência precisa ter até 160 caracteres."]}}));
        if(!(hasTitle||hasReference||request.ClearOwner||request.OwnerId.HasValue))
            return ValidationProblem(new ValidationProblemDetails(new Dictionary<string,string[]>{{"body",["Informe ao menos um campo para atualizar."]}}));
        await using var c=await dataSource.OpenConnectionAsync(ct);
        if(!await Allowed(c,actor.Value,tenantId,"tenant.contracts.manage",ct))return Forbid();
        await using var tx=await c.BeginTransactionAsync(ct);await SetTenant(c,tenantId,actor.Value,tx,ct);
        if(request.OwnerId.HasValue)
        {
            var ownerActive=await c.ExecuteScalarAsync<bool>(new CommandDefinition("SELECT EXISTS(SELECT 1 FROM odca.memberships WHERE tenant_id=@tenantId AND user_id=@ownerId AND status='active')",new{tenantId,ownerId=request.OwnerId.Value},tx,cancellationToken:ct));
            if(!ownerActive)return ValidationProblem(new ValidationProblemDetails(new Dictionary<string,string[]>{{"ownerId",["O responsável precisa ter vínculo ativo nesta organização."]}}));
        }
        var current=await c.QuerySingleOrDefaultAsync<ContractUpdateRow>(new CommandDefinition("SELECT version AS Version,title AS Title,reference AS Reference,owner_id AS OwnerId FROM odca.contracts WHERE tenant_id=@tenantId AND id=@contractId FOR UPDATE",new{tenantId,contractId},tx,cancellationToken:ct));
        if(current is null)return NotFound();
        if(current.Version!=version)return Conflict(new{title="O contrato foi alterado em outra sessão. Recarregue e reaplique suas mudanças.",code="contract.version.conflict",currentVersion=current.Version});
        string title=hasTitle?request.Title!.Trim():current.Title;
        string? reference=hasReference?(request.Reference!.Trim().Length==0?null:request.Reference.Trim()):current.Reference;
        string ownerExpr=request.ClearOwner?"NULL":request.OwnerId.HasValue?"@ownerId":"owner_id";
        long newVersion=current.Version+1;
        var item=await c.QuerySingleOrDefaultAsync<ContractListItem>(new CommandDefinition($"""
            UPDATE odca.contracts SET title=@title,reference=@reference,owner_id={ownerExpr},version=version+1,updated_at=now()
            WHERE tenant_id=@tenantId AND id=@contractId
            RETURNING id AS Id,title AS Title,reference AS Reference,contract_type AS ContractType,counterparty AS Counterparty,
              start_date AS StartDate,end_date AS EndDate,value AS Value,currency AS Currency,renewal_notice_days AS RenewalNoticeDays,
              owner_id AS OwnerId,version AS Version,
              archived_at AS ArchivedAt,archive_reason AS ArchiveReason,closed_at AS ClosedAt,closed_on AS ClosedOn,closure_reason AS ClosureReason,updated_at AS UpdatedAt
            """,new{tenantId,contractId,title,reference,ownerId=(object?)request.OwnerId??Guid.Empty},tx,cancellationToken:ct));
        await c.ExecuteAsync(new CommandDefinition("""
            INSERT INTO odca.contract_events(tenant_id,contract_id,actor_id,event_type,details)
            VALUES(@tenantId,@contractId,@actor,'contract.updated',jsonb_build_object('changed',jsonb_build_object('title',@changedTitle,'reference',@changedReference,'owner',@changedOwner)))
            """,new{tenantId,contractId,actor,changedTitle=hasTitle,changedReference=hasReference,changedOwner=request.ClearOwner||request.OwnerId.HasValue},tx,cancellationToken:ct));
        await c.ExecuteAsync(new CommandDefinition("""
            INSERT INTO odca.audit_events(scope_type,tenant_id,actor_user_id,action,entity_type,entity_id,result,metadata)
            VALUES('tenant',@tenantId,@actor,'contract.updated','contract',@contractId,'success',jsonb_build_object('version',@newVersion))
            """,new{tenantId,actor,contractId,newVersion},tx,cancellationToken:ct));
        await tx.CommitAsync(ct);
        return Ok(item);
    }

    [HttpPost("{contractId:guid}/duplicate")]
    public async Task<IActionResult> Duplicate(Guid tenantId,Guid contractId,CancellationToken ct)
    {
        var actor=Actor();if(actor is null)return Unauthorized();
        await using var c=await dataSource.OpenConnectionAsync(ct);
        if(!await Allowed(c,actor.Value,tenantId,"tenant.contracts.manage",ct))return Forbid();
        await using var tx=await c.BeginTransactionAsync(ct);await SetTenant(c,tenantId,actor.Value,tx,ct);
        var source=await c.QuerySingleOrDefaultAsync<DuplicateSourceRow>(new CommandDefinition("""
            SELECT c.title AS Title,c.reference AS Reference,c.contract_type AS ContractType,c.counterparty AS Counterparty,
              c.start_date AS StartDate,c.end_date AS EndDate,c.value AS Value,c.currency AS Currency,c.renewal_notice_days AS RenewalNoticeDays,
              c.owner_id AS OwnerId,c.renewal_policy AS RenewalPolicy,c.renewal_notice_amount AS RenewalNoticeAmount,c.renewal_notice_unit AS RenewalNoticeUnit,
              c.renewal_decision_owner_id AS RenewalDecisionOwnerId,c.renewal_policy_notes AS RenewalPolicyNotes,c.renewal_policy_reference AS RenewalPolicyReference,
              c.closed_at AS ClosedAt,d.id AS DraftId,d.source_template_id AS TemplateId,d.source_template_version_id AS TemplateVersionId,
              d.content::text AS Content,d.fields::text AS Fields,d.values::text AS Values,d.patient_id AS PatientId,d.patient_row_version AS PatientRowVersion,
              d.patient_selection_snapshot::text AS PatientSnapshot
            FROM odca.contracts c LEFT JOIN odca.contract_drafts d ON (d.tenant_id,d.contract_id)=(c.tenant_id,c.id)
            WHERE c.tenant_id=@tenantId AND c.id=@contractId FOR UPDATE OF c
            """,new{tenantId,contractId},tx,cancellationToken:ct));
        if(source is null)return NotFound();
        if(source.ClosedAt is not null)return Conflict(new{title="Um contrato encerrado não pode ser duplicado. Crie um novo contrato se precisar recomeçar.",code="contract.closed"});
        if(source.DraftId is null)return Conflict(new{title="Este contrato não possui minuta vinculada e não pode ser duplicado.",code="contract.no_draft"});
        var baseTitle=source.Title.Length>152?source.Title[..152].TrimEnd():source.Title;
        string newTitle=baseTitle+" (cópia)";
        Guid targetContractId=Guid.NewGuid(),targetDraftId=Guid.NewGuid();
        await c.ExecuteAsync(new CommandDefinition("""
            INSERT INTO odca.contracts(id,tenant_id,title,reference,contract_type,counterparty,start_date,end_date,value,currency,renewal_notice_days,owner_id,renewal_policy,renewal_notice_amount,renewal_notice_unit,renewal_decision_owner_id,renewal_policy_notes,renewal_policy_reference)
            VALUES(@targetContractId,@tenantId,@title,@reference,@contractType,@counterparty,@startDate,@endDate,@value,@currency,@renewalNoticeDays,@ownerId,@renewalPolicy,@renewalNoticeAmount,@renewalNoticeUnit,@renewalDecisionOwnerId,@renewalPolicyNotes,@renewalPolicyReference);
            INSERT INTO odca.contract_drafts(id,tenant_id,contract_id,source_template_id,source_template_version_id,content,fields,values,created_by,updated_by,patient_id,patient_row_version,patient_selection_snapshot)
            VALUES(@targetDraftId,@tenantId,@targetContractId,@templateId,@templateVersionId,@content::jsonb,@fields::jsonb,@values::jsonb,@actor,@actor,@patientId,@patientRowVersion,@patientSnapshot::jsonb);
            INSERT INTO odca.contract_events(tenant_id,contract_id,actor_id,event_type,details)
            VALUES(@tenantId,@targetContractId,@actor,'contract.duplicated',jsonb_build_object('sourceContractId',@contractId,'sourceDraftId',@draftId));
            INSERT INTO odca.audit_events(scope_type,tenant_id,actor_user_id,action,entity_type,entity_id,result,metadata)
            VALUES('tenant',@tenantId,@actor,'contract.duplicated','contract',@targetContractId,'success',jsonb_build_object('sourceContractId',@contractId));
            """,new{
                tenantId,contractId,targetContractId,targetDraftId,title=newTitle,reference=source.Reference,contractType=source.ContractType,counterparty=source.Counterparty,
                startDate=source.StartDate,endDate=source.EndDate,value=source.Value,currency=source.Currency,renewalNoticeDays=source.RenewalNoticeDays,ownerId=source.OwnerId,
                renewalPolicy=source.RenewalPolicy,renewalNoticeAmount=source.RenewalNoticeAmount,renewalNoticeUnit=source.RenewalNoticeUnit,
                renewalDecisionOwnerId=source.RenewalDecisionOwnerId,renewalPolicyNotes=source.RenewalPolicyNotes,renewalPolicyReference=source.RenewalPolicyReference,
                actor,draftId=source.DraftId,templateId=source.TemplateId,templateVersionId=source.TemplateVersionId,content=source.Content,fields=source.Fields,values=source.Values,
                patientId=source.PatientId,patientRowVersion=source.PatientRowVersion,patientSnapshot=source.PatientSnapshot
            },tx,cancellationToken:ct));
        await tx.CommitAsync(ct);
        return Created($"/api/v1/organizations/{tenantId}/studio/drafts/{targetDraftId}",new{contractId=targetContractId,draftId=targetDraftId});
    }

    [HttpPost("{contractId:guid}/archive")]
    public async Task<IActionResult> Archive(Guid tenantId,Guid contractId,[FromQuery] long version,[FromBody] ArchiveContractRequest? request,CancellationToken ct)
    {
        var actor=Actor();if(actor is null)return Unauthorized();
        string? reason=null;
        if(request?.Reason is { } raw)
        {
            reason=raw.Trim();
            if(reason.Length>500)return ValidationProblem(new ValidationProblemDetails(new Dictionary<string,string[]>{{"reason",["A justificativa precisa ter até 500 caracteres."]}}));
            if(reason.Length==0)reason=null;
        }
        await using var c=await dataSource.OpenConnectionAsync(ct);
        if(!await Allowed(c,actor.Value,tenantId,"tenant.contracts.manage",ct))return Forbid();
        await using var tx=await c.BeginTransactionAsync(ct);await SetTenant(c,tenantId,actor.Value,tx,ct);
        var current=await c.QuerySingleOrDefaultAsync<ContractLifecycleRow>(new CommandDefinition("SELECT version AS Version,archived_at AS ArchivedAt FROM odca.contracts WHERE tenant_id=@tenantId AND id=@contractId FOR UPDATE",new{tenantId,contractId},tx,cancellationToken:ct));
        if(current is null)return NotFound();
        if(current.Version!=version)return Conflict(new{title="O contrato foi alterado em outra sessão. Recarregue e repita a ação.",code="contract.version.conflict",currentVersion=current.Version});
        if(current.ArchivedAt is not null){await tx.CommitAsync(ct);return Ok(new{replayed=true,version=current.Version});}
        long newVersion=current.Version+1;
        await c.ExecuteAsync(new CommandDefinition("UPDATE odca.contracts SET archived_at=now(),archived_by=@actor,archive_reason=@reason,version=version+1,updated_at=now() WHERE tenant_id=@tenantId AND id=@contractId",new{tenantId,contractId,actor,reason},tx,cancellationToken:ct));
        await c.ExecuteAsync(new CommandDefinition("""
            INSERT INTO odca.contract_events(tenant_id,contract_id,actor_id,event_type,details)
            VALUES(@tenantId,@contractId,@actor,'contract.archived',jsonb_build_object('reason',@reason))
            """,new{tenantId,contractId,actor,reason},tx,cancellationToken:ct));
        await c.ExecuteAsync(new CommandDefinition("""
            INSERT INTO odca.audit_events(scope_type,tenant_id,actor_user_id,action,entity_type,entity_id,result,metadata)
            VALUES('tenant',@tenantId,@actor,'contract.archived','contract',@contractId,'success',jsonb_build_object('version',@newVersion,'reason',@reason))
            """,new{tenantId,actor,contractId,newVersion,reason},tx,cancellationToken:ct));
        await tx.CommitAsync(ct);
        return Ok(new{replayed=false,version=newVersion});
    }

    [HttpPost("{contractId:guid}/restore")]
    public async Task<IActionResult> Restore(Guid tenantId,Guid contractId,[FromQuery] long version,CancellationToken ct)
    {
        var actor=Actor();if(actor is null)return Unauthorized();
        await using var c=await dataSource.OpenConnectionAsync(ct);
        if(!await Allowed(c,actor.Value,tenantId,"tenant.contracts.manage",ct))return Forbid();
        await using var tx=await c.BeginTransactionAsync(ct);await SetTenant(c,tenantId,actor.Value,tx,ct);
        var current=await c.QuerySingleOrDefaultAsync<ContractLifecycleRow>(new CommandDefinition("SELECT version AS Version,archived_at AS ArchivedAt FROM odca.contracts WHERE tenant_id=@tenantId AND id=@contractId FOR UPDATE",new{tenantId,contractId},tx,cancellationToken:ct));
        if(current is null)return NotFound();
        if(current.Version!=version)return Conflict(new{title="O contrato foi alterado em outra sessão. Recarregue e repita a ação.",code="contract.version.conflict",currentVersion=current.Version});
        if(current.ArchivedAt is null){await tx.CommitAsync(ct);return Ok(new{replayed=true,version=current.Version});}
        long newVersion=current.Version+1;
        await c.ExecuteAsync(new CommandDefinition("UPDATE odca.contracts SET archived_at=NULL,archived_by=NULL,archive_reason=NULL,version=version+1,updated_at=now() WHERE tenant_id=@tenantId AND id=@contractId",new{tenantId,contractId},tx,cancellationToken:ct));
        await c.ExecuteAsync(new CommandDefinition("""
            INSERT INTO odca.contract_events(tenant_id,contract_id,actor_id,event_type,details)
            VALUES(@tenantId,@contractId,@actor,'contract.restored',jsonb_build_object())
            """,new{tenantId,contractId,actor},tx,cancellationToken:ct));
        await c.ExecuteAsync(new CommandDefinition("""
            INSERT INTO odca.audit_events(scope_type,tenant_id,actor_user_id,action,entity_type,entity_id,result,metadata)
            VALUES('tenant',@tenantId,@actor,'contract.restored','contract',@contractId,'success',jsonb_build_object('version',@newVersion))
            """,new{tenantId,actor,contractId,newVersion},tx,cancellationToken:ct));
        await tx.CommitAsync(ct);
        return Ok(new{replayed=false,version=newVersion});
    }

    [HttpPost("{contractId:guid}/close")]
    public async Task<IActionResult> Close(Guid tenantId,Guid contractId,[FromQuery] long version,[FromBody] CloseContractRequest request,CancellationToken ct)
    {
        var actor=Actor();if(actor is null)return Unauthorized();
        if(request.ClosedOn is null)
            return ValidationProblem(new ValidationProblemDetails(new Dictionary<string,string[]>{{"closedOn",["Informe a data de encerramento."]}}));
        var reason=request.Reason?.Trim();
        if(string.IsNullOrWhiteSpace(reason)||reason.Length is < 5 or > 2000)
            return ValidationProblem(new ValidationProblemDetails(new Dictionary<string,string[]>{{"reason",["Descreva o motivo do encerramento com 5 a 2000 caracteres."]}}));
        await using var c=await dataSource.OpenConnectionAsync(ct);
        if(!await Allowed(c,actor.Value,tenantId,"tenant.contracts.manage",ct))return Forbid();
        await using var tx=await c.BeginTransactionAsync(ct);await SetTenant(c,tenantId,actor.Value,tx,ct);
        var current=await c.QuerySingleOrDefaultAsync<ContractLifecycleRow>(new CommandDefinition("SELECT version AS Version,archived_at AS ArchivedAt,closed_at AS ClosedAt,start_date AS StartDate FROM odca.contracts WHERE tenant_id=@tenantId AND id=@contractId FOR UPDATE",new{tenantId,contractId},tx,cancellationToken:ct));
        if(current is null)return NotFound();
        if(current.Version!=version)return Conflict(new{title="O contrato foi alterado em outra sessão. Recarregue e repita a ação.",code="contract.version.conflict",currentVersion=current.Version});
        if(current.ClosedAt is not null){await tx.CommitAsync(ct);return Ok(new{replayed=true,version=current.Version});}
        if(current.StartDate is not null&&request.ClosedOn.Value<current.StartDate)
            return ValidationProblem(new ValidationProblemDetails(new Dictionary<string,string[]>{{"closedOn",["A data de encerramento não pode ser anterior ao início da vigência."]}}));
        long newVersion=current.Version+1;
        await c.ExecuteAsync(new CommandDefinition("""
            UPDATE odca.contracts SET closed_at=now(),closed_by=@actor,closed_on=@closedOn,closure_reason=@reason,
              archived_at=COALESCE(archived_at,now()),archived_by=COALESCE(archived_by,@actor),version=version+1,updated_at=now()
            WHERE tenant_id=@tenantId AND id=@contractId
            """,new{tenantId,contractId,actor,closedOn=request.ClosedOn.Value,reason},tx,cancellationToken:ct));
        await c.ExecuteAsync(new CommandDefinition("""
            INSERT INTO odca.contract_events(tenant_id,contract_id,actor_id,event_type,details)
            VALUES(@tenantId,@contractId,@actor,'contract.closed',jsonb_build_object('closedOn',@closedOn,'reason',@reason))
            """,new{tenantId,contractId,actor,closedOn=request.ClosedOn.Value,reason},tx,cancellationToken:ct));
        await c.ExecuteAsync(new CommandDefinition("""
            INSERT INTO odca.audit_events(scope_type,tenant_id,actor_user_id,action,entity_type,entity_id,result,metadata)
            VALUES('tenant',@tenantId,@actor,'contract.closed','contract',@contractId,'success',jsonb_build_object('version',@newVersion,'closedOn',@closedOn,'reason',@reason))
            """,new{tenantId,actor,contractId,newVersion,closedOn=request.ClosedOn.Value,reason},tx,cancellationToken:ct));
        await tx.CommitAsync(ct);
        return Ok(new{replayed=false,version=newVersion});
    }

    private Guid? Actor()=>Guid.TryParse(User.FindFirstValue("sub"),out var id)?id:null;
    private static Task<bool> Allowed(NpgsqlConnection c,Guid actor,Guid tenant,string permission,CancellationToken ct)=>c.ExecuteScalarAsync<bool>(new CommandDefinition("SELECT odca.tenant_actor_has_permission(@actor,@tenant,@permission)",new{actor,tenant,permission},cancellationToken:ct));
    private static Task<bool> Allowed(NpgsqlConnection c,Guid actor,Guid tenant,string primary,string fallback,CancellationToken ct)=>c.ExecuteScalarAsync<bool>(new CommandDefinition("SELECT odca.tenant_actor_has_permission(@actor,@tenant,@primary) OR odca.tenant_actor_has_permission(@actor,@tenant,@fallback)",new{actor,tenant,primary,fallback},cancellationToken:ct));
    private static Task<int> SetTenant(NpgsqlConnection c,Guid tenant,Guid actor,NpgsqlTransaction tx,CancellationToken ct)=>c.ExecuteAsync(new CommandDefinition("SELECT set_config('odca.tenant_id',@value,true),set_config('odca.actor_id',@actor,true)",new{value=tenant.ToString(),actor=actor.ToString()},tx,cancellationToken:ct));

    public sealed record UpdateContractRequest(string? Title,string? Reference,Guid? OwnerId,bool ClearOwner);
    public sealed record ArchiveContractRequest(string? Reason);
    public sealed record CloseContractRequest(DateOnly? ClosedOn,string? Reason);
    private sealed class ContractUpdateRow
    {
        public long Version { get; init; }
        public string Title { get; init; } = "";
        public string? Reference { get; init; }
        public Guid? OwnerId { get; init; }
    }
    private sealed class ContractLifecycleRow
    {
        public long Version { get; init; }
        public DateTime? ArchivedAt { get; init; }
        public DateTime? ClosedAt { get; init; }
        public DateOnly? StartDate { get; init; }
    }
    private sealed class DuplicateSourceRow
    {
        public string Title { get; init; } = "";
        public string? Reference { get; init; }
        public string? ContractType { get; init; }
        public string? Counterparty { get; init; }
        public DateOnly? StartDate { get; init; }
        public DateOnly? EndDate { get; init; }
        public decimal? Value { get; init; }
        public string? Currency { get; init; }
        public int? RenewalNoticeDays { get; init; }
        public Guid? OwnerId { get; init; }
        public string? RenewalPolicy { get; init; }
        public int? RenewalNoticeAmount { get; init; }
        public string? RenewalNoticeUnit { get; init; }
        public Guid? RenewalDecisionOwnerId { get; init; }
        public string? RenewalPolicyNotes { get; init; }
        public string? RenewalPolicyReference { get; init; }
        public DateTime? ClosedAt { get; init; }
        public Guid? DraftId { get; init; }
        public Guid TemplateId { get; init; }
        public Guid TemplateVersionId { get; init; }
        public string Content { get; init; } = "";
        public string Fields { get; init; } = "";
        public string Values { get; init; } = "";
        public Guid? PatientId { get; init; }
        public long? PatientRowVersion { get; init; }
        public string? PatientSnapshot { get; init; }
    }
    private sealed class ContractListItem
    {
        public Guid Id { get; init; }
        public string Title { get; init; } = "";
        public string? Reference { get; init; }
        public string? ContractType { get; init; }
        public string? Counterparty { get; init; }
        public DateOnly? StartDate { get; init; }
        public DateOnly? EndDate { get; init; }
        public decimal? Value { get; init; }
        public string? Currency { get; init; }
        public int? RenewalNoticeDays { get; init; }
        public Guid? OwnerId { get; init; }
        public string? Owner { get; init; }
        public long Version { get; init; }
        public int? LatestVersionNumber { get; init; }
        public string? LatestReviewStatus { get; init; }
        public DateTime? ArchivedAt { get; init; }
        public string? ArchiveReason { get; init; }
        public DateTime? ClosedAt { get; init; }
        public DateOnly? ClosedOn { get; init; }
        public string? ClosureReason { get; init; }
        public DateTime? UpdatedAt { get; init; }
    }
    private sealed class ContractEventItem
    {
        public long Id { get; init; }
        public string Type { get; init; } = "";
        public DateTime OccurredAt { get; init; }
        public Guid? ActorId { get; init; }
        public string? Actor { get; init; }
        public string? Details { get; init; }
    }
}
