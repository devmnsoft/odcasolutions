using System.Security.Claims;
using System.Security.Cryptography;
using System.Net.Mail;
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
            canEdit, canGenerate, row.SelectedPatientVersion != row.CurrentPatientVersion, row.ReviewStatus, canGenerate ? "generate" : row.SelectedPatientVersion != row.CurrentPatientVersion ? "confirm_patient" : "complete_fields",
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
        var actor=Actor();if(actor is null)return Unauthorized();
        if(request.IdempotencyKey==Guid.Empty || request.ExpectedVersion<=0)return ValidationProblem("Informe a versão esperada da minuta e uma chave de repetição válida.");
        await using var c=await dataSource.OpenConnectionAsync(ct);if(!await Allowed(c,actor.Value,tenantId,"tenant.contract_drafts.manage",ct))return Forbid();await using var tx=await c.BeginTransactionAsync(ct);await SetTenant(c,tenantId,actor.Value,tx,ct);
        var existing=await c.QuerySingleOrDefaultAsync<GeneratedExistingRow>(new CommandDefinition("SELECT id AS Id,draft_id AS DraftId,version_number AS Number,canonical_sha256 AS Sha256,byte_size AS ByteSize,created_at AS CreatedAt,review_status AS Status,draft_row_version AS DraftVersion FROM odca.generated_contract_versions WHERE tenant_id=@tenantId AND idempotency_key=@idempotencyKey",new{tenantId,request.IdempotencyKey},tx,cancellationToken:ct));if(existing is not null){if(existing.DraftId!=draftId||request.ExpectedVersion!=existing.DraftVersion)return Conflict(new{title="A chave de idempotência já foi usada com outra minuta ou versão.",code="idempotency.payload.conflict"});await tx.CommitAsync(ct);return Ok(new GeneratedVersionResponse(existing.Id,existing.Number,existing.Sha256,existing.ByteSize,existing.CreatedAt,existing.Status));}
        var d=await c.QuerySingleOrDefaultAsync<DraftRow>(new CommandDefinition("SELECT d.id AS Id,d.contract_id AS ContractId,c.title AS Title,d.source_template_id AS SourceTemplateId,d.source_template_version_id AS SourceTemplateVersionId,d.content::text AS Content,d.fields::text AS Fields,d.values::text AS Values,d.row_version AS Version,d.last_client_revision AS LastClientRevision,d.updated_at AS UpdatedAt,d.patient_id AS PatientId,d.patient_row_version AS PatientVersion,t.status AS TemplateStatus,p.row_version AS CurrentPatientVersion,p.inactive_at AS PatientInactiveAt,odca.patient_document_snapshot(@tenantId,d.patient_id)::text AS PatientSnapshot FROM odca.contract_drafts d JOIN odca.contracts c ON c.id=d.contract_id AND c.tenant_id=d.tenant_id JOIN odca.contract_templates t ON t.id=d.source_template_id LEFT JOIN LATERAL (SELECT patient.row_version,patient.inactive_at FROM odca.patients patient WHERE (patient.tenant_id,patient.id)=(d.tenant_id,d.patient_id) FOR UPDATE) p ON true WHERE d.tenant_id=@tenantId AND d.id=@draftId FOR UPDATE OF d",new{tenantId,draftId},tx,cancellationToken:ct));if(d is null)return NotFound();
        if(d.TemplateStatus!="published")return Conflict(new{title="O modelo não está disponível para nova geração.",code="template.unavailable"});
        if(d.PatientId is not null && d.PatientInactiveAt is not null)return Conflict(new{title="O paciente está inativo.",detail="Restaure o cadastro antes de gerar um novo documento.",code="patient.inactive"});
        if(d.PatientId is not null && d.PatientVersion!=d.CurrentPatientVersion)return Conflict(new{title="Os dados do paciente mudaram durante a conferência.",detail="Crie uma nova minuta ou reconfirme os dados atualizados antes da emissão.",code="patient.version.conflict",currentPatientVersion=d.CurrentPatientVersion});
        if(d.Version!=request.ExpectedVersion)return Conflict(new{title="Salve e resolva o conflito antes de gerar a versão.",currentVersion=d.Version});try{Validate(d.Content,d.Fields,d.Values,true);}catch(InvalidDataException e){return ValidationProblem(new ValidationProblemDetails(new Dictionary<string,string[]>{{"pendingFields",[e.Message]}}));}
        var number=await c.ExecuteScalarAsync<int>(new CommandDefinition("SELECT coalesce(max(version_number),0)+1 FROM odca.generated_contract_versions WHERE draft_id=@draftId",new{draftId},tx,cancellationToken:ct));
        var snapshot=Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new{schemaVersion=2,patient=d.PatientSnapshot is null?(JsonElement?)null:JsonSerializer.Deserialize<JsonElement>(d.PatientSnapshot),content=JsonSerializer.Deserialize<JsonElement>(d.Content),fields=JsonSerializer.Deserialize<JsonElement>(d.Fields),values=JsonSerializer.Deserialize<JsonElement>(d.Values)}));var hash=Convert.ToHexString(SHA256.HashData(snapshot)).ToLowerInvariant();var id=Guid.NewGuid();var key=$"generated/{tenantId:N}/{d.ContractId:N}/{id:N}.json";var root=configuration["Documents:StoragePath"]??Path.Combine(AppContext.BaseDirectory,"App_Data","documents");var path=Path.Combine(root,key.Replace('/',Path.DirectorySeparatorChar));Directory.CreateDirectory(Path.GetDirectoryName(path)!);await System.IO.File.WriteAllBytesAsync(path,snapshot,ct);
        var metadata=JsonSerializer.Serialize(new{title=d.Title,organization=await c.ExecuteScalarAsync<string>(new CommandDefinition("SELECT display_name FROM odca.tenants WHERE id=@tenantId",new{tenantId},tx,cancellationToken:ct)),template=await c.ExecuteScalarAsync<string>(new CommandDefinition("SELECT name FROM odca.contract_templates WHERE id=@sourceTemplateId",new{d.SourceTemplateId},tx,cancellationToken:ct)),author=await c.ExecuteScalarAsync<string>(new CommandDefinition("SELECT display_name FROM odca.users WHERE id=@actor",new{actor},tx,cancellationToken:ct))});
        await c.ExecuteAsync(new CommandDefinition("INSERT INTO odca.generated_contract_versions(id,tenant_id,contract_id,draft_id,version_number,content_schema_version,content,fields,values,source_template_id,source_template_version_id,canonical_sha256,storage_key,byte_size,created_by,idempotency_key,draft_row_version,patient_id,patient_snapshot,emission_metadata) VALUES(@id,@tenantId,@contractId,@draftId,@number,1,@content::jsonb,@fields::jsonb,@values::jsonb,@sourceTemplateId,@sourceTemplateVersionId,@hash,@key,@size,@actor,@idempotencyKey,@draftVersion,@PatientId,@PatientSnapshot::jsonb,@metadata::jsonb); INSERT INTO odca.contract_events(tenant_id,contract_id,actor_id,event_type,details) VALUES(@tenantId,@contractId,@actor,'version.generated',jsonb_build_object('versionId',@id,'number',@number,'sha256',@hash))",new{id,tenantId,contractId=d.ContractId,draftId,number,content=d.Content,fields=d.Fields,values=d.Values,d.SourceTemplateId,d.SourceTemplateVersionId,hash,key,size=snapshot.LongLength,actor,idempotencyKey=request.IdempotencyKey,draftVersion=d.Version,d.PatientId,d.PatientSnapshot,metadata},tx,cancellationToken:ct));await tx.CommitAsync(ct);return Created($"/api/v1/organizations/{tenantId}/studio/versions/{id}",new GeneratedVersionResponse(id,number,hash,snapshot.LongLength,DateTimeOffset.UtcNow,"generated"));
    }

    [HttpGet("versions/{versionId:guid}")]
    public async Task<IActionResult> Version(Guid tenantId, Guid versionId, CancellationToken ct)
    {
        var actor=Actor(); if(actor is null)return Unauthorized();
        await using var c=await dataSource.OpenConnectionAsync(ct);
        var canReadPatientDocuments=await Allowed(c,actor.Value,tenantId,"tenant.patients.documents.read",ct);
        var canReadDrafts=await Allowed(c,actor.Value,tenantId,"tenant.contract_drafts.read",ct);
        if(!canReadPatientDocuments&&!canReadDrafts)return Forbid();
        await using var tx=await c.BeginTransactionAsync(ct);await SetTenant(c,tenantId,actor.Value,tx,ct);
        var row=await c.QuerySingleOrDefaultAsync<GeneratedDetailRow>(new CommandDefinition("""
            SELECT v.id AS Id,v.contract_id AS ContractId,v.draft_id AS DraftId,v.patient_id AS PatientId,v.version_number AS Number,
              coalesce(v.emission_metadata->>'title',c.title) AS Title,t.contract_type AS DocumentType,coalesce(v.emission_metadata->>'organization',tenant.display_name) AS Organization,
              coalesce(v.emission_metadata->>'template',t.name) AS Template,tv.version_number AS TemplateVersion,coalesce(v.emission_metadata->>'author',u.display_name) AS Author,
              v.created_at AS CreatedAt,v.canonical_sha256 AS Sha256,v.review_status AS ReviewStatus,
              'not_available' AS SignatureStatus,review.id AS ReviewId,v.patient_snapshot::text AS PatientSnapshot,
              v.content::text AS Content,v.fields::text AS Fields,v.values::text AS Values,v.pdf_status AS PdfStatus,v.pdf_byte_size AS PdfByteSize,v.pdf_completed_at AS PdfCompletedAt
            FROM odca.generated_contract_versions v
            JOIN odca.contracts c ON (c.tenant_id,c.id)=(v.tenant_id,v.contract_id)
            JOIN odca.tenants tenant ON tenant.id=v.tenant_id
            JOIN odca.contract_templates t ON t.id=v.source_template_id
            JOIN odca.contract_template_versions tv ON tv.id=v.source_template_version_id AND tv.template_id=t.id
            JOIN odca.users u ON u.id=v.created_by
            LEFT JOIN LATERAL (SELECT r.id FROM odca.contract_review_requests r
              WHERE r.tenant_id=v.tenant_id AND r.generated_version_id=v.id
              ORDER BY r.opened_at DESC,r.id LIMIT 1) review ON true
            WHERE v.tenant_id=@tenantId AND v.id=@versionId
            """,new{tenantId,versionId},tx,cancellationToken:ct));
        if(row is null)return NotFound();
        if(!canReadDrafts&&row.PatientId is null)return Forbid();
        var preparation=await ReadPreparation(c,tenantId,versionId,tx,ct);
        if(preparation is not null)preparation=preparation with{Readiness=await CalculateReadiness(c,tenantId,versionId,tx,ct)};
        string html;
        try { html=ContractDocumentRenderer.ToHtml(row.Content,row.Fields,row.Values); }
        catch(InvalidDataException exception) { return Problem(statusCode:StatusCodes.Status422UnprocessableEntity,title:"A estrutura histórica não pode ser apresentada.",detail:exception.Message); }
        await tx.CommitAsync(ct); return Ok(new GeneratedVersionDetail(row.Id,row.ContractId,row.DraftId,row.Number,row.Title,row.DocumentType,
            row.Organization,row.Template,row.TemplateVersion,row.Author,row.CreatedAt,row.Sha256,row.ReviewStatus,
            row.SignatureStatus,row.ReviewId,ParseOptional(row.PatientSnapshot),JsonSerializer.Deserialize<JsonElement>(row.Content),
            JsonSerializer.Deserialize<JsonElement>(row.Fields),JsonSerializer.Deserialize<JsonElement>(row.Values),html,row.PdfStatus,row.PdfByteSize,row.PdfCompletedAt,preparation));
    }

    [HttpPost("versions/{versionId:guid}/pdf")]
    public async Task<IActionResult> GeneratePdf(Guid tenantId,Guid versionId,CancellationToken ct)
    {
        var actor=Actor();if(actor is null)return Unauthorized();await using var c=await dataSource.OpenConnectionAsync(ct);
        if(!await Allowed(c,actor.Value,tenantId,"tenant.contract_drafts.manage",ct))return Forbid();
        await using var tx=await c.BeginTransactionAsync(ct);await SetTenant(c,tenantId,actor.Value,tx,ct);
        var row=await c.QuerySingleOrDefaultAsync<PdfRow>(new CommandDefinition("SELECT id AS Id,coalesce(emission_metadata->>'title','Documento') AS Title,version_number AS Number,content::text AS Content,fields::text AS Fields,values::text AS Values,pdf_status AS Status,pdf_storage_key AS StorageKey,pdf_byte_size AS ByteSize FROM odca.generated_contract_versions WHERE tenant_id=@tenantId AND id=@versionId FOR UPDATE",new{tenantId,versionId},tx,cancellationToken:ct));
        if(row is null)return NotFound();if(row.Status=="completed"){await tx.CommitAsync(ct);return Ok(new{status="completed",byteSize=row.ByteSize,replayed=true});}
        byte[] pdf;try{pdf=ContractDocumentRenderer.ToPdf(row.Content,row.Fields,row.Values,row.Title,row.Number);}catch(InvalidDataException e){await c.ExecuteAsync(new CommandDefinition("UPDATE odca.generated_contract_versions SET pdf_status='failed',pdf_failure_code='invalid_structure' WHERE tenant_id=@tenantId AND id=@versionId",new{tenantId,versionId},tx,cancellationToken:ct));await tx.CommitAsync(ct);return Problem(statusCode:StatusCodes.Status422UnprocessableEntity,title:"Não foi possível gerar o PDF.",detail:e.Message);}
        var hash=Convert.ToHexString(SHA256.HashData(pdf)).ToLowerInvariant();var key=$"generated/{tenantId:N}/{versionId:N}/final-{ContractDocumentRenderer.PdfRendererVersion}-{hash[..16]}.pdf";var root=configuration["Documents:StoragePath"]??Path.Combine(AppContext.BaseDirectory,"App_Data","documents");var path=Path.Combine(root,key.Replace('/',Path.DirectorySeparatorChar));var temporary=path+".attempt-"+Guid.NewGuid().ToString("N");Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        try
        {
            await System.IO.File.WriteAllBytesAsync(temporary,pdf,ct);
            if(System.IO.File.Exists(path)){var existing=await System.IO.File.ReadAllBytesAsync(path,ct);if(!CryptographicOperations.FixedTimeEquals(SHA256.HashData(existing),SHA256.HashData(pdf)))return Problem(statusCode:StatusCodes.Status409Conflict,title:"Já existe um artefato divergente para esta tentativa.");System.IO.File.Delete(temporary);}else System.IO.File.Move(temporary,path,false);
            var reserved=await c.ExecuteScalarAsync<bool>(new CommandDefinition("""
                INSERT INTO odca.tenant_storage_usage(tenant_id) VALUES(@tenantId) ON CONFLICT DO NOTHING;
                UPDATE odca.tenant_storage_usage u SET quota_bytes=q.effective_quota
                  FROM (SELECT t.id,1073741824::bigint+COALESCE((SELECT sum(g.quantity_bytes) FROM odca.storage_capacity_grants g WHERE g.tenant_id=t.id AND g.revoked_at IS NULL AND(g.valid_until IS NULL OR g.valid_until>now())),0) AS effective_quota FROM odca.tenants t WHERE t.id=@tenantId) q WHERE u.tenant_id=q.id;
                WITH movement AS (INSERT INTO odca.resource_movements(tenant_id,resource_type,movement_type,quantity,unit,source_type,source_id,idempotency_key,actor_user_id)
                  SELECT @tenantId,'storage_usage','consume',@size,'bytes','generated_contract_pdf',@versionId,'pdf:'||@versionId::text,@actor
                  WHERE EXISTS(SELECT 1 FROM odca.tenant_storage_usage WHERE tenant_id=@tenantId AND used_bytes+reserved_bytes+@size<=quota_bytes)
                  ON CONFLICT(tenant_id,idempotency_key) DO NOTHING RETURNING 1)
                UPDATE odca.tenant_storage_usage SET used_bytes=used_bytes+@size WHERE tenant_id=@tenantId AND EXISTS(SELECT 1 FROM movement) RETURNING true
                """,new{tenantId,versionId,size=pdf.LongLength,actor},tx,cancellationToken:ct));
            if(!reserved)return Problem(statusCode:StatusCodes.Status413PayloadTooLarge,title:"A cota efetiva de armazenamento da organização foi atingida.");
            await c.ExecuteAsync(new CommandDefinition("UPDATE odca.generated_contract_versions SET pdf_status='completed',pdf_storage_key=@key,pdf_sha256=@hash,pdf_byte_size=@size,pdf_renderer_version=@renderer,pdf_completed_at=now(),pdf_failure_code=NULL WHERE tenant_id=@tenantId AND id=@versionId",new{tenantId,versionId,key,hash,size=pdf.LongLength,renderer=ContractDocumentRenderer.PdfRendererVersion},tx,cancellationToken:ct));await tx.CommitAsync(ct);return Ok(new{status="completed",byteSize=pdf.LongLength,replayed=false});
        }
        finally{if(System.IO.File.Exists(temporary))System.IO.File.Delete(temporary);}
    }

    [HttpGet("versions/{versionId:guid}/pdf")]
    public async Task<IActionResult> DownloadPdf(Guid tenantId,Guid versionId,CancellationToken ct)
    {
        var actor=Actor();if(actor is null)return Unauthorized();await using var c=await dataSource.OpenConnectionAsync(ct);var canReadPatientDocuments=await Allowed(c,actor.Value,tenantId,"tenant.patients.documents.read",ct);var canReadDrafts=await Allowed(c,actor.Value,tenantId,"tenant.contract_drafts.read",ct);if(!canReadPatientDocuments&&!canReadDrafts)return Forbid();await using var tx=await c.BeginTransactionAsync(ct);await SetTenant(c,tenantId,actor.Value,tx,ct);
        var row=await c.QuerySingleOrDefaultAsync<PdfDownloadRow>(new CommandDefinition("SELECT pdf_storage_key AS StorageKey,pdf_sha256 AS Sha256,coalesce(emission_metadata->>'title','documento') AS Title,patient_id AS PatientId FROM odca.generated_contract_versions WHERE tenant_id=@tenantId AND id=@versionId AND pdf_status='completed'",new{tenantId,versionId},tx,cancellationToken:ct));if(row is null)return NotFound();if(!canReadDrafts&&row.PatientId is null)return Forbid();var root=configuration["Documents:StoragePath"]??Path.Combine(AppContext.BaseDirectory,"App_Data","documents");var path=Path.Combine(root,row.StorageKey.Replace('/',Path.DirectorySeparatorChar));if(!System.IO.File.Exists(path))return Problem(statusCode:StatusCodes.Status410Gone,title:"O arquivo final não está disponível.");var bytes=await System.IO.File.ReadAllBytesAsync(path,ct);if(!CryptographicOperations.FixedTimeEquals(SHA256.HashData(bytes),Convert.FromHexString(row.Sha256)))return Problem(statusCode:StatusCodes.Status409Conflict,title:"A integridade do PDF não pôde ser confirmada.");await tx.CommitAsync(ct);return File(bytes,"application/pdf",$"{SafeFileName(row.Title)}.pdf",false);
    }

    [HttpGet("versions/{versionId:guid}/signature-preparation/readiness")]
    public async Task<IActionResult> SignatureReadiness(Guid tenantId,Guid versionId,CancellationToken ct)
    {
        var actor=Actor();if(actor is null)return Unauthorized();await using var c=await dataSource.OpenConnectionAsync(ct);
        if(!await Allowed(c,actor.Value,tenantId,"tenant.contract_drafts.read",ct))return Forbid();
        await using var tx=await c.BeginTransactionAsync(ct);await SetTenant(c,tenantId,actor.Value,tx,ct);
        var readiness=await CalculateReadiness(c,tenantId,versionId,tx,ct);if(readiness is null)return NotFound();await tx.CommitAsync(ct);return Ok(readiness);
    }

    [HttpPut("versions/{versionId:guid}/signature-preparation")]
    public async Task<IActionResult> SavePreparation(Guid tenantId,Guid versionId,SaveSignaturePreparationRequest request,CancellationToken ct)
    {
        var actor=Actor();if(actor is null)return Unauthorized();
        var participants=request.Participants;
        var errors=ValidateParticipants(participants);
        if(request.Confirm&&(request.OperationId is null||request.OperationId==Guid.Empty))errors["operationId"]=["A chave da confirmação é obrigatória."];
        if(errors.Count>0)return ValidationProblem(new ValidationProblemDetails(errors));
        var normalized=participants!.Select(x=>x with{Role=x.Role.Trim(),Name=x.Name.Trim(),Email=NullIfBlank(x.Email),Phone=NullIfBlank(x.Phone)}).OrderBy(x=>x.Position).ToArray();
        await using var c=await dataSource.OpenConnectionAsync(ct);if(!await Allowed(c,actor.Value,tenantId,"tenant.contract_drafts.manage",ct))return Forbid();
        await using var tx=await c.BeginTransactionAsync(ct);await SetTenant(c,tenantId,actor.Value,tx,ct);
        var version=await c.QuerySingleOrDefaultAsync<PreparationVersionRow>(new CommandDefinition("SELECT id AS Id,patient_id AS PatientId,patient_snapshot::text AS PatientSnapshot,pdf_status AS PdfStatus,pdf_storage_key AS PdfStorageKey,pdf_sha256 AS PdfSha256,review_status AS ReviewStatus FROM odca.generated_contract_versions WHERE tenant_id=@tenantId AND id=@versionId",new{tenantId,versionId},tx,cancellationToken:ct));
        if(version is null)return NotFound();
        var sourceErrors=await ValidateParticipantSources(c,tenantId,version,normalized,tx,ct);if(sourceErrors.Count>0)return ValidationProblem(new ValidationProblemDetails(sourceErrors));
        var current=await c.QuerySingleOrDefaultAsync<PreparationStateRow>(new CommandDefinition("SELECT id AS Id,status AS Status,row_version AS Version,composition_revision AS CompositionRevision,confirmed_revision AS ConfirmedRevision,confirmed_pdf_sha256 AS ConfirmedPdfSha256,confirmed_at AS ConfirmedAt,confirmation_operation_id AS ConfirmationOperationId FROM odca.signature_preparations WHERE tenant_id=@tenantId AND generated_version_id=@versionId FOR UPDATE",new{tenantId,versionId},tx,cancellationToken:ct));
        Guid preparationId;int revision;long nextVersion;
        if(current is null)
        {
            if(request.ExpectedVersion!=0)return Conflict(new{title="A preparação ainda não existe nesta versão. Recarregue a página.",code="preparation.creation.conflict"});
            preparationId=Guid.NewGuid();
            var created=await c.ExecuteScalarAsync<bool>(new CommandDefinition("INSERT INTO odca.signature_preparations(id,tenant_id,generated_version_id,status,created_by,updated_by) VALUES(@preparationId,@tenantId,@versionId,'draft',@actor,@actor) ON CONFLICT(tenant_id,generated_version_id) DO NOTHING RETURNING true",new{preparationId,tenantId,versionId,actor},tx,cancellationToken:ct));
            if(!created)return Conflict(new{title="Outra sessão iniciou esta preparação. Recarregue para preservar as duas composições.",code="preparation.creation.conflict"});
            revision=1;nextVersion=1;
            await AddPreparationEvent(c,tenantId,preparationId,revision,actor,"created",new{count=normalized.Length},tx,ct);
        }
        else
        {
            if(request.Confirm&&current.Status=="confirmed"&&current.ConfirmationOperationId==request.OperationId){await tx.CommitAsync(ct);return Ok(new{status="confirmed",version=current.Version,compositionRevision=current.CompositionRevision,replayed=true});}
            if(current.Version!=request.ExpectedVersion)return Conflict(new{title="A composição foi alterada em outra sessão. Recarregue e reaplique suas mudanças.",code="preparation.version.conflict",currentVersion=current.Version});
            if(current.Status=="confirmed")return Conflict(new{title="A composição confirmada está congelada. Reabra-a com justificativa antes de editar.",code="preparation.confirmed"});
            preparationId=current.Id;revision=current.CompositionRevision+1;nextVersion=current.Version+1;
            await c.ExecuteAsync(new CommandDefinition("UPDATE odca.signature_preparations SET composition_revision=@revision,row_version=row_version+1,updated_by=@actor,updated_at=now() WHERE tenant_id=@tenantId AND id=@preparationId",new{tenantId,preparationId,revision,actor},tx,cancellationToken:ct));
            await AddPreparationEvent(c,tenantId,preparationId,revision,actor,"updated",new{count=normalized.Length},tx,ct);
        }
        foreach(var participant in normalized)
            await c.ExecuteAsync(new CommandDefinition("INSERT INTO odca.signature_participants(id,client_id,tenant_id,preparation_id,composition_revision,participant_type,source_id,role,name,email,phone,position,recorded_by) VALUES(gen_random_uuid(),@Id,@tenantId,@preparationId,@revision,@ParticipantType,@SourceId,@Role,@Name,@Email,@Phone,@Position,@actor)",new{participant.Id,tenantId,preparationId,revision,participant.ParticipantType,participant.SourceId,participant.Role,participant.Name,participant.Email,participant.Phone,participant.Position,actor},tx,cancellationToken:ct));
        if(revision==1)await AddPreparationEvent(c,tenantId,preparationId,revision,actor,"participant_added",new{participantIds=normalized.Select(x=>x.Id).ToArray()},tx,ct);
        if(revision>1)await c.ExecuteAsync(new CommandDefinition("""
            INSERT INTO odca.signature_preparation_events(tenant_id,preparation_id,composition_revision,actor_user_id,event_type,details)
            SELECT @tenantId,@preparationId,@revision,@actor,k.event_type,jsonb_build_object('participantIds',k.ids)
            FROM (SELECT 'participant_added' event_type,jsonb_agg(n.client_id) ids FROM odca.signature_participants n WHERE n.tenant_id=@tenantId AND n.preparation_id=@preparationId AND n.composition_revision=@revision AND NOT EXISTS(SELECT 1 FROM odca.signature_participants o WHERE o.tenant_id=n.tenant_id AND o.preparation_id=n.preparation_id AND o.composition_revision=@revision-1 AND o.client_id=n.client_id)
              UNION ALL SELECT 'participant_removed',jsonb_agg(o.client_id) FROM odca.signature_participants o WHERE o.tenant_id=@tenantId AND o.preparation_id=@preparationId AND o.composition_revision=@revision-1 AND NOT EXISTS(SELECT 1 FROM odca.signature_participants n WHERE n.tenant_id=o.tenant_id AND n.preparation_id=o.preparation_id AND n.composition_revision=@revision AND n.client_id=o.client_id)
              UNION ALL SELECT 'participant_changed',jsonb_agg(n.client_id) FROM odca.signature_participants n JOIN odca.signature_participants o ON (o.tenant_id,o.preparation_id,o.client_id)=(n.tenant_id,n.preparation_id,n.client_id) AND o.composition_revision=@revision-1 WHERE n.tenant_id=@tenantId AND n.preparation_id=@preparationId AND n.composition_revision=@revision AND (n.participant_type,n.source_id,n.role,n.name,n.email,n.phone) IS DISTINCT FROM (o.participant_type,o.source_id,o.role,o.name,o.email,o.phone)
              UNION ALL SELECT 'participant_reordered',jsonb_agg(n.client_id) FROM odca.signature_participants n JOIN odca.signature_participants o ON (o.tenant_id,o.preparation_id,o.client_id)=(n.tenant_id,n.preparation_id,n.client_id) AND o.composition_revision=@revision-1 WHERE n.tenant_id=@tenantId AND n.preparation_id=@preparationId AND n.composition_revision=@revision AND n.position<>o.position) k WHERE k.ids IS NOT NULL
            """,new{tenantId,preparationId,revision,actor},tx,cancellationToken:ct));
        if(request.Confirm)
        {
            var readiness=await CalculateReadiness(c,tenantId,versionId,tx,ct);
            if(readiness is null||!readiness.CanConfirm)return BadRequest(new ValidationProblemDetails(new Dictionary<string,string[]>{{"confirm",readiness?.Blockers.Select(x=>x.Message).ToArray()??["A conferência não pôde ser calculada."]}}));
            await c.ExecuteAsync(new CommandDefinition("UPDATE odca.signature_preparations SET status='confirmed',confirmed_revision=composition_revision,confirmed_pdf_storage_key=@key,confirmed_pdf_sha256=@hash,confirmed_at=now(),confirmed_by=@actor,confirmation_operation_id=@operationId WHERE tenant_id=@tenantId AND id=@preparationId",new{tenantId,preparationId,key=version.PdfStorageKey,hash=version.PdfSha256,actor,operationId=request.OperationId},tx,cancellationToken:ct));
            await AddPreparationEvent(c,tenantId,preparationId,revision,actor,"confirmed",new{pdfSha256=version.PdfSha256,pdfStorageKey=version.PdfStorageKey},tx,ct);
        }
        await tx.CommitAsync(ct);return Ok(new{status=request.Confirm?"confirmed":"draft",version=nextVersion,compositionRevision=revision});
    }

    [HttpPost("versions/{versionId:guid}/signature-preparation/reopen")]
    public async Task<IActionResult> ReopenPreparation(Guid tenantId,Guid versionId,ReopenSignaturePreparationRequest request,CancellationToken ct)
    {
        var actor=Actor();if(actor is null)return Unauthorized();if(request.OperationId==Guid.Empty||string.IsNullOrWhiteSpace(request.Justification)||request.Justification.Trim().Length is < 5 or > 1000)return ValidationProblem(new ValidationProblemDetails(new Dictionary<string,string[]>{{"justification",["Informe uma justificativa de 5 a 1000 caracteres."]}}));
        await using var c=await dataSource.OpenConnectionAsync(ct);if(!await Allowed(c,actor.Value,tenantId,"tenant.contract_drafts.manage",ct))return Forbid();await using var tx=await c.BeginTransactionAsync(ct);await SetTenant(c,tenantId,actor.Value,tx,ct);
        var current=await c.QuerySingleOrDefaultAsync<PreparationStateRow>(new CommandDefinition("SELECT id AS Id,status AS Status,row_version AS Version,composition_revision AS CompositionRevision,confirmed_revision AS ConfirmedRevision,confirmed_pdf_sha256 AS ConfirmedPdfSha256,confirmed_at AS ConfirmedAt,confirmation_operation_id AS ConfirmationOperationId FROM odca.signature_preparations WHERE tenant_id=@tenantId AND generated_version_id=@versionId FOR UPDATE",new{tenantId,versionId},tx,cancellationToken:ct));
        if(current is null)return NotFound();if(current.Status=="draft"){await tx.CommitAsync(ct);return Ok(new{status="draft",version=current.Version,replayed=true});}if(current.Version!=request.ExpectedVersion)return Conflict(new{title="A preparação foi alterada em outra sessão.",code="preparation.version.conflict",currentVersion=current.Version});
        await c.ExecuteAsync(new CommandDefinition("UPDATE odca.signature_preparations SET status='draft',confirmed_revision=NULL,row_version=row_version+1,updated_by=@actor,updated_at=now() WHERE tenant_id=@tenantId AND id=@id",new{tenantId,id=current.Id,actor},tx,cancellationToken:ct));
        await AddPreparationEvent(c,tenantId,current.Id,current.CompositionRevision,actor,"reopened",new{justification=request.Justification.Trim(),priorConfirmedRevision=current.ConfirmedRevision,priorPdfSha256=current.ConfirmedPdfSha256,priorConfirmedAt=current.ConfirmedAt,operationId=request.OperationId},tx,ct);
        await tx.CommitAsync(ct);return Ok(new{status="draft",version=current.Version+1,replayed=false});
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
        var actor=Actor();if(actor is null)return Unauthorized();
        if(request.GeneratedVersionId==Guid.Empty||request.ReviewerId==Guid.Empty||request.IdempotencyKey==Guid.Empty)return ValidationProblem("Informe a versão, o responsável e uma chave de repetição válidos.");
        await using var c=await dataSource.OpenConnectionAsync(ct);if(!await Allowed(c,actor.Value,tenantId,"tenant.reviews.request",ct))return Forbid();await using var tx=await c.BeginTransactionAsync(ct);await SetTenant(c,tenantId,actor.Value,tx,ct);
        var prior=await c.QuerySingleOrDefaultAsync<SubmittedReviewRow>(new CommandDefinition("""
            SELECT r.id AS ReviewId,r.generated_version_id AS GeneratedVersionId,r.status AS Status,
              r.instructions AS Instructions,r.due_at AS DueAt,s.reviewer_id AS ReviewerId
            FROM odca.contract_review_requests r
            JOIN odca.contract_review_steps s ON s.tenant_id=r.tenant_id AND s.review_id=r.id AND s.sequence=1
            WHERE r.tenant_id=@tenantId AND r.idempotency_key=@idempotencyKey
            """,new{tenantId,request.IdempotencyKey},tx,cancellationToken:ct));
        if(prior is not null){
            if(prior.GeneratedVersionId!=request.GeneratedVersionId||prior.ReviewerId!=request.ReviewerId||
               prior.DueAt!=request.DueAt||!string.Equals(prior.Instructions,request.Instructions?.Trim(),StringComparison.Ordinal))
                return Conflict(new{title="A chave de repetição já foi usada para outra solicitação.",code="review.idempotency.conflict"});
            await tx.CommitAsync(ct);return Ok(new ReviewSubmittedResponse(prior.ReviewId,prior.GeneratedVersionId,prior.Status));}
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
          """,new{reviewId,tenantId,contractId=v.ContractId,versionId=v.Id,actor,request.DueAt,Instructions=request.Instructions?.Trim(),content=v.Content,sha256=v.Sha256,request.IdempotencyKey,request.ReviewerId},tx,cancellationToken:ct));await tx.CommitAsync(ct);return Ok(new ReviewSubmittedResponse(reviewId,v.Id,"in_review"));
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
        var rows = (await c.QueryAsync<StudioCommentItem>(new CommandDefinition("""
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
            """, new { tenantId, draftId, includeResolved }, tx, cancellationToken: ct))).AsList();
        await tx.CommitAsync(ct); return Ok(rows);
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

    private static Dictionary<string,string[]> ValidateParticipants(IReadOnlyList<SignatureParticipantInput>? participants)
    {
        var errors=new Dictionary<string,string[]>();
        if(participants is null){errors["participants"]=["Informe a coleção de participantes."];return errors;}
        if(participants.Count is <1 or >20)errors["participants"]=["Informe de 1 a 20 participantes."];
        var allowed=new HashSet<string>(["patient","representative","professional","organization_representative"],StringComparer.Ordinal);
        for(var i=0;i<participants.Count;i++)
        {
            var p=participants[i];var prefix=$"participants[{i}]";
            if(p.Id==Guid.Empty)errors[$"{prefix}.id"]=["O identificador da linha é obrigatório."];
            if(!allowed.Contains(p.ParticipantType))errors[$"{prefix}.participantType"]=["Selecione um tipo permitido."];
            if(string.IsNullOrWhiteSpace(p.Name)||p.Name.Trim().Length is <2 or >160)errors[$"{prefix}.name"]=["Informe um nome de 2 a 160 caracteres."];
            if(string.IsNullOrWhiteSpace(p.Role)||p.Role.Trim().Length>120)errors[$"{prefix}.role"]=["Informe um papel de até 120 caracteres."];
            var email=NullIfBlank(p.Email);var phone=NullIfBlank(p.Phone);
            if(email is not null&&(email.Length>254||!MailAddress.TryCreate(email,out var parsed)||!string.Equals(parsed.Address,email,StringComparison.OrdinalIgnoreCase)))errors[$"{prefix}.email"]=["Informe um e-mail válido de até 254 caracteres."];
            if(phone is not null&&phone.Length>40)errors[$"{prefix}.phone"]=["O telefone deve ter até 40 caracteres."];
            if(email is null&&phone is null)errors[$"{prefix}.contact"]=["Informe e-mail ou telefone."];
            if(p.Position<1||p.Position>20)errors[$"{prefix}.position"]=["A posição deve estar entre 1 e 20."];
            if((p.ParticipantType is "patient" or "representative")&&p.SourceId is null)errors[$"{prefix}.sourceId"]=["Selecione explicitamente a pessoa de origem."];
            if((p.ParticipantType is "professional" or "organization_representative")&&p.SourceId is not null)errors[$"{prefix}.sourceId"]=["A identidade anterior não pode ser mantida ao trocar para um participante manual."];
        }
        if(participants.Select(x=>x.Id).Distinct().Count()!=participants.Count)errors["participants.id"]=["Há identificadores de linha duplicados."];
        if(participants.Select(x=>x.Position).Distinct().Count()!=participants.Count)errors["participants.position"]=["Há posições duplicadas."];
        return errors;
    }
    private static string? NullIfBlank(string? value)=>string.IsNullOrWhiteSpace(value)?null:value.Trim();
    private static async Task<Dictionary<string,string[]>> ValidateParticipantSources(NpgsqlConnection c,Guid tenantId,PreparationVersionRow version,IReadOnlyList<SignatureParticipantInput> participants,NpgsqlTransaction tx,CancellationToken ct)
    {
        var errors=new Dictionary<string,string[]>();
        for(var i=0;i<participants.Count;i++)
        {
            var p=participants[i];if(p.ParticipantType=="patient"&&p.SourceId!=version.PatientId)errors[$"participants[{i}].sourceId"]=["O paciente não pertence ao snapshot desta versão."];
            if(p.ParticipantType=="representative")
            {
                if(version.PatientId is null){errors[$"participants[{i}].sourceId"]=["Este documento não possui paciente ou representante no snapshot."];continue;}
                var valid=await c.ExecuteScalarAsync<bool>(new CommandDefinition("SELECT EXISTS(SELECT 1 FROM odca.patients p JOIN odca.patient_representatives r ON (r.tenant_id,r.id)=(p.tenant_id,p.representative_id) WHERE p.tenant_id=@tenantId AND p.id=@patientId AND r.id=@sourceId AND @snapshot::jsonb->'representative'->>'fullName'=r.full_name)",new{tenantId,patientId=version.PatientId,sourceId=p.SourceId,snapshot=version.PatientSnapshot??"null"},tx,cancellationToken:ct));
                if(!valid)errors[$"participants[{i}].sourceId"]=["O representante selecionado não pertence ao snapshot e à relação deste paciente."];
            }
        }
        return errors;
    }
    private static async Task AddPreparationEvent(NpgsqlConnection c,Guid tenantId,Guid preparationId,int revision,Guid actor,string eventType,object details,NpgsqlTransaction tx,CancellationToken ct)
        =>await c.ExecuteAsync(new CommandDefinition("INSERT INTO odca.signature_preparation_events(tenant_id,preparation_id,composition_revision,actor_user_id,event_type,details) VALUES(@tenantId,@preparationId,@revision,@actor,@eventType,@details::jsonb)",new{tenantId,preparationId,revision,actor,eventType,details=JsonSerializer.Serialize(details)},tx,cancellationToken:ct));
    private async Task<SignatureReadinessResponse?> CalculateReadiness(NpgsqlConnection c,Guid tenantId,Guid versionId,NpgsqlTransaction tx,CancellationToken ct)
    {
        var row=await c.QuerySingleOrDefaultAsync<ReadinessRow>(new CommandDefinition("SELECT v.pdf_status AS PdfStatus,v.pdf_storage_key AS PdfStorageKey,v.pdf_sha256 AS PdfSha256,v.review_status AS ReviewStatus,p.id AS PreparationId,p.status AS PreparationStatus,p.composition_revision AS CompositionRevision,(SELECT count(*) FROM odca.signature_participants sp WHERE sp.tenant_id=p.tenant_id AND sp.preparation_id=p.id AND sp.composition_revision=p.composition_revision) AS ParticipantCount FROM odca.generated_contract_versions v LEFT JOIN odca.signature_preparations p ON (p.tenant_id,p.generated_version_id)=(v.tenant_id,v.id) WHERE v.tenant_id=@tenantId AND v.id=@versionId",new{tenantId,versionId},tx,cancellationToken:ct));
        if(row is null)return null;var ok=new List<SignatureReadinessItem>();var blockers=new List<SignatureReadinessItem>();var warnings=new List<SignatureReadinessItem>();
        ok.Add(new("document.exists","Versão documental localizada.","A preparação está vinculada a uma versão imutável.","Nenhuma ação.",null));
        if(row.PdfStatus!="completed"||string.IsNullOrWhiteSpace(row.PdfStorageKey)||string.IsNullOrWhiteSpace(row.PdfSha256))blockers.Add(new("pdf.incomplete","Gere um PDF íntegro antes de confirmar.","A confirmação deve congelar o artefato exato.","Gerar PDF final.","tenant.contract_drafts.manage"));
        else
        {
            var root=configuration["Documents:StoragePath"]??Path.Combine(AppContext.BaseDirectory,"App_Data","documents");var path=Path.Combine(root,row.PdfStorageKey.Replace('/',Path.DirectorySeparatorChar));
            if(!System.IO.File.Exists(path))blockers.Add(new("pdf.missing","O arquivo PDF publicado não foi localizado.","Os metadados não bastam sem o arquivo.","Solicitar reconciliação do armazenamento.","tenant.contract_drafts.manage"));
            else{var bytes=await System.IO.File.ReadAllBytesAsync(path,ct);if(!CryptographicOperations.FixedTimeEquals(SHA256.HashData(bytes),Convert.FromHexString(row.PdfSha256)))blockers.Add(new("pdf.integrity","O hash do PDF diverge do artefato publicado.","A integridade precisa ser comprovada.","Solicitar reconciliação do armazenamento.","tenant.contract_drafts.manage"));else ok.Add(new("pdf.integrity","PDF concluído e íntegro.","O arquivo corresponde ao hash persistido.","Nenhuma ação.",null));}
        }
        if(row.PreparationId is null||row.ParticipantCount<1)blockers.Add(new("participants.empty","Inclua ao menos um participante válido.","Uma composição vazia não pode ser confirmada.","Editar participantes.","tenant.contract_drafts.manage"));else ok.Add(new("participants.valid",$"Composição com {row.ParticipantCount} participante(s).","A composição atual foi validada no servidor.","Nenhuma ação.",null));
        if(row.ReviewStatus=="changes_requested")blockers.Add(new("review.changes","A revisão interna solicitou ajustes.","A política existente impede usar esta versão sem corrigir os ajustes.","Criar e revisar uma nova versão.","tenant.contract_drafts.manage"));
        else if(row.ReviewStatus=="internally_approved")ok.Add(new("review.approved","Documento aprovado internamente.","A revisão aplicável foi concluída.","Nenhuma ação.",null));
        else warnings.Add(new("review.separate","A revisão interna não está aprovada.","Aprovação só é obrigatória quando a política da organização assim determinar.","Consultar a revisão aplicável.","tenant.reviews.read"));
        warnings.Add(new("integration.unavailable","Envio para assinatura indisponível.","Nenhum provedor de assinatura está configurado.","Aguardar integração posterior.",null));
        return new(blockers.Count==0,row.ReviewStatus=="internally_approved",false,blockers.Count==0,ok,blockers,warnings);
    }

    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive=true, Converters={new JsonStringEnumConverter()} };
    private static void Validate(string content,string fields,string values,bool confirmed){var definitions=JsonSerializer.Deserialize<ContractFieldDefinition[]>(fields,JsonOptions)??[];var parsed=StructuredContractDocument.Parse(content,definitions);var fieldValues=JsonSerializer.Deserialize<ContractFieldValue[]>(values,JsonOptions)??[];StructuredContractDocument.ValidateValues(definitions,fieldValues,confirmed);if(definitions.Any(x=>!parsed.FieldOccurrences.ContainsKey(x.Id)))throw new InvalidDataException("Todo campo definido precisa ter ao menos uma ocorrência no documento.");}
    private static DraftResponse ToResponse(DraftRow r)=>new(r.Id,r.ContractId,r.Title,r.SourceTemplateId,r.SourceTemplateVersionId,JsonSerializer.Deserialize<JsonElement>(r.Content),JsonSerializer.Deserialize<JsonElement>(r.Fields),JsonSerializer.Deserialize<JsonElement>(r.Values),r.Version,r.LastClientRevision,r.UpdatedAt);
    private Guid? Actor()=>Guid.TryParse(User.FindFirstValue("sub"),out var id)?id:null;
    private static Task<bool> Allowed(NpgsqlConnection c,Guid actor,Guid tenant,string permission,CancellationToken ct)=>c.ExecuteScalarAsync<bool>(new CommandDefinition("SELECT odca.tenant_actor_has_permission(@actor,@tenant,@permission)",new{actor,tenant,permission},cancellationToken:ct));
    private static Task<int> SetTenant(NpgsqlConnection c,Guid tenant,Guid actor,NpgsqlTransaction tx,CancellationToken ct)=>c.ExecuteAsync(new CommandDefinition("SELECT set_config('odca.tenant_id',@value,true),set_config('odca.actor_id',@actor,true)",new{value=tenant.ToString(),actor=actor.ToString()},tx,cancellationToken:ct));
    private static Task<int> SetTenant(NpgsqlConnection c,Guid tenant,Guid actor,CancellationToken ct)=>c.ExecuteAsync(new CommandDefinition("SELECT set_config('odca.tenant_id',@value,false),set_config('odca.actor_id',@actor,false)",new{value=tenant.ToString(),actor=actor.ToString()},cancellationToken:ct));
    private static async Task<SignaturePreparationResponse?> ReadPreparation(NpgsqlConnection c,Guid tenantId,Guid versionId,NpgsqlTransaction tx,CancellationToken ct)
    {
        var preparation=await c.QuerySingleOrDefaultAsync<PreparationRow>(new CommandDefinition("SELECT id AS Id,status AS Status,row_version AS Version,composition_revision AS CompositionRevision,confirmed_revision AS ConfirmedRevision,confirmed_pdf_sha256 AS ConfirmedPdfSha256,confirmed_at AS ConfirmedAt,confirmation_operation_id AS ConfirmationOperationId FROM odca.signature_preparations WHERE tenant_id=@tenantId AND generated_version_id=@versionId",new{tenantId,versionId},tx,cancellationToken:ct));if(preparation is null)return null;
        var participants=await c.QueryAsync<SignatureParticipantInput>(new CommandDefinition("SELECT client_id AS Id,participant_type AS ParticipantType,source_id AS SourceId,role AS Role,name AS Name,email AS Email,phone AS Phone,position AS Position FROM odca.signature_participants WHERE tenant_id=@tenantId AND preparation_id=@id AND composition_revision=@revision ORDER BY position",new{tenantId,preparation.Id,revision=preparation.CompositionRevision},tx,cancellationToken:ct));return new(preparation.Id,preparation.Status,preparation.Version,participants.AsList(),preparation.CompositionRevision,preparation.ConfirmedRevision,preparation.ConfirmedPdfSha256,preparation.ConfirmedAt);
    }
    private static string SafeFileName(string value)=>string.Concat(value.Normalize().Select(x=>char.IsLetterOrDigit(x)||x is '-' or '_'?x:'_')).Trim('_') is {Length:>0} safe?safe:"documento";
    private sealed record TemplateSource(Guid TemplateId,Guid VersionId,string Content,string Fields);
    private sealed record DraftRow(Guid Id,Guid ContractId,string Title,Guid SourceTemplateId,Guid SourceTemplateVersionId,string Content,string Fields,string Values,long Version,Guid? LastClientRevision,DateTimeOffset UpdatedAt,Guid? PatientId=null,string? PatientSnapshot=null,long? PatientVersion=null,long? CurrentPatientVersion=null,DateTimeOffset? PatientInactiveAt=null,string? TemplateStatus=null);
    private sealed record SaveRow(long Version,Guid ClientRevision,DateTimeOffset SavedAt);
    private sealed record VersionRow(Guid Id,Guid ContractId,string Content,string Sha256,string Status);
    private sealed record GeneratedExistingRow(Guid Id,Guid DraftId,int Number,string Sha256,long ByteSize,DateTimeOffset CreatedAt,string Status,long DraftVersion);
    private sealed record ComparisonRow(Guid Id,Guid ContractId,int Number,string Author,DateTimeOffset CreatedAt,string Status,string Content,string Fields,string Values);
    private sealed record CommentStateRow(bool Resolved,string Reference,long DraftRevision,Guid ContractId);
    private static StudioVersionItem ToItem(ComparisonRow row)=>new(row.Id,row.Number,row.Author,row.CreatedAt,row.Status);
    private static PatientDataChange[] PatientChanges(string? before, string? after)
    {
        if (before is null || after is null) return [];
        using var left = JsonDocument.Parse(before); using var right = JsonDocument.Parse(after);
        var fields = new[] { "fullName", "preferredName", "birthDate", "email", "phone", "address", "identifierType", "identifierValue", "representative" };
        return fields.Select(field => new PatientDataChange(field, JsonValue(left.RootElement, field), JsonValue(right.RootElement, field)))
            .Where(change => !string.Equals(change.Before, change.After, StringComparison.Ordinal)).ToArray();
    }
    private static string? JsonValue(JsonElement element, string name) => element.TryGetProperty(name, out var value) && value.ValueKind != JsonValueKind.Null ? value.ToString() : null;
    private static JsonElement? ParseOptional(string? json)=>json is null?null:JsonSerializer.Deserialize<JsonElement>(json);
    private sealed record ConferenceRow(Guid DraftId,long DraftVersion,string Organization,string Template,int TemplateVersion,string DocumentType,string TemplateStatus,Guid? PatientId,string? PatientName,string? RepresentativeName,long? SelectedPatientVersion,long? CurrentPatientVersion,bool PatientActive,string? SelectedPatientSnapshot,string? CurrentPatientSnapshot,string Content,string Fields,string Values,string ReviewStatus);
    private sealed record PatientConfirmationRow(Guid ContractId,long DraftVersion,long SelectedPatientVersion,long CurrentPatientVersion,DateTimeOffset? PatientInactiveAt);
    private sealed record GeneratedDetailRow(Guid Id,Guid ContractId,Guid DraftId,Guid? PatientId,int Number,string Title,string DocumentType,
        string Organization,string Template,int TemplateVersion,string Author,DateTimeOffset CreatedAt,string Sha256,
        string ReviewStatus,string SignatureStatus,Guid? ReviewId,string? PatientSnapshot,string Content,string Fields,string Values,string PdfStatus,long? PdfByteSize,DateTimeOffset? PdfCompletedAt);
    private sealed record PdfRow(Guid Id,string Title,int Number,string Content,string Fields,string Values,string Status,string? StorageKey,long? ByteSize);
    private sealed record PdfDownloadRow(string StorageKey,string Sha256,string Title,Guid? PatientId);
    private sealed record PreparationRow(Guid Id,string Status,long Version,int CompositionRevision,int? ConfirmedRevision,string? ConfirmedPdfSha256,DateTimeOffset? ConfirmedAt);
    private sealed record PreparationStateRow(Guid Id,string Status,long Version,int CompositionRevision,int? ConfirmedRevision,string? ConfirmedPdfSha256,DateTimeOffset? ConfirmedAt,Guid? ConfirmationOperationId);
    private sealed record PreparationVersionRow(Guid Id,Guid? PatientId,string? PatientSnapshot,string PdfStatus,string? PdfStorageKey,string? PdfSha256,string ReviewStatus);
    private sealed record ReadinessRow(string PdfStatus,string? PdfStorageKey,string? PdfSha256,string ReviewStatus,Guid? PreparationId,string? PreparationStatus,int? CompositionRevision,int ParticipantCount);
    private sealed record SubmittedReviewRow(Guid ReviewId,Guid GeneratedVersionId,string Status,string? Instructions,DateTimeOffset? DueAt,Guid ReviewerId);
}
