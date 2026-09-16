using System.Security.Claims;
using Dapper;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Npgsql;
using Odca.Application.Tenancy;
using Odca.Contracts.DocumentImports;

namespace Odca.Api.Controllers;

[ApiController]
[Authorize(Policy = "PasswordChanged")]
[Route("api/v1/organizations/{tenantId:guid}/contract-imports")]
public sealed partial class ContractImportsController(
    NpgsqlDataSource dataSource,
    IConfiguration configuration,
    ILogger<ContractImportsController> logger) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> List(Guid tenantId, [FromQuery] string? status, [FromQuery] Guid? requester,
        [FromQuery] bool mine = false, [FromQuery] bool awaitingReview = false,
        [FromQuery] DateOnly? from = null, [FromQuery] DateOnly? to = null, CancellationToken ct = default)
    {
        var actor = Actor(); if (actor is null) return Unauthorized();
        if (from.HasValue && to.HasValue && from.Value > to.Value)
            return ValidationProblem(new ValidationProblemDetails(new Dictionary<string, string[]>
            {
                ["to"] = ["A data final deve ser igual ou posterior à data inicial."]
            }));
        await using var connection = await dataSource.OpenConnectionAsync(ct);
        if (!await Allowed(connection, actor.Value, tenantId, "tenant.imports.read", ct)) return Forbid();
        await using var tx = await connection.BeginTransactionAsync(ct); await SetTenant(connection, tenantId, tx, ct);
        var timeZoneId = await connection.QuerySingleOrDefaultAsync<string?>(new CommandDefinition(
            "SELECT timezone FROM odca.tenants WHERE id=@tenantId",
            new { tenantId }, tx, cancellationToken: ct));
        (DateTimeOffset? FromInclusive, DateTimeOffset? ToExclusive) range;
        try
        {
            var timeZone = TimeZonePolicy.Resolve(timeZoneId, configuration["TimeZone:DefaultId"]);
            range = CreateUtcRange(from, to, timeZone);
        }
        catch (TimeZoneConfigurationException exception)
        {
            LogTimeZoneConfigurationUnavailable(logger, exception, tenantId, exception.Error);
            return Problem(statusCode: StatusCodes.Status503ServiceUnavailable,
                title: "A configuração de data da organização está indisponível.",
                detail: "Contate o administrador para corrigir o fuso horário configurado.");
        }
        catch (InvalidOperationException exception)
        {
            LogInvalidLocalRange(logger, exception, tenantId);
            return Problem(statusCode: StatusCodes.Status422UnprocessableEntity,
                title: "O intervalo informado não pode ser convertido no fuso da organização.");
        }
        var args = new { tenantId, status, requester, actor = actor.Value, mine, awaitingReview, range.FromInclusive, range.ToExclusive };
        const string filter = """
            WHERE i.tenant_id=@tenantId
              AND (CAST(@status AS text) IS NULL OR i.status=CAST(@status AS text))
              AND (@requester IS NULL OR i.requested_by=@requester)
              AND (NOT @mine OR i.requested_by=@actor)
              AND (NOT @awaitingReview OR i.status='awaiting_review')
              AND (CAST(@FromInclusive AS timestamptz) IS NULL OR i.created_at>=CAST(@FromInclusive AS timestamptz))
              AND (CAST(@ToExclusive AS timestamptz) IS NULL OR i.created_at<CAST(@ToExclusive AS timestamptz))
            """;
        var items = await connection.QueryAsync<ContractImportListItem>(new CommandDefinition("""
            SELECT i.id AS Id,v.display_name AS DocumentName,u.display_name AS Requester,i.created_at AS CreatedAt,
                   i.status AS Status,i.current_step AS CurrentStep,i.safe_diagnostic_code AS DiagnosticCode,
                   i.result_contract_id AS ResultContractId,i.review_version AS ReviewVersion
              FROM odca.contract_imports i
              JOIN odca.document_versions v ON v.tenant_id=i.tenant_id AND v.id=i.document_version_id
              JOIN odca.users u ON u.id=i.requested_by
            """ + filter + " ORDER BY i.created_at DESC", args, tx, cancellationToken: ct));
        var total = await connection.ExecuteScalarAsync<int>(new CommandDefinition(
            "SELECT count(*)::integer FROM odca.contract_imports i " + filter, args, tx, cancellationToken: ct));
        await tx.CommitAsync(ct); return Ok(new ContractImportPage(items.AsList(), total));
    }

    internal static (DateTimeOffset? FromInclusive, DateTimeOffset? ToExclusive) CreateUtcRange(
        DateOnly? from,
        DateOnly? to,
        TimeZoneInfo timeZone)
    {
        if (from.HasValue && to.HasValue && from.Value > to.Value)
            throw new ArgumentException("A data final deve ser igual ou posterior à data inicial.", nameof(to));

        return (
            from is null ? null : StartOfDayUtc(from.Value, timeZone),
            to is null || to == DateOnly.MaxValue ? null : StartOfDayUtc(to.Value.AddDays(1), timeZone));
    }

    private static DateTimeOffset StartOfDayUtc(DateOnly date, TimeZoneInfo timeZone)
    {
        var local = date.ToDateTime(TimeOnly.MinValue, DateTimeKind.Unspecified);
        if (timeZone.IsInvalidTime(local))
            throw new InvalidOperationException($"O início do dia {date:yyyy-MM-dd} não existe no fuso configurado.");
        if (timeZone.IsAmbiguousTime(local))
        {
            // An inclusive start must choose the first instant bearing this local clock value.
            var earliestOffset = timeZone.GetAmbiguousTimeOffsets(local).Max();
            return new DateTimeOffset(local, earliestOffset).ToUniversalTime();
        }
        return new DateTimeOffset(TimeZoneInfo.ConvertTimeToUtc(local, timeZone));
    }

    [HttpPost]
    public async Task<IActionResult> Create(Guid tenantId, CreateContractImport request, CancellationToken ct)
    {
        var actor = Actor(); if (actor is null) return Unauthorized();
        await using var connection = await dataSource.OpenConnectionAsync(ct);
        if (!await Allowed(connection, actor.Value, tenantId, "tenant.imports.manage", ct)) return Forbid();
        await using var tx = await connection.BeginTransactionAsync(ct); await SetTenant(connection, tenantId, tx, ct);
        var source = await connection.QuerySingleOrDefaultAsync<ImportSource>(new CommandDefinition("""
            SELECT v.id AS VersionId,v.contract_id AS ContractId,v.sha256 AS Sha256,v.security_status AS SecurityStatus,
                   j.id AS JobId,j.status AS JobStatus
              FROM odca.document_versions v
              LEFT JOIN odca.extraction_jobs j ON j.tenant_id=v.tenant_id AND j.id=@ExtractionJobId AND j.version_id=v.id
             WHERE v.tenant_id=@tenantId AND v.id=@DocumentVersionId
            """, new { tenantId, request.DocumentVersionId, request.ExtractionJobId }, tx, cancellationToken: ct));
        if (source is null || (request.ExtractionJobId.HasValue && source.JobId is null)) return NotFound();
        var prior = await connection.ExecuteScalarAsync<Guid?>(new CommandDefinition(
            "SELECT id FROM odca.contract_imports WHERE tenant_id=@tenantId AND document_version_id=@VersionId",
            new { tenantId, source.VersionId }, tx, cancellationToken: ct));
        if (prior.HasValue) { await tx.CommitAsync(ct); return Conflict(new { title = "Documento já registrado para importação.", importId = prior }); }
        var state = State(source.SecurityStatus, source.JobStatus);
        var id = Guid.NewGuid();
        await connection.ExecuteAsync(new CommandDefinition("""
            INSERT INTO odca.contract_imports(id,tenant_id,requested_by,document_version_id,extraction_job_id,file_sha256,status,current_step)
            VALUES(@id,@tenantId,@actor,@VersionId,@JobId,@Sha256,@Status,@Step);
            INSERT INTO odca.contract_import_events(tenant_id,import_id,actor_id,event_type,details)
            VALUES(@tenantId,@id,@actor,'import_registered',jsonb_build_object('documentVersionId',@VersionId));
            """, new { id, tenantId, actor, source.VersionId, source.JobId, source.Sha256, state.Status, state.Step }, tx, cancellationToken: ct));
        await tx.CommitAsync(ct); return CreatedAtAction(nameof(Get), new { tenantId, importId = id }, new { id, state.Status, state.Step });
    }

    [HttpGet("{importId:guid}")]
    public async Task<IActionResult> Get(Guid tenantId, Guid importId, CancellationToken ct)
    {
        var actor = Actor(); if (actor is null) return Unauthorized();
        await using var c = await dataSource.OpenConnectionAsync(ct);
        if (!await Allowed(c, actor.Value, tenantId, "tenant.imports.read", ct)) return Forbid();
        await using var tx = await c.BeginTransactionAsync(ct); await SetTenant(c, tenantId, tx, ct);
        var item = await c.QuerySingleOrDefaultAsync(new CommandDefinition("""
            SELECT i.id,i.status,i.current_step,i.review_version,i.processing_version,i.attempt_count,i.safe_diagnostic_code,
                   i.result_contract_id,v.document_id,v.id AS document_version_id,v.contract_id,c.version AS contract_version,v.display_name,v.detected_type,
                   v.security_status,j.id AS extraction_job_id,j.status AS extraction_status
              FROM odca.contract_imports i JOIN odca.document_versions v ON v.tenant_id=i.tenant_id AND v.id=i.document_version_id
              JOIN odca.contracts c ON c.tenant_id=v.tenant_id AND c.id=v.contract_id
              LEFT JOIN odca.extraction_jobs j ON j.tenant_id=i.tenant_id AND j.id=i.extraction_job_id
             WHERE i.tenant_id=@tenantId AND i.id=@importId
            """, new { tenantId, importId }, tx, cancellationToken: ct));
        if (item is null) return NotFound();
        var suggestions = await c.QueryAsync(new CommandDefinition("""
            SELECT s.id,s.field_name,s.extracted_value,s.normalized_value,s.evidence,s.page_number,s.location,s.method,s.review_status,s.reviewed_value
              FROM odca.contract_imports i JOIN odca.extraction_results r ON r.job_id=i.extraction_job_id AND r.tenant_id=i.tenant_id
              JOIN odca.extraction_suggestions s ON s.result_id=r.id AND s.tenant_id=r.tenant_id
             WHERE i.tenant_id=@tenantId AND i.id=@importId ORDER BY s.field_name,s.id
            """, new { tenantId, importId }, tx, cancellationToken: ct));
        await tx.CommitAsync(ct); return Ok(new { import = item, suggestions });
    }

    [HttpPost("{importId:guid}/confirm")]
    public async Task<IActionResult> Confirm(Guid tenantId, Guid importId, ConfirmContractImport request, CancellationToken ct)
    {
        var actor = Actor(); if (actor is null) return Unauthorized();
        await using var c = await dataSource.OpenConnectionAsync(ct);
        if (!await Allowed(c, actor.Value, tenantId, "tenant.imports.confirm", ct)) return Forbid();
        await using var tx = await c.BeginTransactionAsync(ct); await SetTenant(c, tenantId, tx, ct);
        var item = await c.QuerySingleOrDefaultAsync<ConfirmableImport>(new CommandDefinition("""
            SELECT i.status AS Status,i.review_version AS ReviewVersion,i.result_contract_id AS ResultContractId,
                   v.contract_id AS ContractId,v.security_status AS SecurityStatus,j.status AS ExtractionStatus
              FROM odca.contract_imports i JOIN odca.document_versions v ON v.tenant_id=i.tenant_id AND v.id=i.document_version_id
              JOIN odca.extraction_jobs j ON j.tenant_id=i.tenant_id AND j.id=i.extraction_job_id
             WHERE i.tenant_id=@tenantId AND i.id=@importId FOR UPDATE OF i
            """, new { tenantId, importId }, tx, cancellationToken: ct));
        if (item is null) return NotFound();
        if (item.Status == "confirmed") { await tx.CommitAsync(ct); return Ok(new ContractImportResult(importId, item.ResultContractId!.Value, item.Status, true)); }
        if (item.ReviewVersion != request.ReviewVersion) return Conflict(new { title = "A revisão foi alterada em outra sessão.", currentVersion = item.ReviewVersion });
        if (item.SecurityStatus != "safe" || item.ExtractionStatus != "ready_for_review") return Conflict(new { title = "O documento ainda não está aprovado e pronto para confirmação." });
        var pending = await c.ExecuteScalarAsync<int>(new CommandDefinition("""
            SELECT count(*)::integer FROM odca.extraction_suggestions s JOIN odca.extraction_results r ON r.id=s.result_id AND r.tenant_id=s.tenant_id
            JOIN odca.contract_imports i ON i.extraction_job_id=r.job_id AND i.tenant_id=r.tenant_id
            WHERE i.tenant_id=@tenantId AND i.id=@importId AND s.review_status='pending'
            """, new { tenantId, importId }, tx, cancellationToken: ct));
        if (pending > 0) return UnprocessableEntity(new { title = "Revise todas as sugestões antes de confirmar.", issues = new[] { new ImportValidationIssue("unreviewed-suggestions", ImportIssueSeverity.Blocking, "Há sugestões ainda não revisadas.") } });
        var contract = await c.QuerySingleOrDefaultAsync<ContractForValidation>(new CommandDefinition(
            "SELECT title AS Title,start_date AS StartDate,end_date AS EndDate,value AS Value,currency AS Currency,renewal_notice_days AS RenewalNoticeDays FROM odca.contracts WHERE tenant_id=@tenantId AND id=@ContractId",
            new { tenantId, item.ContractId }, tx, cancellationToken: ct));
        var issues = Validate(contract);
        if (issues.Any(issue => issue.Severity == ImportIssueSeverity.Blocking))
            return UnprocessableEntity(new { title = "Corrija as inconsistências bloqueantes antes de confirmar.", issues });
        await c.ExecuteAsync(new CommandDefinition("""
            UPDATE odca.contract_imports SET status='confirmed',current_step='confirmation',result_contract_id=@ContractId,
                   confirmed_by=@actor,confirmed_at=now(),updated_at=now(),review_version=review_version+1
             WHERE tenant_id=@tenantId AND id=@importId;
            INSERT INTO odca.contract_import_events(tenant_id,import_id,actor_id,event_type,details)
            VALUES(@tenantId,@importId,@actor,'import_confirmed',jsonb_build_object('contractId',@ContractId,'idempotencyKey',@IdempotencyKey));
            INSERT INTO odca.audit_events(scope_type,tenant_id,actor_user_id,action,entity_type,entity_id,result,metadata)
            VALUES('tenant',@tenantId,@actor,'contract.import.confirmed','contract_import',@importId,'success',jsonb_build_object('contractId',@ContractId));
            """, new { tenantId, importId, actor, item.ContractId, request.IdempotencyKey }, tx, cancellationToken: ct));
        await tx.CommitAsync(ct); return Ok(new ContractImportResult(importId, item.ContractId, "confirmed", false));
    }

    [HttpPost("{importId:guid}/cancel")]
    public async Task<IActionResult> Cancel(Guid tenantId, Guid importId, CancellationToken ct)
    {
        var actor = Actor(); if (actor is null) return Unauthorized();
        await using var c = await dataSource.OpenConnectionAsync(ct);
        if (!await Allowed(c, actor.Value, tenantId, "tenant.imports.manage", ct)) return Forbid();
        await using var tx = await c.BeginTransactionAsync(ct); await SetTenant(c, tenantId, tx, ct);
        var changed = await c.ExecuteAsync(new CommandDefinition("""
            UPDATE odca.contract_imports SET status='cancelled',cancelled_by=@actor,cancelled_at=now(),updated_at=now()
             WHERE tenant_id=@tenantId AND id=@importId AND status NOT IN('confirmed','cancelled');
            INSERT INTO odca.contract_import_events(tenant_id,import_id,actor_id,event_type)
            SELECT @tenantId,@importId,@actor,'import_cancelled' WHERE EXISTS(SELECT 1 FROM odca.contract_imports WHERE tenant_id=@tenantId AND id=@importId AND status='cancelled');
            """, new { tenantId, importId, actor }, tx, cancellationToken: ct));
        await tx.CommitAsync(ct); return changed == 0 ? Conflict(new { title = "A importação confirmada não pode ser cancelada." }) : NoContent();
    }


    internal static IReadOnlyList<ImportValidationIssue> Validate(ContractForValidation? contract)
    {
        var issues = new List<ImportValidationIssue>();
        if (contract is null || string.IsNullOrWhiteSpace(contract.Title))
            issues.Add(new("required-contract-data", ImportIssueSeverity.Blocking, "A identificação do contrato é obrigatória.", "title"));
        if (contract?.StartDate is not null && contract.EndDate is not null && contract.EndDate < contract.StartDate)
            issues.Add(new("end-before-start", ImportIssueSeverity.Blocking, "O término não pode ser anterior ao início.", "end_date"));
        if (contract?.Value is not null && string.IsNullOrWhiteSpace(contract.Currency))
            issues.Add(new("missing-currency", ImportIssueSeverity.Blocking, "Informe a moeda do valor contratual.", "currency"));
        if (contract?.RenewalNoticeDays is < 0)
            issues.Add(new("invalid-notice-period", ImportIssueSeverity.Blocking, "O prazo de comunicação não pode ser negativo.", "renewal_notice_days"));
        return issues;
    }

    private Guid? Actor() => Guid.TryParse(User.FindFirstValue("sub"), out var id) ? id : null;
    private static Task<bool> Allowed(NpgsqlConnection c, Guid actor, Guid tenant, string permission, CancellationToken ct) => c.ExecuteScalarAsync<bool>(new CommandDefinition("SELECT odca.tenant_actor_has_permission(@actor,@tenant,@permission)", new { actor, tenant, permission }, cancellationToken: ct));
    private static Task<int> SetTenant(NpgsqlConnection c, Guid tenant, NpgsqlTransaction tx, CancellationToken ct) => c.ExecuteAsync(new CommandDefinition("SELECT set_config('odca.tenant_id',@value,true)", new { value = tenant.ToString() }, tx, cancellationToken: ct));
    private static (string Status, string Step) State(string security, string? extraction) =>
        security switch { "pending" or "scanning" => ("security_review", "document"), "rejected" or "scan_failed" => ("failed", "document"), _ => extraction switch { null => ("received", "document"), "queued" => ("queued", "document"), "processing" => ("processing", "document"), "ready_for_review" => ("awaiting_review", "contract_data"), _ => ("failed", "document") } };

    [LoggerMessage(EventId = 2101, Level = LogLevel.Error, Message = "Configuração de fuso indisponível para tenant={TenantId}; motivo={Reason}.")]
    private static partial void LogTimeZoneConfigurationUnavailable(ILogger logger, Exception exception, Guid tenantId, TimeZoneConfigurationError reason);

    [LoggerMessage(EventId = 2102, Level = LogLevel.Error, Message = "Limite local inválido para tenant={TenantId}.")]
    private static partial void LogInvalidLocalRange(ILogger logger, Exception exception, Guid tenantId);

    private sealed record ImportSource(Guid VersionId, Guid ContractId, string Sha256, string SecurityStatus, Guid? JobId, string? JobStatus);
    internal sealed record ContractForValidation(string Title, DateOnly? StartDate, DateOnly? EndDate, decimal? Value, string? Currency, int? RenewalNoticeDays);
    private sealed record ConfirmableImport(string Status, long ReviewVersion, Guid? ResultContractId, Guid ContractId, string SecurityStatus, string ExtractionStatus);
}
