using System.Security.Claims;
using System.Security.Cryptography;
using Dapper;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Npgsql;
using Odca.Application.Documents;
using IOFile = System.IO.File;

namespace Odca.Api.Controllers;

[ApiController]
[Route("api/v1/organizations/{tenantId:guid}/contracts/{contractId:guid}/documents")]
[Authorize(Policy = "PasswordChanged")]
public sealed class ContractDocumentsController(NpgsqlDataSource dataSource, IConfiguration configuration) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> List(Guid tenantId, Guid contractId, CancellationToken ct)
    {
        var actor = Actor(); if (actor is null) return Unauthorized();
        await using var connection = await dataSource.OpenConnectionAsync(ct);
        if (!await Allowed(connection, actor.Value, tenantId, "tenant.contracts.read", ct)) return Forbid();
        await using var transaction = await connection.BeginTransactionAsync(ct);
        await SetTenant(connection, tenantId, transaction, ct);
        var rows = await connection.QueryAsync(new CommandDefinition("""
            SELECT d.id, d.title, v.id AS version_id, v.version_number, v.display_name, v.detected_type,
                   v.byte_size, v.security_status, v.uploaded_at, v.uploaded_by
              FROM odca.contract_documents d JOIN odca.document_versions v ON v.document_id=d.id
             WHERE d.tenant_id=@tenantId AND d.contract_id=@contractId AND d.deleted_at IS NULL
             ORDER BY d.created_at DESC,v.version_number DESC
            """, new { tenantId, contractId }, transaction, cancellationToken: ct));
        await transaction.CommitAsync(ct);
        return Ok(rows);
    }

    [HttpPost]
    [RequestSizeLimit(250_000_000)]
    public async Task<IActionResult> Upload(Guid tenantId, Guid contractId, IFormFile file, [FromForm] Guid? documentId, CancellationToken ct)
    {
        var actor = Actor(); if (actor is null) return Unauthorized();
        if (file.Length <= 0) return ValidationProblem();
        long maximumFileBytes;
        await using (var limitsConnection = await dataSource.OpenConnectionAsync(ct))
        {
            if (!await Allowed(limitsConnection, actor.Value, tenantId, "tenant.documents.manage", ct)) return Forbid();
            maximumFileBytes = await limitsConnection.ExecuteScalarAsync<long>(new CommandDefinition("""
                SELECT COALESCE(max(e.limit_value) FILTER(WHERE e.entitlement_code='file_bytes'),0)
                  FROM odca.subscriptions s JOIN odca.plan_entitlements e ON e.plan_version_id=s.plan_version_id
                 WHERE s.tenant_id=@tenantId AND s.status='active'
                """,new{tenantId},cancellationToken:ct));
        }
        if (maximumFileBytes<=0) return Conflict(new ProblemDetails{Title="Upload indisponível",Detail="A assinatura não possui um limite de arquivo ativo."});
        if (file.Length>maximumFileBytes) return Problem(statusCode:413,title:"Arquivo excede o limite do plano.",detail:$"O limite atual é {maximumFileBytes} bytes.");
        var root = Path.GetFullPath(configuration["Documents:StoragePath"] ?? "./private-documents");
        Directory.CreateDirectory(Path.Combine(root, "quarantine"));
        var temporary = Path.Combine(root, "quarantine", $"upload-{Guid.NewGuid():N}.tmp");
        try
        {
            string hash; SupportedDocumentType type; long size;
            await using (var source = file.OpenReadStream())
            await using (var destinationStream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, FileOptions.Asynchronous | FileOptions.WriteThrough))
            using (var sha = IncrementalHash.CreateHash(HashAlgorithmName.SHA256))
            {
                var buffer = new byte[81920]; size = 0; var prefix = new byte[16]; var prefixCount = 0;
                int read;
                while ((read = await source.ReadAsync(buffer, ct)) > 0)
                {
                    size += read; if (size > maximumFileBytes) return Problem(statusCode: 413, title: "Arquivo excede o limite do plano.",detail:$"O stream ultrapassou {maximumFileBytes} bytes e foi interrompido.");
                    var copy = Math.Min(read, prefix.Length - prefixCount); if (copy > 0) { buffer.AsSpan(0, copy).CopyTo(prefix.AsSpan(prefixCount)); prefixCount += copy; }
                    sha.AppendData(buffer, 0, read); await destinationStream.WriteAsync(buffer.AsMemory(0, read), ct);
                }
                type = DocumentContent.Detect(prefix.AsSpan(0, prefixCount), file.FileName);
                hash = Convert.ToHexString(sha.GetHashAndReset()).ToLowerInvariant();
            }
            var versionId = Guid.NewGuid(); var logicalId = documentId ?? Guid.NewGuid(); var storageObjectKey = $"{tenantId:N}/{contractId:N}/{versionId:N}";
            await using var connection = await dataSource.OpenConnectionAsync(ct);
            if (!await Allowed(connection, actor.Value, tenantId, "tenant.documents.manage", ct)) return Forbid();
            await using var transaction = await connection.BeginTransactionAsync(ct);
            await SetTenant(connection, tenantId, transaction, ct);
            var contractExists = await connection.ExecuteScalarAsync<bool>(new CommandDefinition("SELECT EXISTS(SELECT 1 FROM odca.contracts WHERE tenant_id=@tenantId AND id=@contractId)", new { tenantId, contractId }, transaction, cancellationToken: ct));
            if (!contractExists) return NotFound();
            var operationKey = Request.Headers.TryGetValue("Idempotency-Key", out var suppliedKey) && Guid.TryParse(suppliedKey, out var parsedKey) ? parsedKey : versionId;
            await connection.ExecuteAsync(new CommandDefinition("""
                INSERT INTO odca.tenant_storage_usage(tenant_id) VALUES(@tenantId) ON CONFLICT DO NOTHING;
                UPDATE odca.tenant_storage_usage u SET quota_bytes=q.effective_quota
                  FROM (SELECT COALESCE(max(e.limit_value) FILTER(WHERE e.entitlement_code='storage_bytes'),0)+
                               COALESCE((SELECT sum(g.quantity_bytes) FROM odca.storage_capacity_grants g WHERE g.tenant_id=@tenantId AND g.revoked_at IS NULL AND(g.valid_until IS NULL OR g.valid_until>now())),0) AS effective_quota
                          FROM odca.subscriptions s JOIN odca.plan_entitlements e ON e.plan_version_id=s.plan_version_id WHERE s.tenant_id=@tenantId) q
                 WHERE u.tenant_id=@tenantId;
                """, new { tenantId }, transaction, cancellationToken: ct));
            var existing = await connection.QuerySingleOrDefaultAsync<ExistingReservation>(new CommandDefinition("SELECT id AS Id,status AS Status FROM odca.storage_reservations WHERE tenant_id=@tenantId AND operation_key=CAST(@operationKey AS text)",new{tenantId,operationKey},transaction,cancellationToken:ct));
            if(existing is not null&&existing.Status=="confirmed") return Conflict(new ProblemDetails{Title="Upload já confirmado",Detail="A chave de idempotência já foi utilizada."});
            var reservationId=existing?.Id??Guid.NewGuid();
            var reserved = await connection.ExecuteScalarAsync<bool>(new CommandDefinition("UPDATE odca.tenant_storage_usage SET reserved_bytes=reserved_bytes+@size WHERE tenant_id=@tenantId AND used_bytes+reserved_bytes+@size<=quota_bytes RETURNING true", new { tenantId, size }, transaction, cancellationToken: ct));
            if (!reserved) return Problem(statusCode: 413, title: "A cota de armazenamento da organização foi atingida.");
            await connection.ExecuteAsync(new CommandDefinition("INSERT INTO odca.storage_reservations(id,tenant_id,operation_key,requested_bytes,status,created_by,expires_at) VALUES(@reservationId,@tenantId,CAST(@operationKey AS text),@size,'reserved',@actor,now()+interval '30 minutes') ON CONFLICT(tenant_id,operation_key) DO UPDATE SET requested_bytes=excluded.requested_bytes,status='reserved',expires_at=excluded.expires_at",new{reservationId,tenantId,operationKey,size,actor},transaction,cancellationToken:ct));
            if (documentId is null)
                await connection.ExecuteAsync(new CommandDefinition("INSERT INTO odca.contract_documents(id,tenant_id,contract_id,title,created_by) VALUES(@logicalId,@tenantId,@contractId,@title,@actor)", new { logicalId, tenantId, contractId, title = Path.GetFileName(file.FileName), actor }, transaction, cancellationToken: ct));
            else
            {
                var lockedDocumentId = await connection.ExecuteScalarAsync<Guid?>(new CommandDefinition("SELECT id FROM odca.contract_documents WHERE id=@logicalId AND tenant_id=@tenantId AND contract_id=@contractId AND deleted_at IS NULL FOR UPDATE", new { logicalId, tenantId, contractId }, transaction, cancellationToken: ct));
                if (lockedDocumentId is null) return NotFound();
            }
            var number = await connection.ExecuteScalarAsync<int>(new CommandDefinition("SELECT COALESCE(max(version_number),0)+1 FROM odca.document_versions WHERE document_id=@logicalId", new { logicalId }, transaction, cancellationToken: ct));
            await connection.ExecuteAsync(new CommandDefinition("""
                INSERT INTO odca.document_versions(id,tenant_id,contract_id,document_id,version_number,uploaded_by,display_name,detected_type,byte_size,sha256,storage_key)
                VALUES(@versionId,@tenantId,@contractId,@logicalId,@number,@actor,@name,@type,@size,@hash,@key);
                UPDATE odca.tenant_storage_usage SET reserved_bytes=reserved_bytes-@size,used_bytes=used_bytes+@size WHERE tenant_id=@tenantId;
                UPDATE odca.storage_reservations SET status='confirmed',finalized_at=now() WHERE tenant_id=@tenantId AND id=@reservationId;
                INSERT INTO odca.resource_movements(tenant_id,resource_type,movement_type,quantity,unit,source_type,source_id,idempotency_key,actor_user_id)
                VALUES(@tenantId,'storage_usage','consume',@size,'bytes','document_version',@versionId,'upload:'||CAST(@operationKey AS text),@actor);
                INSERT INTO odca.contract_events(tenant_id,contract_id,actor_id,event_type,details) VALUES(@tenantId,@contractId,@actor,'document_uploaded',jsonb_build_object('versionId',@versionId,'bytes',@size));
                """, new { versionId, tenantId, contractId, logicalId, number, actor, name = Path.GetFileName(file.FileName), type = type.ToString().ToLowerInvariant(), size, hash, key = storageObjectKey,reservationId,operationKey }, transaction, cancellationToken: ct));
            var destinationPath = Path.Combine(root, storageObjectKey.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(destinationPath)!);
            IOFile.Move(temporary, destinationPath);
            try
            {
                await transaction.CommitAsync(ct);
            }
            catch
            {
                IOFile.Delete(destinationPath);
                throw;
            }
            return Created($"{Request.Path}/{logicalId:D}/versions/{versionId:D}", new { documentId = logicalId, versionId, version = number, securityStatus = "pending" });
        }
        finally { if (IOFile.Exists(temporary)) IOFile.Delete(temporary); }
    }

    [HttpGet("{documentId:guid}/versions/{versionId:guid}/content")]
    public async Task<IActionResult> Content(Guid tenantId, Guid contractId, Guid documentId, Guid versionId, CancellationToken ct)
    {
        var actor = Actor(); if (actor is null) return Unauthorized();
        await using var connection = await dataSource.OpenConnectionAsync(ct);
        if (!await Allowed(connection, actor.Value, tenantId, "tenant.documents.download", ct)) return Forbid();
        await using var transaction = await connection.BeginTransactionAsync(ct);
        await SetTenant(connection, tenantId, transaction, ct);
        var item = await connection.QuerySingleOrDefaultAsync<StoredVersion>(new CommandDefinition("SELECT storage_key AS StorageKey,display_name AS DisplayName,detected_type AS DetectedType,security_status AS SecurityStatus FROM odca.document_versions WHERE tenant_id=@tenantId AND contract_id=@contractId AND document_id=@documentId AND id=@versionId", new { tenantId, contractId, documentId, versionId }, transaction, cancellationToken: ct));
        if (item is null) return NotFound();
        if (item.SecurityStatus != "safe") return Conflict(new ProblemDetails { Title = "Arquivo indisponível", Detail = "O arquivo somente é liberado após uma análise de segurança concluída." });
        var root = Path.GetFullPath(configuration["Documents:StoragePath"] ?? "./private-documents"); var path = Path.GetFullPath(Path.Combine(root, item.StorageKey));
        if (!path.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.Ordinal) || !IOFile.Exists(path)) return Problem(statusCode: 503, title: "Arquivo requer reconciliação de armazenamento.");
        await transaction.CommitAsync(ct);
        return PhysicalFile(path, Mime(item.DetectedType), item.DisplayName, enableRangeProcessing: true);
    }

    [HttpPost("{documentId:guid}/versions/{versionId:guid}/extractions")]
    public async Task<IActionResult> Extract(Guid tenantId, Guid contractId, Guid documentId, Guid versionId, CancellationToken ct)
    {
        var actor = Actor(); if (actor is null) return Unauthorized();
        await using var connection = await dataSource.OpenConnectionAsync(ct);
        if (!await Allowed(connection, actor.Value, tenantId, "tenant.extractions.request", ct)) return Forbid();
        await using var transaction = await connection.BeginTransactionAsync(ct); await SetTenant(connection, tenantId, transaction, ct);
        var safe = await connection.ExecuteScalarAsync<bool>(new CommandDefinition("SELECT EXISTS(SELECT 1 FROM odca.document_versions WHERE tenant_id=@tenantId AND contract_id=@contractId AND document_id=@documentId AND id=@versionId AND security_status='safe')", new { tenantId, contractId, documentId, versionId }, transaction, cancellationToken: ct));
        if (!safe) return Conflict(new ProblemDetails { Title = "A versão ainda não foi aprovada pela análise de segurança." });
        var id = Guid.NewGuid(); await connection.ExecuteAsync(new CommandDefinition("INSERT INTO odca.extraction_jobs(id,tenant_id,contract_id,version_id,requested_by) VALUES(@id,@tenantId,@contractId,@versionId,@actor)", new { id, tenantId, contractId, versionId, actor }, transaction, cancellationToken: ct)); await transaction.CommitAsync(ct);
        return Accepted(new { extractionId = id, status = "queued" });
    }

    [HttpGet("extractions/{extractionId:guid}")]
    public async Task<IActionResult> Extraction(Guid tenantId, Guid contractId, Guid extractionId, CancellationToken ct)
    {
        var actor = Actor(); if (actor is null) return Unauthorized();
        await using var connection = await dataSource.OpenConnectionAsync(ct);
        if (!await Allowed(connection, actor.Value, tenantId, "tenant.extractions.review", ct)) return Forbid();
        await using var transaction = await connection.BeginTransactionAsync(ct);
        await SetTenant(connection, tenantId, transaction, ct);
        var job = await connection.QuerySingleOrDefaultAsync(new CommandDefinition("SELECT id,status,attempt_count,failure_code,version_id,completed_at FROM odca.extraction_jobs WHERE tenant_id=@tenantId AND contract_id=@contractId AND id=@extractionId", new { tenantId, contractId, extractionId }, transaction, cancellationToken: ct));
        if (job is null) return NotFound();
        var suggestions = await connection.QueryAsync(new CommandDefinition("SELECT s.id,s.field_name,s.extracted_value,s.normalized_value,s.evidence,s.page_number,s.method,s.review_status,s.reviewed_value FROM odca.extraction_suggestions s JOIN odca.extraction_results r ON r.id=s.result_id AND r.tenant_id=s.tenant_id WHERE r.job_id=@extractionId ORDER BY s.field_name,s.id", new { extractionId }, transaction, cancellationToken: ct));
        await transaction.CommitAsync(ct);
        return Ok(new { job, suggestions });
    }

    [HttpPost("extractions/{extractionId:guid}/apply")]
    public async Task<IActionResult> Apply(Guid tenantId, Guid contractId, Guid extractionId, [FromBody] ApplyReviewRequest request, CancellationToken ct)
    {
        var actor = Actor(); if (actor is null) return Unauthorized();
        if (request.Decisions.Count is 0 or > 30 || request.Decisions.Any(x => x.Status is not ("accepted" or "edited" or "rejected"))) return ValidationProblem();
        await using var connection = await dataSource.OpenConnectionAsync(ct);
        if (!await Allowed(connection, actor.Value, tenantId, "tenant.extractions.apply", ct)) return Forbid();
        await using var tx = await connection.BeginTransactionAsync(ct); await SetTenant(connection, tenantId, tx, ct);
        var prior = await connection.QuerySingleOrDefaultAsync<string>(new CommandDefinition("SELECT status FROM odca.extraction_reviews WHERE tenant_id=@tenantId AND idempotency_key=@key", new { tenantId, key = request.IdempotencyKey }, tx, cancellationToken: ct));
        if (prior == "applied") { await tx.CommitAsync(ct); return Ok(new { status = "applied", repeated = true }); }
        var currentVersion = await connection.ExecuteScalarAsync<long?>(new CommandDefinition("SELECT version FROM odca.contracts WHERE tenant_id=@tenantId AND id=@contractId FOR UPDATE", new { tenantId, contractId }, tx, cancellationToken: ct));
        if (currentVersion is null) return NotFound();
        var reviewId = Guid.NewGuid();
        await connection.ExecuteAsync(new CommandDefinition("INSERT INTO odca.extraction_reviews(id,tenant_id,contract_id,job_id,reviewed_by,contract_version,idempotency_key) VALUES(@reviewId,@tenantId,@contractId,@extractionId,@actor,@expected,@key) ON CONFLICT(tenant_id,idempotency_key) DO NOTHING", new { reviewId, tenantId, contractId, extractionId, actor, expected = request.ContractVersion, key = request.IdempotencyKey }, tx, cancellationToken: ct));
        if (currentVersion != request.ContractVersion)
        {
            await connection.ExecuteAsync(new CommandDefinition("UPDATE odca.extraction_reviews SET status='conflicting' WHERE tenant_id=@tenantId AND idempotency_key=@key", new { tenantId, key = request.IdempotencyKey }, tx, cancellationToken: ct));
            await tx.CommitAsync(ct); return Conflict(new { title = "O contrato foi alterado em outra sessão.", currentVersion, decisionsPreserved = true });
        }
        var allowed = new Dictionary<string, string>(StringComparer.Ordinal) { ["title"]="title", ["reference"]="reference", ["start_date"]="start_date", ["end_date"]="end_date", ["value"]="value", ["currency"]="currency", ["renewal_notice_days"]="renewal_notice_days" };
        foreach (var decision in request.Decisions)
        {
            var suggestion = await connection.QuerySingleOrDefaultAsync<SuggestionForApply>(new CommandDefinition("SELECT s.field_name AS FieldName,s.normalized_value AS NormalizedValue FROM odca.extraction_suggestions s JOIN odca.extraction_results r ON r.id=s.result_id AND r.tenant_id=s.tenant_id WHERE s.tenant_id=@tenantId AND s.id=@id AND r.job_id=@extractionId FOR UPDATE", new { tenantId, id = decision.SuggestionId, extractionId }, tx, cancellationToken: ct));
            if (suggestion is null) return ValidationProblem();
            var value = decision.Status == "edited" ? decision.Value?.Trim() : suggestion.NormalizedValue;
            await connection.ExecuteAsync(new CommandDefinition("UPDATE odca.extraction_suggestions SET review_status=@status,reviewed_value=@value,reviewed_by=@actor,reviewed_at=now() WHERE tenant_id=@tenantId AND id=@id", new { decision.Status, value, actor, tenantId, id = decision.SuggestionId }, tx, cancellationToken: ct));
            if (decision.Status == "rejected" || !allowed.TryGetValue(suggestion.FieldName, out var column)) continue;
            if (string.IsNullOrWhiteSpace(value)) return ValidationProblem();
            var cast = column switch { "start_date" or "end_date" => "::date", "value" => "::numeric(18,2)", "renewal_notice_days" => "::integer", _ => "" };
            await connection.ExecuteAsync(new CommandDefinition($"UPDATE odca.contracts SET {column}=@value{cast} WHERE tenant_id=@tenantId AND id=@contractId", new { value, tenantId, contractId }, tx, cancellationToken: ct));
        }
        await connection.ExecuteAsync(new CommandDefinition("""
            UPDATE odca.contracts SET version=version+1,updated_at=now() WHERE tenant_id=@tenantId AND id=@contractId;
            UPDATE odca.extraction_reviews SET status='applied',applied_at=now() WHERE tenant_id=@tenantId AND idempotency_key=@key;
            INSERT INTO odca.contract_events(tenant_id,contract_id,actor_id,event_type,details) VALUES(@tenantId,@contractId,@actor,'extraction_review_applied',jsonb_build_object('extractionId',@extractionId,'idempotencyKey',@key));
            """, new { tenantId, contractId, actor, extractionId, key = request.IdempotencyKey }, tx, cancellationToken: ct));
        await tx.CommitAsync(ct); return Ok(new { status = "applied", version = currentVersion + 1 });
    }

    private Guid? Actor() => Guid.TryParse(User.FindFirstValue("sub"), out var id) ? id : null;
    private static Task<bool> Allowed(NpgsqlConnection c, Guid actor, Guid tenant, string permission, CancellationToken ct) => c.ExecuteScalarAsync<bool>(new CommandDefinition("SELECT odca.tenant_actor_has_permission(@actor,@tenant,@permission)", new { actor, tenant, permission }, cancellationToken: ct));
    private static Task<int> SetTenant(NpgsqlConnection c, Guid tenant, NpgsqlTransaction tx, CancellationToken ct) => c.ExecuteAsync(new CommandDefinition("SELECT set_config('odca.tenant_id',@value,true)", new { value = tenant.ToString() }, tx, cancellationToken: ct));
    private static string Mime(string type) => type switch { "pdf" => "application/pdf", "png" => "image/png", "jpeg" => "image/jpeg", "docx" => "application/vnd.openxmlformats-officedocument.wordprocessingml.document", _ => "application/octet-stream" };
    private sealed record StoredVersion(string StorageKey, string DisplayName, string DetectedType, string SecurityStatus);
    private sealed record ExistingReservation(Guid Id,string Status);
    private sealed record SuggestionForApply(string FieldName, string? NormalizedValue);
    public sealed record ReviewDecision(Guid SuggestionId, string Status, string? Value);
    public sealed record ApplyReviewRequest(Guid IdempotencyKey, long ContractVersion, IReadOnlyList<ReviewDecision> Decisions);
}
