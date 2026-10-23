using System.Globalization;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Net.Mail;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using Dapper;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Logging;
using Npgsql;
using Odca.Configuration.Localization;
using Odca.Api.Localization;
using Odca.Application.Contracts;
using Odca.Application.Documents;
using Odca.Application.Signatures;
using Odca.Application.Tenancy;
using Odca.AI;
using Odca.Contracts.Studio;

namespace Odca.Api.Controllers;

[ApiController]
[Authorize(Policy = "PasswordChanged")]
[Route("api/v1/organizations/{tenantId:guid}/studio")]
public sealed partial class ContractStudioController(NpgsqlDataSource dataSource, IConfiguration configuration, ITemplateCatalogRepository templateCatalog, ISignatureEnvelopeProvider envelopeProvider, IAiService ai, IOptions<AiOptions> aiOptions) : ControllerBase
{
    [HttpGet("templates")]
    public async Task<IActionResult> Templates(Guid tenantId, [FromQuery] string? search, [FromQuery] string? type, [FromQuery] string? scope, [FromQuery] string? purpose, [FromQuery] int page = 1, [FromQuery] int pageSize = 12, CancellationToken ct = default)
    {
        var actor = Actor(); if (actor is null) return Unauthorized();
        await using var c = await dataSource.OpenConnectionAsync(ct);
        if (!await Allowed(c, actor.Value, tenantId, "tenant.templates.read", ct)) return Forbid();
        page = Math.Max(page, 1); pageSize = Math.Clamp(pageSize, 1, 50);
        return Ok(await templateCatalog.ListAsync(tenantId, search, type, scope, purpose, page, pageSize, ct));
    }

    [HttpPost("templates")]
    public async Task<IActionResult> CreateTemplate(Guid tenantId,[FromBody] CreateTemplateRequest request,CancellationToken ct)
    {
        var actor=Actor();if(actor is null)return Unauthorized();if(string.IsNullOrWhiteSpace(request.Name)||request.Scope is not ("private" or "consultancy"))return this.Invalid("api.studio.template_name_scope_invalid");
        try{Validate(request.Content.GetRawText(),request.Fields.GetRawText(),"[]",ContractValidationMode.Structural);}catch(InvalidDataException e){return this.CanonicalField("template",e.Message);}
        await using var c=await dataSource.OpenConnectionAsync(ct);if(!await Allowed(c,actor.Value,tenantId,"tenant.templates.manage",ct))return Forbid();await using var tx=await c.BeginTransactionAsync(ct);await SetTenant(c,tenantId,actor.Value,tx,ct);var id=Guid.NewGuid();var versionId=Guid.NewGuid();
        await c.ExecuteAsync(new CommandDefinition("INSERT INTO odca.contract_templates(id,owner_tenant_id,name,description,contract_type,scope,author_id) VALUES(@id,@tenantId,@name,@description,@contractType,@scope,@actor); INSERT INTO odca.contract_template_versions(id,template_id,version_number,content,fields,created_by) VALUES(@versionId,@id,1,@content::jsonb,@fields::jsonb,@actor); INSERT INTO odca.audit_events(scope_type,tenant_id,actor_user_id,action,entity_type,entity_id,result,metadata) VALUES('tenant',@tenantId,@actor,'template.created','contract_template',@id,'success',jsonb_build_object('scope',@scope))",new{id,tenantId,name=request.Name.Trim(),request.Description,request.ContractType,request.Scope,actor,versionId,content=request.Content.GetRawText(),fields=request.Fields.GetRawText()},tx,cancellationToken:ct));await tx.CommitAsync(ct);return Created($"/api/v1/organizations/{tenantId}/studio/templates/{id}",new TemplateMutationResponse(id,1,1,"draft"));
    }

    [HttpPut("templates/{templateId:guid}")]
    public async Task<IActionResult> UpdateTemplate(Guid tenantId,Guid templateId,[FromBody] UpdateTemplateRequest request,CancellationToken ct)
    {
        var actor=Actor();if(actor is null)return Unauthorized();try{Validate(request.Content.GetRawText(),request.Fields.GetRawText(),"[]",ContractValidationMode.Structural);}catch(InvalidDataException e){return this.CanonicalField("template",e.Message);}
        await using var c=await dataSource.OpenConnectionAsync(ct);if(!await Allowed(c,actor.Value,tenantId,"tenant.templates.manage",ct))return Forbid();await using var tx=await c.BeginTransactionAsync(ct);await SetTenant(c,tenantId,actor.Value,tx,ct);
        var changed=await c.QuerySingleOrDefaultAsync<TemplateMutationResponse>(new CommandDefinition("WITH changed AS (UPDATE odca.contract_templates SET name=@name,description=@description,contract_type=@contractType,row_version=row_version+1 WHERE id=@templateId AND owner_tenant_id=@tenantId AND status='draft' AND row_version=@expected RETURNING id,current_version,row_version) UPDATE odca.contract_template_versions v SET content=@content::jsonb,fields=@fields::jsonb FROM changed WHERE v.template_id=@templateId AND v.version_number=changed.current_version RETURNING changed.id AS Id, changed.current_version AS Version, changed.row_version AS RowVersion, 'draft' AS Status",new{name=request.Name.Trim(),request.Description,request.ContractType,templateId,tenantId,expected=request.ExpectedVersion,content=request.Content.GetRawText(),fields=request.Fields.GetRawText()},tx,cancellationToken:ct));if(changed is null)return OdcaErrors.Conflict(this, "api.studio.template_changed_published");await tx.CommitAsync(ct);return Ok(changed);
    }

    [HttpPost("templates/{templateId:guid}/publish")]
    public async Task<IActionResult> PublishTemplate(Guid tenantId,Guid templateId,[FromQuery] long expectedVersion,CancellationToken ct)
    {
        var actor=Actor();if(actor is null)return Unauthorized();await using var c=await dataSource.OpenConnectionAsync(ct);if(!await Allowed(c,actor.Value,tenantId,"tenant.templates.manage",ct))return Forbid();await using var tx=await c.BeginTransactionAsync(ct);await SetTenant(c,tenantId,actor.Value,tx,ct);var source=await c.QuerySingleOrDefaultAsync<TemplateSource>(new CommandDefinition("SELECT t.id AS TemplateId,v.id AS VersionId,v.content::text AS Content,v.fields::text AS Fields,t.name AS TemplateName FROM odca.contract_templates t JOIN odca.contract_template_versions v ON v.template_id=t.id AND v.version_number=t.current_version WHERE t.id=@templateId AND t.owner_tenant_id=@tenantId AND t.status='draft' AND t.row_version=@expectedVersion FOR UPDATE",new{templateId,tenantId,expectedVersion},tx,cancellationToken:ct));if(source is null)return OdcaErrors.Conflict(this, "api.studio.template_not_draft");try{Validate(source.Content,source.Fields,"[]",ContractValidationMode.Structural);}catch(InvalidDataException e){return this.CanonicalField("template",e.Message);}var published=await c.QuerySingleAsync<TemplateMutationResponse>(new CommandDefinition("WITH published AS (UPDATE odca.contract_templates SET status='published',published_at=now(),row_version=row_version+1 WHERE id=@templateId RETURNING id,current_version,row_version), versioned AS (UPDATE odca.contract_template_versions SET published_at=now() WHERE id=@versionId RETURNING 1), audited AS (INSERT INTO odca.audit_events(scope_type,tenant_id,actor_user_id,action,entity_type,entity_id,result) VALUES('tenant',@tenantId,@actor,'template.published','contract_template',@templateId,'success') RETURNING 1) SELECT id AS Id, current_version AS Version, row_version AS RowVersion, 'published' AS Status FROM published",new{templateId,versionId=source.VersionId,tenantId,actor},tx,cancellationToken:ct));await tx.CommitAsync(ct);return Ok(published);
    }

    [HttpPost("templates/{templateId:guid}/duplicate")]
    public async Task<IActionResult> DuplicateTemplate(Guid tenantId,Guid templateId,CancellationToken ct)
    {
        var actor=Actor();if(actor is null)return Unauthorized();await using var c=await dataSource.OpenConnectionAsync(ct);if(!await Allowed(c,actor.Value,tenantId,"tenant.templates.manage",ct))return Forbid();await using var tx=await c.BeginTransactionAsync(ct);await SetTenant(c,tenantId,actor.Value,tx,ct);var id=Guid.NewGuid();var versionId=Guid.NewGuid();var count=await c.ExecuteAsync(new CommandDefinition("INSERT INTO odca.contract_templates(id,owner_tenant_id,name,description,contract_type,scope,author_id) SELECT @id,@tenantId,name||' — cópia',description,contract_type,'private',@actor FROM odca.contract_templates WHERE id=@templateId AND status='published' AND (scope='global' OR owner_tenant_id=@tenantId OR EXISTS(SELECT 1 FROM odca.contract_template_access WHERE template_id=@templateId AND tenant_id=@tenantId AND revoked_at IS NULL)); INSERT INTO odca.contract_template_versions(id,template_id,version_number,content,fields,created_by) SELECT @versionId,@id,1,v.content,v.fields,@actor FROM odca.contract_template_versions v WHERE v.template_id=@templateId AND v.version_number=(SELECT current_version FROM odca.contract_templates WHERE id=@templateId)",new{id,tenantId,actor,templateId,versionId},tx,cancellationToken:ct));if(count<2)return NotFound();await tx.CommitAsync(ct);return Created($"/api/v1/organizations/{tenantId}/studio/templates/{id}",new TemplateMutationResponse(id,1,1,"draft"));
    }

    [HttpPost("templates/{templateId:guid}/archive")]
    public async Task<IActionResult> ArchiveTemplate(Guid tenantId,Guid templateId,[FromQuery] long expectedVersion,[FromQuery] string? reason,CancellationToken ct)
    {var actor=Actor();if(actor is null)return Unauthorized();// D-OC34 B.5: arquivar e ato com motivo obrigatorio — o historico preservado
        // deve dizer por que o modelo saiu de circulacao para novas minutas.
        var reasonText=reason?.Trim()??string.Empty;if(reasonText.Length<5||reasonText.Length>500)return this.Invalid("api.studio.archive_reason_required");await using var c=await dataSource.OpenConnectionAsync(ct);if(!await Allowed(c,actor.Value,tenantId,"tenant.templates.manage",ct))return Forbid();var changed=await c.ExecuteAsync(new CommandDefinition("UPDATE odca.contract_templates SET status='archived',archived_at=now(),archive_reason=@reason,row_version=row_version+1 WHERE id=@templateId AND owner_tenant_id=@tenantId AND row_version=@expectedVersion AND status<>'archived'",new{templateId,tenantId,expectedVersion,reason=reasonText},cancellationToken:ct));return changed==1?NoContent():OdcaErrors.Conflict(this, "api.studio.template_stale");}

    [HttpPost("drafts")]
    public async Task<IActionResult> CreateDraft(Guid tenantId, [FromBody] CreateDraftRequest request, CancellationToken ct)
    {
        var actor = Actor(); if (actor is null) return Unauthorized();
        if (string.IsNullOrWhiteSpace(request.Title) || request.Title.Trim().Length is < 2 or > 160) return this.Invalid("api.studio.draft_title_invalid");
        var contractorSource = request.ContractorSource?.Trim().ToLowerInvariant();
        if (string.IsNullOrEmpty(contractorSource) || contractorSource == "manual") contractorSource = null;
        else if (contractorSource is not ("patient" or "representative"))
            return this.Field("contractorSource", "api.studio.contractor_source_invalid");
        else if (request.PatientId is null)
            return this.Field("contractorSource", "api.studio.contractor_patient_required");
        await using var c = await dataSource.OpenConnectionAsync(ct);
        if (!await Allowed(c, actor.Value, tenantId, "tenant.contract_drafts.manage", ct)) return Forbid();
        await using var tx = await c.BeginTransactionAsync(ct); await SetTenant(c, tenantId, actor.Value, tx, ct);
        var source = await c.QuerySingleOrDefaultAsync<TemplateSource>(new CommandDefinition("""
            SELECT t.id AS TemplateId,v.id AS VersionId,v.content::text AS Content,v.fields::text AS Fields,t.name AS TemplateName,t.official_key AS OfficialKey,t.required_activity_profile AS RequiredProfile
            FROM odca.contract_templates t JOIN odca.contract_template_versions v ON v.template_id=t.id AND v.version_number=t.current_version
            WHERE t.id=@templateId AND t.status='published' AND (t.scope='global' OR t.owner_tenant_id=@tenantId OR EXISTS(SELECT 1 FROM odca.contract_template_access a WHERE a.template_id=t.id AND a.tenant_id=@tenantId AND a.revoked_at IS NULL))
            """, new { request.TemplateId, tenantId }, tx, cancellationToken: ct));
        if (source is null)
        {
            var templateRowExists = await c.ExecuteScalarAsync<bool>(new CommandDefinition(
                "SELECT EXISTS(SELECT 1 FROM odca.contract_templates WHERE id=@templateId)",
                new { request.TemplateId }, tx, cancellationToken: ct));
            return templateRowExists
                ? NotFound(new {
                    title = this.T("api.template.version_missing"),
                    detail = this.T("api.template.version_missing.detail"),
                    code = "template.version_missing",
                    templateId = request.TemplateId
                })
                : NotFound(new {
                    title = this.T("api.template.not_found"),
                    detail = this.T("api.template.not_found.detail"),
                    code = "template.not_found",
                    templateId = request.TemplateId
                });
        }
        // B.3.4 (D1/D3): the surgical template is an Enterprise entitlement gated on the
        // tenant's activity profile; the therapy overlay extends the source document
        // before any draft value or validation runs.
        var activityProfile = await c.ExecuteScalarAsync<string>(new CommandDefinition(
            "SELECT activity_profile FROM odca.tenants WHERE id=@tenantId",
            new { tenantId }, tx, cancellationToken: ct));
        if (OfficialContractTemplates.RequiresOdcaApproval(source.OfficialKey) &&
            !string.Equals(activityProfile, ActivityProfileOverlays.PlasticSurgery, StringComparison.Ordinal))
        {
            return Conflict(new {
                title = this.T("api.draft.profile.surgical_required"),
                detail = this.T("api.draft.profile.surgical_required.detail"),
                code = "draft.profile.surgical_required"
            });
        }
        // D-OC34 B.5: quando o catalogo declara o perfil exigido pelo modelo, divergencia e
        // conflito estavel; coluna nula nao restringe (decisao de catalogo futura).
        if (!string.IsNullOrWhiteSpace(source.RequiredProfile) &&
            !string.Equals(source.RequiredProfile, activityProfile, StringComparison.Ordinal))
        {
            return Conflict(new {
                title = this.T("api.draft.profile.template_required"),
                detail = this.T("api.draft.profile.template_required.detail"),
                code = "draft.profile.template_mismatch"
            });
        }
        var (draftContent, draftFields) = ActivityProfileOverlays.Apply(source.OfficialKey, activityProfile, source.Content, source.Fields);
        var initialValues = await InitialDraftValuesAsync(c, tx, tenantId, request.PatientId, draftFields, contractorSource, ct);
        if (request.PatientId is not null && !await c.ExecuteScalarAsync<bool>(new CommandDefinition("SELECT EXISTS(SELECT 1 FROM odca.patients WHERE tenant_id=@tenantId AND id=@PatientId AND inactive_at IS NULL)", new { tenantId, request.PatientId }, tx, cancellationToken: ct)))
            return this.Field("patientId", "api.studio.draft_patient_inactive");
        Validate(draftContent, draftFields, "[]", ContractValidationMode.Structural);
        // B.3.4: the server seeds the computed mensalidade total on creation when the
        // formula is already decidable (initially "0.00" — nothing selected yet).
        initialValues = StructuredContractDocument.NormalizeStoredValues(draftFields, initialValues);

        if (request.ChangeRequestId.HasValue && !request.ContractId.HasValue)
        {
            return this.Field("changeRequestId", "api.studio.change_request_contract_required");
        }

        Guid contractId;
        Guid draftId;
        bool isResumed = false;
        string? effectiveReference = request.Reference?.Trim();

        async Task<(IActionResult? Error, bool Resumed)> ReconcileExistingDraftAsync(ExistingDraftRow existingDraft, Guid targetContractId)
        {
            if (existingDraft.TemplateId != source.TemplateId)
            {
                return (Conflict(new {
                    title = this.T("api.draft_template_conflict", existingDraft.TemplateName),
                    code = "draft_template_conflict",
                    existingDraftId = existingDraft.Id,
                    existingTemplateId = existingDraft.TemplateId
                }), false);
            }

            if (existingDraft.PatientId.HasValue && request.PatientId.HasValue && existingDraft.PatientId.Value != request.PatientId.Value)
            {
                return (Conflict(new {
                    title = this.T("api.draft_patient_conflict"),
                    code = "draft_patient_conflict",
                    existingDraftId = existingDraft.Id
                }), false);
            }

            if (existingDraft.PatientId.HasValue && !request.PatientId.HasValue)
            {
                return (Conflict(new {
                    title = this.T("api.draft_patient_required"),
                    code = "draft_patient_required",
                    existingDraftId = existingDraft.Id
                }), false);
            }

            if (!existingDraft.PatientId.HasValue && request.PatientId.HasValue)
            {
                await c.ExecuteAsync(new CommandDefinition("""
                    UPDATE odca.contract_drafts
                    SET patient_id = @PatientId,
                        patient_row_version = (SELECT row_version FROM odca.patients WHERE tenant_id=@tenantId AND id=@PatientId),
                        patient_selection_snapshot = odca.patient_document_snapshot(@tenantId, @PatientId),
                        row_version = row_version + 1,
                        updated_by = @actor,
                        updated_at = now()
                    WHERE tenant_id = @tenantId AND id = @existingDraftId;
                    INSERT INTO odca.contract_events(tenant_id, contract_id, actor_id, event_type, details)
                    VALUES(@tenantId, @targetContractId, @actor, 'draft.patient_associated', jsonb_build_object('patientId', @PatientId));
                    """, new { tenantId, targetContractId, existingDraftId = existingDraft.Id, request.PatientId, actor }, tx, cancellationToken: ct));
            }

            return (null, true);
        }

        if (request.ContractId.HasValue)
        {
            var targetContractId = request.ContractId.Value;
            var contractRow = await c.QuerySingleOrDefaultAsync<ContractInfoRow>(new CommandDefinition(
                "SELECT id AS Id, title AS Title, reference AS Reference FROM odca.contracts WHERE tenant_id=@tenantId AND id=@targetContractId",
                new { tenantId, targetContractId }, tx, cancellationToken: ct));
            if (contractRow is null) return NotFound();

            if (string.IsNullOrWhiteSpace(effectiveReference))
            {
                effectiveReference = contractRow.Reference;
            }

            contractId = targetContractId;

            var existingDraft = await c.QuerySingleOrDefaultAsync<ExistingDraftRow>(new CommandDefinition("""
                SELECT d.id AS Id, d.source_template_id AS TemplateId, d.patient_id AS PatientId, t.name AS TemplateName
                FROM odca.contract_drafts d
                JOIN odca.contract_templates t ON t.id = d.source_template_id
                WHERE d.tenant_id = @tenantId AND d.contract_id = @targetContractId
                """, new { tenantId, targetContractId }, tx, cancellationToken: ct));

            if (existingDraft is not null)
            {
                var (error, resumed) = await ReconcileExistingDraftAsync(existingDraft, targetContractId);
                if (error is not null) return error;
                draftId = existingDraft.Id;
                isResumed = resumed;
            }
            else
            {
                draftId = Guid.NewGuid();
                await c.ExecuteAsync(new CommandDefinition("SAVEPOINT create_draft;", tx, cancellationToken: ct));
                try
                {
                    await c.ExecuteAsync(new CommandDefinition("""
                        INSERT INTO odca.contract_drafts(id,tenant_id,contract_id,source_template_id,source_template_version_id,content,fields,values,created_by,updated_by,patient_id,patient_row_version,patient_selection_snapshot)
                        VALUES(@draftId,@tenantId,@contractId,@templateId,@versionId,@content::jsonb,@fields::jsonb,@initialValues::jsonb,@actor,@actor,@PatientId,
                          (SELECT row_version FROM odca.patients WHERE tenant_id=@tenantId AND id=@PatientId),
                          odca.patient_document_snapshot(@tenantId,@PatientId));
                        INSERT INTO odca.contract_events(tenant_id,contract_id,actor_id,event_type,details) VALUES(@tenantId,@contractId,@actor,'draft.created',jsonb_build_object('templateId',@templateId,'templateVersionId',@versionId));
                        """, new { contractId, tenantId, request.PatientId, draftId, templateId = source.TemplateId, versionId = source.VersionId, Content = draftContent, Fields = draftFields, initialValues, actor }, tx, cancellationToken: ct));
                    await c.ExecuteAsync(new CommandDefinition("RELEASE SAVEPOINT create_draft;", tx, cancellationToken: ct));
                }
                catch (PostgresException ex) when (ex.SqlState == "23505" && (ex.ConstraintName == "contract_drafts_tenant_id_contract_id_key" || ex.ConstraintName?.Contains("contract_id") == true))
                {
                    await c.ExecuteAsync(new CommandDefinition("ROLLBACK TO SAVEPOINT create_draft;", tx, cancellationToken: ct));

                    var conflictDraft = await c.QuerySingleOrDefaultAsync<ExistingDraftRow>(new CommandDefinition("""
                        SELECT d.id AS Id, d.source_template_id AS TemplateId, d.patient_id AS PatientId, t.name AS TemplateName
                        FROM odca.contract_drafts d
                        JOIN odca.contract_templates t ON t.id = d.source_template_id
                        WHERE d.tenant_id = @tenantId AND d.contract_id = @contractId
                        """, new { tenantId, contractId }, tx, cancellationToken: ct));

                    if (conflictDraft is null)
                    {
                        return Conflict(new {
                            title = this.T("api.draft_concurrency_conflict"),
                            code = "draft_concurrency_conflict"
                        });
                    }

                    var (conflictError, resumed) = await ReconcileExistingDraftAsync(conflictDraft, contractId);
                    if (conflictError is not null) return conflictError;
                    draftId = conflictDraft.Id;
                    isResumed = resumed;
                }
            }
        }
        else
        {
            contractId = Guid.NewGuid();
            await c.ExecuteAsync(new CommandDefinition("""
                INSERT INTO odca.contracts(id,tenant_id,title,reference) VALUES(@contractId,@tenantId,@title,@reference);
                """, new { contractId, tenantId, title = request.Title.Trim(), reference = effectiveReference }, tx, cancellationToken: ct));

            draftId = Guid.NewGuid();
            await c.ExecuteAsync(new CommandDefinition("""
                INSERT INTO odca.contract_drafts(id,tenant_id,contract_id,source_template_id,source_template_version_id,content,fields,values,created_by,updated_by,patient_id,patient_row_version,patient_selection_snapshot)
                VALUES(@draftId,@tenantId,@contractId,@templateId,@versionId,@content::jsonb,@fields::jsonb,@initialValues::jsonb,@actor,@actor,@PatientId,
                  (SELECT row_version FROM odca.patients WHERE tenant_id=@tenantId AND id=@PatientId),
                  odca.patient_document_snapshot(@tenantId,@PatientId));
                INSERT INTO odca.contract_events(tenant_id,contract_id,actor_id,event_type,details) VALUES(@tenantId,@contractId,@actor,'draft.created',jsonb_build_object('templateId',@templateId,'templateVersionId',@versionId));
                """, new { contractId, tenantId, request.PatientId, draftId, templateId = source.TemplateId, versionId = source.VersionId, Content = draftContent, Fields = draftFields, initialValues, actor }, tx, cancellationToken: ct));
        }

        if (request.ChangeRequestId.HasValue)
        {
            var changeRequestId = request.ChangeRequestId.Value;
            var changeReq = await c.QuerySingleOrDefaultAsync<ChangeRequestRow>(new CommandDefinition("""
                SELECT id AS Id, contract_id AS ContractId, status AS Status, draft_id AS DraftId, row_version AS RowVersion
                FROM odca.contract_change_requests
                WHERE tenant_id = @tenantId AND id = @changeRequestId
                FOR UPDATE
                """, new { tenantId, changeRequestId }, tx, cancellationToken: ct));

            if (changeReq is null || changeReq.ContractId != contractId)
            {
                return this.Field("changeRequestId", "api.studio.change_request_mismatch");
            }

            if (changeReq.Status is not ("draft" or "proposed"))
            {
                return Conflict(new {
                    title = this.T("api.change_request_invalid_status", changeReq.Status),
                    code = "change_request_invalid_status"
                });
            }

            if (changeReq.DraftId.HasValue && changeReq.DraftId.Value != draftId)
            {
                return Conflict(new {
                    title = this.T("api.change_request_already_linked"),
                    code = "change_request_already_linked",
                    draftId = changeReq.DraftId.Value
                });
            }

            if (changeReq.DraftId != draftId)
            {
                var updatedCr = await c.ExecuteAsync(new CommandDefinition("""
                    UPDATE odca.contract_change_requests
                    SET draft_id = @draftId, row_version = row_version + 1, updated_at = now()
                    WHERE tenant_id = @tenantId AND id = @changeRequestId AND draft_id IS NULL AND status IN ('draft', 'proposed');
                    """, new { tenantId, changeRequestId, draftId }, tx, cancellationToken: ct));

                if (updatedCr != 1)
                {
                    return Conflict(new {
                        title = this.T("api.change_request_concurrency_conflict"),
                        code = "change_request_concurrency_conflict"
                    });
                }

                await c.ExecuteAsync(new CommandDefinition("""
                    INSERT INTO odca.audit_events(scope_type, tenant_id, actor_user_id, action, entity_type, entity_id, result, metadata)
                    VALUES('tenant', @tenantId, @actor, 'change_request.draft_linked', 'contract_change_request', @changeRequestId, 'success', jsonb_build_object('contractId', @contractId, 'draftId', @draftId));
                    """, new { tenantId, actor, changeRequestId, contractId, draftId }, tx, cancellationToken: ct));
            }
        }

        await tx.CommitAsync(ct);
        if (isResumed)
        {
            return Ok(new { id = draftId, contractId, resumed = true, message = this.T("api.studio.resume_existing_draft") });
        }

        return Created($"/api/v1/organizations/{tenantId}/studio/drafts/{draftId}", new { id = draftId, contractId });
    }

    [HttpGet("documents")]
    public async Task<IActionResult> ListDocuments(
        Guid tenantId,
        [FromQuery] string? search,
        [FromQuery] string? type,
        [FromQuery] string? stage,
        [FromQuery] Guid? patientId,
        [FromQuery] string? reference = null,
        [FromQuery] string? responsible = null,
        [FromQuery] DateOnly? updatedFrom = null,
        [FromQuery] DateOnly? updatedTo = null,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        CancellationToken ct = default)
    {
        var actor = Actor(); if (actor is null) return Unauthorized();
        await using var c = await dataSource.OpenConnectionAsync(ct);
        var canReadDrafts = await Allowed(c, actor.Value, tenantId, "tenant.contract_drafts.read", ct);
        var canReadPatientDocs = await Allowed(c, actor.Value, tenantId, "tenant.patients.documents.read", ct);
        var canDownloadDocs = await Allowed(c, actor.Value, tenantId, "tenant.documents.download", ct);
        var canManageDrafts = await Allowed(c, actor.Value, tenantId, "tenant.contract_drafts.manage", ct);
        var canRequestReview = await Allowed(c, actor.Value, tenantId, "tenant.reviews.request", ct);
        var canReadReviews = await Allowed(c, actor.Value, tenantId, "tenant.reviews.read", ct);

        if (!canReadDrafts && !canReadPatientDocs && !canDownloadDocs) return Forbid();
        if (updatedFrom is not null && updatedTo is not null && updatedFrom > updatedTo)
            return this.Invalid("api.studio.documents_period_invalid");

        page = Math.Max(page, 1);
        pageSize = Math.Clamp(pageSize, 1, 50);
        var offset = (page - 1) * pageSize;

        await using var tx = await c.BeginTransactionAsync(ct);
        await SetTenant(c, tenantId, actor.Value, tx, ct);

        var queryParams = new DynamicParameters();
        queryParams.Add("tenantId", tenantId);
        queryParams.Add("offset", offset);
        queryParams.Add("limit", pageSize);

        var filters = new List<string> { "c.tenant_id = @tenantId" };
        StudioSemanticSearchInfo? semanticInfo = null;

        if (!string.IsNullOrWhiteSpace(search))
        {
            var phrase = search.Trim();
            queryParams.Add("search", $"%{phrase}%");
            // D-OC53 (Sprint 19/F-D): a busca chega ao texto extraido de cada versao. Os termos
            // (deterministicos no modo palavras-chave; interpretados pelo provedor no modo IA)
            // entram como OR: ampliam recall e jamais retiram um resultado que casava antes.
            var hints = SearchQueryParser.FromText(phrase);
            var engine = "keywords";
            var gateOn = configuration.GetValue("Features:AiAnalysis", false);
            var httpActive = string.Equals(aiOptions.Value.Provider, "http", StringComparison.OrdinalIgnoreCase)
                && !string.IsNullOrWhiteSpace(aiOptions.Value.Endpoint)
                && !string.IsNullOrWhiteSpace(aiOptions.Value.ApiKey);
            if (gateOn && httpActive)
            {
                var moduleState = await c.ExecuteScalarAsync<string>(new CommandDefinition(
                    "SELECT odca.organization_feature_state(@tenantId, 'ia_analysis')", new { tenantId }, tx, cancellationToken: ct));
                if (moduleState is not null and not "allowed")
                {
                    var interpretation = await ai.InterpretSearchQueryAsync(phrase, ct);
                    if (interpretation.ProviderUsed) engine = "ai";
                    hints = interpretation.Hints;
                }
            }

            var termFilters = new List<string>();
            for (var i = 0; i < hints.Terms.Count; i++)
            {
                queryParams.Add("kw" + i, $"%{hints.Terms[i]}%");
                termFilters.Add($"er.raw_text ILIKE @kw{i}");
            }
            var textCondition = string.Join(" OR ", termFilters.Append("er.raw_text ILIKE @search"));
            filters.Add($"""
                (c.title ILIKE @search OR c.reference ILIKE @search OR p.full_name ILIKE @search
                    OR p.identifier_value ILIKE @search
                    OR EXISTS (SELECT 1 FROM odca.document_versions dv
                               JOIN odca.extraction_results er ON er.tenant_id = dv.tenant_id AND er.version_id = dv.id
                              WHERE dv.tenant_id = c.tenant_id AND dv.contract_id = c.id AND ({textCondition}))
                )
                """);

            if (hints.PenaltyMinPercent is double penaltyMin)
            {
                queryParams.Add("penaltyMin", penaltyMin);
                // Limite declarado: formulacao canonica "multa(s)/penalidade(s) ... X %", precisao de uma casa
                // decimal; aplicada nos dois modos (o percentual vem do parser deterministico quando o
                // provedor nao declara).
                filters.Add(@"
                    EXISTS (SELECT 1 FROM odca.document_versions dv
                            JOIN odca.extraction_results er ON er.tenant_id = dv.tenant_id AND er.version_id = dv.id
                           WHERE dv.tenant_id = c.tenant_id AND dv.contract_id = c.id
                             AND (regexp_match(er.raw_text, '(?:multa|penalidade)[^0-9]{0,40}([0-9]+)(?:[.,]([0-9]))?\s*%', 'i'))[1] IS NOT NULL
                             AND ((regexp_match(er.raw_text, '(?:multa|penalidade)[^0-9]{0,40}([0-9]+)(?:[.,]([0-9]))?\s*%', 'i'))[1])::numeric
                               + coalesce(((regexp_match(er.raw_text, '(?:multa|penalidade)[^0-9]{0,40}([0-9]+)(?:[.,]([0-9]))?\s*%', 'i'))[2])::numeric, 0) / 10.0
                               > @penaltyMin)");
            }
            semanticInfo = new StudioSemanticSearchInfo(engine, [.. hints.Terms]);
        }

        if (!string.IsNullOrWhiteSpace(type))
        {
            queryParams.Add("type", type.Trim());
            filters.Add("t.contract_type = @type");
        }

        if (patientId.HasValue)
        {
            queryParams.Add("patientId", patientId.Value);
            filters.Add("p.id = @patientId");
        }

        if (!string.IsNullOrWhiteSpace(reference))
        {
            queryParams.Add("reference", $"%{reference.Trim()}%");
            filters.Add("c.reference ILIKE @reference");
        }

        if (!string.IsNullOrWhiteSpace(responsible))
        {
            queryParams.Add("responsible", $"%{responsible.Trim()}%");
            filters.Add("responsible.display_name ILIKE @responsible");
        }

        if (updatedFrom is not null)
        {
            queryParams.Add("updatedFrom", updatedFrom.Value);
            filters.Add("(COALESCE(d.updated_at, v.created_at, c.updated_at) AT TIME ZONE 'America/Sao_Paulo')::date >= @updatedFrom");
        }

        if (updatedTo is not null)
        {
            queryParams.Add("updatedTo", updatedTo.Value);
            filters.Add("(COALESCE(d.updated_at, v.created_at, c.updated_at) AT TIME ZONE 'America/Sao_Paulo')::date <= @updatedTo");
        }

        if (!string.IsNullOrWhiteSpace(stage))
        {
            switch (stage.Trim().ToLowerInvariant())
            {
                case "draft":
                    filters.Add("d.id IS NOT NULL AND v.id IS NULL");
                    break;
                case "in_review":
                    // 'in_review' é estado do request, não da versão (restrito ao CHECK da tabela);
                    // deriva do request mais recente aberto para a versão.
                    filters.Add("EXISTS (SELECT 1 FROM odca.contract_review_requests rr WHERE rr.tenant_id = c.tenant_id AND rr.generated_version_id = v.id AND rr.status = 'in_review')");
                    break;
                case "changes_requested":
                    filters.Add("EXISTS (SELECT 1 FROM odca.contract_review_requests rr WHERE rr.tenant_id = c.tenant_id AND rr.generated_version_id = v.id AND rr.status = 'changes_requested')");
                    break;
                case "approved":
                    filters.Add("v.review_status = 'internally_approved'");
                    break;
                case "ready_for_signature":
                    filters.Add("v.review_status = 'internally_approved' AND sp.confirmed_revision IS NOT NULL");
                    break;
                case "completed":
                    filters.Add("v.review_status = 'externally_signed'");
                    break;
            }
        }

        var whereClause = string.Join(" AND ", filters);

        var countSql = $"""
            SELECT count(DISTINCT c.id)::int
            FROM odca.contracts c
            LEFT JOIN odca.contract_drafts d ON (d.tenant_id, d.contract_id) = (c.tenant_id, c.id)
            LEFT JOIN LATERAL (
                SELECT v1.id, v1.source_template_id, v1.patient_id, v1.review_status, v1.created_at, v1.created_by
                FROM odca.generated_contract_versions v1
                WHERE (v1.tenant_id, v1.contract_id) = (c.tenant_id, c.id)
                ORDER BY v1.version_number DESC LIMIT 1
            ) v ON true
            LEFT JOIN odca.contract_templates t ON t.id = COALESCE(d.source_template_id, v.source_template_id)
            LEFT JOIN odca.patients p ON (p.tenant_id, p.id) = (c.tenant_id, COALESCE(d.patient_id, v.patient_id))
            LEFT JOIN odca.users responsible ON responsible.id = COALESCE(d.updated_by, v.created_by)
            LEFT JOIN LATERAL (
                SELECT sp1.confirmed_revision
                FROM odca.signature_preparations sp1
                WHERE (sp1.tenant_id, sp1.generated_version_id) = (c.tenant_id, v.id)
                ORDER BY sp1.updated_at DESC LIMIT 1
            ) sp ON true
            WHERE {whereClause}
            """;

        var total = await c.ExecuteScalarAsync<int>(new CommandDefinition(countSql, queryParams, tx, cancellationToken: ct));

        var selectSql = $"""
            SELECT c.id AS ContractId,
                   d.id AS DraftId,
                   v.id AS LatestVersionId,
                   v.version_number AS LatestVersionNumber,
                   c.title AS Title,
                   c.reference AS Reference,
                   COALESCE(t.contract_type, 'service') AS DocumentType,
                   COALESCE(t.name, 'Modelo de documento') AS TemplateName,
                   p.id AS PatientId,
                   p.full_name AS PatientName,
                   COALESCE(d.updated_at, v.created_at, c.updated_at) AS UpdatedAt,
                   CASE WHEN d.id IS NOT NULL THEN 'draft' ELSE 'none' END AS DraftStatus,
                   CASE
                       WHEN review_request.status = 'changes_requested' THEN 'changes_requested'
                       ELSE COALESCE(v.review_status, 'none')
                   END AS ReviewStatus,
                   COALESCE(v.pdf_status, 'not_generated') AS PdfStatus,
                   CASE WHEN sp.id IS NOT NULL THEN (CASE WHEN sp.confirmed_revision IS NOT NULL THEN 'confirmed' ELSE 'preparing' END) ELSE 'not_started' END AS SignatureStatus,
                   v.pdf_byte_size AS PdfByteSize,
                   v.pdf_storage_key AS PdfStorageKey,
                   responsible.display_name AS ResponsibleName,
                   COALESCE(tv.version_number, t.current_version) AS TemplateVersion,
                   COALESCE(p.inactive_at IS NULL, true) AS PatientActive
            FROM odca.contracts c
            LEFT JOIN odca.contract_drafts d ON (d.tenant_id, d.contract_id) = (c.tenant_id, c.id)
            LEFT JOIN LATERAL (
                SELECT v1.id, v1.version_number, v1.created_at, v1.created_by, v1.review_status, v1.pdf_status, v1.pdf_byte_size, v1.pdf_storage_key, v1.source_template_id, v1.source_template_version_id, v1.patient_id
                FROM odca.generated_contract_versions v1
                WHERE (v1.tenant_id, v1.contract_id) = (c.tenant_id, c.id)
                ORDER BY v1.version_number DESC LIMIT 1
            ) v ON true
            LEFT JOIN odca.contract_templates t ON t.id = COALESCE(d.source_template_id, v.source_template_id)
            LEFT JOIN odca.contract_template_versions tv ON tv.id = COALESCE(d.source_template_version_id, v.source_template_version_id)
            LEFT JOIN odca.patients p ON (p.tenant_id, p.id) = (c.tenant_id, COALESCE(d.patient_id, v.patient_id))
            LEFT JOIN odca.users responsible ON responsible.id = COALESCE(d.updated_by, v.created_by)
            LEFT JOIN LATERAL (
                SELECT r.status
                  FROM odca.contract_review_requests r
                 WHERE r.tenant_id = c.tenant_id AND r.generated_version_id = v.id
                 ORDER BY r.updated_at DESC
                 LIMIT 1
            ) review_request ON true
            LEFT JOIN LATERAL (
                SELECT sp1.id, sp1.status, sp1.confirmed_revision
                FROM odca.signature_preparations sp1
                WHERE (sp1.tenant_id, sp1.generated_version_id) = (c.tenant_id, v.id)
                ORDER BY sp1.updated_at DESC LIMIT 1
            ) sp ON true
            WHERE {whereClause}
            ORDER BY COALESCE(d.updated_at, v.created_at, c.updated_at) DESC
            LIMIT @limit OFFSET @offset
            """;

        var rows = (await c.QueryAsync<DocumentRow>(new CommandDefinition(selectSql, queryParams, tx, cancellationToken: ct))).AsList();
        var signaturesAllowed = await c.ExecuteScalarAsync<string>(new CommandDefinition(
            "SELECT odca.organization_feature_state(@tenantId, 'signatures')", queryParams, tx, cancellationToken: ct)) == "allowed";
        await tx.CommitAsync(ct);

        var items = rows.Select(r =>
        {
            var nextAction = "view_sheet";
            if (r.ReviewStatus == "changes_requested")
            {
                nextAction = canManageDrafts && r.DraftId is not null ? "correct_draft" : "view_sheet";
            }
            else if (r.LatestVersionId is null && r.DraftId is not null)
            {
                nextAction = canManageDrafts ? "edit_draft" : "view_sheet";
            }
            else if (r.LatestVersionId is not null && r.PdfStatus != "completed")
            {
                nextAction = canManageDrafts ? "generate_pdf" : "view_version";
            }
            else if (r.LatestVersionId is not null && r.ReviewStatus == "generated")
            {
                nextAction = canRequestReview ? "request_review" : "view_version";
            }
            else if (r.LatestVersionId is not null && (r.ReviewStatus == "submitted" || r.ReviewStatus == "in_review"))
            {
                nextAction = canReadReviews ? "open_review" : "view_version";
            }
            else if (r.LatestVersionId is not null && r.ReviewStatus == "internally_approved" && r.SignatureStatus != "confirmed")
            {
                nextAction = signaturesAllowed && canManageDrafts ? "prepare_signature" : "view_version";
            }
            else if (r.LatestVersionId is not null && r.ReviewStatus == "externally_signed")
            {
                // D-OC34 B.9: jornada encerrada — a proxima acao e concluir; o download do PDF
                // segue disponivel pelo botao de acoes da linha.
                nextAction = "completed";
            }
            else if (r.PdfStatus == "completed")
            {
                nextAction = "download_pdf";
            }

            var canDownloadPdf = r.PdfStatus == "completed" && (canReadDrafts || canDownloadDocs || (canReadPatientDocs && r.PatientId is not null));
            return new StudioDocumentItem(
                r.ContractId,
                r.DraftId,
                r.LatestVersionId,
                r.LatestVersionNumber,
                r.Title,
                r.Reference,
                r.DocumentType,
                r.TemplateName,
                r.PatientId,
                r.PatientName,
                ToUtcOffset(r.UpdatedAt),
                r.DraftStatus,
                r.ReviewStatus,
                r.PdfStatus,
                r.SignatureStatus,
                nextAction,
                canManageDrafts && r.DraftId is not null && nextAction is "edit_draft" or "correct_draft",
                canDownloadPdf,
                r.ResponsibleName,
                r.TemplateVersion,
                r.PatientActive);
        }).ToList();

        return Ok(new StudioDocumentPage(items, page, pageSize, total, semanticInfo));
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
        // D-OC28: textos da conferência resolvidos por catálogo; códigos/permisões permanecem estáveis no wire.
        var pending = checklist.Where(x => x.Severity == "blocker")
            .Select(x => new DocumentPendingItem(x.Code, LocalizeChecklistMessage(x.Code, x.Message, 0),
                this.T("api.studio.conference_reason_blocker"), x.Reference is null ? this.T("api.studio.conference_target_document_data") : this.T("api.studio.conference_target_field", x.Reference!), canEdit)).ToList();
        if (!row.PatientActive) pending.Add(new("patient.inactive", this.T("api.studio.conference_patient_inactive_message"), this.T("api.studio.conference_patient_inactive_detail"), this.T("api.studio.conference_patient_inactive_target"), canEdit));
        if (row.SelectedPatientVersion != row.CurrentPatientVersion) pending.Add(new("patient.version.conflict", this.T("api.studio.conference_patient_updated_message"), this.T("api.studio.conference_patient_updated_detail"), this.T("api.studio.conference_patient_updated_target"), canEdit));
        if (row.TemplateStatus != "published") pending.Add(new("template.unavailable", this.T("api.studio.conference_template_unavailable_message"), this.T("api.studio.conference_template_unavailable_detail"), this.T("api.studio.conference_template_unavailable_target"), false));
        var warnings = checklist.Where(x => x.Severity == "warning")
            .Select(x => new DocumentPendingItem(x.Code, LocalizeChecklistMessage(x.Code, x.Message, 0),
                this.T("api.studio.conference_reason_warning"), x.Reference is null ? this.T("api.studio.conference_target_document_data") : this.T("api.studio.conference_target_field", x.Reference!), canEdit)).ToList();
        var changes = PatientChanges(row.SelectedPatientSnapshot, row.CurrentPatientSnapshot);
        var canGenerate = canEdit && pending.Count == 0;
        await tx.CommitAsync(ct);
        return Ok(new DocumentConferenceResponse(row.DraftId, row.DraftVersion, row.Organization, row.Template,
            row.TemplateVersion, row.DocumentType, row.TemplateStatus, row.PatientId, row.PatientName,
            row.RepresentativeName, row.SelectedPatientVersion, row.CurrentPatientVersion, row.PatientActive,
            canEdit, canGenerate, row.SelectedPatientVersion != row.CurrentPatientVersion, row.ReviewStatus, canGenerate ? "generate" : row.SelectedPatientVersion != row.CurrentPatientVersion ? "confirm_patient" : "complete_fields",
            changes, pending, warnings));
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
              p.row_version AS CurrentPatientVersion,p.inactive_at AS PatientInactiveAt,
              d.fields::text AS Fields,d.values::text AS Values,
              d.patient_selection_snapshot::text AS SelectedSnapshot,
              odca.patient_document_snapshot(@tenantId,d.patient_id)::text AS CurrentSnapshot
            FROM odca.contract_drafts d JOIN odca.patients p ON (p.tenant_id,p.id)=(d.tenant_id,d.patient_id)
            WHERE d.tenant_id=@tenantId AND d.id=@draftId FOR UPDATE OF d,p
            """, new { tenantId, draftId }, tx, cancellationToken: ct));
        if (current is null) return NotFound();
        if (current.PatientInactiveAt is not null) return Conflict(new { title=this.T("api.patient.inactive"), code="patient.inactive" });
        if (current.CurrentPatientVersion != request.ExpectedPatientVersion)
            return Conflict(new { title=this.T("api.patient.registration_changed"), code="patient.version.conflict", currentPatientVersion=current.CurrentPatientVersion });
        if (current.SelectedPatientVersion == current.CurrentPatientVersion) { await tx.CommitAsync(ct); return NoContent(); }
        if (current.DraftVersion != request.ExpectedDraftVersion)
            return Conflict(new { title=this.T("api.draft.version.conflict"), code="draft.version.conflict", currentVersion=current.DraftVersion });
        var differences = PatientChanges(current.SelectedSnapshot, current.CurrentSnapshot);
        var accepted = new HashSet<string>((request.AcceptedKeys ?? []).Where(key => !string.IsNullOrWhiteSpace(key)).Select(key => key.Trim()), StringComparer.Ordinal);
        var declined = differences.Select(change => change.Field).Where(key => !accepted.Contains(key)).ToArray();
        if (declined.Length > 0 && !request.AcknowledgeRemaining)
            return this.Field("acceptedKeys", "api.studio.reconfirm_selection_required");
        var definitions = JsonSerializer.Deserialize<ContractFieldDefinition[]>(current.Fields, JsonOptions) ?? [];
        var existingValues = JsonSerializer.Deserialize<ContractFieldValue[]>(current.Values, JsonOptions) ?? [];
        var selectedValues = DocumentFieldAutofill.ApplyRegistrationSelection(definitions, existingValues, current.CurrentSnapshot, accepted.ToArray());
        var valuesJson = JsonSerializer.Serialize(selectedValues, CamelCaseJsonOptions);
        try
        {
            FieldMapping.Validate(definitions);
            StructuredContractDocument.ValidateValues(definitions, selectedValues, ContractValidationMode.Structural);
        }
        catch (InvalidDataException exception) { return this.CanonicalField("values", exception.Message); }
        var updatedFields = selectedValues
            .Where(value => existingValues.All(currentValue => currentValue.FieldId != value.FieldId || !string.Equals(currentValue.Value, value.Value, StringComparison.Ordinal) || currentValue.Confirmed != value.Confirmed))
            .Select(value => value.FieldId)
            .ToArray();
        var changed = await c.ExecuteAsync(new CommandDefinition("""
            UPDATE odca.contract_drafts SET patient_row_version=@patientVersion,
              patient_selection_snapshot=odca.patient_document_snapshot(@tenantId,patient_id),
              values=@values::jsonb,row_version=row_version+1,
              updated_by=@actor,updated_at=now()
            WHERE tenant_id=@tenantId AND id=@draftId AND row_version=@draftVersion;
            INSERT INTO odca.contract_events(tenant_id,contract_id,actor_id,event_type,details)
            VALUES(@tenantId,@contractId,@actor,'patient.reconfirmed',jsonb_build_object(
              'previousVersion',@previousVersion,'acceptedVersion',@patientVersion,'draftVersion',@draftVersion,
              'acceptedKeys',@acceptedKeys::jsonb,'declinedKeys',@declinedKeys::jsonb,'updatedFields',@updatedFields::jsonb));
            """, new { tenantId, draftId, actor, draftVersion=current.DraftVersion, patientVersion=current.CurrentPatientVersion,
                previousVersion=current.SelectedPatientVersion, current.ContractId, values=valuesJson,
                acceptedKeys=JsonSerializer.Serialize(accepted.OrderBy(key => key, StringComparer.Ordinal).ToArray()),
                declinedKeys=JsonSerializer.Serialize(declined),
                updatedFields=JsonSerializer.Serialize(updatedFields) }, tx, cancellationToken: ct));
        if (changed != 2) return Conflict(new { title=this.T("api.draft.reconfirmation_failed"), code="draft.version.conflict" });
        await tx.CommitAsync(ct); return NoContent();
    }

    // D-OC34 B.6: vincula (ou remove) o paciente de uma minuta existente com versao otimista do
    // rascunho e snapshot cadastral imutavel; aplica os dados cadastrais aos campos de origem
    // patient/representative ainda nao confirmados (mesma semantica da conferencia), preservando
    // valores confirmados. Nunca altera versoes ja geradas.
    [HttpPut("drafts/{draftId:guid}/patient")]
    public async Task<IActionResult> SetDraftPatient(Guid tenantId, Guid draftId, [FromBody] SetDraftPatientRequest request, CancellationToken ct)
    {
        var actor = Actor(); if (actor is null) return Unauthorized();
        await using var c = await dataSource.OpenConnectionAsync(ct);
        if (!await Allowed(c, actor.Value, tenantId, "tenant.contract_drafts.manage", ct)) return Forbid();
        await using var tx = await c.BeginTransactionAsync(ct); await SetTenant(c, tenantId, actor.Value, tx, ct);
        var current = await c.QuerySingleOrDefaultAsync<SetPatientDraftRow>(new CommandDefinition("""
            SELECT d.contract_id AS ContractId, d.row_version AS DraftVersion, d.patient_id AS CurrentPatientId,
                   d.fields::text AS Fields, d.values::text AS Values
            FROM odca.contract_drafts d WHERE d.tenant_id=@tenantId AND d.id=@draftId FOR UPDATE
            """, new { tenantId, draftId }, tx, cancellationToken: ct));
        if (current is null) return NotFound();
        if (current.DraftVersion != request.ExpectedVersion)
            return Conflict(new { title = this.T("api.studio.draft_changed_other_session"), code = "draft.save.conflict", currentVersion = current.DraftVersion });
        string? snapshotJson = null; long? patientRowVersion = null;
        if (request.PatientId is not null)
        {
            var patient = await c.QuerySingleOrDefaultAsync<PatientLinkRow>(new CommandDefinition(
                "SELECT row_version AS RowVersion, inactive_at AS InactiveAt FROM odca.patients WHERE tenant_id=@tenantId AND id=@patientId FOR UPDATE",
                new { tenantId, patientId = request.PatientId }, tx, cancellationToken: ct));
            if (patient is null) return this.Field("patientId", "api.studio.draft_patient_missing");
            if (patient.InactiveAt is not null) return Conflict(new { title = this.T("api.patient.inactive"), code = "patient.inactive" });
            patientRowVersion = patient.RowVersion;
            snapshotJson = await c.ExecuteScalarAsync<string>(new CommandDefinition(
                "SELECT odca.patient_document_snapshot(@tenantId,@patientId)::text", new { tenantId, patientId = request.PatientId }, tx, cancellationToken: ct));
        }
        string valuesJson = current.Values; string[] updatedFieldIds = [];
        if (snapshotJson is not null)
        {
            var definitions = JsonSerializer.Deserialize<ContractFieldDefinition[]>(current.Fields, JsonOptions) ?? [];
            var existingValues = JsonSerializer.Deserialize<ContractFieldValue[]>(current.Values, JsonOptions) ?? [];
            var acceptedKeys = PatientChanges(null, snapshotJson).Select(change => change.Field).ToArray();
            var selectedValues = DocumentFieldAutofill.ApplyRegistrationSelection(definitions, existingValues, snapshotJson, acceptedKeys);
            try
            {
                FieldMapping.Validate(definitions);
                StructuredContractDocument.ValidateValues(definitions, selectedValues, ContractValidationMode.Structural);
            }
            catch (InvalidDataException exception) { return this.CanonicalField("values", exception.Message); }
            valuesJson = JsonSerializer.Serialize(selectedValues, CamelCaseJsonOptions);
            updatedFieldIds = selectedValues
                .Where(value => existingValues.All(existing => existing.FieldId != value.FieldId || !string.Equals(existing.Value, value.Value, StringComparison.Ordinal) || existing.Confirmed != value.Confirmed))
                .Select(value => value.FieldId)
                .ToArray();
        }
        var saved = await c.QuerySingleOrDefaultAsync<SetPatientSavedRow>(new CommandDefinition("""
            UPDATE odca.contract_drafts SET patient_id=@patientId, patient_row_version=@patientVersion,
               patient_selection_snapshot=@snapshot::jsonb, values=@values::jsonb, row_version=row_version+1,
               updated_by=@actor, updated_at=now()
            WHERE tenant_id=@tenantId AND id=@draftId AND row_version=@expectedVersion
            RETURNING row_version AS Version
            """, new { tenantId, draftId, actor, expectedVersion = current.DraftVersion, patientId = request.PatientId, patientVersion = patientRowVersion, snapshot = snapshotJson, values = valuesJson }, tx, cancellationToken: ct));
        if (saved is null) return Conflict(new { title = this.T("api.studio.draft_changed_other_session"), code = "draft.save.conflict" });
        await c.ExecuteAsync(new CommandDefinition("""
            INSERT INTO odca.contract_events(tenant_id, contract_id, actor_id, event_type, details)
            VALUES (@tenantId, @contractId, @actor, 'patient.linked', jsonb_build_object('previousPatientId', @previousPatientId, 'patientId', @patientId, 'draftVersion', @draftVersion, 'updatedFields', @updatedFields::jsonb))
            """, new { tenantId, contractId = current.ContractId, actor, previousPatientId = current.CurrentPatientId, patientId = request.PatientId, draftVersion = saved.Version, updatedFields = JsonSerializer.Serialize(updatedFieldIds) }, tx, cancellationToken: ct));
        await tx.CommitAsync(ct);
        return Ok(new { linked = request.PatientId is not null, version = saved.Version });
    }

    [HttpGet("drafts/{draftId:guid}")]
    public async Task<IActionResult> Draft(Guid tenantId, Guid draftId, CancellationToken ct)
    {
        var actor=Actor(); if(actor is null)return Unauthorized(); await using var c=await dataSource.OpenConnectionAsync(ct);
        if(!await Allowed(c,actor.Value,tenantId,"tenant.contract_drafts.read",ct))return Forbid(); await using var tx=await c.BeginTransactionAsync(ct); await SetTenant(c,tenantId,actor.Value,tx,ct);
        var row=await c.QuerySingleOrDefaultAsync<DraftRow>(new CommandDefinition("SELECT d.id AS Id,d.contract_id AS ContractId,c.title AS Title,d.source_template_id AS SourceTemplateId,d.source_template_version_id AS SourceTemplateVersionId,d.content::text AS Content,d.fields::text AS Fields,d.values::text AS Values,d.row_version AS Version,d.last_client_revision AS LastClientRevision,d.updated_at AS UpdatedAt,t.official_key AS OfficialKey,(SELECT ta.decision FROM odca.template_approvals ta WHERE ta.tenant_id=d.tenant_id AND ta.official_key=t.official_key) AS ApprovalDecision FROM odca.contract_drafts d JOIN odca.contracts c ON c.id=d.contract_id AND c.tenant_id=d.tenant_id LEFT JOIN odca.contract_templates t ON t.id=d.source_template_id WHERE d.tenant_id=@tenantId AND d.id=@draftId",new{tenantId,draftId},tx,cancellationToken:ct));
        if(row is null)return NotFound(); await tx.CommitAsync(ct); return Ok(ToResponse(row));
    }

    [HttpPut("drafts/{draftId:guid}")]
    public async Task<IActionResult> Save(Guid tenantId,Guid draftId,[FromBody] SaveDraftRequest request,CancellationToken ct)
    {
        var actor=Actor();if(actor is null)return Unauthorized(); string content=request.Content.GetRawText(),fields=request.Fields.GetRawText(),values=request.Values.GetRawText();
        try{values=StructuredContractDocument.NormalizeStoredValues(fields,values);Validate(content,fields,values,ContractValidationMode.Structural);}catch(InvalidDataException e){return this.CanonicalField("document",e.Message);}catch(JsonException){return this.Field("document","api.studio.document_format_invalid");}
        await using var c=await dataSource.OpenConnectionAsync(ct);if(!await Allowed(c,actor.Value,tenantId,"tenant.contract_drafts.manage",ct))return Forbid();await using var tx=await c.BeginTransactionAsync(ct);await SetTenant(c,tenantId,actor.Value,tx,ct);
        var receipt=await c.QuerySingleOrDefaultAsync<SaveRow>(new CommandDefinition("SELECT saved_version AS Version,client_revision AS ClientRevision,saved_at AS SavedAt FROM odca.draft_save_receipts WHERE tenant_id=@tenantId AND draft_id=@draftId AND client_revision=@clientRevision",new{tenantId,draftId,request.ClientRevision},tx,cancellationToken:ct));if(receipt is not null){await tx.CommitAsync(ct);return Ok(new SaveDraftResponse(receipt.Version,receipt.ClientRevision,ToUtcOffset(receipt.SavedAt)));}
        var previousValues=await c.ExecuteScalarAsync<string>(new CommandDefinition("SELECT values::text FROM odca.contract_drafts WHERE tenant_id=@tenantId AND id=@draftId",new{tenantId,draftId},tx,cancellationToken:ct));values=StructuredContractDocument.ProtectAutomaticOrigins(fields,previousValues,values);
        var saved=await c.QuerySingleOrDefaultAsync<SaveRow>(new CommandDefinition("UPDATE odca.contract_drafts SET content=@content::jsonb,fields=@fields::jsonb,values=@values::jsonb,row_version=row_version+1,last_client_revision=@clientRevision,updated_by=@actor,updated_at=now() WHERE tenant_id=@tenantId AND id=@draftId AND row_version=@expectedVersion RETURNING row_version AS Version,last_client_revision AS ClientRevision,updated_at AS SavedAt",new{content,fields,values,request.ClientRevision,actor,tenantId,draftId,request.ExpectedVersion},tx,cancellationToken:ct));
        if(saved is null){var current=await c.ExecuteScalarAsync<long?>(new CommandDefinition("SELECT row_version FROM odca.contract_drafts WHERE tenant_id=@tenantId AND id=@draftId",new{tenantId,draftId},tx,cancellationToken:ct));return current is null?NotFound():Conflict(new{title=this.T("api.studio.draft_changed_other_session"),code="draft.save.conflict",currentVersion=current,clientRevision=request.ClientRevision});}
        await c.ExecuteAsync(new CommandDefinition("INSERT INTO odca.draft_save_receipts(tenant_id,draft_id,client_revision,saved_version,saved_at) VALUES(@tenantId,@draftId,@clientRevision,@version,@savedAt); INSERT INTO odca.contract_events(tenant_id,contract_id,actor_id,event_type,details) SELECT tenant_id,contract_id,@actor,'draft.saved',jsonb_build_object('version',row_version,'clientRevision',@clientRevision) FROM odca.contract_drafts WHERE id=@draftId AND tenant_id=@tenantId",new{actor,request.ClientRevision,draftId,tenantId,version=saved.Version,savedAt=saved.SavedAt},tx,cancellationToken:ct));await tx.CommitAsync(ct);return Ok(new SaveDraftResponse(saved.Version,saved.ClientRevision,ToUtcOffset(saved.SavedAt)));
    }

    // D-OC32 B.3: aplica sugestoes de extracao/OCR ao rascunho do estudio. Mapeamento fechado
    // (ExtractionDraftMapping): sem destino unico provavel, o campo fica para decissao humana.
    // Valores aterrissam source="extraction" confirmed=false; nunca sobrescreve valor confirmado
    // nem campo calculado; idempotencia por chave com payload registrado (divergente vira 409).
    [HttpPost("drafts/{draftId:guid}/apply-extraction")]
    public async Task<IActionResult> ApplyExtraction(Guid tenantId, Guid draftId, [FromBody] ApplyDraftExtractionRequest request, CancellationToken ct)
    {
        var actor = Actor(); if (actor is null) return Unauthorized();
        if (request.Decisions is not { Count: > 0 and <= 30 } || request.Decisions.Any(x => x.Status is not ("accepted" or "edited" or "rejected"))) return this.Invalid("api.documents.apply_invalid");
        await using var c = await dataSource.OpenConnectionAsync(ct);
        if (!await Allowed(c, actor.Value, tenantId, "tenant.contract_drafts.manage", ct) || !await Allowed(c, actor.Value, tenantId, "tenant.extractions.apply", ct)) return Forbid();
        await using var tx = await c.BeginTransactionAsync(ct);
        await SetTenant(c, tenantId, actor.Value, tx, ct);
        var draft = await c.QuerySingleOrDefaultAsync<ApplyDraftRow>(new CommandDefinition("SELECT d.id AS Id,d.contract_id AS ContractId,d.row_version AS Version,d.values::text AS Values,d.fields::text AS Fields FROM odca.contract_drafts d WHERE d.tenant_id=@tenantId AND d.id=@draftId FOR UPDATE", new { tenantId, draftId }, tx, cancellationToken: ct));
        if (draft is null) return NotFound();
        var prior = await c.QuerySingleOrDefaultAsync<PriorDraftReview>(new CommandDefinition("SELECT status AS Status,decisions::text AS Decisions FROM odca.draft_extraction_reviews WHERE tenant_id=@tenantId AND idempotency_key=@key", new { tenantId, key = request.IdempotencyKey }, tx, cancellationToken: ct));
        if (prior?.Status == "applied")
        {
            await tx.CommitAsync(ct);
            return SameDraftDecisions(prior.Decisions, request.Decisions)
                ? Ok(new { status = "applied", repeated = true })
                : Conflict(new { title = this.T("api.idempotency.payload.conflict"), code = "idempotency.payload.conflict" });
        }
        await c.ExecuteAsync(new CommandDefinition("INSERT INTO odca.draft_extraction_reviews(id,tenant_id,draft_id,job_id,reviewed_by,draft_row_version,idempotency_key,decisions) VALUES(@reviewId,@tenantId,@draftId,@extractionId,@actor,@expected,@key,@decisions::jsonb) ON CONFLICT(tenant_id,idempotency_key) DO NOTHING", new { reviewId = Guid.NewGuid(), tenantId, draftId, extractionId = request.ExtractionId, actor, expected = request.ExpectedVersion, key = request.IdempotencyKey, decisions = SerializeDraftDecisions(request.Decisions) }, tx, cancellationToken: ct));
        if (draft.Version != request.ExpectedVersion)
        {
            await c.ExecuteAsync(new CommandDefinition("UPDATE odca.draft_extraction_reviews SET status='conflicting' WHERE tenant_id=@tenantId AND idempotency_key=@key", new { tenantId, key = request.IdempotencyKey }, tx, cancellationToken: ct));
            await tx.CommitAsync(ct);
            return Conflict(new { title = this.T("api.studio.draft_changed_other_session"), code = "draft.save.conflict", currentVersion = draft.Version });
        }
        var suggestions = (await c.QueryAsync<ApplySuggestionRow>(new CommandDefinition("""
            SELECT s.id AS Id,s.field_name AS FieldName,s.normalized_value AS NormalizedValue
              FROM odca.extraction_suggestions s
              JOIN odca.extraction_results r ON r.id=s.result_id AND r.tenant_id=s.tenant_id
              JOIN odca.extraction_jobs j ON j.id=r.job_id AND j.tenant_id=r.tenant_id
             WHERE s.tenant_id=@tenantId AND j.id=@extractionId AND j.contract_id=@contractId
             ORDER BY s.id FOR UPDATE
            """, new { tenantId, extractionId = request.ExtractionId, contractId = draft.ContractId }, tx, cancellationToken: ct))).ToDictionary(x => x.Id);
        var definitions = JsonSerializer.Deserialize<ContractFieldDefinition[]>(draft.Fields, CamelCaseJsonOptions) ?? [];
        var values = JsonNode.Parse(draft.Values)?.AsArray() ?? new JsonArray();
        var byId = definitions.ToDictionary(d => d.Id, StringComparer.Ordinal);
        var applied = new JsonArray();
        var skipped = new JsonArray();
        foreach (var decision in request.Decisions)
        {
            if (!suggestions.TryGetValue(decision.SuggestionId, out var suggestion)) return this.Invalid("api.documents.suggestion_missing");
            var value = decision.Status == "edited" ? decision.Value?.Trim() : suggestion.NormalizedValue;
            await c.ExecuteAsync(new CommandDefinition("UPDATE odca.extraction_suggestions SET review_status=@status,reviewed_value=@value,reviewed_by=@actor,reviewed_at=now() WHERE tenant_id=@tenantId AND id=@id", new { decision.Status, value, actor, tenantId, id = decision.SuggestionId }, tx, cancellationToken: ct));
            if (decision.Status == "rejected") continue;
            var skip = (string why) => skipped.Add(new JsonObject { ["suggestionId"] = decision.SuggestionId.ToString(), ["reason"] = why, ["field"] = suggestion.FieldName });
            var (fieldId, reason) = ExtractionDraftMapping.Resolve(suggestion.FieldName, suggestion.NormalizedValue, definitions);
            if (fieldId is null) { skip(reason ?? ExtractionDraftMapping.Unmapped); continue; }
            if (string.IsNullOrWhiteSpace(value)) { skip("value_missing"); continue; }
            var definition = byId[fieldId];
            if (definition.Type == ContractFieldType.Formula) { skip("calculated"); continue; }
            var index = -1;
            for (var i = 0; i < values.Count; i++)
                if ((values[i] as JsonObject)?["fieldId"]?.GetValue<string>() == fieldId) { index = i; break; }
            var current = index >= 0 ? values[index] as JsonObject : null;
            if (current?["confirmed"]?.GetValue<bool>() == true && !string.IsNullOrWhiteSpace(current?["value"]?.GetValue<string>())) { skip("already_confirmed"); continue; }
            if (!StructuredContractDocument.TryValidateValue(definition, value)) { skip("value_invalid_for_field"); continue; }
            var entry = new JsonObject { ["fieldId"] = fieldId, ["value"] = value, ["confirmed"] = false, ["source"] = "extraction" };
            if (index >= 0) values[index] = entry; else values.Add(entry);
            applied.Add(new JsonObject { ["suggestionId"] = decision.SuggestionId.ToString(), ["fieldId"] = fieldId, ["field"] = suggestion.FieldName });
        }
        var jsonOptions = RelaxedJsonOptions;
        var saved = await c.ExecuteScalarAsync<long?>(new CommandDefinition("UPDATE odca.contract_drafts SET values=@values::jsonb,row_version=row_version+1,updated_by=@actor,updated_at=now() WHERE tenant_id=@tenantId AND id=@draftId AND row_version=@expectedVersion RETURNING row_version", new { values = values.ToJsonString(jsonOptions), actor, tenantId, draftId, expectedVersion = request.ExpectedVersion }, tx, cancellationToken: ct));
        if (saved is null)
        {
            await c.ExecuteAsync(new CommandDefinition("UPDATE odca.draft_extraction_reviews SET status='conflicting' WHERE tenant_id=@tenantId AND idempotency_key=@key", new { tenantId, key = request.IdempotencyKey }, tx, cancellationToken: ct));
            await tx.CommitAsync(ct);
            return Conflict(new { title = this.T("api.studio.draft_changed_other_session"), code = "draft.save.conflict", currentVersion = draft.Version });
        }
        await c.ExecuteAsync(new CommandDefinition("""
            UPDATE odca.draft_extraction_reviews SET status='applied',applied_at=now(),applied_field_ids=@appliedFields::jsonb WHERE tenant_id=@tenantId AND idempotency_key=@key;
            INSERT INTO odca.contract_events(tenant_id,contract_id,actor_id,event_type,details) VALUES(@tenantId,@contractId,@actor,'draft.extraction_applied',jsonb_build_object('extractionId',@extractionId,'version',@version,'applied',@appliedDetail::jsonb,'skipped',@skippedDetail::jsonb));
            """, new { appliedFields = applied.ToJsonString(jsonOptions), tenantId, key = request.IdempotencyKey, contractId = draft.ContractId, actor, extractionId = request.ExtractionId, version = saved.Value, appliedDetail = applied.ToJsonString(jsonOptions), skippedDetail = skipped.ToJsonString(jsonOptions) }, tx, cancellationToken: ct));
        await tx.CommitAsync(ct);
        return Ok(new { status = "applied", repeated = false, version = saved.Value, applied, skipped });
    }

    private static string SerializeDraftDecisions(IReadOnlyList<DraftExtractionDecision> decisions)
    {
        var nodes = decisions.Select(x => (JsonNode)new JsonObject { ["suggestionId"] = x.SuggestionId.ToString(), ["status"] = x.Status, ["value"] = x.Value })
            .OrderBy(x => x["suggestionId"]!.GetValue<string>(), StringComparer.Ordinal).ToArray();
        return new JsonArray(nodes).ToJsonString();
    }

    private static bool SameDraftDecisions(string? storedJson, IReadOnlyList<DraftExtractionDecision> submitted)
    {
        var stored = JsonNode.Parse(storedJson ?? "[]")?.AsArray();
        if (stored is null || stored.Count != submitted.Count) return false;
        const char sep = '\u001F';
        var normalized = new List<string>(stored.Count);
        foreach (var node in stored)
        {
            if (node is not JsonObject item) continue;
            normalized.Add(item["suggestionId"]!.GetValue<string>() + sep + item["status"]!.GetValue<string>() + sep + (item["value"]?.ToJsonString() ?? "null"));
        }
        var current = submitted.Select(x => x.SuggestionId.ToString() + sep + x.Status + sep + (System.Text.Json.Nodes.JsonValue.Create(x.Value)?.ToJsonString() ?? "null")).ToList();
        normalized.Sort(StringComparer.Ordinal);
        current.Sort(StringComparer.Ordinal);
        return normalized.SequenceEqual(current, StringComparer.Ordinal);
    }

    private sealed record ApplyDraftRow(Guid Id, Guid ContractId, long Version, string Values, string Fields);
    private sealed record PriorDraftReview(string Status, string Decisions);
    private sealed record ApplySuggestionRow(Guid Id, string FieldName, string? NormalizedValue);

    [HttpPost("drafts/{draftId:guid}/restore-from-version")]
    public async Task<IActionResult> RestoreFromVersion(Guid tenantId,Guid draftId,[FromBody] RestoreDraftVersionRequest request,CancellationToken ct)
    {
        var actor=Actor();if(actor is null)return Unauthorized();
        if(request.VersionId==Guid.Empty||request.ExpectedVersion<=0)return this.Invalid("api.studio.restore_fields_invalid");
        await using var c=await dataSource.OpenConnectionAsync(ct);if(!await Allowed(c,actor.Value,tenantId,"tenant.contract_drafts.manage",ct))return Forbid();await using var tx=await c.BeginTransactionAsync(ct);await SetTenant(c,tenantId,actor.Value,tx,ct);
        var v=await c.QuerySingleOrDefaultAsync<RestoreVersionRow>(new CommandDefinition("SELECT id AS Id,draft_id AS DraftId,version_number AS Number,content::text AS Content,fields::text AS Fields,values::text AS Values FROM odca.generated_contract_versions WHERE tenant_id=@tenantId AND id=@versionId",new{tenantId,versionId=request.VersionId},tx,cancellationToken:ct));
        if(v is null||v.DraftId!=draftId)return NotFound();
        string content=v.Content,fields=v.Fields;string values;
        try{values=StructuredContractDocument.NormalizeStoredValues(fields,v.Values);Validate(content,fields,values,ContractValidationMode.Structural);}catch(InvalidDataException e){return this.CanonicalField("document",e.Message);}catch(JsonException){return this.Field("document","api.studio.document_format_invalid");}
        var d=await c.QuerySingleOrDefaultAsync<RestoreSnapshotRow>(new CommandDefinition("SELECT row_version AS Version,values::text AS Values FROM odca.contract_drafts WHERE tenant_id=@tenantId AND id=@draftId FOR UPDATE",new{tenantId,draftId},tx,cancellationToken:ct));
        if(d is null)return NotFound();
        if(d.Version!=request.ExpectedVersion)return Conflict(new{title=this.T("api.studio.draft_changed_other_session"),code="draft.save.conflict",currentVersion=d.Version});
        values=StructuredContractDocument.ProtectAutomaticOrigins(fields,d.Values,values);
        var saved=await c.QuerySingleOrDefaultAsync<RestoreAppliedRow>(new CommandDefinition("UPDATE odca.contract_drafts SET content=@content::jsonb,fields=@fields::jsonb,values=@values::jsonb,row_version=row_version+1,updated_by=@actor,updated_at=now() WHERE tenant_id=@tenantId AND id=@draftId AND row_version=@expectedVersion RETURNING row_version AS Version,updated_at AS SavedAt",new{content,fields,values,actor,tenantId,draftId,expectedVersion=request.ExpectedVersion},tx,cancellationToken:ct));
        if(saved is null)return Conflict(new{title=this.T("api.studio.draft_changed_other_session"),code="draft.save.conflict",currentVersion=d.Version});
        // D-OC31 B.3: restaurar não reescreve o histórico — a nova revisão fica registrada
        // como evento traceável com a origem (retrato imutável da versão gerada).
        await c.ExecuteAsync(new CommandDefinition("INSERT INTO odca.contract_events(tenant_id,contract_id,actor_id,event_type,details) SELECT tenant_id,contract_id,@actor,'draft.restored',jsonb_build_object('fromVersionId',@versionId,'fromNumber',@number,'newVersion',row_version) FROM odca.contract_drafts WHERE id=@draftId AND tenant_id=@tenantId",new{actor,versionId=request.VersionId,number=v.Number,draftId,tenantId},tx,cancellationToken:ct));
        await tx.CommitAsync(ct);return Ok(new RestoreDraftResponse(saved.Version,v.Number,ToUtcOffset(saved.SavedAt)));
    }

    [HttpPost("drafts/{draftId:guid}/versions")]
    public async Task<IActionResult> Generate(Guid tenantId,Guid draftId,[FromBody] GenerateVersionRequest request,CancellationToken ct)
    {
        var actor=Actor();if(actor is null)return Unauthorized();
        if(request.IdempotencyKey==Guid.Empty || request.ExpectedVersion<=0)return this.Invalid("api.studio.generate_fields_invalid");
        await using var c=await dataSource.OpenConnectionAsync(ct);if(!await Allowed(c,actor.Value,tenantId,"tenant.contract_drafts.manage",ct))return Forbid();await using var tx=await c.BeginTransactionAsync(ct);await SetTenant(c,tenantId,actor.Value,tx,ct);
        var existing=await c.QuerySingleOrDefaultAsync<GeneratedExistingRow>(new CommandDefinition("SELECT id AS Id,draft_id AS DraftId,version_number AS Number,canonical_sha256 AS Sha256,byte_size AS ByteSize,created_at AS CreatedAt,review_status AS Status,draft_row_version AS DraftVersion FROM odca.generated_contract_versions WHERE tenant_id=@tenantId AND idempotency_key=@idempotencyKey",new{tenantId,request.IdempotencyKey},tx,cancellationToken:ct));if(existing is not null){if(existing.DraftId!=draftId||request.ExpectedVersion!=existing.DraftVersion)return Conflict(new{title=this.T("api.idempotency.payload.conflict"),code="idempotency.payload.conflict"});await tx.CommitAsync(ct);return Ok(new GeneratedVersionResponse(existing.Id,existing.Number,existing.Sha256,existing.ByteSize,ToUtcOffset(existing.CreatedAt),existing.Status));}
        var d=await c.QuerySingleOrDefaultAsync<DraftRow>(new CommandDefinition("SELECT d.id AS Id,d.contract_id AS ContractId,c.title AS Title,d.source_template_id AS SourceTemplateId,d.source_template_version_id AS SourceTemplateVersionId,d.content::text AS Content,d.fields::text AS Fields,d.values::text AS Values,d.row_version AS Version,d.last_client_revision AS LastClientRevision,d.updated_at AS UpdatedAt,d.patient_id AS PatientId,d.patient_row_version AS PatientVersion,t.status AS TemplateStatus,p.row_version AS CurrentPatientVersion,p.inactive_at AS PatientInactiveAt,odca.patient_document_snapshot(@tenantId,d.patient_id)::text AS PatientSnapshot FROM odca.contract_drafts d JOIN odca.contracts c ON c.id=d.contract_id AND c.tenant_id=d.tenant_id JOIN odca.contract_templates t ON t.id=d.source_template_id LEFT JOIN LATERAL (SELECT patient.row_version,patient.inactive_at FROM odca.patients patient WHERE (patient.tenant_id,patient.id)=(d.tenant_id,d.patient_id) FOR UPDATE) p ON true WHERE d.tenant_id=@tenantId AND d.id=@draftId FOR UPDATE OF d",new{tenantId,draftId},tx,cancellationToken:ct));if(d is null)return NotFound();
        if(d.TemplateStatus!="published")return Conflict(new{title=this.T("api.template.unavailable"),code="template.unavailable"});
        if(d.PatientId is not null && d.PatientInactiveAt is not null)return Conflict(new{title=this.T("api.patient.inactive"),detail=this.T("api.patient.inactive.generate_detail"),code="patient.inactive"});
        if(d.PatientId is not null && d.PatientVersion!=d.CurrentPatientVersion)return Conflict(new{title=this.T("api.patient.data_changed"),detail=this.T("api.patient.data_changed.detail"),code="patient.version.conflict",currentPatientVersion=d.CurrentPatientVersion});
        if(d.Version!=request.ExpectedVersion)return Conflict(new{title=this.T("api.studio.generate_version_conflict"),code="draft.generate.conflict",currentVersion=d.Version});try{d.Values=StructuredContractDocument.NormalizeStoredValues(d.Fields,d.Values);Validate(d.Content,d.Fields,d.Values,ContractValidationMode.Confirmed);}catch(InvalidDataException e){return this.CanonicalField("pendingFields",e.Message);}
        var number=await c.ExecuteScalarAsync<int>(new CommandDefinition("SELECT coalesce(max(version_number),0)+1 FROM odca.generated_contract_versions WHERE draft_id=@draftId",new{draftId},tx,cancellationToken:ct));
        var snapshot=Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new{schemaVersion=2,patient=d.PatientSnapshot is null?(JsonElement?)null:JsonSerializer.Deserialize<JsonElement>(d.PatientSnapshot),content=JsonSerializer.Deserialize<JsonElement>(d.Content),fields=JsonSerializer.Deserialize<JsonElement>(d.Fields),values=JsonSerializer.Deserialize<JsonElement>(d.Values)}));var hash=Convert.ToHexString(SHA256.HashData(snapshot)).ToLowerInvariant();var id=Guid.NewGuid();var key=$"generated/{tenantId:N}/{d.ContractId:N}/{id:N}.json";var root=configuration["Documents:StoragePath"]??Path.Combine(AppContext.BaseDirectory,"App_Data","documents");var path=Path.Combine(root,key.Replace('/',Path.DirectorySeparatorChar));Directory.CreateDirectory(Path.GetDirectoryName(path)!);await System.IO.File.WriteAllBytesAsync(path,snapshot,ct);
        // D-A4: o CNPJ da organização na data da emissão faz parte do snapshot imutável.
        var metadata=JsonSerializer.Serialize(new{title=d.Title,organization=await c.ExecuteScalarAsync<string>(new CommandDefinition("SELECT display_name FROM odca.tenants WHERE id=@tenantId",new{tenantId},tx,cancellationToken:ct)),tax_id=await c.ExecuteScalarAsync<string?>(new CommandDefinition("SELECT business_code FROM odca.tenants WHERE id=@tenantId",new{tenantId},tx,cancellationToken:ct)),template=await c.ExecuteScalarAsync<string>(new CommandDefinition("SELECT name FROM odca.contract_templates WHERE id=@sourceTemplateId",new{d.SourceTemplateId},tx,cancellationToken:ct)),author=await c.ExecuteScalarAsync<string>(new CommandDefinition("SELECT display_name FROM odca.users WHERE id=@actor",new{actor},tx,cancellationToken:ct))});
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
              coalesce(v.emission_metadata->>'tax_id',tenant.business_code) AS OrganizationTaxId,
              coalesce(v.emission_metadata->>'template',t.name) AS Template,tv.version_number AS TemplateVersion,coalesce(v.emission_metadata->>'author',u.display_name) AS Author,
              v.created_at AS CreatedAt,v.canonical_sha256 AS Sha256,
              CASE WHEN review.status='changes_requested' THEN 'changes_requested' ELSE v.review_status END AS ReviewStatus,
              review.id AS ReviewId,v.patient_snapshot::text AS PatientSnapshot,
              v.content::text AS Content,v.fields::text AS Fields,v.values::text AS Values,v.pdf_status AS PdfStatus,v.pdf_byte_size AS PdfByteSize,v.pdf_completed_at AS PdfCompletedAt
            FROM odca.generated_contract_versions v
            JOIN odca.contracts c ON (c.tenant_id,c.id)=(v.tenant_id,v.contract_id)
            JOIN odca.tenants tenant ON tenant.id=v.tenant_id
            JOIN odca.contract_templates t ON t.id=v.source_template_id
            JOIN odca.contract_template_versions tv ON tv.id=v.source_template_version_id AND tv.template_id=t.id
            JOIN odca.users u ON u.id=v.created_by
            LEFT JOIN LATERAL (SELECT r.id,r.status FROM odca.contract_review_requests r
              WHERE r.tenant_id=v.tenant_id AND r.generated_version_id=v.id
              ORDER BY r.opened_at DESC,r.id LIMIT 1) review ON true
            WHERE v.tenant_id=@tenantId AND v.id=@versionId
            """,new{tenantId,versionId},tx,cancellationToken:ct));
        if(row is null)return NotFound();
        if(!canReadDrafts&&row.PatientId is null)return Forbid();
        var preparation=await ReadPreparation(c,tenantId,versionId,actor.Value,tx,ct);
        if(preparation is not null)preparation=preparation with{Readiness=await CalculateReadiness(c,tenantId,versionId,tx,ct)};
        // D-OC37 (F-1): o status de assinatura do detalhe passa a ser derivado da preparação real,
        // com o mesmo vocabulário estável do endpoint de listagem (not_started/preparing/confirmed) —
        // antes saía 'not_available' hardcoded, inconsistente com a própria listagem.
        var signatureStatus=preparation is null?"not_started":string.Equals(preparation.Status,"confirmed",StringComparison.Ordinal)?"confirmed":"preparing";
        string html;
        var cover = new ContractDocumentRenderer.DocumentCoverInfo(row.Organization, row.OrganizationTaxId, row.Title, row.Number, ToUtcOffset(row.CreatedAt), row.Author);
        try { html=ContractDocumentRenderer.ToHtml(row.Content,row.Fields,row.Values,cover); }
        catch(InvalidDataException exception)
        {
            // D-OC27: título por chave; detalhe canônico (renderer) resolvido por mapa.
            var culture=this.Culture();
            var localizer=HttpContext.RequestServices.GetRequiredService<Odca.Configuration.Localization.LocalizationService>();
            return this.Status(StatusCodes.Status422UnprocessableEntity,"api.studio.history_render_failed",ApiMessages.Localize(localizer,exception.Message,culture) ?? exception.Message);
        }
        await tx.CommitAsync(ct); return Ok(new GeneratedVersionDetail(row.Id,row.ContractId,row.DraftId,row.Number,row.Title,row.DocumentType,
            row.Organization,row.Template,row.TemplateVersion,row.Author,ToUtcOffset(row.CreatedAt),row.Sha256,row.ReviewStatus,
            signatureStatus,row.ReviewId,ParseOptional(row.PatientSnapshot),JsonSerializer.Deserialize<JsonElement>(row.Content),
            JsonSerializer.Deserialize<JsonElement>(row.Fields),JsonSerializer.Deserialize<JsonElement>(row.Values),html,row.PdfStatus,row.PdfByteSize,ToUtcOffset(row.PdfCompletedAt),preparation));
    }

    [HttpPost("versions/{versionId:guid}/pdf")]
    public async Task<IActionResult> GeneratePdf(Guid tenantId,Guid versionId,CancellationToken ct)
    {
        var actor=Actor();if(actor is null)return Unauthorized();await using var c=await dataSource.OpenConnectionAsync(ct);
        if(!await Allowed(c,actor.Value,tenantId,"tenant.contract_drafts.manage",ct))return Forbid();
        await using var tx=await c.BeginTransactionAsync(ct);await SetTenant(c,tenantId,actor.Value,tx,ct);
        var row=await c.QuerySingleOrDefaultAsync<PdfRow>(new CommandDefinition("""
            SELECT id AS Id,coalesce(emission_metadata->>'title','Documento') AS Title,version_number AS Number,content::text AS Content,fields::text AS Fields,values::text AS Values,pdf_status AS Status,pdf_storage_key AS StorageKey,pdf_byte_size AS ByteSize,
              coalesce(emission_metadata->>'organization',(SELECT display_name FROM odca.tenants WHERE id=odca.generated_contract_versions.tenant_id)) AS Organization,
              coalesce(emission_metadata->>'tax_id',(SELECT business_code FROM odca.tenants WHERE id=odca.generated_contract_versions.tenant_id)) AS TaxId,
              coalesce(emission_metadata->>'author',(SELECT display_name FROM odca.users WHERE id=odca.generated_contract_versions.created_by)) AS Author,
              created_at AS CreatedAt
            FROM odca.generated_contract_versions WHERE tenant_id=@tenantId AND id=@versionId FOR UPDATE
            """,new{tenantId,versionId},tx,cancellationToken:ct));
        if(row is null)return NotFound();if(row.Status=="completed"){await tx.CommitAsync(ct);return Ok(new{status="completed",byteSize=row.ByteSize,replayed=true});}
        // D-A4: artefato imutável usa apenas o snapshot persistido (created_at +
        // emission_metadata), nunca o relógio do momento da reimpressão.
        byte[] pdf;try{pdf=ContractDocumentRenderer.ToPdf(row.Content,row.Fields,row.Values,row.Title,row.Number,new ContractDocumentRenderer.DocumentCoverInfo(row.Organization,row.TaxId,row.Title,row.Number,ToUtcOffset(row.CreatedAt),row.Author));}catch(InvalidDataException e){await c.ExecuteAsync(new CommandDefinition("UPDATE odca.generated_contract_versions SET pdf_status='failed',pdf_failure_code='invalid_structure' WHERE tenant_id=@tenantId AND id=@versionId",new{tenantId,versionId},tx,cancellationToken:ct));await tx.CommitAsync(ct);var culture=this.Culture();var localizer=HttpContext.RequestServices.GetRequiredService<Odca.Configuration.Localization.LocalizationService>();return this.Status(StatusCodes.Status422UnprocessableEntity,"api.studio.pdf_generation_failed",ApiMessages.Localize(localizer,e.Message,culture) ?? e.Message);}
        var hash=Convert.ToHexString(SHA256.HashData(pdf)).ToLowerInvariant();var key=$"generated/{tenantId:N}/{versionId:N}/final-{ContractDocumentRenderer.PdfRendererVersion}-{hash[..16]}.pdf";var root=configuration["Documents:StoragePath"]??Path.Combine(AppContext.BaseDirectory,"App_Data","documents");var path=Path.Combine(root,key.Replace('/',Path.DirectorySeparatorChar));var temporary=path+".attempt-"+Guid.NewGuid().ToString("N");Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        try
        {
            await System.IO.File.WriteAllBytesAsync(temporary,pdf,ct);
            if(System.IO.File.Exists(path)){var existing=await System.IO.File.ReadAllBytesAsync(path,ct);if(!CryptographicOperations.FixedTimeEquals(SHA256.HashData(existing),SHA256.HashData(pdf)))return this.Status(StatusCodes.Status409Conflict,"api.studio.pdf_artifact_divergent");System.IO.File.Delete(temporary);}else System.IO.File.Move(temporary,path,false);
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
            if(!reserved)return this.Status(StatusCodes.Status413PayloadTooLarge,"api.studio.pdf_storage_quota");
            await c.ExecuteAsync(new CommandDefinition("UPDATE odca.generated_contract_versions SET pdf_status='completed',pdf_storage_key=@key,pdf_sha256=@hash,pdf_byte_size=@size,pdf_renderer_version=@renderer,pdf_completed_at=now(),pdf_failure_code=NULL WHERE tenant_id=@tenantId AND id=@versionId",new{tenantId,versionId,key,hash,size=pdf.LongLength,renderer=ContractDocumentRenderer.PdfRendererVersion},tx,cancellationToken:ct));await tx.CommitAsync(ct);return Ok(new{status="completed",byteSize=pdf.LongLength,replayed=false});
        }
        finally{if(System.IO.File.Exists(temporary))System.IO.File.Delete(temporary);}
    }

    [HttpGet("versions/{versionId:guid}/pdf")]
    public async Task<IActionResult> DownloadPdf(Guid tenantId,Guid versionId,CancellationToken ct)
    {
        var actor=Actor();if(actor is null)return Unauthorized();await using var c=await dataSource.OpenConnectionAsync(ct);var canReadPatientDocuments=await Allowed(c,actor.Value,tenantId,"tenant.patients.documents.read",ct);var canReadDrafts=await Allowed(c,actor.Value,tenantId,"tenant.contract_drafts.read",ct);if(!canReadPatientDocuments&&!canReadDrafts)return Forbid();await using var tx=await c.BeginTransactionAsync(ct);await SetTenant(c,tenantId,actor.Value,tx,ct);
        var row=await c.QuerySingleOrDefaultAsync<PdfDownloadRow>(new CommandDefinition("SELECT pdf_storage_key AS StorageKey,pdf_sha256 AS Sha256,coalesce(emission_metadata->>'title','documento') AS Title,patient_id AS PatientId FROM odca.generated_contract_versions WHERE tenant_id=@tenantId AND id=@versionId AND pdf_status='completed'",new{tenantId,versionId},tx,cancellationToken:ct));if(row is null)return NotFound();if(!canReadDrafts&&row.PatientId is null)return Forbid();var root=configuration["Documents:StoragePath"]??Path.Combine(AppContext.BaseDirectory,"App_Data","documents");var path=Path.Combine(root,row.StorageKey.Replace('/',Path.DirectorySeparatorChar));if(!System.IO.File.Exists(path))return this.Status(StatusCodes.Status410Gone,"api.studio.pdf_unavailable");var bytes=await System.IO.File.ReadAllBytesAsync(path,ct);if(!CryptographicOperations.FixedTimeEquals(SHA256.HashData(bytes),Convert.FromHexString(row.Sha256)))return this.Status(StatusCodes.Status409Conflict,"api.studio.pdf_integrity_unconfirmed");await tx.CommitAsync(ct);return File(bytes,"application/pdf",$"{SafeFileName(row.Title)}.pdf",false);
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
        if(Actor() is not Guid actor)return Unauthorized();
        // D-OC27: mensagens canônicas do validador resolvidas por mapa reverso; desconhecidas voltam como vieram.
        var culture=this.Culture();
        var localizer=HttpContext.RequestServices.GetRequiredService<Odca.Configuration.Localization.LocalizationService>();
        var participants=request.Participants;
        var errors=ValidateParticipants(participants);
        if(request.Confirm&&(request.OperationId is null||request.OperationId==Guid.Empty))errors["operationId"]=["A chave da confirmação é obrigatória."];
        if(errors.Count>0)return this.Errors(errors.Select(entry=>new KeyValuePair<string,string[]>(entry.Key,entry.Value.Select(message=>ApiMessages.Localize(localizer,message,culture) ?? message).ToArray())).ToDictionary(pair=>pair.Key,pair=>pair.Value));
        var normalized=participants!.Select(x=>x with{Role=x.Role.Trim(),Name=x.Name.Trim(),Email=NullIfBlank(x.Email),Phone=NullIfBlank(x.Phone)}).OrderBy(x=>x.Position).ToArray();
        await using var c=await dataSource.OpenConnectionAsync(ct);if(!await Allowed(c,actor,tenantId,"tenant.contract_drafts.manage",ct))return Forbid();
        await using var tx=await c.BeginTransactionAsync(ct);await SetTenant(c,tenantId,actor,tx,ct);
        var version=await c.QuerySingleOrDefaultAsync<PreparationVersionRow>(new CommandDefinition("SELECT id AS Id,patient_id AS PatientId,patient_snapshot::text AS PatientSnapshot,pdf_status AS PdfStatus,pdf_storage_key AS PdfStorageKey,pdf_sha256 AS PdfSha256,review_status AS ReviewStatus FROM odca.generated_contract_versions WHERE tenant_id=@tenantId AND id=@versionId",new{tenantId,versionId},tx,cancellationToken:ct));
        if(version is null)return NotFound();
        string? commandHash=null;
        if(request.Confirm)
        {
            commandHash=CommandHash(new{versionId,request.ExpectedVersion,request.Confirm,participants=normalized.Select(x=>new{x.Id,x.ParticipantType,x.SourceId,x.Role,x.Name,x.Email,x.Phone,x.Position}).ToArray()});
            await LockOperation(c,request.OperationId!.Value,tx,ct);
            var receipt=await ReadOperation(c,tenantId,request.OperationId.Value,tx,ct);
            if(receipt is not null)
            {
                if(receipt.OperationType!="confirm"||receipt.CommandHash!=commandHash)return Conflict(new{title=this.T("api.preparation.idempotency.conflict"),code="preparation.idempotency.conflict"});
                // A resposta persistida é o snapshot original (replayed=false); o replay
                // deve sinalizar que é reexecução idempotente do comando.
                var stored=JsonNode.Parse(receipt.ResponseJson) as JsonObject;
                if(stored is not null)stored["replayed"]=true;
                await tx.CommitAsync(ct);return Content(stored?.ToJsonString() ?? receipt.ResponseJson,"application/json");
            }
        }
        var sourceErrors=await ValidateParticipantSources(c,tenantId,version,normalized,tx,ct);if(sourceErrors.Count>0)return this.Errors(sourceErrors.Select(entry=>new KeyValuePair<string,string[]>(entry.Key,entry.Value.Select(message=>ApiMessages.Localize(localizer,message,culture) ?? message).ToArray())).ToDictionary(pair=>pair.Key,pair=>pair.Value));
        var current=await c.QuerySingleOrDefaultAsync<PreparationStateRow>(new CommandDefinition("SELECT id AS Id,status AS Status,row_version AS Version,composition_revision AS CompositionRevision,confirmed_revision AS ConfirmedRevision,confirmed_pdf_sha256 AS ConfirmedPdfSha256,confirmed_at AS ConfirmedAt,confirmation_operation_id AS ConfirmationOperationId FROM odca.signature_preparations WHERE tenant_id=@tenantId AND generated_version_id=@versionId FOR UPDATE",new{tenantId,versionId},tx,cancellationToken:ct));
        Guid preparationId;int revision;long nextVersion;
        if(current is null)
        {
            if(request.ExpectedVersion!=0)return Conflict(new{title=this.T("api.preparation.creation.not_found"),code="preparation.creation.conflict"});
            preparationId=Guid.NewGuid();
            var created=await c.ExecuteScalarAsync<bool>(new CommandDefinition("INSERT INTO odca.signature_preparations(id,tenant_id,generated_version_id,status,created_by,updated_by) VALUES(@preparationId,@tenantId,@versionId,'draft',@actor,@actor) ON CONFLICT(tenant_id,generated_version_id) DO NOTHING RETURNING true",new{preparationId,tenantId,versionId,actor},tx,cancellationToken:ct));
            if(!created)return Conflict(new{title=this.T("api.preparation.creation.conflict"),code="preparation.creation.conflict"});
            revision=1;nextVersion=1;
            await AddPreparationEvent(c,tenantId,preparationId,revision,actor,"created",new{count=normalized.Length},tx,ct);
        }
        else
        {
            if(current.Version!=request.ExpectedVersion)return Conflict(new{title=this.T("api.preparation.version.conflict"),code="preparation.version.conflict",currentVersion=current.Version});
            if(current.Status=="confirmed")return Conflict(new{title=this.T("api.preparation.confirmed"),code="preparation.confirmed"});
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
            string[]? readinessBlockers = readiness?.Blockers.Select(x => ApiMessages.Localize(localizer, x.Message, culture) ?? x.Message).ToArray();
            if (readiness is null || !readiness.CanConfirm)
            {
                if (readinessBlockers is null)
                {
                    var fallback = ApiMessages.Localize(localizer, "A conferência não pôde ser calculada.", culture) ?? "A conferência não pôde ser calculada.";
                    readinessBlockers = [fallback];
                }
                return this.Errors(new Dictionary<string, string[]> { { "confirm", readinessBlockers } });
            }
            await c.ExecuteAsync(new CommandDefinition("UPDATE odca.signature_preparations SET status='confirmed',confirmed_revision=composition_revision,confirmed_pdf_storage_key=@key,confirmed_pdf_sha256=@hash,confirmed_at=now(),confirmed_by=@actor,confirmation_operation_id=@operationId WHERE tenant_id=@tenantId AND id=@preparationId",new{tenantId,preparationId,key=version.PdfStorageKey,hash=version.PdfSha256,actor,operationId=request.OperationId},tx,cancellationToken:ct));
            await AddPreparationEvent(c,tenantId,preparationId,revision,actor,"confirmed",new{pdfSha256=version.PdfSha256,pdfStorageKey=version.PdfStorageKey},tx,ct);
            var response=JsonSerializer.Serialize(new{status="confirmed",version=nextVersion,compositionRevision=revision,replayed=false});
            await AddOperation(c,tenantId,preparationId,request.OperationId!.Value,"confirm",commandHash!,response,actor,tx,ct);
            await tx.CommitAsync(ct);return Content(response,"application/json");
        }
        await tx.CommitAsync(ct);return Ok(new{status=request.Confirm?"confirmed":"draft",version=nextVersion,compositionRevision=revision});
    }

    // D-OC37 (Sprint 3/A-1): envelope externo de assinatura da versão confirmada. Um envelope por
    // versão gerada (idempotência por chave natural determinística, sem OperationId): em "sent" o
    // envio é replay sem novo consumo; após "declined"/"failed" uma nova tentativa consome unidade
    // de franquia com chave v{row_version+1}. O despacho no provedor precede a persistência e ambos
    // são idempotentes (arquivo por ref / chave fixa na franquia), então falhas convergem no retry.
    [HttpGet("versions/{versionId:guid}/signature-envelope")]
    public async Task<IActionResult> GetSignatureEnvelope(Guid tenantId,Guid versionId,CancellationToken ct)
    {
        if(Actor() is not Guid actor)return Unauthorized();
        await using var c=await dataSource.OpenConnectionAsync(ct);if(!await Allowed(c,actor,tenantId,"tenant.contract_drafts.read",ct))return Forbid();
        await using var tx=await c.BeginTransactionAsync(ct);await SetTenant(c,tenantId,actor,tx,ct);
        var envelope=await c.QuerySingleOrDefaultAsync<EnvelopeRow>(new CommandDefinition("SELECT id AS Id,provider_code AS ProviderCode,envelope_ref AS EnvelopeRef,status AS Status,sent_at AS SentAt,state_at AS StateAt,row_version AS RowVersion FROM odca.signature_envelopes WHERE tenant_id=@tenantId AND generated_version_id=@versionId",new{tenantId,versionId},tx,cancellationToken:ct));
        if(envelope is null){await tx.CommitAsync(ct);return NotFound(new{title=this.T("api.signatures.envelope.not_found"),code="signatures.envelope.not_found"});}
        var events=(await c.QueryAsync<EnvelopeEventRow>(new CommandDefinition("SELECT source_event_id AS SourceEventId,event_type AS EventType,occurred_at AS OccurredAt,received_at AS ReceivedAt FROM odca.signature_envelope_events WHERE tenant_id=@tenantId AND envelope_id=@id ORDER BY received_at,id",new{tenantId,id=envelope.Id},tx,cancellationToken:ct))).AsList();
        await tx.CommitAsync(ct);return Ok(new{envelope.ProviderCode,envelope.EnvelopeRef,envelope.Status,envelope.SentAt,envelope.StateAt,envelope.RowVersion,replayed=false,events});
    }

    [HttpPost("versions/{versionId:guid}/signature-envelope")]
    public async Task<IActionResult> SendSignatureEnvelope(Guid tenantId,Guid versionId,CancellationToken ct)
    {
        if(Actor() is not Guid actor)return Unauthorized();
        await using var c=await dataSource.OpenConnectionAsync(ct);if(!await Allowed(c,actor,tenantId,"tenant.contract_drafts.manage",ct))return Forbid();
        await using var tx=await c.BeginTransactionAsync(ct);await SetTenant(c,tenantId,actor,tx,ct);
        await c.ExecuteAsync(new CommandDefinition("SELECT pg_advisory_xact_lock(hashtextextended(@key,0))",new{key=$"signature-envelope:{versionId:N}"},tx,cancellationToken:ct));
        var version=await c.QuerySingleOrDefaultAsync<PreparationVersionRow>(new CommandDefinition("SELECT id AS Id,patient_id AS PatientId,patient_snapshot::text AS PatientSnapshot,pdf_status AS PdfStatus,pdf_storage_key AS PdfStorageKey,pdf_sha256 AS PdfSha256,review_status AS ReviewStatus FROM odca.generated_contract_versions WHERE tenant_id=@tenantId AND id=@versionId FOR UPDATE",new{tenantId,versionId},tx,cancellationToken:ct));
        if(version is null)return NotFound();
        var preparation=await c.QuerySingleOrDefaultAsync<PreparationStateRow>(new CommandDefinition("SELECT id AS Id,status AS Status,row_version AS Version,composition_revision AS CompositionRevision,confirmed_revision AS ConfirmedRevision,confirmed_pdf_sha256 AS ConfirmedPdfSha256,confirmed_at AS ConfirmedAt,confirmation_operation_id AS ConfirmationOperationId FROM odca.signature_preparations WHERE tenant_id=@tenantId AND generated_version_id=@versionId FOR UPDATE",new{tenantId,versionId},tx,cancellationToken:ct));
        if(preparation is null||preparation.Status!="confirmed")return Conflict(new{title=this.T("api.preparation.not_confirmed.envelope"),code="preparation.not_confirmed"});
        if(string.Equals(envelopeProvider.Code,"none",StringComparison.Ordinal))return Conflict(new{title=this.T("api.signatures.envelope.provider_unconfigured"),code="signatures.provider_unconfigured"});
        var existing=await c.QuerySingleOrDefaultAsync<EnvelopeRow>(new CommandDefinition("SELECT id AS Id,provider_code AS ProviderCode,envelope_ref AS EnvelopeRef,status AS Status,sent_at AS SentAt,state_at AS StateAt,row_version AS RowVersion FROM odca.signature_envelopes WHERE tenant_id=@tenantId AND generated_version_id=@versionId FOR UPDATE",new{tenantId,versionId},tx,cancellationToken:ct));
        if(existing is not null&&existing.Status=="sent"){await tx.CommitAsync(ct);return Ok(new{existing.ProviderCode,existing.EnvelopeRef,existing.Status,existing.SentAt,existing.StateAt,existing.RowVersion,replayed=true});}
        if(existing is not null&&existing.Status=="accepted")return Conflict(new{title=this.T("api.signatures.envelope.accepted"),code="signatures.envelope.accepted"});
        var envelopeId=existing?.Id??Guid.NewGuid();
        var nextVersion=(existing?.RowVersion??0)+1;
        var envelopeRef=$"env-{versionId:N}";
        var participants=(await c.QueryAsync<SignatureEnvelopeParticipantInfo>(new CommandDefinition("SELECT client_id AS ClientId,name AS Name,participant_type AS ParticipantType,role AS Role FROM odca.signature_participants WHERE tenant_id=@tenantId AND preparation_id=@preparationId AND composition_revision=@revision ORDER BY position",new{tenantId,preparationId=preparation.Id,revision=preparation.ConfirmedRevision},tx,cancellationToken:ct))).AsList();
        try
        {
            var sent=await envelopeProvider.SendAsync(new SignatureEnvelopeSendRequest(tenantId,actor,preparation.Id,versionId,envelopeProvider.Code,envelopeRef,participants,version.PdfSha256),ct);
            if(!sent.Sent)return Conflict(new{title=this.T("api.signatures.envelope.send_failed"),code="signatures.envelope.send_failed"});
        }
        catch(Exception ex)when(ex is not OperationCanceledException)
        {
            LogEnvelopeDispatchFailed(HttpContext.RequestServices.GetRequiredService<ILogger<ContractStudioController>>(),ex,envelopeRef);
            return new ObjectResult(new ProblemDetails{Title=this.T("api.signatures.envelope.provider_error"),Detail=this.T("api.signatures.envelope.provider_error"),Status=502,Extensions={["code"]="signatures.envelope.provider_error"}}){StatusCode=502};
        }
        var consumed=await c.ExecuteScalarAsync<string>(new CommandDefinition("SELECT odca.consume_monthly_franchise(@tenantId,'signature_credit',1,@key,'signature_envelope',@envelopeId,'studio-api')",new{tenantId,key=$"signature-envelope:{versionId:N}:v{nextVersion}",envelopeId},tx,cancellationToken:ct));
        if(consumed is "exhausted" or "not_contracted")return Conflict(new{title=this.T("api.signatures.envelope.quota_unavailable"),code="signatures.franchise_exhausted"});
        if(consumed is not ("consumed" or "duplicate"))return Conflict(new{title=this.T("api.signatures.envelope.quota_rejected"),code="signatures.franchise_rejected"});
        if(existing is null)await c.ExecuteAsync(new CommandDefinition("INSERT INTO odca.signature_envelopes(id,tenant_id,generated_version_id,preparation_id,provider_code,envelope_ref,status,sent_at,created_by) VALUES(@envelopeId,@tenantId,@versionId,@preparationId,@providerCode,@refValue,'sent',now(),@actor)",new{envelopeId,tenantId,versionId,preparationId=preparation.Id,providerCode=envelopeProvider.Code,refValue=envelopeRef,actor},tx,cancellationToken:ct));
        else await c.ExecuteAsync(new CommandDefinition("UPDATE odca.signature_envelopes SET status='sent',state_at=NULL,sent_at=now(),row_version=row_version+1 WHERE tenant_id=@tenantId AND id=@envelopeId",new{tenantId,envelopeId},tx,cancellationToken:ct));
        await c.ExecuteAsync(new CommandDefinition("INSERT INTO odca.signature_envelope_events(tenant_id,envelope_id,source_event_id,event_type,payload,occurred_at) VALUES(@tenantId,@envelopeId,@source,'sent','{}'::jsonb,now()) ON CONFLICT(envelope_id,source_event_id) DO NOTHING",new{tenantId,envelopeId,source=$"{envelopeId:N}:v{nextVersion}"},tx,cancellationToken:ct));
        // D-OC38 (Sprint 4/F-1): sino aos participantes vinculado e ainda pendentes no envio do
        // envelope externo — uma linha por tentativa (nextVersion entra na identidade); o replay
        // em "sent" retorna antes deste ponto, sem duplicar.
        var (protector,notifLogger)=NotificationEnqueue.Resolve(HttpContext.RequestServices);
        var envelopeRecipients=(await c.QueryAsync<Guid>(new CommandDefinition("""
            SELECT DISTINCT m.user_id FROM odca.signature_participants sp
            JOIN odca.memberships m ON m.id=sp.identity_membership_id
            WHERE sp.tenant_id=@tenantId AND sp.preparation_id=@preparationId AND sp.composition_revision=@revision AND sp.signed_at IS NULL
            """,new{tenantId,preparationId=preparation.Id,revision=preparation.ConfirmedRevision},tx,cancellationToken:ct))).ToList();
        var localizer=HttpContext.RequestServices.GetRequiredService<LocalizationService>();
        foreach(var recipient in envelopeRecipients)
        {
            // D-OC46 (Sprint 12/F-1): conteúdo no idioma preferido do destinatário (fallback pt-BR).
            var lang=await NotificationEnqueue.RecipientLanguageAsync(c,tx,tenantId,recipient,ct);
            await NotificationEnqueue.EnqueueInAppAsync(c,tx,tenantId,recipient,"signature_pending",localizer.T("notif.title.signature_envelope_sent",lang),localizer.T("notif.body.signature_envelope_sent",lang),NotificationEnqueue.DeterministicSource("signature.envelope_sent",versionId,nextVersion,recipient),notifLogger,ct);
        }
        await tx.CommitAsync(ct);return Ok(new{providerCode=envelopeProvider.Code,envelopeRef,status="sent",replayed=false});
    }

    // D-OC37 (Sprint 3/A-1): gatilho manual de reconciliação (o worker executa periodicamente);
    // aplica a transição com evento deduplicado — seguro para chamadas repetidas.
    [HttpPost("versions/{versionId:guid}/signature-envelope/sync")]
    public async Task<IActionResult> SyncSignatureEnvelope(Guid tenantId,Guid versionId,CancellationToken ct)
    {
        if(Actor() is not Guid actor)return Unauthorized();
        await using var c=await dataSource.OpenConnectionAsync(ct);if(!await Allowed(c,actor,tenantId,"tenant.contract_drafts.manage",ct))return Forbid();
        await using var tx=await c.BeginTransactionAsync(ct);await SetTenant(c,tenantId,actor,tx,ct);
        var envelope=await c.QuerySingleOrDefaultAsync<EnvelopeRow>(new CommandDefinition("SELECT id AS Id,provider_code AS ProviderCode,envelope_ref AS EnvelopeRef,status AS Status,sent_at AS SentAt,state_at AS StateAt,row_version AS RowVersion FROM odca.signature_envelopes WHERE tenant_id=@tenantId AND generated_version_id=@versionId FOR UPDATE",new{tenantId,versionId},tx,cancellationToken:ct));
        if(envelope is null){await tx.CommitAsync(ct);return NotFound(new{title=this.T("api.signatures.envelope.not_found"),code="signatures.envelope.not_found"});}
        var state=string.Equals(envelopeProvider.Code,"none",StringComparison.Ordinal)?null:await envelopeProvider.GetStateAsync(tenantId,envelope.EnvelopeRef,ct);
        if(state is null){await tx.CommitAsync(ct);return Ok(new{envelope.ProviderCode,envelope.Status,updated=false});}
        var result=await c.ExecuteScalarAsync<string>(new CommandDefinition("SELECT odca.signature_envelope_apply_state(@tenantId,@envelopeId,@status,@source,@occurredAt)",new{tenantId,envelopeId=envelope.Id,status=state.Status,source=$"{envelope.EnvelopeRef}:{state.Status}@{state.OccurredAt:O}",state.OccurredAt},tx,cancellationToken:ct));
        await tx.CommitAsync(ct);
        var transitioned=result=="transitioned";
        return Ok(new{envelope.ProviderCode,status=transitioned?state.Status:envelope.Status,updated=transitioned});
    }

    private sealed record EnvelopeRow(Guid Id,string ProviderCode,string EnvelopeRef,string Status,DateTime SentAt,DateTime? StateAt,long RowVersion);
    private sealed record EnvelopeEventRow(string SourceEventId,string EventType,DateTime? OccurredAt,DateTime ReceivedAt);
    // D-OC37 (Sprint 3/F-1): leitura dedilhada da linha de participante (o registro publico
    // SignatureParticipantInput tem parametro positional com valor padrao, incompativel com o mapa do Dapper).
    private sealed record ParticipantReadRow(Guid Id,string ParticipantType,Guid? SourceId,string Role,string Name,string? Email,string? Phone,int Position,DateTime? SignedAt,Guid? SignedBy,Guid? IdentityMembershipId,string? SignedThrough);

    // EventId 3101 reservado para o despacho de envelope de assinatura (D-OC37 Sprint 3/A-1).
    [LoggerMessage(EventId = 3101, Level = LogLevel.Warning, Message = "Falha do provedor de envelope ao despachar o envelope {EnvelopeRef}.")]
    private static partial void LogEnvelopeDispatchFailed(ILogger logger, Exception exception, string envelopeRef);

    [HttpPost("versions/{versionId:guid}/signature-preparation/reopen")]
    public async Task<IActionResult> ReopenPreparation(Guid tenantId,Guid versionId,ReopenSignaturePreparationRequest request,CancellationToken ct)
    {
        if(Actor() is not Guid actor)return Unauthorized();if(request.OperationId==Guid.Empty||string.IsNullOrWhiteSpace(request.Justification)||request.Justification.Trim().Length is < 5 or > 1000)return this.Field("justification","api.studio.reopen_justification_length");
        await using var c=await dataSource.OpenConnectionAsync(ct);if(!await Allowed(c,actor,tenantId,"tenant.contract_drafts.manage",ct))return Forbid();await using var tx=await c.BeginTransactionAsync(ct);await SetTenant(c,tenantId,actor,tx,ct);
        var commandHash=CommandHash(new{versionId,request.ExpectedVersion,justification=request.Justification.Trim()});
        await LockOperation(c,request.OperationId,tx,ct);
        var receipt=await ReadOperation(c,tenantId,request.OperationId,tx,ct);
        if(receipt is not null)
        {
            if(receipt.OperationType!="reopen"||receipt.CommandHash!=commandHash)return Conflict(new{title=this.T("api.preparation.idempotency.conflict"),code="preparation.idempotency.conflict"});
            await tx.CommitAsync(ct);return Content(receipt.ResponseJson,"application/json");
        }
        var current=await c.QuerySingleOrDefaultAsync<PreparationStateRow>(new CommandDefinition("SELECT id AS Id,status AS Status,row_version AS Version,composition_revision AS CompositionRevision,confirmed_revision AS ConfirmedRevision,confirmed_pdf_sha256 AS ConfirmedPdfSha256,confirmed_at AS ConfirmedAt,confirmation_operation_id AS ConfirmationOperationId FROM odca.signature_preparations WHERE tenant_id=@tenantId AND generated_version_id=@versionId FOR UPDATE",new{tenantId,versionId},tx,cancellationToken:ct));
        if(current is null)return NotFound();if(current.Version!=request.ExpectedVersion)return Conflict(new{title=this.T("api.preparation.version.changed"),code="preparation.version.conflict",currentVersion=current.Version});if(current.Status=="draft")return Conflict(new{title=this.T("api.preparation.state.conflict"),code="preparation.state.conflict"});
        await c.ExecuteAsync(new CommandDefinition("UPDATE odca.signature_preparations SET status='draft',confirmed_revision=NULL,row_version=row_version+1,updated_by=@actor,updated_at=now() WHERE tenant_id=@tenantId AND id=@id",new{tenantId,id=current.Id,actor},tx,cancellationToken:ct));
        await AddPreparationEvent(c,tenantId,current.Id,current.CompositionRevision,actor,"reopened",new{justification=request.Justification.Trim(),priorConfirmedRevision=current.ConfirmedRevision,priorPdfSha256=current.ConfirmedPdfSha256,priorConfirmedAt=current.ConfirmedAt,operationId=request.OperationId},tx,ct);
        var response=JsonSerializer.Serialize(new{status="draft",version=current.Version+1,replayed=false});
        await AddOperation(c,tenantId,current.Id,request.OperationId,"reopen",commandHash,response,actor,tx,ct);
        await tx.CommitAsync(ct);return Content(response,"application/json");
    }

    [HttpGet("versions/{versionId:guid}/signature-preparation/history")]
    public async Task<IActionResult> PreparationHistory(Guid tenantId,Guid versionId,int page=1,int pageSize=20,CancellationToken ct=default)
    {
        if(Actor() is not Guid actor)return Unauthorized();await using var c=await dataSource.OpenConnectionAsync(ct);if(!await Allowed(c,actor,tenantId,"tenant.contract_drafts.read",ct))return Forbid();page=Math.Max(page,1);pageSize=Math.Clamp(pageSize,1,50);await using var tx=await c.BeginTransactionAsync(ct);await SetTenant(c,tenantId,actor,tx,ct);
        var preparationId=await c.ExecuteScalarAsync<Guid?>(new CommandDefinition("SELECT id FROM odca.signature_preparations WHERE tenant_id=@tenantId AND generated_version_id=@versionId",new{tenantId,versionId},tx,cancellationToken:ct));if(preparationId is null)return NotFound();
        var total=await c.ExecuteScalarAsync<int>(new CommandDefinition("SELECT count(*) FROM odca.signature_preparation_events WHERE tenant_id=@tenantId AND preparation_id=@preparationId",new{tenantId,preparationId},tx,cancellationToken:ct));
        var timeZoneId=await c.QuerySingleOrDefaultAsync<string?>(new CommandDefinition("SELECT timezone FROM odca.tenants WHERE id=@tenantId",new{tenantId},tx,cancellationToken:ct));
        string timeZone;
        try
        {
            timeZone=TimeZonePolicy.Resolve(timeZoneId,configuration["TimeZone:DefaultId"]).Id;
        }
        catch(TimeZoneConfigurationException)
        {
            // D-OC27: texto por chave; código wire aditivo.
            return new ObjectResult(new ProblemDetails { Title = this.T("api.studio.timezone_unavailable"), Detail = this.T("api.studio.timezone_unavailable.detail"), Status = 503, Extensions = { ["code"] = "studio.timezone_unavailable" } }) { StatusCode = 503 };
        }
        var items=await c.QueryAsync<SignaturePreparationHistoryItem>(new CommandDefinition("""
            SELECT e.id AS Id,(e.occurred_at AT TIME ZONE @timeZone) AS OccurredAt,e.actor_user_id AS ActorId,u.display_name AS Actor,
              e.event_type AS Operation,e.composition_revision AS CompositionRevision,e.details->>'justification' AS Justification,
              e.event_type='confirmed' AS Confirmed,e.details->>'pdfSha256' AS PdfSha256
            FROM odca.signature_preparation_events e JOIN odca.users u ON u.id=e.actor_user_id JOIN odca.tenants t ON t.id=e.tenant_id
            WHERE e.tenant_id=@tenantId AND e.preparation_id=@preparationId ORDER BY e.occurred_at DESC,e.id DESC LIMIT @pageSize OFFSET @offset
            """,new{tenantId,preparationId,pageSize,offset=(page-1)*pageSize,timeZone},tx,cancellationToken:ct));await tx.CommitAsync(ct);return Ok(new SignaturePreparationHistoryPage(items.AsList(),page,pageSize,total,timeZone));
    }

    [HttpGet("versions/{versionId:guid}/signature-preparation/compare")]
    public async Task<IActionResult> ComparePreparations(Guid tenantId,Guid versionId,int beforeRevision,int afterRevision,CancellationToken ct)
    {
        if(Actor() is not Guid actor)return Unauthorized();if(beforeRevision<1||afterRevision<1||beforeRevision==afterRevision)return this.Invalid("api.studio.compare_revisions_invalid");await using var c=await dataSource.OpenConnectionAsync(ct);if(!await Allowed(c,actor,tenantId,"tenant.contract_drafts.read",ct))return Forbid();await using var tx=await c.BeginTransactionAsync(ct);await SetTenant(c,tenantId,actor,tx,ct);
        var preparationId=await c.ExecuteScalarAsync<Guid?>(new CommandDefinition("SELECT id FROM odca.signature_preparations WHERE tenant_id=@tenantId AND generated_version_id=@versionId",new{tenantId,versionId},tx,cancellationToken:ct));if(preparationId is null)return NotFound();
        var rows=(await c.QueryAsync<ComparisonParticipant>(new CommandDefinition("SELECT client_id AS Id,name AS Name,participant_type AS ParticipantType,source_id AS SourceId,role AS Role,email AS Email,phone AS Phone,position AS Position,composition_revision AS Revision FROM odca.signature_participants WHERE tenant_id=@tenantId AND preparation_id=@preparationId AND composition_revision=ANY(@revisions) ORDER BY composition_revision,position",new{tenantId,preparationId,revisions=new[]{beforeRevision,afterRevision}},tx,cancellationToken:ct))).AsList();
        if(!rows.Any(x=>x.Revision==beforeRevision)||!rows.Any(x=>x.Revision==afterRevision))return NotFound();var changes=CompareCompositions(rows.Where(x=>x.Revision==beforeRevision),rows.Where(x=>x.Revision==afterRevision));await tx.CommitAsync(ct);return Ok(new SignatureCompositionComparison(beforeRevision,afterRevision,changes));
    }

    [HttpPost("versions/{versionId:guid}/signature-preparations/{preparationId:guid}/participants/{participantClientId:guid}/sign")]
    public async Task<IActionResult> SignParticipant(Guid tenantId,Guid versionId,Guid preparationId,Guid participantClientId,[FromBody] SignParticipantRequest? request,CancellationToken ct)
    {
        if(Actor() is not Guid actor)return Unauthorized();
        // D-A1/D-A2: a assinatura registra modalidade. "session" é autoassinatura do
        // participante vinculado à própria conta (exige MFA); "evidence" registra um
        // terceiro com descrição de evidência. contract_drafts.manage não declara
        // assinaturas sozinha.
        var mode=request?.Mode?.Trim().ToLowerInvariant();
        string permission;
        if(mode=="session")permission="tenant.signature_participants.sign_self";
        else if(mode=="evidence")permission="tenant.signature_participants.record";
        else return this.Field("mode", "api.studio.sign_mode_invalid");
        string? evidence=null;
        if(mode=="evidence")
        {
            evidence=request!.Evidence?.Trim();
            if(evidence is null||evidence.Length<5||evidence.Length>2000)return this.Field("evidence", "api.studio.sign_evidence_length");
        }
        else if(!string.IsNullOrWhiteSpace(request?.Evidence))
        {
            return this.Field("evidence", "api.studio.sign_evidence_only_evidence_mode");
        }
        // D-OC31 B.2: vinculo opcional de documento do acervo como evidência (somente modo "evidence");
        // o hash é copiado do servidor a partir da versão validada — o cliente não o envia.
        var evidenceDocId=request?.EvidenceDocumentVersionId;
        bool hasEvidenceDoc=evidenceDocId.HasValue&&evidenceDocId.Value!=Guid.Empty;
        if(hasEvidenceDoc&&mode!="evidence")
        {
            return this.Field("evidenceDocumentVersionId", "api.studio.sign_evidence_only_evidence_mode");
        }
        await using var c=await dataSource.OpenConnectionAsync(ct);if(!await Allowed(c,actor,tenantId,permission,ct))return Forbid();
        await using var tx=await c.BeginTransactionAsync(ct);await SetTenant(c,tenantId,actor,tx,ct);
        var version=await c.QuerySingleOrDefaultAsync<SignVersionRow>(new CommandDefinition("SELECT id AS Id,contract_id AS ContractId,review_status AS ReviewStatus FROM odca.generated_contract_versions WHERE tenant_id=@tenantId AND id=@versionId FOR UPDATE",new{tenantId,versionId},tx,cancellationToken:ct));
        if(version is null)return NotFound();
        var preparation=await c.QuerySingleOrDefaultAsync<PreparationStateRow>(new CommandDefinition("SELECT id AS Id,created_by AS CreatedBy,status AS Status,row_version AS Version,composition_revision AS CompositionRevision,confirmed_revision AS ConfirmedRevision,confirmed_pdf_sha256 AS ConfirmedPdfSha256,confirmed_at AS ConfirmedAt,confirmation_operation_id AS ConfirmationOperationId FROM odca.signature_preparations WHERE tenant_id=@tenantId AND id=@preparationId AND generated_version_id=@versionId FOR UPDATE",new{tenantId,preparationId,versionId},tx,cancellationToken:ct));
        if(preparation is null)return NotFound();
        if(preparation.Status!="confirmed")return Conflict(new{title=this.T("api.preparation.not_confirmed.signing"),code="preparation.not_confirmed"});
        var participant=await c.QuerySingleOrDefaultAsync<SignatureParticipantStateRow>(new CommandDefinition("SELECT client_id AS ClientId,name AS Name,signed_at AS SignedAt,signed_by AS SignedBy,identity_membership_id AS IdentityMembershipId FROM odca.signature_participants WHERE tenant_id=@tenantId AND preparation_id=@preparationId AND composition_revision=@revision AND client_id=@clientId FOR UPDATE",new{tenantId,preparationId,revision=preparation.ConfirmedRevision,clientId=participantClientId},tx,cancellationToken:ct));
        if(participant is null)return Conflict(new{title=this.T("api.participant.gone"),code="participant.gone"});
        if(participant.SignedAt is null&&mode=="session")
        {
            // D-A3: autoassinatura exige vínculo próprio ativo e sessão em nível MFA.
            var linkedUser=await c.ExecuteScalarAsync<Guid?>(new CommandDefinition("SELECT user_id FROM odca.memberships WHERE id=@membershipId AND tenant_id=@tenantId",new{membershipId=participant.IdentityMembershipId,tenantId},tx,cancellationToken:ct));
            if(linkedUser is null||linkedUser.Value!=actor)return Conflict(new{title=this.T("api.participant.identity_not_linked"),code="participant.identity_not_linked"});
            if(SessionId() is not Guid sessionId)return Conflict(new{title=this.T("api.sign.session_required"),code="sign.session_required"});
            var mfaSession=await c.ExecuteScalarAsync<bool>(new CommandDefinition("SELECT EXISTS(SELECT 1 FROM odca.sessions WHERE id=@sid AND user_id=@actor AND authentication_level='mfa' AND revoked_at IS NULL)",new{sid=sessionId,actor},tx,cancellationToken:ct));
            if(!mfaSession)return Conflict(new{title=this.T("api.sign.mfa_required"),code="sign.mfa_required"});
        }
        if(participant.SignedAt is not null)
        {
            if(participant.SignedBy==actor)
            {
                // Replay devolve o estado real da composição confirmada.
                var openCount=await c.ExecuteScalarAsync<int>(new CommandDefinition("SELECT count(*)::int FROM odca.signature_participants WHERE tenant_id=@tenantId AND preparation_id=@preparationId AND composition_revision=@revision AND signed_at IS NULL",new{tenantId,preparationId,revision=preparation.ConfirmedRevision},tx,cancellationToken:ct));
                await tx.CommitAsync(ct);return Ok(new{signedAt=ToUtcOffset(participant.SignedAt.Value),signedBy=actor,remaining=openCount,allSigned=openCount==0,replayed=true});
            }
            return Conflict(new{title=this.T("api.participant.already_signed"),code="participant.already_signed"});
        }
        // D-OC31 B.2: a evidência documental é uma versão segura do acervo deste contrato;
        // o sha256 é lido do servidor (char(64)) e gravado em par com o id (CHECK do v054).
        string? evidenceDocSha=null;
        if(hasEvidenceDoc&&evidenceDocId is { } docId)
        {
            var evSha=await c.ExecuteScalarAsync<string?>(new CommandDefinition("""
                SELECT v.sha256 FROM odca.document_versions v
                JOIN odca.contract_documents d ON d.id=v.document_id AND d.tenant_id=v.tenant_id
                WHERE v.id=@docId AND v.tenant_id=@tenantId AND v.contract_id=@contractId AND v.security_status='safe' AND d.deleted_at IS NULL
                """,new{docId,tenantId,contractId=version.ContractId},tx,cancellationToken:ct));
            if(evSha is null)return this.Field("evidenceDocumentVersionId","api.studio.sign_evidence_doc_invalid");
            evidenceDocSha=evSha;
        }
        var marked=await c.ExecuteAsync(new CommandDefinition("UPDATE odca.signature_participants SET signed_at=now(),signed_by=@actor,signed_through=@mode,signed_session_id=@sessionId,signature_evidence=@evidence,evidence_document_version_id=@evidenceDocId,evidence_document_sha256=@evidenceDocSha WHERE tenant_id=@tenantId AND preparation_id=@preparationId AND composition_revision=@revision AND client_id=@clientId AND signed_at IS NULL",new{tenantId,preparationId,revision=preparation.ConfirmedRevision,clientId=participantClientId,actor,mode,sessionId=mode=="session"?SessionId():null,evidence,evidenceDocId,evidenceDocSha},tx,cancellationToken:ct));
        if(marked==0)
        {
            var retried=await c.QuerySingleOrDefaultAsync<SignatureParticipantStateRow>(new CommandDefinition("SELECT client_id AS ClientId,name AS Name,signed_at AS SignedAt,signed_by AS SignedBy,identity_membership_id AS IdentityMembershipId FROM odca.signature_participants WHERE tenant_id=@tenantId AND preparation_id=@preparationId AND composition_revision=@revision AND client_id=@clientId",new{tenantId,preparationId,revision=preparation.ConfirmedRevision,clientId=participantClientId},tx,cancellationToken:ct));
            if(retried is null||retried.SignedAt is null)return Conflict(new{title=this.T("api.participant.gone.composition_changed"),code="participant.gone"});
            if(retried.SignedBy!=actor)return Conflict(new{title=this.T("api.participant.already_signed"),code="participant.already_signed"});
            var openCount=await c.ExecuteScalarAsync<int>(new CommandDefinition("SELECT count(*)::int FROM odca.signature_participants WHERE tenant_id=@tenantId AND preparation_id=@preparationId AND composition_revision=@revision AND signed_at IS NULL",new{tenantId,preparationId,revision=preparation.ConfirmedRevision},tx,cancellationToken:ct));
            await tx.CommitAsync(ct);return Ok(new{signedAt=ToUtcOffset(retried.SignedAt.Value),signedBy=actor,remaining=openCount,allSigned=openCount==0,replayed=true});
        }
        // D-OC31 B.2: o ato vincula a versão exata confirmada (hash do PDF) e a evidência documental, se houver.
        await AddPreparationEvent(c,tenantId,preparationId,preparation.ConfirmedRevision!.Value,actor,"participant_signed",new{participantId=participantClientId,name=participant.Name,mode,confirmedPdfSha256=preparation.ConfirmedPdfSha256,evidenceDocumentVersionId=evidenceDocId,evidenceDocumentSha256=evidenceDocSha},tx,ct);
        var remaining=await c.ExecuteScalarAsync<int>(new CommandDefinition("SELECT count(*)::int FROM odca.signature_participants WHERE tenant_id=@tenantId AND preparation_id=@preparationId AND composition_revision=@revision AND signed_at IS NULL",new{tenantId,preparationId,revision=preparation.ConfirmedRevision},tx,cancellationToken:ct));
        bool completed=remaining==0&&version.ReviewStatus!="externally_signed";
        if(completed)
        {
            await c.ExecuteAsync(new CommandDefinition("UPDATE odca.generated_contract_versions SET review_status='externally_signed' WHERE tenant_id=@tenantId AND id=@versionId",new{tenantId,versionId},tx,cancellationToken:ct));
            await c.ExecuteAsync(new CommandDefinition("INSERT INTO odca.contract_events(tenant_id,contract_id,actor_id,event_type,details) VALUES(@tenantId,@contractId,@actor,'signature.completed',jsonb_build_object('versionId',@versionId,'confirmedPdfSha256',@pdfSha))",new{tenantId,contractId=version.ContractId,actor,versionId,pdfSha=preparation.ConfirmedPdfSha256},tx,cancellationToken:ct));
            await c.ExecuteAsync(new CommandDefinition("INSERT INTO odca.audit_events(scope_type,tenant_id,actor_user_id,action,entity_type,entity_id,result,metadata) VALUES('tenant',@tenantId,@actor,'signature.completed','generated_contract_version',@versionId,'success',jsonb_build_object('preparationId',@preparationId,'mode',@mode,'confirmedPdfSha256',@pdfSha))",new{tenantId,actor,versionId,preparationId,mode,pdfSha=preparation.ConfirmedPdfSha256},tx,cancellationToken:ct));
            // D-OC34 B.10: evento assinatura concluida - assunto generico por tipo enviado a quem
            // preparou a assinatura (created_by); dedup deterministico por preparation+version.
            // D-OC38 (Sprint 4/F-1): sino materializa a mesma identidade deduplicada.
            var (protector,notifLogger)=NotificationEnqueue.Resolve(HttpContext.RequestServices);
            var localizer=HttpContext.RequestServices.GetRequiredService<LocalizationService>();
            var completedSource=NotificationEnqueue.DeterministicSource("signature.completed",preparationId,version.Id);
            // D-OC46 (Sprint 12/F-1): título/corpo no idioma preferido do destinatário (fallback pt-BR).
            var lang=await NotificationEnqueue.RecipientLanguageAsync(c,tx,tenantId,preparation.CreatedBy,ct);
            var completedTitle=localizer.T("notif.title.signature_completed",lang);
            await NotificationEnqueue.EnqueueEmailAsync(c,tx,tenantId,preparation.CreatedBy,"signature_completed",completedTitle,protector,completedSource,notifLogger,ct);
            await NotificationEnqueue.EnqueueInAppAsync(c,tx,tenantId,preparation.CreatedBy,"signature_completed",completedTitle,localizer.T("notif.body.signature_completed",lang),completedSource,notifLogger,ct);
        }
        else
        {
            await c.ExecuteAsync(new CommandDefinition("INSERT INTO odca.audit_events(scope_type,tenant_id,actor_user_id,action,entity_type,entity_id,result,metadata) VALUES('tenant',@tenantId,@actor,'signature.participant_signed','generated_contract_version',@versionId,'success',jsonb_build_object('participantId',@clientId,'mode',@mode,'confirmedPdfSha256',@pdfSha,'evidenceDocumentVersionId',@evidenceDocId,'evidenceDocumentSha256',@evidenceDocSha))",new{tenantId,actor,versionId,clientId=participantClientId,mode,pdfSha=preparation.ConfirmedPdfSha256,evidenceDocId,evidenceDocSha},tx,cancellationToken:ct));
        }
        await tx.CommitAsync(ct);
        return Ok(new{signedAt=ToUtcOffset(await c.ExecuteScalarAsync<DateTime>(new CommandDefinition("SELECT now()",null,cancellationToken:ct))),signedBy=actor,remaining,allSigned=remaining==0,replayed=false,confirmedPdfSha256=preparation.ConfirmedPdfSha256,evidenceDocumentVersionId=evidenceDocId,evidenceDocumentSha256=evidenceDocSha});
    }

    [HttpPost("versions/{versionId:guid}/signature-preparations/{preparationId:guid}/participants/{participantClientId:guid}/identity-link")]
    public async Task<IActionResult> LinkParticipantIdentity(Guid tenantId,Guid versionId,Guid preparationId,Guid participantClientId,CancellationToken ct)
    {
        // D-A3: vínculo de identidade é ato próprio — cada usuário vincula apenas a
        // própria conta a um participante da composição confirmada.
        if(Actor() is not Guid actor)return Unauthorized();
        await using var c=await dataSource.OpenConnectionAsync(ct);
        await using var tx=await c.BeginTransactionAsync(ct);await SetTenant(c,tenantId,actor,tx,ct);
        var membership=await c.ExecuteScalarAsync<Guid?>(new CommandDefinition("SELECT id FROM odca.memberships WHERE tenant_id=@tenantId AND user_id=@actor",new{tenantId,actor},tx,cancellationToken:ct));
        if(membership is null)return Conflict(new{title=this.T("api.link.not_member"),code="link.not_member"});
        var preparation=await c.QuerySingleOrDefaultAsync<PreparationStateRow>(new CommandDefinition("SELECT id AS Id,status AS Status,row_version AS Version,composition_revision AS CompositionRevision,confirmed_revision AS ConfirmedRevision,confirmed_pdf_sha256 AS ConfirmedPdfSha256,confirmed_at AS ConfirmedAt,confirmation_operation_id AS ConfirmationOperationId FROM odca.signature_preparations WHERE tenant_id=@tenantId AND id=@preparationId AND generated_version_id=@versionId FOR UPDATE",new{tenantId,preparationId,versionId},tx,cancellationToken:ct));
        if(preparation is null)return NotFound();
        if(preparation.Status!="confirmed")return Conflict(new{title=this.T("api.preparation.not_confirmed.linking"),code="preparation.not_confirmed"});
        var participant=await c.QuerySingleOrDefaultAsync<SignatureParticipantStateRow>(new CommandDefinition("SELECT client_id AS ClientId,name AS Name,signed_at AS SignedAt,signed_by AS SignedBy,identity_membership_id AS IdentityMembershipId FROM odca.signature_participants WHERE tenant_id=@tenantId AND preparation_id=@preparationId AND composition_revision=@revision AND client_id=@clientId FOR UPDATE",new{tenantId,preparationId,revision=preparation.ConfirmedRevision,clientId=participantClientId},tx,cancellationToken:ct));
        if(participant is null)return Conflict(new{title=this.T("api.participant.gone"),code="participant.gone"});
        if(participant.SignedAt is not null)return Conflict(new{title=this.T("api.participant.already_signed.link_note"),code="participant.already_signed"});
        var linked=await c.ExecuteAsync(new CommandDefinition("UPDATE odca.signature_participants SET identity_membership_id=@membership WHERE tenant_id=@tenantId AND preparation_id=@preparationId AND composition_revision=@revision AND client_id=@clientId",new{tenantId,membership=membership.Value,preparationId,revision=preparation.ConfirmedRevision,clientId=participantClientId},tx,cancellationToken:ct));
        if(linked==0)return Conflict(new{title=this.T("api.participant.gone.composition_changed"),code="participant.gone"});
        await c.ExecuteAsync(new CommandDefinition("INSERT INTO odca.audit_events(scope_type,tenant_id,actor_user_id,action,entity_type,entity_id,result,metadata) VALUES('tenant',@tenantId,@actor,'signature.identity_linked','signature_participant',@clientId,'success',jsonb_build_object('preparationId',@preparationId,'versionId',@versionId))",new{tenantId,actor,clientId=participantClientId,preparationId,versionId},tx,cancellationToken:ct));
        await tx.CommitAsync(ct);
        return Ok(new{linked=true,participantId=participantClientId});
    }

    [HttpPost("versions/{versionId:guid}/signature-preparations/{preparationId:guid}/remind")]
    public async Task<IActionResult> RemindPreparation(Guid tenantId,Guid versionId,Guid preparationId,CancellationToken ct)
    {
        if(Actor() is not Guid actor)return Unauthorized();
        await using var c=await dataSource.OpenConnectionAsync(ct);if(!await Allowed(c,actor,tenantId,"tenant.contract_drafts.manage",ct))return Forbid();
        await using var tx=await c.BeginTransactionAsync(ct);await SetTenant(c,tenantId,actor,tx,ct);
        var preparation=await c.QuerySingleOrDefaultAsync<ReminderStateRow>(new CommandDefinition("""
            SELECT p.id AS Id,p.status AS Status,p.row_version AS Version,p.composition_revision AS CompositionRevision,p.confirmed_revision AS ConfirmedRevision,p.last_reminded_at AS LastRemindedAt,g.id AS VersionId
            FROM odca.signature_preparations p JOIN odca.generated_contract_versions g ON g.id=p.generated_version_id
            WHERE p.tenant_id=@tenantId AND p.id=@preparationId AND p.generated_version_id=@versionId FOR UPDATE OF p
            """,new{tenantId,preparationId,versionId},tx,cancellationToken:ct));
        if(preparation is null)return NotFound();
        if(preparation.Status!="confirmed")return Conflict(new{title=this.T("api.preparation.not_confirmed.reminder"),code="preparation.not_confirmed"});
        var now=await c.ExecuteScalarAsync<DateTime>(new CommandDefinition("SELECT now()",null,tx,cancellationToken:ct));
        if(preparation.LastRemindedAt is not null&&(now-preparation.LastRemindedAt.Value).TotalSeconds<60)
            return Conflict(new{title=this.T("api.reminder.too_soon"),code="reminder.too_soon",retryAfterSeconds=Math.Max(1,60-(int)((now-preparation.LastRemindedAt.Value).TotalSeconds))});
        long nextVersion=preparation.Version+1;
        int unsignedCount=await c.ExecuteScalarAsync<int>(new CommandDefinition("SELECT count(*)::int FROM odca.signature_participants WHERE tenant_id=@tenantId AND preparation_id=@preparationId AND composition_revision=@revision AND signed_at IS NULL",new{tenantId,preparationId,revision=preparation.ConfirmedRevision},tx,cancellationToken:ct));
        // D-OC31 B.1: lembretes entram na outbox na mesma transação do registro do envio,
        // somente para participantes com conta vinculada e canal e-mail ativo. A entrega é
        // assíncrona e honesta: sem provedor configurado o stub falha com notification_provider_missing.
        var unsignedUsers=(await c.QueryAsync<Guid>(new CommandDefinition("""
            SELECT DISTINCT m.user_id FROM odca.signature_participants sp
            JOIN odca.memberships m ON m.id=sp.identity_membership_id
            WHERE sp.tenant_id=@tenantId AND sp.preparation_id=@preparationId AND sp.composition_revision=@revision AND sp.signed_at IS NULL
            """,new{tenantId,preparationId,revision=preparation.ConfirmedRevision},tx,cancellationToken:ct))).ToList();
        int queuedCount=0;
        // D-OC34 B.10: assunto generico por tipo — dados livres do contrato nao entram no subject
        // SMTP; os detalhes ficam na notificacao em app / corpo da mensagem.
        // D-OC38 (Sprint 4/F-1): sino para cada participante vinculado com a mesma identidade deduplicada.
        if(unsignedUsers.Count>0){var (protector,notifLogger)=NotificationEnqueue.Resolve(HttpContext.RequestServices);var localizer=HttpContext.RequestServices.GetRequiredService<LocalizationService>();foreach(var recipient in unsignedUsers){var reminderSource=NotificationEnqueue.DeterministicSource("signature.reminded",preparationId,preparation.Version,recipient);// D-OC46 (Sprint 12/F-1): conteúdo no idioma preferido do destinatário (fallback pt-BR).
        var lang=await NotificationEnqueue.RecipientLanguageAsync(c,tx,tenantId,recipient,ct);
        var pendingTitle=localizer.T("notif.title.signature_pending",lang);
        if(await NotificationEnqueue.EnqueueEmailAsync(c,tx,tenantId,recipient,"signature_pending",pendingTitle,protector,reminderSource,notifLogger,ct))queuedCount++;
        await NotificationEnqueue.EnqueueInAppAsync(c,tx,tenantId,recipient,"signature_pending",pendingTitle,localizer.T("notif.body.signature_pending",lang),reminderSource,notifLogger,ct);}}
        string deliveryChannel=queuedCount>0?"outbox":"not_configured";
        await c.ExecuteAsync(new CommandDefinition("UPDATE odca.signature_preparations SET last_reminded_at=now(),last_reminded_by=@actor,row_version=row_version+1,updated_by=@actor,updated_at=now() WHERE tenant_id=@tenantId AND id=@preparationId",new{tenantId,preparationId,actor},tx,cancellationToken:ct));
        await AddPreparationEvent(c,tenantId,preparationId,preparation.ConfirmedRevision!.Value,actor,"reminded",new{unsignedCount,queuedCount,delivery=new{channel=deliveryChannel,sent=false}},tx,ct);
        await c.ExecuteAsync(new CommandDefinition("INSERT INTO odca.audit_events(scope_type,tenant_id,actor_user_id,action,entity_type,entity_id,result,metadata) VALUES('tenant',@tenantId,@actor,'signature.reminded','generated_contract_version',@versionId,'success',jsonb_build_object('preparationId',@preparationId,'channel',@channel,'queued',@queued,'sent',false))",new{tenantId,actor,versionId,preparationId,channel=deliveryChannel,queued=queuedCount},tx,cancellationToken:ct));
        await tx.CommitAsync(ct);
        return Ok(new{remindedAt=ToUtcOffset(now),version=nextVersion,delivery=new{channel=deliveryChannel,queued=queuedCount,sent=false,message=this.T(queuedCount>0?"api.studio.reminder_queued":"api.studio.reminder_local_only")}});
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
        if(request.GeneratedVersionId==Guid.Empty||request.ReviewerId==Guid.Empty||request.IdempotencyKey==Guid.Empty)return this.Invalid("api.studio.review_submit_fields_invalid");
        // D-OC59 (Sprint 25/F-1): o prazo de revisao chega do navegador com o fuso local (ex.:
        // 2026-11-15T09:00-03:00). O Npgsql recusa DateTimeOffset com offset != 0 em timestamptz,
        // o que derrubava a solicitacao com 500 em host fora de UTC. Normaliza para UTC antes de
        // persistir; a comparacao de replay abaixo continua por instante (DateTimeOffset ==).
        var dueAt=request.DueAt?.ToUniversalTime();
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
                return Conflict(new{title=this.T("api.review.idempotency.conflict"),code="review.idempotency.conflict"});
            await tx.CommitAsync(ct);return Ok(new ReviewSubmittedResponse(prior.ReviewId,prior.GeneratedVersionId,prior.Status));}
        var v=await c.QuerySingleOrDefaultAsync<VersionRow>(new CommandDefinition("SELECT id AS Id,contract_id AS ContractId,content::text AS Content,canonical_sha256 AS Sha256,review_status AS Status FROM odca.generated_contract_versions WHERE tenant_id=@tenantId AND id=@id FOR UPDATE",new{tenantId,id=request.GeneratedVersionId},tx,cancellationToken:ct));if(v is null)return NotFound();if(v.Status!="generated")return OdcaErrors.Conflict(this, "api.studio.review_version_already_sent");
        var reviewer=await c.ExecuteScalarAsync<bool>(new CommandDefinition("SELECT EXISTS(SELECT 1 FROM odca.memberships WHERE tenant_id=@tenantId AND user_id=@reviewer AND status='active' AND odca.tenant_actor_has_permission(user_id,@tenantId,'tenant.reviews.decide'))",new{tenantId,reviewer=request.ReviewerId},tx,cancellationToken:ct));if(!reviewer)return this.Invalid("api.studio.reviewer_not_authorized");var reviewId=Guid.NewGuid();
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
          """,new{reviewId,tenantId,contractId=v.ContractId,versionId=v.Id,actor,dueAt,Instructions=request.Instructions?.Trim(),content=v.Content,sha256=v.Sha256,request.IdempotencyKey,request.ReviewerId},tx,cancellationToken:ct));
        // D-OC31 B.1: e-mail ao revisor na mesma transação do envio; deduplicação por
        // source_id determinístico (evento + chave de idempotência).
        // D-OC34 B.10: assunto genérico por tipo (o título livre do contrato não vai no subject).
        var (protector,notifLogger)=NotificationEnqueue.Resolve(HttpContext.RequestServices);
        var localizer=HttpContext.RequestServices.GetRequiredService<LocalizationService>();
        var assignedSource=NotificationEnqueue.DeterministicSource("review.assigned",reviewId,request.IdempotencyKey);
        // D-OC46 (Sprint 12/F-1): título/corpo no idioma preferido do destinatário (fallback pt-BR).
        var assignedLang=await NotificationEnqueue.RecipientLanguageAsync(c,tx,tenantId,request.ReviewerId,ct);
        var assignedTitle=localizer.T("notif.title.review_assigned",assignedLang);
        await NotificationEnqueue.EnqueueEmailAsync(c,tx,tenantId,request.ReviewerId,"review_assigned",
            assignedTitle,protector,
            assignedSource,notifLogger,ct);
        // D-OC38 (Sprint 4/F-1): sino ao revisor com a mesma identidade deduplicada do e-mail.
        await NotificationEnqueue.EnqueueInAppAsync(c,tx,tenantId,request.ReviewerId,"review_assigned",
            assignedTitle,localizer.T("notif.body.review_assigned_submitted",assignedLang),assignedSource,notifLogger,ct);
        await tx.CommitAsync(ct);return Ok(new ReviewSubmittedResponse(reviewId,v.Id,"in_review"));
    }

    [HttpGet("drafts/{draftId:guid}/versions")]
    public async Task<IActionResult> Versions(Guid tenantId, Guid draftId, CancellationToken ct)
    {
        var actor = Actor(); if (actor is null) return Unauthorized();
        await using var c = await dataSource.OpenConnectionAsync(ct);
        if (!await Allowed(c, actor.Value, tenantId, "tenant.contract_drafts.read", ct)) return Forbid();
        await using var tx = await c.BeginTransactionAsync(ct); await SetTenant(c, tenantId, actor.Value, tx, ct);
        var rows = await c.QueryAsync<StudioVersionRow>(new CommandDefinition("""
            SELECT v.id AS Id,v.version_number AS Number,u.display_name AS Author,v.created_at AS CreatedAt,v.review_status AS Status
            FROM odca.generated_contract_versions v JOIN odca.users u ON u.id=v.created_by
            WHERE v.tenant_id=@tenantId AND v.draft_id=@draftId ORDER BY v.version_number DESC
            """, new { tenantId, draftId }, tx, cancellationToken: ct));
        var items = rows.Select(r => new StudioVersionItem(r.Id, r.Number, r.Author, ToUtcOffset(r.CreatedAt), r.Status)).AsList();
        await tx.CommitAsync(ct); return Ok(items);
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
        if (left.ContractId != right.ContractId) return this.Status(StatusCodes.Status400BadRequest, "api.studio.compare_same_contract");
        IReadOnlyList<StudioChange> changes;
        try { changes = ContractStudioAnalysis.Compare(left.Content, left.Fields, left.Values, right.Content, right.Fields, right.Values); }
        catch (InvalidDataException e) { var culture=this.Culture();var localizer=HttpContext.RequestServices.GetRequiredService<Odca.Configuration.Localization.LocalizationService>();return StatusCode(StatusCodes.Status413PayloadTooLarge,new { title = ApiMessages.Localize(localizer,e.Message,culture) ?? e.Message }); }
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
        await tx.CommitAsync(ct); return Ok(new ChecklistResponse(items.All(x => x.Severity != "blocker"), items.Select(x => new ChecklistItem(x.Severity, x.Code, LocalizeChecklistMessage(x.Code, x.Message, open), x.Reference)).ToArray()));
    }

    // D-OC28: localiza as mensagens da checklist. Códigos fixos usam o catálogo;
    // 'fields.invalid' (mensagens do StructuredContractDocument) resolve por mapa reverso.
    private string LocalizeChecklistMessage(string code, string message, int openComments)
    {
        var culture = this.Culture();
        var localizer = HttpContext.RequestServices.GetRequiredService<Odca.Configuration.Localization.LocalizationService>();
        return code switch
        {
            "draft.unsaved" => this.T("api.studio.checklist_draft_unsaved"),
            "draft.conflict" => this.T("api.studio.checklist_draft_conflict"),
            "comments.open" => this.T("api.studio.checklist_comments_open", openComments.ToString(CultureInfo.InvariantCulture)),
            "field.manual" => this.T("api.studio.checklist_field_manual"),
            _ => ApiMessages.Localize(localizer, message, culture) ?? message
        };
    }

    [HttpGet("drafts/{draftId:guid}/comments")]
    public async Task<IActionResult> Comments(Guid tenantId, Guid draftId, [FromQuery] bool includeResolved, CancellationToken ct)
    {
        var actor = Actor(); if (actor is null) return Unauthorized(); await using var c = await dataSource.OpenConnectionAsync(ct);
        if (!await Allowed(c, actor.Value, tenantId, "tenant.contract_drafts.read", ct)) return Forbid(); await using var tx = await c.BeginTransactionAsync(ct); await SetTenant(c, tenantId, actor.Value, tx, ct);
        var rows = (await c.QueryAsync<StudioCommentRow>(new CommandDefinition("""
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
        var items = rows.Select(r => new StudioCommentItem(
            r.Id, r.VersionId, r.DraftRevision, r.Reference, r.Body, r.AuthorId, r.Author,
            r.ParentId, ToUtcOffset(r.CreatedAt), r.Resolved, ToUtcOffset(r.ResolvedAt), r.ReferenceLocated,
            r.ResolvedBy, ToUtcOffset(r.LastMovementAt), r.OriginVersion)).ToList();
        await tx.CommitAsync(ct); return Ok(items);
    }

    [HttpPost("drafts/{draftId:guid}/comments")]
    public async Task<IActionResult> AddComment(Guid tenantId, Guid draftId, [FromBody] CreateStudioCommentRequest request, CancellationToken ct)
    {
        var actor = Actor(); if (actor is null) return Unauthorized(); if (string.IsNullOrWhiteSpace(request.Body) || request.Body.Trim().Length > 4000 || string.IsNullOrWhiteSpace(request.Reference) || request.Reference.Length > 200) return this.Invalid("api.studio.comment_fields_invalid");
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
            return this.Field("observation", "api.studio.comment_reopen_observation_required");
        if (request.Observation?.Trim().Length > 4000) return this.Invalid("api.studio.comment_observation_too_long");
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
        if (current.Resolved != request.ExpectedResolved) return OdcaErrors.Conflict(this, "api.studio.comment_state_changed");
        var action = resolve ? "resolve" : "reopen";
        var count = await c.ExecuteAsync(new CommandDefinition(resolve
            ? "UPDATE odca.studio_comments SET resolved_at=now(),resolved_by=@actor WHERE tenant_id=@tenantId AND draft_id=@draftId AND id=@commentId AND resolved_at IS NULL AND deleted_at IS NULL"
            : "UPDATE odca.studio_comments SET resolved_at=NULL,resolved_by=NULL WHERE tenant_id=@tenantId AND draft_id=@draftId AND id=@commentId AND resolved_at IS NOT NULL AND deleted_at IS NULL",
            new { actor, tenantId, draftId, commentId }, tx, cancellationToken: ct));
        if (count != 1) return OdcaErrors.Conflict(this, "api.studio.comment_state_changed_short");
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
    private static async Task<Dictionary<string,string[]>> ValidateParticipantSources(NpgsqlConnection c,Guid tenantId,PreparationVersionRow version,SignatureParticipantInput[] participants,NpgsqlTransaction tx,CancellationToken ct)
    {
        var errors=new Dictionary<string,string[]>();
        for(var i=0;i<participants.Length;i++)
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
    private static string CommandHash(object command)=>Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(command)))).ToLowerInvariant();
    private static Task<int> LockOperation(NpgsqlConnection c,Guid operationId,NpgsqlTransaction tx,CancellationToken ct)
        =>c.ExecuteAsync(new CommandDefinition("SELECT pg_advisory_xact_lock(hashtext(@operationId::text))",new{operationId},tx,cancellationToken:ct));
    private static Task<PreparationOperationRow?> ReadOperation(NpgsqlConnection c,Guid tenantId,Guid operationId,NpgsqlTransaction tx,CancellationToken ct)
        =>c.QuerySingleOrDefaultAsync<PreparationOperationRow>(new CommandDefinition("SELECT operation_type AS OperationType,command_sha256 AS CommandHash,response::text AS ResponseJson FROM odca.signature_preparation_operations WHERE tenant_id=@tenantId AND operation_id=@operationId",new{tenantId,operationId},tx,cancellationToken:ct));
    private static Task<int> AddOperation(NpgsqlConnection c,Guid tenantId,Guid preparationId,Guid operationId,string operationType,string commandHash,string response,Guid actor,NpgsqlTransaction tx,CancellationToken ct)
        =>c.ExecuteAsync(new CommandDefinition("INSERT INTO odca.signature_preparation_operations(tenant_id,preparation_id,operation_id,operation_type,command_sha256,response,actor_user_id) VALUES(@tenantId,@preparationId,@operationId,@operationType,@commandHash,@response::jsonb,@actor)",new{tenantId,preparationId,operationId,operationType,commandHash,response,actor},tx,cancellationToken:ct));
    private List<SignatureCompositionChange> CompareCompositions(IEnumerable<ComparisonParticipant> before,IEnumerable<ComparisonParticipant> after)
    {
        var left=before.ToDictionary(x=>x.Id);var right=after.ToDictionary(x=>x.Id);var changes=new List<SignatureCompositionChange>();
        foreach(var id in left.Keys.Union(right.Keys).OrderBy(x=>x))
        {
            if(!left.TryGetValue(id,out var old)){var added=right[id];changes.Add(new(id,added.Name,"included",null,added.Name));continue;}
            if(!right.TryGetValue(id,out var current)){changes.Add(new(id,old.Name,"removed",old.Name,null));continue;}
            if(old.Role!=current.Role)changes.Add(new(id,current.Name,"role",old.Role,current.Role));
            var oldContact=$"{old.Email ?? "—"} · {old.Phone ?? "—"}";var newContact=$"{current.Email ?? "—"} · {current.Phone ?? "—"}";if(oldContact!=newContact)changes.Add(new(id,current.Name,"contact",oldContact,newContact));
            var oldOrigin=ParticipantOrigin(old.ParticipantType,old.SourceId);var newOrigin=ParticipantOrigin(current.ParticipantType,current.SourceId);if(oldOrigin!=newOrigin)changes.Add(new(id,current.Name,"origin",oldOrigin,newOrigin));
            if(old.Position!=current.Position)changes.Add(new(id,current.Name,"order",old.Position.ToString(CultureInfo.InvariantCulture),current.Position.ToString(CultureInfo.InvariantCulture)));
        }
        return changes;
    }
    // D-OC28: rótulos de tipo/origem resolvidos por catálogo; tipo desconhecido volta como veio.
    private string ParticipantOrigin(string type,Guid? sourceId)
    {
        var label=type switch{"patient"=>this.T("api.studio.participant_type_patient"),"representative"=>this.T("api.studio.participant_type_representative"),"professional"=>this.T("api.studio.participant_type_professional"),"organization_representative"=>this.T("api.studio.participant_type_org_representative"),_=>type};
        return $"{label} · {(sourceId is null?this.T("api.studio.participant_source_manual"):this.T("api.studio.participant_source_selected"))}";
    }
    private async Task<SignatureReadinessResponse?> CalculateReadiness(NpgsqlConnection c,Guid tenantId,Guid versionId,NpgsqlTransaction tx,CancellationToken ct)
    {
        var row=await c.QuerySingleOrDefaultAsync<ReadinessRow>(new CommandDefinition("""
            SELECT v.pdf_status AS PdfStatus,v.pdf_storage_key AS PdfStorageKey,v.pdf_sha256 AS PdfSha256,v.review_status AS ReviewStatus,t.official_key AS OfficialKey,
              coalesce(p.id,'00000000-0000-0000-0000-000000000000'::uuid) AS PreparationId,p.status AS PreparationStatus,coalesce(p.composition_revision,0) AS CompositionRevision,
              (SELECT count(*) FROM odca.signature_participants sp WHERE sp.tenant_id=p.tenant_id AND sp.preparation_id=p.id AND sp.composition_revision=p.composition_revision) AS ParticipantCount,
              (SELECT ta.decision FROM odca.template_approvals ta WHERE ta.tenant_id=v.tenant_id AND ta.official_key=t.official_key) AS ApprovalDecision,
              (SELECT CASE WHEN ta.template_version_id IS NULL OR ta.template_version_id=v.source_template_version_id THEN true ELSE false END
                 FROM odca.template_approvals ta WHERE ta.tenant_id=v.tenant_id AND ta.official_key=t.official_key) AS ApprovalRevisionMatches,
              v.source_template_version_id AS SourceTemplateVersionId,
              (SELECT r.status FROM odca.contract_review_requests r WHERE r.tenant_id=v.tenant_id AND r.generated_version_id=v.id
                ORDER BY r.opened_at DESC,r.id DESC LIMIT 1) AS LatestReviewRequestStatus
            FROM odca.generated_contract_versions v
            LEFT JOIN odca.contract_templates t ON t.id=v.source_template_id
            LEFT JOIN odca.signature_preparations p ON (p.tenant_id,p.generated_version_id)=(v.tenant_id,v.id)
            WHERE v.tenant_id=@tenantId AND v.id=@versionId
            """,new{tenantId,versionId},tx,cancellationToken:ct));
        if(row is null)return null;var ok=new List<SignatureReadinessItem>();var blockers=new List<SignatureReadinessItem>();var warnings=new List<SignatureReadinessItem>();
        // D-OC28: textos de prontidão resolvidos por catálogo; code e RequiredPermission permanecem estáveis no wire.
        ok.Add(new("document.exists",this.T("api.studio.readiness_document_exists_message"),this.T("api.studio.readiness_document_exists_reason"),this.T("api.studio.readiness_action_none"),null));
        if(OfficialContractTemplates.RequiresOdcaApproval(row.OfficialKey))
        {
            // Auditoria A §4: a aprovação cobre apenas a revisão aprovada; modelo
            // republicado exige nova decisão. Linhas pré-v043 sem revisão estampa
            // permanecem cobertas por continuidade (template_version_id NULL).
            var approved=string.Equals(row.ApprovalDecision,"aprovado",StringComparison.Ordinal);
            var rejected=string.Equals(row.ApprovalDecision,"reprovado",StringComparison.Ordinal);
            if(approved&&row.ApprovalRevisionMatches==true)ok.Add(new("approval.odca.granted",this.T("api.studio.readiness_approval_granted_message"),this.T("api.studio.readiness_approval_granted_reason"),this.T("api.studio.readiness_action_none"),null));
            else if(rejected&&row.ApprovalRevisionMatches==true)blockers.Add(new("approval.odca.rejected",this.T("api.studio.readiness_approval_rejected_message"),this.T("api.studio.readiness_approval_rejected_reason"),this.T("api.studio.readiness_approval_rejected_action"),"platform.templates.approve"));
            else if(approved&&row.ApprovalRevisionMatches==false)blockers.Add(new("approval.odca.stale",this.T("api.studio.readiness_approval_stale_message"),this.T("api.studio.readiness_approval_stale_reason"),this.T("api.studio.readiness_approval_stale_action"),"platform.templates.approve"));
            else blockers.Add(new("approval.odca.required",this.T("api.studio.readiness_approval_required_message"),this.T("api.studio.readiness_approval_required_reason"),this.T("api.studio.readiness_approval_required_action"),"tenant.templates.manage"));
        }
        if(row.PdfStatus!="completed"||string.IsNullOrWhiteSpace(row.PdfStorageKey)||string.IsNullOrWhiteSpace(row.PdfSha256))blockers.Add(new("pdf.incomplete",this.T("api.studio.readiness_pdf_incomplete_message"),this.T("api.studio.readiness_pdf_incomplete_reason"),this.T("api.studio.readiness_pdf_incomplete_action"),"tenant.contract_drafts.manage"));
        else
        {
            var root=configuration["Documents:StoragePath"]??Path.Combine(AppContext.BaseDirectory,"App_Data","documents");var path=Path.Combine(root,row.PdfStorageKey.Replace('/',Path.DirectorySeparatorChar));
            if(!System.IO.File.Exists(path))blockers.Add(new("pdf.missing",this.T("api.studio.readiness_pdf_missing_message"),this.T("api.studio.readiness_pdf_missing_reason"),this.T("api.studio.readiness_storage_reconcile_action"),"tenant.contract_drafts.manage"));
            else{var bytes=await System.IO.File.ReadAllBytesAsync(path,ct);if(!CryptographicOperations.FixedTimeEquals(SHA256.HashData(bytes),Convert.FromHexString(row.PdfSha256)))blockers.Add(new("pdf.integrity",this.T("api.studio.readiness_pdf_integrity_blocker_message"),this.T("api.studio.readiness_pdf_integrity_blocker_reason"),this.T("api.studio.readiness_storage_reconcile_action"),"tenant.contract_drafts.manage"));else ok.Add(new("pdf.integrity",this.T("api.studio.readiness_pdf_integrity_ok_message"),this.T("api.studio.readiness_pdf_integrity_ok_reason"),this.T("api.studio.readiness_action_none"),null));}
        }
        if(row.PreparationId==Guid.Empty||row.ParticipantCount<1)blockers.Add(new("participants.empty",this.T("api.studio.readiness_participants_empty_message"),this.T("api.studio.readiness_participants_empty_reason"),this.T("api.studio.readiness_participants_empty_action"),"tenant.contract_drafts.manage"));else ok.Add(new("participants.valid",this.T("api.studio.readiness_participants_valid_message",row.ParticipantCount.ToString(CultureInfo.InvariantCulture)),this.T("api.studio.readiness_participants_valid_reason"),this.T("api.studio.readiness_action_none"),null));
        // Auditoria A §1: 'changes_requested' é estado do request, não da versão
        // (impossível pelo CHECK da tabela); o bloqueio deriva do request mais recente.
        if(row.LatestReviewRequestStatus=="changes_requested")blockers.Add(new("review.changes",this.T("api.studio.readiness_review_changes_message"),this.T("api.studio.readiness_review_changes_reason"),this.T("api.studio.readiness_review_changes_action"),"tenant.contract_drafts.manage"));
        else if(row.ReviewStatus=="internally_approved")ok.Add(new("review.approved",this.T("api.studio.readiness_review_approved_message"),this.T("api.studio.readiness_review_approved_reason"),this.T("api.studio.readiness_action_none"),null));
        else warnings.Add(new("review.separate",this.T("api.studio.readiness_review_separate_message"),this.T("api.studio.readiness_review_separate_reason"),this.T("api.studio.readiness_review_separate_action"),"tenant.reviews.read"));
        // D-OC37 (Sprint 3/A-1): integração externa reflete o provedor efetivamente configurado —
        // sem provedor escolhido a advertência honesta permanece ("proposto, não escolhido").
        var integrationAvailable=!string.Equals(envelopeProvider.Code,"none",StringComparison.Ordinal);
        if(!integrationAvailable)warnings.Add(new("integration.unavailable",this.T("api.studio.readiness_integration_unavailable_message"),this.T("api.studio.readiness_integration_unavailable_reason"),this.T("api.studio.readiness_integration_unavailable_action"),null));
        return new(blockers.Count==0,row.ReviewStatus=="internally_approved",integrationAvailable,blockers.Count==0,ok,blockers,warnings);
    }

    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive=true, Converters={new JsonStringEnumConverter()} };
    private static readonly JsonSerializerOptions CamelCaseJsonOptions = new(JsonOptions) { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
    private static readonly JsonSerializerOptions RelaxedJsonOptions = new() { Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping };
    private static async Task<string> InitialDraftValuesAsync(NpgsqlConnection connection, NpgsqlTransaction transaction, Guid tenantId, Guid? patientId, string fieldsJson, string? contractorSource, CancellationToken ct)
    {
        string? snapshot = null;
        if (patientId is Guid selected)
            snapshot = await connection.ExecuteScalarAsync<string?>(new CommandDefinition("SELECT odca.patient_document_snapshot(@tenantId,@selected)::text", new { tenantId, selected }, transaction, cancellationToken: ct));
        var organization = await connection.QuerySingleAsync<(string Name, string Code)>(new CommandDefinition("SELECT display_name AS Name, business_code AS Code FROM odca.tenants WHERE id=@tenantId", new { tenantId }, transaction, cancellationToken: ct));
        var definitions = JsonSerializer.Deserialize<ContractFieldDefinition[]>(fieldsJson, JsonOptions) ?? [];
        return JsonSerializer.Serialize(DocumentFieldAutofill.Apply(definitions, snapshot, organization.Name, organization.Code, contractorSource), JsonOptions);
    }

    private static void Validate(string content,string fields,string values,ContractValidationMode mode){var definitions=JsonSerializer.Deserialize<ContractFieldDefinition[]>(fields,JsonOptions)??[];FieldMapping.Validate(definitions);var parsed=StructuredContractDocument.Parse(content,definitions);var fieldValues=JsonSerializer.Deserialize<ContractFieldValue[]>(values,JsonOptions)??[];StructuredContractDocument.ValidateValues(definitions,fieldValues,mode);if(definitions.Any(x=>!parsed.FieldOccurrences.ContainsKey(x.Id)))throw new InvalidDataException("Todo campo definido precisa ter ao menos uma ocorrência no documento.");}
    private static DraftResponse ToResponse(DraftRow r)=>new(r.Id,r.ContractId,r.Title,r.SourceTemplateId,r.SourceTemplateVersionId,JsonSerializer.Deserialize<JsonElement>(r.Content),JsonSerializer.Deserialize<JsonElement>(r.Fields),JsonSerializer.Deserialize<JsonElement>(r.Values),r.Version,r.LastClientRevision,ToUtcOffset(r.UpdatedAt),OfficialContractTemplates.RequiresOdcaApproval(r.OfficialKey)&&!string.Equals(r.ApprovalDecision,"aprovado",StringComparison.Ordinal));
    private Guid? Actor()=>Guid.TryParse(User.FindFirstValue("sub"),out var id)?id:null;
    private Guid? SessionId()=>Guid.TryParse(User.FindFirstValue("sid"),out var id)?id:null;
    private static Task<bool> Allowed(NpgsqlConnection c,Guid actor,Guid tenant,string permission,CancellationToken ct)=>c.ExecuteScalarAsync<bool>(new CommandDefinition("SELECT odca.tenant_actor_has_permission(@actor,@tenant,@permission)",new{actor,tenant,permission},cancellationToken:ct));
    private static Task<int> SetTenant(NpgsqlConnection c,Guid tenant,Guid actor,NpgsqlTransaction tx,CancellationToken ct)=>c.ExecuteAsync(new CommandDefinition("SELECT set_config('odca.tenant_id',@value,true),set_config('odca.actor_id',@actor,true)",new{value=tenant.ToString(),actor=actor.ToString()},tx,cancellationToken:ct));
    private static Task<int> SetTenant(NpgsqlConnection c,Guid tenant,Guid actor,CancellationToken ct)=>c.ExecuteAsync(new CommandDefinition("SELECT set_config('odca.tenant_id',@value,false),set_config('odca.actor_id',@actor,false)",new{value=tenant.ToString(),actor=actor.ToString()},cancellationToken:ct));
    private static async Task<SignaturePreparationResponse?> ReadPreparation(NpgsqlConnection c,Guid tenantId,Guid versionId,Guid actor,NpgsqlTransaction tx,CancellationToken ct)
    {
        var preparation=await c.QuerySingleOrDefaultAsync<PreparationRow>(new CommandDefinition("SELECT id AS Id,status AS Status,row_version AS Version,composition_revision AS CompositionRevision,confirmed_revision AS ConfirmedRevision,confirmed_pdf_sha256 AS ConfirmedPdfSha256,confirmed_at AS ConfirmedAt,confirmation_operation_id AS ConfirmationOperationId FROM odca.signature_preparations WHERE tenant_id=@tenantId AND generated_version_id=@versionId",new{tenantId,versionId},tx,cancellationToken:ct));if(preparation is null)return null;
        var myMembership=await c.ExecuteScalarAsync<Guid?>(new CommandDefinition("SELECT id FROM odca.memberships WHERE tenant_id=@tenantId AND user_id=@actor",new{tenantId,actor},tx,cancellationToken:ct));
        var participantRows=await c.QueryAsync<ParticipantReadRow>(new CommandDefinition("SELECT client_id AS Id,participant_type AS ParticipantType,source_id AS SourceId,role AS Role,name AS Name,email AS Email,phone AS Phone,position AS Position,signed_at AS SignedAt,signed_by AS SignedBy,identity_membership_id AS IdentityMembershipId,signed_through AS SignedThrough FROM odca.signature_participants WHERE tenant_id=@tenantId AND preparation_id=@id AND composition_revision=@revision ORDER BY position",new{tenantId,preparation.Id,revision=preparation.CompositionRevision},tx,cancellationToken:ct));
        var participants=participantRows.Select(p=>new SignatureParticipantInput(p.Id,p.ParticipantType,p.SourceId,p.Role,p.Name,p.Email,p.Phone,p.Position,p.SignedAt,p.SignedBy,p.IdentityMembershipId,p.SignedThrough)).ToList();
        if(myMembership is Guid mine)
            participants=participants.Select(p=>p with{IdentityLinkedToMe=p.IdentityMembershipId is Guid linked&&linked==mine}).AsList();
        return new(preparation.Id,preparation.Status,preparation.Version,participants,preparation.CompositionRevision,preparation.ConfirmedRevision,preparation.ConfirmedPdfSha256,ToUtcOffset(preparation.ConfirmedAt));
    }

    private static string SafeFileName(string value)=>string.Concat(value.Normalize().Select(x=>char.IsLetterOrDigit(x)||x is '-' or '_'?x:'_')).Trim('_') is {Length:>0} safe?safe:"documento";
    private sealed record SetPatientDraftRow(Guid ContractId,long DraftVersion,Guid? CurrentPatientId,string Fields,string Values);
    private sealed record PatientLinkRow(long RowVersion,DateTime? InactiveAt);
    private sealed record SetPatientSavedRow(long Version);
    public sealed record SetDraftPatientRequest(Guid? PatientId,long ExpectedVersion);
    private sealed record TemplateSource(Guid TemplateId,Guid VersionId,string Content,string Fields,string? TemplateName = null,string? OfficialKey = null,string? RequiredProfile = null);
    private sealed record ExistingDraftRow(Guid Id, Guid TemplateId, Guid? PatientId, string TemplateName);
    private sealed record ContractInfoRow(Guid Id, string Title, string? Reference);
    private sealed record ChangeRequestRow(Guid Id, Guid ContractId, string Status, Guid? DraftId, long RowVersion);
    private sealed class DraftRow
    {
        public Guid Id { get; set; }
        public Guid ContractId { get; set; }
        public string Title { get; set; } = "";
        public Guid SourceTemplateId { get; set; }
        public Guid SourceTemplateVersionId { get; set; }
        public string Content { get; set; } = "";
        public string Fields { get; set; } = "";
        public string Values { get; set; } = "";
        public long Version { get; set; }
        public Guid? LastClientRevision { get; set; }
        public DateTime UpdatedAt { get; set; }
        public Guid? PatientId { get; set; }
        public string? PatientSnapshot { get; set; }
        public long? PatientVersion { get; set; }
        public long? CurrentPatientVersion { get; set; }
        public DateTime? PatientInactiveAt { get; set; }
        public string? TemplateStatus { get; set; }
        public string? OfficialKey { get; set; }
        public string? ApprovalDecision { get; set; }
    }
    private sealed class SaveRow
    {
        public long Version { get; set; }
        public Guid ClientRevision { get; set; }
        public DateTime SavedAt { get; set; }
    }
    private sealed class RestoreVersionRow { public Guid Id { get; set; } public Guid DraftId { get; set; } public int Number { get; set; } public string Content { get; set; } = ""; public string Fields { get; set; } = ""; public string Values { get; set; } = ""; }
    private sealed class RestoreSnapshotRow { public long Version { get; set; } public string Values { get; set; } = ""; }
    private sealed class RestoreAppliedRow { public long Version { get; set; } public DateTime SavedAt { get; set; } }
    private sealed record VersionRow(Guid Id,Guid ContractId,string Content,string Sha256,string Status);
    private sealed class GeneratedExistingRow
    {
        public Guid Id { get; set; }
        public Guid DraftId { get; set; }
        public int Number { get; set; }
        public string Sha256 { get; set; } = "";
        public long ByteSize { get; set; }
        public DateTime CreatedAt { get; set; }
        public string Status { get; set; } = "";
        public long DraftVersion { get; set; }
    }
    private static DateTimeOffset ToUtcOffset(DateTime dt) => dt.Kind == DateTimeKind.Utc ? new DateTimeOffset(dt, TimeSpan.Zero) : new DateTimeOffset(DateTime.SpecifyKind(dt, DateTimeKind.Utc), TimeSpan.Zero);
    private static DateTimeOffset? ToUtcOffset(DateTime? dt) => dt.HasValue ? ToUtcOffset(dt.Value) : null;
    private sealed class StudioVersionRow { public Guid Id { get; init; } public int Number { get; init; } public string Author { get; init; } = ""; public DateTime CreatedAt { get; init; } public string Status { get; init; } = ""; }
    private sealed class StudioCommentRow
    {
        public Guid Id { get; init; }
        public Guid? VersionId { get; init; }
        public long DraftRevision { get; init; }
        public string Reference { get; init; } = "";
        public string Body { get; init; } = "";
        public Guid AuthorId { get; init; }
        public string Author { get; init; } = "";
        public Guid? ParentId { get; init; }
        public DateTime CreatedAt { get; init; }
        public bool Resolved { get; init; }
        public DateTime? ResolvedAt { get; init; }
        public bool ReferenceLocated { get; init; }
        public string? ResolvedBy { get; init; }
        public DateTime? LastMovementAt { get; init; }
        public int? OriginVersion { get; init; }
    }
    private sealed class ComparisonRow
    {
        public Guid Id { get; init; }
        public Guid ContractId { get; init; }
        public int Number { get; init; }
        public string Author { get; init; } = "";
        public DateTime CreatedAt { get; init; }
        public string Status { get; init; } = "";
        public string Content { get; init; } = "";
        public string Fields { get; init; } = "";
        public string Values { get; init; } = "";
    }
    private sealed record CommentStateRow(bool Resolved,string Reference,long DraftRevision,Guid ContractId);
    private static StudioVersionItem ToItem(ComparisonRow row)=>new(row.Id,row.Number,row.Author,ToUtcOffset(row.CreatedAt),row.Status);
    // D-OC34: interna para cobrir a semantica de diferenca em tests de integracao.
    internal static PatientDataChange[] PatientChanges(string? before, string? after)
    {
        // D-OC34: antes nulo significa cadastro previamente vazio — tudo que existir em
        // "depois" é uma mudança (usado na primeira vinculação de paciente ao rascunho).
        if (after is null) return [];
        using var leftDoc = before is null ? null : JsonDocument.Parse(before);
        using var right = JsonDocument.Parse(after);
        var fields = new[] { "fullName", "preferredName", "birthDate", "email", "phone", "address", "identifierType", "identifierValue", "representative" };
        return fields.Select(field => new PatientDataChange(field, leftDoc is null ? null : JsonValue(leftDoc.RootElement, field), JsonValue(right.RootElement, field)))
            .Where(change => !string.Equals(change.Before, change.After, StringComparison.Ordinal)).ToArray();
    }
    private static string? JsonValue(JsonElement element, string name) => element.TryGetProperty(name, out var value) && value.ValueKind != JsonValueKind.Null ? value.ToString() : null;
    private static JsonElement? ParseOptional(string? json)=>json is null?null:JsonSerializer.Deserialize<JsonElement>(json);
    private sealed record ConferenceRow(Guid DraftId,long DraftVersion,string Organization,string Template,int TemplateVersion,string DocumentType,string TemplateStatus,Guid? PatientId,string? PatientName,string? RepresentativeName,long? SelectedPatientVersion,long? CurrentPatientVersion,bool PatientActive,string? SelectedPatientSnapshot,string? CurrentPatientSnapshot,string Content,string Fields,string Values,string ReviewStatus);
    private sealed class PatientConfirmationRow
    {
        public Guid ContractId { get; set; }
        public long DraftVersion { get; set; }
        public long SelectedPatientVersion { get; set; }
        public long CurrentPatientVersion { get; set; }
        public DateTime? PatientInactiveAt { get; set; }
        public string Fields { get; set; } = "[]";
        public string Values { get; set; } = "[]";
        public string? SelectedSnapshot { get; set; }
        public string? CurrentSnapshot { get; set; }
    }
    private sealed class GeneratedDetailRow
    {
        public Guid Id { get; set; }
        public Guid ContractId { get; set; }
        public Guid DraftId { get; set; }
        public Guid? PatientId { get; set; }
        public int Number { get; set; }
        public string Title { get; set; } = "";
        public string DocumentType { get; set; } = "";
        public string Organization { get; set; } = "";
        public string? OrganizationTaxId { get; set; }
        public string Template { get; set; } = "";
        public int TemplateVersion { get; set; }
        public string Author { get; set; } = "";
        public DateTime CreatedAt { get; set; }
        public string Sha256 { get; set; } = "";
        public string ReviewStatus { get; set; } = "";
        public Guid? ReviewId { get; set; }
        public string? PatientSnapshot { get; set; }
        public string Content { get; set; } = "";
        public string Fields { get; set; } = "";
        public string Values { get; set; } = "";
        public string PdfStatus { get; set; } = "";
        public long? PdfByteSize { get; set; }
        public DateTime? PdfCompletedAt { get; set; }
    }
    private sealed record PdfRow(Guid Id,string Title,int Number,string Content,string Fields,string Values,string Status,string? StorageKey,long? ByteSize,string Organization,string? TaxId,string Author,DateTime CreatedAt);
    private sealed record PdfDownloadRow(string StorageKey,string Sha256,string Title,Guid? PatientId);
    private sealed class PreparationRow
    {
        public Guid Id { get; set; }
        public string Status { get; set; } = "";
        public long Version { get; set; }
        public int CompositionRevision { get; set; }
        public int? ConfirmedRevision { get; set; }
        public string? ConfirmedPdfSha256 { get; set; }
        public DateTime? ConfirmedAt { get; set; }
    }
    private sealed class PreparationStateRow
    {
        public Guid Id { get; set; }
        public string Status { get; set; } = "";
        public long Version { get; set; }
        public int CompositionRevision { get; set; }
        public int? ConfirmedRevision { get; set; }
        public string? ConfirmedPdfSha256 { get; set; }
        public DateTime? ConfirmedAt { get; set; }
        public Guid? ConfirmationOperationId { get; set; }
        public Guid CreatedBy { get; set; }
    }
    private sealed record PreparationVersionRow(Guid Id,Guid? PatientId,string? PatientSnapshot,string PdfStatus,string? PdfStorageKey,string? PdfSha256,string ReviewStatus);
    // Asse: os parâmetros seguem exatamente a ordem das colunas do SELECT em
    // CalculateReadiness (materialização Dapper por nome; sem opcionais).
    private sealed record ReadinessRow(string PdfStatus,string? PdfStorageKey,string? PdfSha256,string ReviewStatus,string? OfficialKey,Guid PreparationId,string? PreparationStatus,int CompositionRevision,long ParticipantCount,string? ApprovalDecision,bool? ApprovalRevisionMatches,Guid? SourceTemplateVersionId,string? LatestReviewRequestStatus);
    private sealed record PreparationOperationRow(string OperationType,string CommandHash,string ResponseJson);
    private sealed record SignVersionRow(Guid Id,Guid ContractId,string ReviewStatus);
    private sealed record SignatureParticipantStateRow(Guid ClientId,string Name,DateTime? SignedAt,Guid? SignedBy,Guid? IdentityMembershipId = null);
    private sealed class ReminderStateRow
    {
        public Guid Id { get; set; }
        public string Status { get; set; } = "";
        public long Version { get; set; }
        public int CompositionRevision { get; set; }
        public int? ConfirmedRevision { get; set; }
        public DateTime? LastRemindedAt { get; set; }
        public Guid VersionId { get; set; }
    }
    private sealed record ComparisonParticipant(Guid Id,string Name,string ParticipantType,Guid? SourceId,string Role,string? Email,string? Phone,int Position,int Revision);
    private sealed class SubmittedReviewRow
    {
        public Guid ReviewId { get; set; }
        public Guid GeneratedVersionId { get; set; }
        public string Status { get; set; } = "";
        public string? Instructions { get; set; }
        public DateTime? DueAt { get; set; }
        public Guid ReviewerId { get; set; }
    }
    private sealed class DocumentRow
    {
        public Guid ContractId { get; init; }
        public Guid? DraftId { get; init; }
        public Guid? LatestVersionId { get; init; }
        public int? LatestVersionNumber { get; init; }
        public string Title { get; init; } = "";
        public string? Reference { get; init; }
        public string DocumentType { get; init; } = "";
        public string TemplateName { get; init; } = "";
        public Guid? PatientId { get; init; }
        public string? PatientName { get; init; }
        public DateTime UpdatedAt { get; init; }
        public string DraftStatus { get; init; } = "";
        public string ReviewStatus { get; init; } = "";
        public string PdfStatus { get; init; } = "";
        public string SignatureStatus { get; init; } = "";
        public long? PdfByteSize { get; init; }
        public string? PdfStorageKey { get; init; }
        public string? ResponsibleName { get; init; }
        public int? TemplateVersion { get; init; }
        public bool PatientActive { get; init; }
    }
}
