using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Dapper;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Npgsql;
using Odca.Application.Contracts;
using Odca.Contracts.Studio;

namespace Odca.Api.Controllers;

[ApiController]
[Authorize(Policy = "PasswordChanged")]
[Route("api/v1/organizations/{tenantId:guid}/studio")]
public sealed class ContractStudioController(NpgsqlDataSource dataSource, IConfiguration configuration, ITemplateCatalogRepository templateCatalog) : ControllerBase
{
    [HttpGet("templates")]
    public async Task<IActionResult> Templates(Guid tenantId, [FromQuery] string? search, [FromQuery] string? type, [FromQuery] string? scope, [FromQuery] int page = 1, [FromQuery] int pageSize = 12, CancellationToken ct = default)
    {
        var actor = Actor(); if (actor is null) return Unauthorized();
        await using var c = await dataSource.OpenConnectionAsync(ct);
        if (!await Allowed(c, actor.Value, tenantId, "tenant.templates.read", ct)) return Forbid();
        page = Math.Max(page, 1); pageSize = Math.Clamp(pageSize, 1, 50);
        return Ok(await templateCatalog.ListAsync(tenantId, search, type, scope, page, pageSize, ct));
    }

    [HttpPost("templates")]
    public async Task<IActionResult> CreateTemplate(Guid tenantId,[FromBody] CreateTemplateRequest request,CancellationToken ct)
    {
        var actor=Actor();if(actor is null)return Unauthorized();if(string.IsNullOrWhiteSpace(request.Name)||request.Scope is not ("private" or "consultancy"))return ValidationProblem();
        try{Validate(request.Content.GetRawText(),request.Fields.GetRawText(),"[]",false);}catch(InvalidDataException e){return ValidationProblem(new ValidationProblemDetails(new Dictionary<string,string[]>{{"template",[e.Message]}}));}
        await using var c=await dataSource.OpenConnectionAsync(ct);if(!await Allowed(c,actor.Value,tenantId,"tenant.templates.manage",ct))return Forbid();await using var tx=await c.BeginTransactionAsync(ct);await SetTenant(c,tenantId,actor.Value,tx,ct);var id=Guid.NewGuid();var versionId=Guid.NewGuid();
        await c.ExecuteAsync(new CommandDefinition("INSERT INTO odca.contract_templates(id,owner_tenant_id,name,description,contract_type,scope,author_id) VALUES(@id,@tenantId,@name,@description,@contractType,@scope,@actor); INSERT INTO odca.contract_template_versions(id,template_id,version_number,content,fields,created_by) VALUES(@versionId,@id,1,@content::jsonb,@fields::jsonb,@actor); INSERT INTO odca.audit_events(scope_type,tenant_id,actor_user_id,action,entity_type,entity_id,result,metadata) VALUES('tenant',@tenantId,@actor,'template.created','contract_template',@id,'success',jsonb_build_object('scope',@scope))",new{id,tenantId,name=request.Name.Trim(),request.Description,request.ContractType,request.Scope,actor,versionId,content=request.Content.GetRawText(),fields=request.Fields.GetRawText()},tx,cancellationToken:ct));await tx.CommitAsync(ct);return Created($"/api/v1/organizations/{tenantId}/studio/templates/{id}",new TemplateMutationResponse(id,1,1,"draft"));
    }

    [HttpPut("templates/{templateId:guid}")]
    public async Task<IActionResult> UpdateTemplate(Guid tenantId,Guid templateId,[FromBody] UpdateTemplateRequest request,CancellationToken ct)
    {
        var actor=Actor();if(actor is null)return Unauthorized();try{Validate(request.Content.GetRawText(),request.Fields.GetRawText(),"[]",false);}catch(InvalidDataException e){return ValidationProblem(new ValidationProblemDetails(new Dictionary<string,string[]>{{"template",[e.Message]}}));}
        await using var c=await dataSource.OpenConnectionAsync(ct);if(!await Allowed(c,actor.Value,tenantId,"tenant.templates.manage",ct))return Forbid();await using var tx=await c.BeginTransactionAsync(ct);await SetTenant(c,tenantId,actor.Value,tx,ct);
        var changed=await c.ExecuteScalarAsync<bool>(new CommandDefinition("WITH changed AS (UPDATE odca.contract_templates SET name=@name,description=@description,contract_type=@contractType,row_version=row_version+1 WHERE id=@templateId AND owner_tenant_id=@tenantId AND status='draft' AND row_version=@expected RETURNING current_version) UPDATE odca.contract_template_versions v SET content=@content::jsonb,fields=@fields::jsonb FROM changed WHERE v.template_id=@templateId AND v.version_number=changed.current_version RETURNING true",new{name=request.Name.Trim(),request.Description,request.ContractType,templateId,tenantId,expected=request.ExpectedVersion,content=request.Content.GetRawText(),fields=request.Fields.GetRawText()},tx,cancellationToken:ct));if(!changed)return Conflict(new{title="O modelo foi alterado, publicado ou não pertence à organização."});await tx.CommitAsync(ct);return Ok();
    }

    [HttpPost("templates/{templateId:guid}/publish")]
    public async Task<IActionResult> PublishTemplate(Guid tenantId,Guid templateId,[FromQuery] long expectedVersion,CancellationToken ct)
    {
        var actor=Actor();if(actor is null)return Unauthorized();await using var c=await dataSource.OpenConnectionAsync(ct);if(!await Allowed(c,actor.Value,tenantId,"tenant.templates.manage",ct))return Forbid();await using var tx=await c.BeginTransactionAsync(ct);await SetTenant(c,tenantId,actor.Value,tx,ct);var source=await c.QuerySingleOrDefaultAsync<TemplateSource>(new CommandDefinition("SELECT t.id AS TemplateId,v.id AS VersionId,v.content::text AS Content,v.fields::text AS Fields FROM odca.contract_templates t JOIN odca.contract_template_versions v ON v.template_id=t.id AND v.version_number=t.current_version WHERE t.id=@templateId AND t.owner_tenant_id=@tenantId AND t.status='draft' AND t.row_version=@expectedVersion FOR UPDATE",new{templateId,tenantId,expectedVersion},tx,cancellationToken:ct));if(source is null)return Conflict();try{Validate(source.Content,source.Fields,"[]",false);}catch(InvalidDataException e){return ValidationProblem(new ValidationProblemDetails(new Dictionary<string,string[]>{{"template",[e.Message]}}));}await c.ExecuteAsync(new CommandDefinition("UPDATE odca.contract_templates SET status='published',published_at=now(),row_version=row_version+1 WHERE id=@templateId; UPDATE odca.contract_template_versions SET published_at=now() WHERE id=@versionId; INSERT INTO odca.audit_events(scope_type,tenant_id,actor_user_id,action,entity_type,entity_id,result) VALUES('tenant',@tenantId,@actor,'template.published','contract_template',@templateId,'success')",new{templateId,source.VersionId,tenantId,actor},tx,cancellationToken:ct));await tx.CommitAsync(ct);return Ok(new TemplateMutationResponse(templateId,1,expectedVersion+1,"published"));
    }

    [HttpPost("templates/{templateId:guid}/duplicate")]
    public async Task<IActionResult> DuplicateTemplate(Guid tenantId,Guid templateId,CancellationToken ct)
    {
        var actor=Actor();if(actor is null)return Unauthorized();await using var c=await dataSource.OpenConnectionAsync(ct);if(!await Allowed(c,actor.Value,tenantId,"tenant.templates.manage",ct))return Forbid();await using var tx=await c.BeginTransactionAsync(ct);await SetTenant(c,tenantId,actor.Value,tx,ct);var id=Guid.NewGuid();var versionId=Guid.NewGuid();var count=await c.ExecuteAsync(new CommandDefinition("INSERT INTO odca.contract_templates(id,owner_tenant_id,name,description,contract_type,scope,author_id) SELECT @id,@tenantId,name||' — cópia',description,contract_type,'private',@actor FROM odca.contract_templates WHERE id=@templateId AND status='published' AND (scope='global' OR owner_tenant_id=@tenantId OR EXISTS(SELECT 1 FROM odca.contract_template_access WHERE template_id=@templateId AND tenant_id=@tenantId AND revoked_at IS NULL)); INSERT INTO odca.contract_template_versions(id,template_id,version_number,content,fields,created_by) SELECT @versionId,@id,1,v.content,v.fields,@actor FROM odca.contract_template_versions v WHERE v.template_id=@templateId AND v.version_number=(SELECT current_version FROM odca.contract_templates WHERE id=@templateId)",new{id,tenantId,actor,templateId,versionId},tx,cancellationToken:ct));if(count<2)return NotFound();await tx.CommitAsync(ct);return Created($"/api/v1/organizations/{tenantId}/studio/templates/{id}",new TemplateMutationResponse(id,1,1,"draft"));
    }

    [HttpPost("templates/{templateId:guid}/archive")]
    public async Task<IActionResult> ArchiveTemplate(Guid tenantId,Guid templateId,[FromQuery] long expectedVersion,CancellationToken ct)
    {var actor=Actor();if(actor is null)return Unauthorized();await using var c=await dataSource.OpenConnectionAsync(ct);if(!await Allowed(c,actor.Value,tenantId,"tenant.templates.manage",ct))return Forbid();var changed=await c.ExecuteAsync(new CommandDefinition("UPDATE odca.contract_templates SET status='archived',archived_at=now(),row_version=row_version+1 WHERE id=@templateId AND owner_tenant_id=@tenantId AND row_version=@expectedVersion AND status<>'archived'",new{templateId,tenantId,expectedVersion},cancellationToken:ct));return changed==1?NoContent():Conflict();}

    [HttpPost("drafts")]
    public async Task<IActionResult> CreateDraft(Guid tenantId, [FromBody] CreateDraftRequest request, CancellationToken ct)
    {
        var actor = Actor(); if (actor is null) return Unauthorized();
        if (string.IsNullOrWhiteSpace(request.Title) || request.Title.Trim().Length is < 2 or > 160) return ValidationProblem();
        await using var c = await dataSource.OpenConnectionAsync(ct);
        if (!await Allowed(c, actor.Value, tenantId, "tenant.contract_drafts.manage", ct)) return Forbid();
        await using var tx = await c.BeginTransactionAsync(ct); await SetTenant(c, tenantId, actor.Value, tx, ct);
        var source = await c.QuerySingleOrDefaultAsync<TemplateSource>(new CommandDefinition("""
            SELECT t.id AS TemplateId,v.id AS VersionId,v.content::text AS Content,v.fields::text AS Fields
            FROM odca.contract_templates t JOIN odca.contract_template_versions v ON v.template_id=t.id AND v.version_number=t.current_version
            WHERE t.id=@templateId AND t.status='published' AND (t.scope='global' OR t.owner_tenant_id=@tenantId OR EXISTS(SELECT 1 FROM odca.contract_template_access a WHERE a.template_id=t.id AND a.tenant_id=@tenantId AND a.revoked_at IS NULL))
            """, new { request.TemplateId, tenantId }, tx, cancellationToken: ct));
        if (source is null) return NotFound();
        if (request.PatientId is not null && !await c.ExecuteScalarAsync<bool>(new CommandDefinition("SELECT EXISTS(SELECT 1 FROM odca.patients WHERE tenant_id=@tenantId AND id=@PatientId AND inactive_at IS NULL)", new { tenantId, request.PatientId }, tx, cancellationToken: ct)))
            return ValidationProblem(new ValidationProblemDetails(new Dictionary<string,string[]>{{"patientId",["Selecione um paciente ativo desta organização."]}}));
        Validate(source.Content, source.Fields, "[]", false);
        var contractId = Guid.NewGuid(); var draftId = Guid.NewGuid();
        await c.ExecuteAsync(new CommandDefinition("""
            INSERT INTO odca.contracts(id,tenant_id,title,reference) VALUES(@contractId,@tenantId,@title,@reference);
            INSERT INTO odca.contract_drafts(id,tenant_id,contract_id,source_template_id,source_template_version_id,content,fields,created_by,updated_by,patient_id,patient_row_version,patient_selection_snapshot)
            VALUES(@draftId,@tenantId,@contractId,@templateId,@versionId,@content::jsonb,@fields::jsonb,@actor,@actor,@PatientId,
              (SELECT row_version FROM odca.patients WHERE tenant_id=@tenantId AND id=@PatientId),
              odca.patient_document_snapshot(@tenantId,@PatientId));
            INSERT INTO odca.contract_events(tenant_id,contract_id,actor_id,event_type,details) VALUES(@tenantId,@contractId,@actor,'draft.created',jsonb_build_object('templateId',@templateId,'templateVersionId',@versionId));
            """, new { contractId, tenantId, title=request.Title.Trim(), request.Reference, request.PatientId, draftId, templateId=source.TemplateId, versionId=source.VersionId, source.Content, source.Fields, actor }, tx, cancellationToken: ct));
        await tx.CommitAsync(ct);
        return Created($"/api/v1/organizations/{tenantId}/studio/drafts/{draftId}", new { id=draftId, contractId });
    }

    [HttpGet("drafts/{draftId:guid}/conference")]
    public async Task<IActionResult> Conference(Guid tenantId, Guid draftId, CancellationToken ct)
    {
        var actor = Actor(); if (actor is null) return Unauthorized();
        await using var c = await dataSource.OpenConnectionAsync(ct);
        if (!await Allowed(c, actor.Value, tenantId, "tenant.contract_drafts.read", ct)) return Forbid();
        var canEdit = await Allowed(c, actor.Value, tenantId, "tenant.contract_drafts.manage", ct);
        await using var tx = await c.BeginTransactionAsync(ct); await SetTenant(c, tenantId, actor.Value, tx, ct);
        var row = await c.QuerySingleOrDefaultAsync<ConferenceRow>(new CommandDefinition("""
            SELECT d.id AS DraftId,d.row_version AS DraftVersion,tenant.display_name AS Organization,t.name AS Template,
              tv.version_number AS TemplateVersion,t.contract_type AS DocumentType,t.status AS TemplateStatus,
              d.patient_id AS PatientId,p.full_name AS PatientName,r.full_name AS RepresentativeName,
              d.patient_row_version AS SelectedPatientVersion,p.row_version AS CurrentPatientVersion,
              (p.id IS NULL OR p.inactive_at IS NULL) AS PatientActive,d.patient_selection_snapshot::text AS SelectedPatientSnapshot,
              odca.patient_document_snapshot(@tenantId,d.patient_id)::text AS CurrentPatientSnapshot,
              d.content::text AS Content,d.fields::text AS Fields,d.values::text AS Values,
              coalesce((SELECT v.review_status FROM odca.generated_contract_versions v WHERE v.tenant_id=d.tenant_id AND v.draft_id=d.id ORDER BY v.version_number DESC LIMIT 1),'draft') AS ReviewStatus
            FROM odca.contract_drafts d
            JOIN odca.contracts c ON (c.tenant_id,c.id)=(d.tenant_id,d.contract_id)
            JOIN odca.tenants tenant ON tenant.id=d.tenant_id
            JOIN odca.contract_templates t ON t.id=d.source_template_id
            JOIN odca.contract_template_versions tv ON tv.id=d.source_template_version_id AND tv.template_id=t.id
            LEFT JOIN odca.patients p ON (p.tenant_id,p.id)=(d.tenant_id,d.patient_id)
            LEFT JOIN odca.patient_representatives r ON (r.tenant_id,r.id)=(p.tenant_id,p.representative_id)
            WHERE d.tenant_id=@tenantId AND d.id=@draftId
            """, new { tenantId, draftId }, tx, cancellationToken: ct));
        if (row is null) return NotFound();
        var checklist = ContractStudioAnalysis.Checklist(row.Content, row.Fields, row.Values, true, false, 0);
        var pending = checklist.Where(x => x.Severity == "blocker")
            .Select(x => new DocumentPendingItem(x.Code, x.Message,
                "A geração final exige que esta pendência seja resolvida.", x.Reference is null ? "Dados do documento" : $"Campo {x.Reference}", canEdit)).ToList();
        if (!row.PatientActive) pending.Add(new("patient.inactive", "O paciente está inativo.", "Cadastros inativos não permitem nova emissão.", "Ficha do paciente", canEdit));
        if (row.SelectedPatientVersion != row.CurrentPatientVersion) pending.Add(new("patient.version.conflict", "O cadastro do paciente foi atualizado.", "Os dados precisam ser comparados e confirmados antes da emissão.", "Comparação cadastral", canEdit));
        if (row.TemplateStatus != "published") pending.Add(new("template.unavailable", "O modelo não está disponível para nova geração.", "Somente modelos publicados podem originar uma nova emissão.", "Biblioteca de modelos", false));
        var changes = PatientChanges(row.SelectedPatientSnapshot, row.CurrentPatientSnapshot);
        var canGenerate = canEdit && pending.Count == 0;
        await tx.CommitAsync(ct);
        return Ok(new DocumentConferenceResponse(row.DraftId, row.DraftVersion, row.Organization, row.Template,
            row.TemplateVersion, row.DocumentType, row.TemplateStatus, row.PatientId, row.PatientName,
            row.RepresentativeName, row.SelectedPatientVersion, row.CurrentPatientVersion, row.PatientActive,
            canEdit, canGenerate, row.ReviewStatus, canGenerate ? "generate" : row.SelectedPatientVersion != row.CurrentPatientVersion ? "confirm_patient" : "complete_fields",
            changes, pending));
    }

    [HttpPost("drafts/{draftId:guid}/patient-confirmation")]
    public async Task<IActionResult> ConfirmPatient(Guid tenantId, Guid draftId, [FromBody] ConfirmPatientVersionRequest request, CancellationToken ct)
    {
        var actor = Actor(); if (actor is null) return Unauthorized();
        await using var c = await dataSource.OpenConnectionAsync(ct);
        if (!await Allowed(c, actor.Value, tenantId, "tenant.contract_drafts.manage", ct)) return Forbid();
        await using var tx = await c.BeginTransactionAsync(ct); await SetTenant(c, tenantId, actor.Value, tx, ct);
        var current = await c.QuerySingleOrDefaultAsync<PatientConfirmationRow>(new CommandDefinition("""
            SELECT d.contract_id AS ContractId,d.row_version AS DraftVersion,d.patient_row_version AS SelectedPatientVersion,
              p.row_version AS CurrentPatientVersion,p.inactive_at AS PatientInactiveAt
            FROM odca.contract_drafts d JOIN odca.patients p ON (p.tenant_id,p.id)=(d.tenant_id,d.patient_id)
            WHERE d.tenant_id=@tenantId AND d.id=@draftId FOR UPDATE OF d,p
            """, new { tenantId, draftId }, tx, cancellationToken: ct));
        if (current is null) return NotFound();
        if (current.PatientInactiveAt is not null) return Conflict(new { title="O paciente está inativo.", code="patient.inactive" });
        if (current.CurrentPatientVersion != request.ExpectedPatientVersion)
            return Conflict(new { title="O cadastro mudou novamente. Refaça a conferência.", code="patient.version.conflict", currentPatientVersion=current.CurrentPatientVersion });
        if (current.SelectedPatientVersion == current.CurrentPatientVersion) { await tx.CommitAsync(ct); return NoContent(); }
        if (current.DraftVersion != request.ExpectedDraftVersion)
            return Conflict(new { title="A minuta mudou durante a conferência. Recarregue sem perder os dados salvos.", code="draft.version.conflict", currentVersion=current.DraftVersion });
        var changed = await c.ExecuteAsync(new CommandDefinition("""
            UPDATE odca.contract_drafts SET patient_row_version=@patientVersion,
              patient_selection_snapshot=odca.patient_document_snapshot(@tenantId,patient_id),row_version=row_version+1,
              updated_by=@actor,updated_at=now()
            WHERE tenant_id=@tenantId AND id=@draftId AND row_version=@draftVersion;
            INSERT INTO odca.contract_events(tenant_id,contract_id,actor_id,event_type,details)
            VALUES(@tenantId,@contractId,@actor,'patient.reconfirmed',jsonb_build_object('previousVersion',@previousVersion,'acceptedVersion',@patientVersion));
            """, new { tenantId, draftId, actor, draftVersion=current.DraftVersion, patientVersion=current.CurrentPatientVersion,
                previousVersion=current.SelectedPatientVersion, current.ContractId }, tx, cancellationToken: ct));
        if (changed != 2) return Conflict(new { title="A conferência não pôde ser registrada.", code="draft.version.conflict" });
        await tx.CommitAsync(ct); return NoContent();
    }

    [HttpGet("drafts/{draftId:guid}")]
    public async Task<IActionResult> Draft(Guid tenantId, Guid draftId, CancellationToken ct)
    {
        var actor=Actor(); if(actor is null)return Unauthorized(); await using var c=await dataSource.OpenConnectionAsync(ct);
        if(!await Allowed(c,actor.Value,tenantId,"tenant.contract_drafts.read",ct))return Forbid(); await using var tx=await c.BeginTransactionAsync(ct); await SetTenant(c,tenantId,actor.Value,tx,ct);
        var row=await c.QuerySingleOrDefaultAsync<DraftRow>(new CommandDefinition("SELECT d.id AS Id,d.contract_id AS ContractId,c.title AS Title,d.source_template_id AS SourceTemplateId,d.source_template_version_id AS SourceTemplateVersionId,d.content::text AS Content,d.fields::text AS Fields,d.values::text AS Values,d.row_version AS Version,d.last_client_revision AS LastClientRevision,d.updated_at AS UpdatedAt FROM odca.contract_drafts d JOIN odca.contracts c ON c.id=d.contract_id AND c.tenant_id=d.tenant_id WHERE d.tenant_id=@tenantId AND d.id=@draftId",new{tenantId,draftId},tx,cancellationToken:ct));
        if(row is null)return NotFound(); await tx.CommitAsync(ct); return Ok(ToResponse(row));
    }

    [HttpPut("drafts/{draftId:guid}")]
    public async Task<IActionResult> Save(Guid tenantId,Guid draftId,[FromBody] SaveDraftRequest request,CancellationToken ct)
    {
        var actor=Actor();if(actor is null)return Unauthorized(); string content=request.Content.GetRawText(),fields=request.Fields.GetRawText(),values=request.Values.GetRawText();
        try{Validate(content,fields,values,false);}catch(InvalidDataException e){return ValidationProblem(new ValidationProblemDetails(new Dictionary<string,string[]>{{"document",[e.Message]}}));}
        await using var c=await dataSource.OpenConnectionAsync(ct);if(!await Allowed(c,actor.Value,tenantId,"tenant.contract_drafts.manage",ct))return Forbid();await using var tx=await c.BeginTransactionAsync(ct);await SetTenant(c,tenantId,actor.Value,tx,ct);
        var receipt=await c.QuerySingleOrDefaultAsync<SaveRow>(new CommandDefinition("SELECT saved_version AS Version,client_revision AS ClientRevision,saved_at AS SavedAt FROM odca.draft_save_receipts WHERE tenant_id=@tenantId AND draft_id=@draftId AND client_revision=@clientRevision",new{tenantId,draftId,request.ClientRevision},tx,cancellationToken:ct));if(receipt is not null){await tx.CommitAsync(ct);return Ok(new SaveDraftResponse(receipt.Version,receipt.ClientRevision,receipt.SavedAt));}
        var saved=await c.QuerySingleOrDefaultAsync<SaveRow>(new CommandDefinition("UPDATE odca.contract_drafts SET content=@content::jsonb,fields=@fields::jsonb,values=@values::jsonb,row_version=row_version+1,last_client_revision=@clientRevision,updated_by=@actor,updated_at=now() WHERE tenant_id=@tenantId AND id=@draftId AND row_version=@expectedVersion RETURNING row_version AS Version,last_client_revision AS ClientRevision,updated_at AS SavedAt",new{content,fields,values,request.ClientRevision,actor,tenantId,draftId,request.ExpectedVersion},tx,cancellationToken:ct));
        if(saved is null){var current=await c.ExecuteScalarAsync<long?>(new CommandDefinition("SELECT row_version FROM odca.contract_drafts WHERE tenant_id=@tenantId AND id=@draftId",new{tenantId,draftId},tx,cancellationToken:ct));return current is null?NotFound():Conflict(new{title="A minuta foi alterada em outra sessão.",currentVersion=current,clientRevision=request.ClientRevision});}
        await c.ExecuteAsync(new CommandDefinition("INSERT INTO odca.draft_save_receipts(tenant_id,draft_id,client_revision,saved_version,saved_at) VALUES(@tenantId,@draftId,@clientRevision,@version,@savedAt); INSERT INTO odca.contract_events(tenant_id,contract_id,actor_id,event_type,details) SELECT tenant_id,contract_id,@actor,'draft.saved',jsonb_build_object('version',row_version,'clientRevision',@clientRevision) FROM odca.contract_drafts WHERE id=@draftId AND tenant_id=@tenantId",new{actor,request.ClientRevision,draftId,tenantId,version=saved.Version,savedAt=saved.SavedAt},tx,cancellationToken:ct));await tx.CommitAsync(ct);return Ok(new SaveDraftResponse(saved.Version,saved.ClientRevision,saved.SavedAt));
    }

    [HttpPost("drafts/{draftId:guid}/versions")]
    public async Task<IActionResult> Generate(Guid tenantId,Guid draftId,[FromBody] GenerateVersionRequest request,CancellationToken ct)
    {
        var actor=Actor();if(actor is null)return Unauthorized();await using var c=await dataSource.OpenConnectionAsync(ct);if(!await Allowed(c,actor.Value,tenantId,"tenant.contract_drafts.manage",ct))return Forbid();await using var tx=await c.BeginTransactionAsync(ct);await SetTenant(c,tenantId,actor.Value,tx,ct);
        var existing=await c.QuerySingleOrDefaultAsync<GeneratedExistingRow>(new CommandDefinition("SELECT id AS Id,version_number AS Number,canonical_sha256 AS Sha256,byte_size AS ByteSize,created_at AS CreatedAt,review_status AS Status,draft_row_version AS DraftVersion FROM odca.generated_contract_versions WHERE tenant_id=@tenantId AND draft_id=@draftId AND idempotency_key=@idempotencyKey",new{tenantId,draftId,request.IdempotencyKey},tx,cancellationToken:ct));if(existing is not null){if(request.ExpectedVersion>0&&request.ExpectedVersion!=existing.DraftVersion)return Conflict(new{title="A chave de idempotência já foi usada com outra versão da minuta.",code="idempotency.payload.conflict"});await tx.CommitAsync(ct);return Ok(new GeneratedVersionResponse(existing.Id,existing.Number,existing.Sha256,existing.ByteSize,existing.CreatedAt,existing.Status));}
        var d=await c.QuerySingleOrDefaultAsync<DraftRow>(new CommandDefinition("SELECT d.id AS Id,d.contract_id AS ContractId,c.title AS Title,d.source_template_id AS SourceTemplateId,d.source_template_version_id AS SourceTemplateVersionId,d.content::text AS Content,d.fields::text AS Fields,d.values::text AS Values,d.row_version AS Version,d.last_client_revision AS LastClientRevision,d.updated_at AS UpdatedAt,d.patient_id AS PatientId,d.patient_row_version AS PatientVersion,t.status AS TemplateStatus,p.row_version AS CurrentPatientVersion,p.inactive_at AS PatientInactiveAt,odca.patient_document_snapshot(@tenantId,d.patient_id)::text AS PatientSnapshot FROM odca.contract_drafts d JOIN odca.contracts c ON c.id=d.contract_id AND c.tenant_id=d.tenant_id JOIN odca.contract_templates t ON t.id=d.source_template_id LEFT JOIN LATERAL (SELECT patient.row_version,patient.inactive_at FROM odca.patients patient WHERE (patient.tenant_id,patient.id)=(d.tenant_id,d.patient_id) FOR UPDATE) p ON true WHERE d.tenant_id=@tenantId AND d.id=@draftId FOR UPDATE OF d",new{tenantId,draftId},tx,cancellationToken:ct));if(d is null)return NotFound();
        if(d.TemplateStatus!="published")return Conflict(new{title="O modelo não está disponível para nova geração.",code="template.unavailable"});
        if(d.PatientId is not null && d.PatientInactiveAt is not null)return Conflict(new{title="O paciente está inativo.",detail="Restaure o cadastro antes de gerar um novo documento.",code="patient.inactive"});
        if(d.PatientId is not null && d.PatientVersion!=d.CurrentPatientVersion)return Conflict(new{title="Os dados do paciente mudaram durante a conferência.",detail="Crie uma nova minuta ou reconfirme os dados atualizados antes da emissão.",code="patient.version.conflict",currentPatientVersion=d.CurrentPatientVersion});
        if(request.ExpectedVersion>0 && d.Version!=request.ExpectedVersion)return Conflict(new{title="Salve e resolva o conflito antes de gerar a versão.",currentVersion=d.Version});try{Validate(d.Content,d.Fields,d.Values,true);}catch(InvalidDataException e){return ValidationProblem(new ValidationProblemDetails(new Dictionary<string,string[]>{{"pendingFields",[e.Message]}}));}
        var number=await c.ExecuteScalarAsync<int>(new CommandDefinition("SELECT coalesce(max(version_number),0)+1 FROM odca.generated_contract_versions WHERE draft_id=@draftId",new{draftId},tx,cancellationToken:ct));
        var snapshot=Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new{schemaVersion=2,patient=d.PatientSnapshot is null?(JsonElement?)null:JsonSerializer.Deserialize<JsonElement>(d.PatientSnapshot),content=JsonSerializer.Deserialize<JsonElement>(d.Content),fields=JsonSerializer.Deserialize<JsonElement>(d.Fields),values=JsonSerializer.Deserialize<JsonElement>(d.Values)}));var hash=Convert.ToHexString(SHA256.HashData(snapshot)).ToLowerInvariant();var id=Guid.NewGuid();var key=$"generated/{tenantId:N}/{d.ContractId:N}/{id:N}.json";var root=configuration["Documents:StoragePath"]??Path.Combine(AppContext.BaseDirectory,"App_Data","documents");var path=Path.Combine(root,key.Replace('/',Path.DirectorySeparatorChar));Directory.CreateDirectory(Path.GetDirectoryName(path)!);await System.IO.File.WriteAllBytesAsync(path,snapshot,ct);
        await c.ExecuteAsync(new CommandDefinition("INSERT INTO odca.generated_contract_versions(id,tenant_id,contract_id,draft_id,version_number,content_schema_version,content,fields,values,source_template_id,source_template_version_id,canonical_sha256,storage_key,byte_size,created_by,idempotency_key,draft_row_version,patient_id,patient_snapshot) VALUES(@id,@tenantId,@contractId,@draftId,@number,1,@content::jsonb,@fields::jsonb,@values::jsonb,@sourceTemplateId,@sourceTemplateVersionId,@hash,@key,@size,@actor,@idempotencyKey,@draftVersion,@PatientId,@PatientSnapshot::jsonb); INSERT INTO odca.contract_events(tenant_id,contract_id,actor_id,event_type,details) VALUES(@tenantId,@contractId,@actor,'version.generated',jsonb_build_object('versionId',@id,'number',@number,'sha256',@hash))",new{id,tenantId,contractId=d.ContractId,draftId,number,content=d.Content,fields=d.Fields,values=d.Values,d.SourceTemplateId,d.SourceTemplateVersionId,hash,key,size=snapshot.LongLength,actor,idempotencyKey=request.IdempotencyKey,draftVersion=d.Version,d.PatientId,d.PatientSnapshot},tx,cancellationToken:ct));await tx.CommitAsync(ct);return Created($"/api/v1/organizations/{tenantId}/studio/versions/{id}",new GeneratedVersionResponse(id,number,hash,snapshot.LongLength,DateTimeOffset.UtcNow,"generated"));
    }

    [HttpGet("reviewers")]
    public async Task<IActionResult> Reviewers(Guid tenantId,CancellationToken ct)
    {
        var actor=Actor();if(actor is null)return Unauthorized();await using var c=await dataSource.OpenConnectionAsync(ct);if(!await Allowed(c,actor.Value,tenantId,"tenant.reviews.request",ct))return Forbid();await using var tx=await c.BeginTransactionAsync(ct);await SetTenant(c,tenantId,actor.Value,tx,ct);
        var reviewers=await c.QueryAsync<StudioReviewerItem>(new CommandDefinition("SELECT u.id AS Id,u.display_name AS Name FROM odca.memberships m JOIN odca.users u ON u.id=m.user_id WHERE m.tenant_id=@tenantId AND m.status='active' AND odca.tenant_actor_has_permission(m.user_id,@tenantId,'tenant.reviews.decide') ORDER BY u.display_name",new{tenantId},tx,cancellationToken:ct));await tx.CommitAsync(ct);return Ok(reviewers.AsList());
    }

    [HttpPost("reviews")]
    public async Task<IActionResult> Submit(Guid tenantId,[FromBody] SubmitReviewRequest request,CancellationToken ct)
    {
        var actor=Actor();if(actor is null)return Unauthorized();await using var c=await dataSource.OpenConnectionAsync(ct);if(!await Allowed(c,actor.Value,tenantId,"tenant.reviews.request",ct))return Forbid();await using var tx=await c.BeginTransactionAsync(ct);await SetTenant(c,tenantId,actor.Value,tx,ct);
        var prior=await c.QuerySingleOrDefaultAsync<ReviewSubmittedResponse>(new CommandDefinition("SELECT id AS ReviewId,generated_version_id AS GeneratedVersionId,status AS Status FROM odca.contract_review_requests WHERE tenant_id=@tenantId AND idempotency_key=@idempotencyKey",new{tenantId,request.IdempotencyKey},tx,cancellationToken:ct));if(prior is not null){await tx.CommitAsync(ct);return Ok(prior);}
        var v=await c.QuerySingleOrDefaultAsync<VersionRow>(new CommandDefinition("SELECT id AS Id,contract_id AS ContractId,content::text AS Content,canonical_sha256 AS Sha256,review_status AS Status FROM odca.generated_contract_versions WHERE tenant_id=@tenantId AND id=@id FOR UPDATE",new{tenantId,id=request.GeneratedVersionId},tx,cancellationToken:ct));if(v is null)return NotFound();if(v.Status!="generated")return Conflict(new{title="A versão já foi encaminhada."});
        var reviewer=await c.ExecuteScalarAsync<bool>(new CommandDefinition("SELECT EXISTS(SELECT 1 FROM odca.memberships WHERE tenant_id=@tenantId AND user_id=@reviewer AND status='active' AND odca.tenant_actor_has_permission(user_id,@tenantId,'tenant.reviews.decide'))",new{tenantId,reviewer=request.ReviewerId},tx,cancellationToken:ct));if(!reviewer)return ValidationProblem("O revisor selecionado não está ativo ou autorizado para revisões.");var reviewId=Guid.NewGuid();
        await c.ExecuteAsync(new CommandDefinition("""
          INSERT INTO odca.contract_review_requests(id,tenant_id,contract_id,generated_version_id,requested_by,due_at,instructions,content_snapshot,document_sha256,idempotency_key)
          VALUES(@reviewId,@tenantId,@contractId,@versionId,@actor,@dueAt,@instructions,@content::jsonb,@sha256,@idempotencyKey);
          INSERT INTO odca.contract_review_steps(tenant_id,review_id,sequence,reviewer_id,status) VALUES(@tenantId,@reviewId,1,@reviewerId,'current');
          INSERT INTO odca.contract_review_events(tenant_id,review_id,actor_id,event_type,details) VALUES(@tenantId,@reviewId,@actor,'review.requested',jsonb_build_object('generatedVersionId',@versionId));
          INSERT INTO odca.contract_review_notifications(tenant_id,review_id,recipient_id,kind,deduplication_key)
          VALUES(@tenantId,@reviewId,@reviewerId,'review.assigned','review-assigned/' || @reviewId::text || '/' || @idempotencyKey::text)
          ON CONFLICT(tenant_id,deduplication_key) DO NOTHING;
          UPDATE odca.generated_contract_versions SET review_status='submitted' WHERE tenant_id=@tenantId AND id=@versionId;
          UPDATE odca.contract_change_requests SET generated_version_id=@versionId,review_id=@reviewId,status='in_review',row_version=row_version+1,updated_at=now()
           WHERE tenant_id=@tenantId AND contract_id=@contractId AND draft_id=(SELECT draft_id FROM odca.generated_contract_versions WHERE tenant_id=@tenantId AND id=@versionId)
             AND status='draft';
          """,new{reviewId,tenantId,contractId=v.ContractId,versionId=v.Id,actor,request.DueAt,request.Instructions,content=v.Content,sha256=v.Sha256,request.IdempotencyKey,request.ReviewerId},tx,cancellationToken:ct));await tx.CommitAsync(ct);return Ok(new ReviewSubmittedResponse(reviewId,v.Id,"in_review"));
    }

    [HttpGet("drafts/{draftId:guid}/versions")]
    public async Task<IActionResult> Versions(Guid tenantId, Guid draftId, CancellationToken ct)
    {
        var actor = Actor(); if (actor is null) return Unauthorized();
        await using var c = await dataSource.OpenConnectionAsync(ct);
        if (!await Allowed(c, actor.Value, tenantId, "tenant.contract_drafts.read", ct)) return Forbid();
        await using var tx = await c.BeginTransactionAsync(ct); await SetTenant(c, tenantId, actor.Value, tx, ct);
        var rows = await c.QueryAsync<StudioVersionItem>(new CommandDefinition("""
            SELECT v.id AS Id,v.version_number AS Number,u.display_name AS Author,v.created_at AS CreatedAt,v.review_status AS Status
            FROM odca.generated_contract_versions v JOIN odca.users u ON u.id=v.created_by
            WHERE v.tenant_id=@tenantId AND v.draft_id=@draftId ORDER BY v.version_number DESC
            """, new { tenantId, draftId }, tx, cancellationToken: ct));
        await tx.CommitAsync(ct); return Ok(rows.AsList());
    }

    [HttpGet("versions/compare")]
    public async Task<IActionResult> Compare(Guid tenantId, [FromQuery] Guid before, [FromQuery] Guid after, CancellationToken ct)
    {
        var actor = Actor(); if (actor is null) return Unauthorized();
        await using var c = await dataSource.OpenConnectionAsync(ct);
        if (!await Allowed(c, actor.Value, tenantId, "tenant.contract_drafts.read", ct)) return Forbid();
        await using var tx = await c.BeginTransactionAsync(ct); await SetTenant(c, tenantId, actor.Value, tx, ct);
        var rows = (await c.QueryAsync<ComparisonRow>(new CommandDefinition("""
            SELECT v.id AS Id,v.contract_id AS ContractId,v.version_number AS Number,u.display_name AS Author,v.created_at AS CreatedAt,
              v.review_status AS Status,v.content::text AS Content,v.fields::text AS Fields,v.values::text AS Values
            FROM odca.generated_contract_versions v JOIN odca.users u ON u.id=v.created_by
            WHERE v.tenant_id=@tenantId AND v.id=ANY(@ids)
            """, new { tenantId, ids = new[] { before, after } }, tx, cancellationToken: ct))).AsList();
        if (rows.Count != 2) return NotFound();
        var left = rows.Single(x => x.Id == before); var right = rows.Single(x => x.Id == after);
        if (left.ContractId != right.ContractId) return BadRequest(new { title = "Selecione versões do mesmo contrato." });
        IReadOnlyList<StudioChange> changes;
        try { changes = ContractStudioAnalysis.Compare(left.Content, left.Fields, left.Values, right.Content, right.Fields, right.Values); }
        catch (InvalidDataException e) { return StatusCode(StatusCodes.Status413PayloadTooLarge, new { title = e.Message }); }
        await tx.CommitAsync(ct);
        return Ok(new VersionComparisonResponse(ToItem(left), ToItem(right), changes.Select(x => new StudioChangeItem(x.Category, x.Reference, x.Before, x.After)).ToArray()));
    }

    [HttpGet("drafts/{draftId:guid}/checklist")]
    public async Task<IActionResult> Checklist(Guid tenantId, Guid draftId, [FromQuery] long expectedVersion, CancellationToken ct)
    {
        var actor = Actor(); if (actor is null) return Unauthorized();
        await using var c = await dataSource.OpenConnectionAsync(ct);
        if (!await Allowed(c, actor.Value, tenantId, "tenant.contract_drafts.read", ct)) return Forbid();
        await using var tx = await c.BeginTransactionAsync(ct); await SetTenant(c, tenantId, actor.Value, tx, ct);
        var draft = await c.QuerySingleOrDefaultAsync<DraftRow>(new CommandDefinition("SELECT d.id AS Id,d.contract_id AS ContractId,c.title AS Title,d.source_template_id AS SourceTemplateId,d.source_template_version_id AS SourceTemplateVersionId,d.content::text AS Content,d.fields::text AS Fields,d.values::text AS Values,d.row_version AS Version,d.last_client_revision AS LastClientRevision,d.updated_at AS UpdatedAt FROM odca.contract_drafts d JOIN odca.contracts c ON c.id=d.contract_id AND c.tenant_id=d.tenant_id WHERE d.tenant_id=@tenantId AND d.id=@draftId", new { tenantId, draftId }, tx, cancellationToken: ct));
        if (draft is null) return NotFound();
        var open = await c.ExecuteScalarAsync<int>(new CommandDefinition("SELECT count(*)::int FROM odca.studio_comments WHERE tenant_id=@tenantId AND draft_id=@draftId AND resolved_at IS NULL AND deleted_at IS NULL", new { tenantId, draftId }, tx, cancellationToken: ct));
        var items = ContractStudioAnalysis.Checklist(draft.Content, draft.Fields, draft.Values, draft.Version == expectedVersion, draft.Version != expectedVersion, open);
        await tx.CommitAsync(ct); return Ok(new ChecklistResponse(items.All(x => x.Severity != "blocker"), items.Select(x => new ChecklistItem(x.Severity, x.Code, x.Message, x.Reference)).ToArray()));
    }

    [HttpGet("drafts/{draftId:guid}/comments")]
    public async Task<IActionResult> Comments(Guid tenantId, Guid draftId, [FromQuery] bool includeResolved, CancellationToken ct)
    {
        var actor = Actor(); if (actor is null) return Unauthorized(); await using var c = await dataSource.OpenConnectionAsync(ct);
        if (!await Allowed(c, actor.Value, tenantId, "tenant.contract_drafts.read", ct)) return Forbid(); await using var tx = await c.BeginTransactionAsync(ct); await SetTenant(c, tenantId, actor.Value, tx, ct);
        var rows = await c.QueryAsync<StudioCommentItem>(new CommandDefinition("""
            SELECT m.id AS Id,m.generated_version_id AS VersionId,m.draft_revision AS DraftRevision,m.reference AS Reference,m.body AS Body,
              m.author_id AS AuthorId,u.display_name AS Author,m.parent_id AS ParentId,m.created_at AS CreatedAt,
              (m.resolved_at IS NOT NULL) AS Resolved,m.resolved_at AS ResolvedAt,m.reference_located AS ReferenceLocated,
              resolver.display_name AS ResolvedBy,coalesce(last_event.occurred_at,m.created_at) AS LastMovementAt,v.version_number AS OriginVersion
            FROM odca.studio_comments m JOIN odca.users u ON u.id=m.author_id
            LEFT JOIN odca.users resolver ON resolver.id=m.resolved_by
            LEFT JOIN odca.generated_contract_versions v ON v.tenant_id=m.tenant_id AND v.id=m.generated_version_id
            LEFT JOIN LATERAL (SELECT occurred_at FROM odca.studio_comment_events e WHERE e.tenant_id=m.tenant_id AND e.comment_id=m.id ORDER BY e.id DESC LIMIT 1) last_event ON true
            WHERE m.tenant_id=@tenantId AND m.draft_id=@draftId AND m.deleted_at IS NULL AND (@includeResolved OR m.resolved_at IS NULL)
            ORDER BY coalesce(last_event.occurred_at,m.created_at) DESC,m.id
            """, new { tenantId, draftId, includeResolved }, tx, cancellationToken: ct));
        await tx.CommitAsync(ct); return Ok(rows.AsList());
    }

    [HttpPost("drafts/{draftId:guid}/comments")]
    public async Task<IActionResult> AddComment(Guid tenantId, Guid draftId, [FromBody] CreateStudioCommentRequest request, CancellationToken ct)
    {
        var actor = Actor(); if (actor is null) return Unauthorized(); if (string.IsNullOrWhiteSpace(request.Body) || request.Body.Trim().Length > 4000 || string.IsNullOrWhiteSpace(request.Reference) || request.Reference.Length > 200) return ValidationProblem();
        await using var c = await dataSource.OpenConnectionAsync(ct); if (!await Allowed(c, actor.Value, tenantId, "tenant.contract_drafts.manage", ct)) return Forbid(); await using var tx = await c.BeginTransactionAsync(ct); await SetTenant(c, tenantId, actor.Value, tx, ct); var id = Guid.NewGuid();
        var inserted = await c.ExecuteScalarAsync<bool>(new CommandDefinition("INSERT INTO odca.studio_comments(id,tenant_id,contract_id,draft_id,generated_version_id,draft_revision,author_id,parent_id,reference,body) SELECT @id,d.tenant_id,d.contract_id,d.id,@versionId,@draftRevision,@actor,@parentId,@reference,@body FROM odca.contract_drafts d WHERE d.tenant_id=@tenantId AND d.id=@draftId AND (@versionId IS NULL OR EXISTS(SELECT 1 FROM odca.generated_contract_versions v WHERE v.tenant_id=d.tenant_id AND v.contract_id=d.contract_id AND v.id=@versionId)) AND (@parentId IS NULL OR EXISTS(SELECT 1 FROM odca.studio_comments p WHERE p.tenant_id=d.tenant_id AND p.draft_id=d.id AND p.id=@parentId)) RETURNING true", new { id, tenantId, draftId, versionId=request.VersionId, request.DraftRevision, actor, request.ParentId, reference=request.Reference.Trim(), body=request.Body.Trim() }, tx, cancellationToken: ct));
        if (!inserted) return NotFound(); await c.ExecuteAsync(new CommandDefinition("INSERT INTO odca.contract_events(tenant_id,contract_id,actor_id,event_type,details) SELECT tenant_id,contract_id,@actor,'comment.created',jsonb_build_object('commentId',@id,'reference',@reference) FROM odca.contract_drafts WHERE tenant_id=@tenantId AND id=@draftId", new { tenantId, draftId, actor, id, reference=request.Reference }, tx, cancellationToken: ct)); await tx.CommitAsync(ct); return Created($"/api/v1/organizations/{tenantId}/studio/drafts/{draftId}/comments/{id}", new { id });
    }

    [HttpPost("drafts/{draftId:guid}/comments/{commentId:guid}/resolve")]
    public Task<IActionResult> ResolveCommentAsync(Guid tenantId, Guid draftId, Guid commentId, [FromBody] ChangeStudioCommentStateRequest request, CancellationToken ct)
        => ChangeCommentStateAsync(tenantId, draftId, commentId, request, resolve: true, ct);

    [HttpPost("drafts/{draftId:guid}/comments/{commentId:guid}/reopen")]
    public Task<IActionResult> ReopenCommentAsync(Guid tenantId, Guid draftId, Guid commentId, [FromBody] ChangeStudioCommentStateRequest request, CancellationToken ct)
        => ChangeCommentStateAsync(tenantId, draftId, commentId, request, resolve: false, ct);

    private async Task<IActionResult> ChangeCommentStateAsync(Guid tenantId, Guid draftId, Guid commentId, ChangeStudioCommentStateRequest request, bool resolve, CancellationToken ct)
    {
        var actor = Actor(); if (actor is null) return Unauthorized();
        if (!resolve && string.IsNullOrWhiteSpace(request.Observation))
            return ValidationProblem(new ValidationProblemDetails(new Dictionary<string, string[]> { ["observation"] = ["Informe a justificativa para reabrir a pendência."] }));
        if (request.Observation?.Trim().Length > 4000) return ValidationProblem();
        await using var c = await dataSource.OpenConnectionAsync(ct); if (!await Allowed(c, actor.Value, tenantId, "tenant.contract_drafts.manage", ct)) return Forbid();
        await using var tx = await c.BeginTransactionAsync(ct); await SetTenant(c, tenantId, actor.Value, tx, ct);
        var current = await c.QuerySingleOrDefaultAsync<CommentStateRow>(new CommandDefinition("""
            SELECT m.resolved_at IS NOT NULL AS Resolved,m.reference AS Reference,m.draft_revision AS DraftRevision,m.contract_id AS ContractId
            FROM odca.studio_comments m JOIN odca.contract_drafts d ON d.tenant_id=m.tenant_id AND d.id=m.draft_id AND d.contract_id=m.contract_id
            JOIN odca.contracts c ON c.tenant_id=d.tenant_id AND c.id=d.contract_id
            WHERE m.tenant_id=@tenantId AND m.draft_id=@draftId AND m.id=@commentId AND m.deleted_at IS NULL FOR UPDATE
            """, new { tenantId, draftId, commentId }, tx, cancellationToken: ct));
        if (current is null) return NotFound();
        if (current.Resolved == resolve) { await tx.CommitAsync(ct); return NoContent(); }
        if (current.Resolved != request.ExpectedResolved) return Conflict(new { title = "A pendência foi atualizada por outra pessoa. Atualize os dados e tente novamente." });
        var action = resolve ? "resolve" : "reopen";
        var count = await c.ExecuteAsync(new CommandDefinition(resolve
            ? "UPDATE odca.studio_comments SET resolved_at=now(),resolved_by=@actor WHERE tenant_id=@tenantId AND draft_id=@draftId AND id=@commentId AND resolved_at IS NULL AND deleted_at IS NULL"
            : "UPDATE odca.studio_comments SET resolved_at=NULL,resolved_by=NULL WHERE tenant_id=@tenantId AND draft_id=@draftId AND id=@commentId AND resolved_at IS NOT NULL AND deleted_at IS NULL",
            new { actor, tenantId, draftId, commentId }, tx, cancellationToken: ct));
        if (count != 1) return Conflict(new { title = "A pendência foi atualizada por outra pessoa." });
        await c.ExecuteAsync(new CommandDefinition("INSERT INTO odca.studio_comment_events(tenant_id,comment_id,actor_id,event_type) VALUES(@tenantId,@commentId,@actor,@action)", new { tenantId, commentId, actor, action }, tx, cancellationToken: ct));
        if (!string.IsNullOrWhiteSpace(request.Observation))
            await c.ExecuteAsync(new CommandDefinition("INSERT INTO odca.studio_comments(tenant_id,contract_id,draft_id,draft_revision,author_id,parent_id,reference,body) VALUES(@tenantId,@contractId,@draftId,@draftRevision,@actor,@commentId,@reference,@body)", new { tenantId, current.ContractId, draftId, current.DraftRevision, actor, commentId, current.Reference, body = request.Observation.Trim() }, tx, cancellationToken: ct));
        await c.ExecuteAsync(new CommandDefinition("INSERT INTO odca.contract_events(tenant_id,contract_id,actor_id,event_type,details) VALUES(@tenantId,@contractId,@actor,@eventType,jsonb_build_object('commentId',@commentId))", new { tenantId, current.ContractId, actor, eventType = $"comment.{action}", commentId }, tx, cancellationToken: ct));
        await tx.CommitAsync(ct); return NoContent();
    }

    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive=true, Converters={new JsonStringEnumConverter()} };
    private static void Validate(string content,string fields,string values,bool confirmed){var definitions=JsonSerializer.Deserialize<ContractFieldDefinition[]>(fields,JsonOptions)??[];var parsed=StructuredContractDocument.Parse(content,definitions);var fieldValues=JsonSerializer.Deserialize<ContractFieldValue[]>(values,JsonOptions)??[];StructuredContractDocument.ValidateValues(definitions,fieldValues,confirmed);if(definitions.Any(x=>!parsed.FieldOccurrences.ContainsKey(x.Id)))throw new InvalidDataException("Todo campo definido precisa ter ao menos uma ocorrência no documento.");}
    private static DraftResponse ToResponse(DraftRow r)=>new(r.Id,r.ContractId,r.Title,r.SourceTemplateId,r.SourceTemplateVersionId,JsonSerializer.Deserialize<JsonElement>(r.Content),JsonSerializer.Deserialize<JsonElement>(r.Fields),JsonSerializer.Deserialize<JsonElement>(r.Values),r.Version,r.LastClientRevision,r.UpdatedAt);
    private Guid? Actor()=>Guid.TryParse(User.FindFirstValue("sub"),out var id)?id:null;
    private static Task<bool> Allowed(NpgsqlConnection c,Guid actor,Guid tenant,string permission,CancellationToken ct)=>c.ExecuteScalarAsync<bool>(new CommandDefinition("SELECT odca.tenant_actor_has_permission(@actor,@tenant,@permission)",new{actor,tenant,permission},cancellationToken:ct));
    private static Task<int> SetTenant(NpgsqlConnection c,Guid tenant,Guid actor,NpgsqlTransaction tx,CancellationToken ct)=>c.ExecuteAsync(new CommandDefinition("SELECT set_config('odca.tenant_id',@value,true),set_config('odca.actor_id',@actor,true)",new{value=tenant.ToString(),actor=actor.ToString()},tx,cancellationToken:ct));
    private sealed record TemplateSource(Guid TemplateId,Guid VersionId,string Content,string Fields);
    private sealed record DraftRow(Guid Id,Guid ContractId,string Title,Guid SourceTemplateId,Guid SourceTemplateVersionId,string Content,string Fields,string Values,long Version,Guid? LastClientRevision,DateTimeOffset UpdatedAt,Guid? PatientId=null,string? PatientSnapshot=null,long? PatientVersion=null,long? CurrentPatientVersion=null,DateTimeOffset? PatientInactiveAt=null,string? TemplateStatus=null);
    private sealed record SaveRow(long Version,Guid ClientRevision,DateTimeOffset SavedAt);
    private sealed record VersionRow(Guid Id,Guid ContractId,string Content,string Sha256,string Status);
    private sealed record GeneratedExistingRow(Guid Id,int Number,string Sha256,long ByteSize,DateTimeOffset CreatedAt,string Status,long DraftVersion);
    private sealed record ComparisonRow(Guid Id,Guid ContractId,int Number,string Author,DateTimeOffset CreatedAt,string Status,string Content,string Fields,string Values);
    private sealed record CommentStateRow(bool Resolved,string Reference,long DraftRevision,Guid ContractId);
    private static StudioVersionItem ToItem(ComparisonRow row)=>new(row.Id,row.Number,row.Author,row.CreatedAt,row.Status);
    private static IReadOnlyList<PatientDataChange> PatientChanges(string? before, string? after)
    {
        if (before is null || after is null) return [];
        using var left = JsonDocument.Parse(before); using var right = JsonDocument.Parse(after);
        var fields = new[] { "fullName", "preferredName", "birthDate", "email", "phone", "address", "identifierType", "identifierValue", "representative" };
        return fields.Select(field => new PatientDataChange(field, JsonValue(left.RootElement, field), JsonValue(right.RootElement, field)))
            .Where(change => !string.Equals(change.Before, change.After, StringComparison.Ordinal)).ToArray();
    }
    private static string? JsonValue(JsonElement element, string name) => element.TryGetProperty(name, out var value) && value.ValueKind != JsonValueKind.Null ? value.ToString() : null;
    private sealed record ConferenceRow(Guid DraftId,long DraftVersion,string Organization,string Template,int TemplateVersion,string DocumentType,string TemplateStatus,Guid? PatientId,string? PatientName,string? RepresentativeName,long? SelectedPatientVersion,long? CurrentPatientVersion,bool PatientActive,string? SelectedPatientSnapshot,string? CurrentPatientSnapshot,string Content,string Fields,string Values,string ReviewStatus);
    private sealed record PatientConfirmationRow(Guid ContractId,long DraftVersion,long SelectedPatientVersion,long CurrentPatientVersion,DateTimeOffset? PatientInactiveAt);
}
