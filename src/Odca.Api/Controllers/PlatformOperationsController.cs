using System.Security.Claims;
using System.Text.Json;
using Dapper;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Npgsql;
using Odca.Api.Localization;
using Odca.Contracts.Administration;

namespace Odca.Api.Controllers;

/// <summary>
/// Operação contratual Bloco B: administração global da plataforma — catálogo oficial
/// (publicar/retirar com justificativa e auditoria), busca global de usuários e políticas
/// de SLA vigentes. As funções SECURITY DEFINER validam o ator via assert_platform_actor,
/// por isso o GUC odca.user_id precisa ser definido antes das chamadas.
/// </summary>
[ApiController]
[Authorize(Policy = "PlatformAdministrator")]
public sealed class PlatformOperationsController(NpgsqlDataSource dataSource) : ControllerBase
{
    [HttpGet("api/v1/platform/template-catalog")]
    public async Task<IActionResult> TemplateCatalog(CancellationToken ct)
    {
        var actor = Actor(); if (actor is null) return Unauthorized();
        await using var c = await dataSource.OpenConnectionAsync(ct);
        var rows = (await c.QueryAsync<TemplateCatalogRow>(new CommandDefinition("""
            SELECT t.id AS TemplateId, t.official_key AS OfficialKey, t.name AS Name, COALESCE(t.description,'') AS Description,
                   t.contract_type AS ContractType, t.status AS Status, t.official_revision AS CurrentRevision,
                   t.created_at AS CreatedAt, t.published_at AS PublishedAt
              FROM odca.contract_templates t
             WHERE t.owner_tenant_id IS NULL AND t.scope='global' AND t.official_key IS NOT NULL
             ORDER BY t.created_at
            """, cancellationToken: ct))).AsList();
        if (rows.Count == 0) return Ok(new TemplateCatalogResponse([]));
        var ids = rows.Select(r => r.TemplateId).ToArray();
        var versions = (await c.QueryAsync<TemplateCatalogVersionRow>(new CommandDefinition("""
            SELECT v.template_id AS TemplateId, v.version_number AS VersionNumber,
                   to_char(v.created_at AT TIME ZONE 'UTC','YYYY-MM-DD"T"HH24:MI:SS"Z"') AS CreatedAt,
                   to_char(v.published_at AT TIME ZONE 'UTC','YYYY-MM-DD"T"HH24:MI:SS"Z"') AS PublishedAt
              FROM odca.contract_template_versions v
             WHERE v.template_id = ANY(@ids)
             ORDER BY v.template_id, v.version_number
            """, new { ids }, cancellationToken: ct))).AsList();
        var byTemplate = versions.GroupBy(v => v.TemplateId)
            .ToDictionary(g => g.Key, g => g.Select(v => new TemplateCatalogVersion(v.VersionNumber, v.CreatedAt, v.PublishedAt)).ToArray());
        // Npgsql lê timestamptz como DateTime (UTC); o DTO expõe DateTimeOffset?.
        var items = rows.Select(r => new TemplateCatalogItem(
            r.OfficialKey, r.Name, r.Description, r.ContractType, r.Status, r.CurrentRevision,
            new DateTimeOffset(r.CreatedAt.ToUniversalTime()),
            r.PublishedAt is { } pub ? new DateTimeOffset(pub.ToUniversalTime()) : null,
            byTemplate.TryGetValue(r.TemplateId, out var vs) ? vs : []))
            .ToArray();
        return Ok(new TemplateCatalogResponse(items));
    }

    [HttpPost("api/v1/platform/template-catalog/{key}/status")]
    public async Task<IActionResult> SetTemplateStatus(string key, [FromBody] TemplateCatalogStatusRequest request, CancellationToken ct)
    {
        var actor = Actor(); if (actor is null) return Unauthorized();
        if (string.IsNullOrWhiteSpace(request.Action)) return this.Invalid("api.platform_catalog.action_required");
        await using var c = await dataSource.OpenConnectionAsync(ct);
        await EnsurePlatformGuc(c, actor.Value, ct);
        var payload = await c.ExecuteScalarAsync<string>(new CommandDefinition(
            "SELECT odca.platform_catalog_set_status(@actor,@key,@action,@reason)::text",
            new { actor = actor.Value, key = key.Trim(), action = request.Action.Trim().ToLowerInvariant(), reason = string.IsNullOrWhiteSpace(request.Reason) ? null : request.Reason.Trim() },
            cancellationToken: ct));
        if (payload is null) return this.Status(500, "api.internal.no_result");
        using var doc = JsonDocument.Parse(payload);
        var root = doc.RootElement;
        if (root.TryGetProperty("code", out var codeEl))
        {
            // D-OC27: (código, mensagem canônica PT) → chave estável api.sql.* no catálogo; par sem mapeamento preserva o original.
            var code = codeEl.GetString() ?? "catalog.error";
            var fallback = root.TryGetProperty("error", out var errEl) ? errEl.GetString() ?? "Operação inválida." : "Operação inválida.";
            var sqlKey = ApiSqlMessages.KeyFor(code, fallback);
            var translated = sqlKey is null ? null : this.T(sqlKey);
            var title = translated is null || translated == sqlKey ? fallback : translated;
            return code switch
            {
                "catalog.not_found" => new ObjectResult(new ProblemDetails { Title = title, Status = 404, Detail = code, Extensions = { ["code"] = code } }) { StatusCode = 404 },
                _ => new ObjectResult(new ProblemDetails { Title = title, Status = 400, Detail = code, Extensions = { ["code"] = code } }) { StatusCode = 400 }
            };
        }
        return Ok(root.Clone());
    }

    [HttpGet("api/v1/platform/template-catalog/{key}/revision/current")]
    public async Task<IActionResult> CurrentRevision(string key, CancellationToken ct)
    {
        var actor = Actor(); if (actor is null) return Unauthorized();
        await using var c = await dataSource.OpenConnectionAsync(ct);
        var row = await c.QueryFirstOrDefaultAsync<TemplateRevisionCurrentRow>(new CommandDefinition("""
            SELECT t.name AS Name, COALESCE(t.description,'') AS Description, t.contract_type AS ContractType,
                   t.status AS Status, t.official_revision AS CurrentRevision,
                   v.content::text AS Content, v.fields::text AS Fields
              FROM odca.contract_templates t
              JOIN odca.contract_template_versions v ON v.template_id = t.id AND v.version_number = t.current_version
             WHERE t.owner_tenant_id IS NULL AND t.scope='global' AND t.official_key=@key
            """, new { key = key.Trim() }, cancellationToken: ct));
        if (row is null) return NotFound(new { error = this.T("api.catalog.not_found", key.Trim()), code = "catalog.not_found" });
        return Ok(new
        {
            officialKey = key.Trim(),
            row.Name,
            row.Description,
            row.ContractType,
            row.Status,
            row.CurrentRevision,
            row.Content,
            row.Fields
        });
    }

    [HttpPost("api/v1/platform/template-catalog/{key}/revisions")]
    public async Task<IActionResult> PublishRevision(string key, [FromBody] TemplateRevisionPublishRequest request, CancellationToken ct)
    {
        var actor = Actor(); if (actor is null) return Unauthorized();
        var summary = request.Summary?.Trim() ?? string.Empty;
        if (summary.Length is < 5 or > 500)
            return this.Invalid("api.platform_catalog.summary_invalid");

        string contentJson, fieldsJson;
        try
        {
            using (var contentDoc = JsonDocument.Parse(request.Content ?? "{}"))
            {
                if (contentDoc.RootElement.ValueKind is not JsonValueKind.Object)
                    return this.Invalid("api.platform_catalog.content_invalid");
                contentJson = contentDoc.RootElement.GetRawText();
            }
            using (var fieldsDoc = JsonDocument.Parse(request.Fields ?? "[]"))
            {
                if (fieldsDoc.RootElement.ValueKind is not JsonValueKind.Array)
                    return this.Invalid("api.platform_catalog.fields_invalid");
                fieldsJson = fieldsDoc.RootElement.GetRawText();
            }
        }
        catch (JsonException)
        {
            return this.Invalid("api.platform_catalog.json_invalid");
        }

        await using var c = await dataSource.OpenConnectionAsync(ct);
        await EnsurePlatformGuc(c, actor.Value, ct);
        int newRevision; Guid newVersionId;
        await using var tx = await c.BeginTransactionAsync(ct);
        try
        {
            var locked = await c.QueryFirstOrDefaultAsync<TemplateRevisionLockRow>(new CommandDefinition("""
                SELECT t.id AS Id, t.status AS Status, t.official_revision AS Revision, t.current_version AS CurrentVersion
                  FROM odca.contract_templates t
                 WHERE t.owner_tenant_id IS NULL AND t.scope='global' AND t.official_key=@key
                 FOR UPDATE
                """, new { key = key.Trim() }, tx, cancellationToken: ct));
            if (locked is null)
            {
                await tx.RollbackAsync(ct);
                return NotFound(new { error = this.T("api.catalog.not_found", key.Trim()), code = "catalog.not_found" });
            }
            if (!string.Equals(locked.Status, "published", StringComparison.Ordinal))
            {
                await tx.RollbackAsync(ct);
                return Conflict(new { error = this.T("api.template.not_published"), code = "template.not_published" });
            }
            if (request.ExpectedRevision is not null && locked.Revision != request.ExpectedRevision.Value)
            {
                await tx.RollbackAsync(ct);
                return Conflict(new
                {
                    error = this.T("api.template.revision_conflict",
                        locked.Revision.ToString(System.Globalization.CultureInfo.InvariantCulture),
                        request.ExpectedRevision.Value.ToString(System.Globalization.CultureInfo.InvariantCulture)),
                    code = "template.revision_conflict"
                });
            }

            newRevision = locked.Revision + 1;
            newVersionId = Guid.NewGuid();
            await c.ExecuteAsync(new CommandDefinition("""
                INSERT INTO odca.contract_template_versions(id, template_id, version_number, content_schema_version, content, fields, created_by, published_at)
                VALUES(@versionId, @id, @newVersionNumber, 1, @content::jsonb, @fields::jsonb, @actor, now());
                UPDATE odca.contract_templates
                   SET current_version=@newVersionNumber,
                       official_revision=official_revision+1,
                       row_version=row_version+1
                 WHERE id=@id;
                INSERT INTO odca.audit_events(scope_type, tenant_id, actor_user_id, action, entity_type, entity_id, result, metadata)
                VALUES('platform', NULL, @actor, 'platform.template.revision_published', 'contract_template', @id, 'success',
                       jsonb_build_object('officialKey',@key,'newRevision',@newRevision,'summary',@summary))
                """, new
                {
                    versionId = newVersionId,
                    id = locked.Id,
                    newVersionNumber = locked.CurrentVersion + 1,
                    content = contentJson,
                    fields = fieldsJson,
                    actor = actor.Value,
                    key = key.Trim(),
                    newRevision,
                    summary
                }, tx, cancellationToken: ct));
            await tx.CommitAsync(ct);
        }
        catch
        {
            await tx.RollbackAsync(ct);
            throw;
        }

        return Ok(new { officialKey = key.Trim(), newRevision, newVersionId });
    }

    private sealed record TemplateRevisionCurrentRow(
        string Name, string Description, string ContractType, string Status, int CurrentRevision, string Content, string Fields);

    private sealed record TemplateRevisionLockRow(Guid Id, string Status, int Revision, int CurrentVersion);

    [HttpGet("api/v1/platform/users")]
    public async Task<IActionResult> SearchUsers([FromQuery] string? search, [FromQuery] int limit = 50, CancellationToken ct = default)
    {
        var actor = Actor(); if (actor is null) return Unauthorized();
        await using var c = await dataSource.OpenConnectionAsync(ct);
        await EnsurePlatformGuc(c, actor.Value, ct);
        var rows = (await c.QueryAsync<PlatformUserSearchRow>(new CommandDefinition(
            "SELECT * FROM odca.platform_user_search(@actor,@search,@limit)",
            new { actor = actor.Value, search = string.IsNullOrWhiteSpace(search) ? null : search.Trim(), limit = Math.Clamp(limit, 1, 100) },
            cancellationToken: ct))).AsList();
        var grouped = new Dictionary<Guid, (PlatformUserRow User, List<PlatformUserLink> Links)>();
        foreach (var row in rows)
        {
            if (!grouped.TryGetValue(row.UserId, out var entry))
            {
                entry = (new PlatformUserRow(row.UserId, row.Email, row.DisplayName, row.IsPlatformAdministrator, row.IsDeleted, []), []);
                grouped[row.UserId] = entry;
            }
            entry.Links.Add(new PlatformUserLink(row.TenantId, row.TenantName, row.MembershipStatus, row.RoleCode));
        }
        var result = grouped.Values.Select(e => e.User with { Links = e.Links.ToArray() }).ToArray();
        return Ok(result);
    }

    [HttpGet("api/v1/platform/sla-policies")]
    public async Task<IActionResult> SlaPolicies(CancellationToken ct)
    {
        var actor = Actor(); if (actor is null) return Unauthorized();
        await using var c = await dataSource.OpenConnectionAsync(ct);
        var rows = (await c.QueryAsync<SlaPolicyRow>(new CommandDefinition("""
            SELECT plan_code AS PlanCode, service AS Service, priority AS Priority,
                   first_response_minutes AS FirstResponseMinutes, resolution_minutes AS ResolutionMinutes,
                   timezone AS Timezone, calendar AS Calendar,
                   to_char(business_start,'HH24:MI') AS BusinessStart, to_char(business_end,'HH24:MI') AS BusinessEnd,
                   enabled AS Enabled
              FROM odca.sla_policies
             ORDER BY plan_code, service, priority DESC
            """, cancellationToken: ct))).AsList();
        return Ok(rows);
    }

    [HttpPost("api/v1/platform/templates")]
    public async Task<IActionResult> CreateTemplate([FromBody] CreateOfficialTemplateRequest request, CancellationToken ct)
    {
        var actor = Actor(); if (actor is null) return Unauthorized();
        if (string.IsNullOrWhiteSpace(request.Name) || request.Name.Trim().Length < 3) return this.Invalid("api.platform_catalog.name_invalid");
        if (string.IsNullOrWhiteSpace(request.ContractType)) return this.Invalid("api.platform_catalog.contract_type_required");

        var validTypes = new[] { "nda", "services", "service", "services-multiple-therapies", "supply", "lease", "consent", "amendment" };
        if (!validTypes.Contains(request.ContractType.ToLowerInvariant()))
            return this.Invalid("api.platform_catalog.type_invalid", string.Join(", ", validTypes));

        var officialKey = string.IsNullOrWhiteSpace(request.OfficialKey)
            ? request.Name.Trim().ToLowerInvariant().Replace(' ', '-')
            : request.OfficialKey.Trim().ToLowerInvariant();

        // Check official_key uniqueness among global templates
        await using var c = await dataSource.OpenConnectionAsync(ct);
        await EnsurePlatformGuc(c, actor.Value, ct);
        var keyExists = await c.ExecuteScalarAsync<bool>(new CommandDefinition(
            "SELECT EXISTS(SELECT 1 FROM odca.contract_templates WHERE owner_tenant_id IS NULL AND scope='global' AND official_key=@key AND archived_at IS NULL)",
            new { key = officialKey }, cancellationToken: ct));
        if (keyExists)
            return Conflict(new { error = this.T("api.template.key_exists", officialKey), code = "template.key_exists" });

        var templateId = Guid.NewGuid();
        var versionId = Guid.NewGuid();
        await using var tx = await c.BeginTransactionAsync(ct);
        try
        {
            await c.ExecuteAsync(new CommandDefinition(@"
                INSERT INTO odca.contract_templates(id, owner_tenant_id, name, description, contract_type, scope, status, current_version, author_id, official_key, document_purpose)
                VALUES(@templateId, NULL, @name, @description, @contractType, 'global', 'draft', 1, @actor, @officialKey, @purpose);
                INSERT INTO odca.contract_template_versions(id, template_id, version_number, content_schema_version, content, fields, created_by)
                VALUES(@versionId, @templateId, 1, 1, @content::jsonb, @fields::jsonb, @actor);
                INSERT INTO odca.audit_events(scope_type, tenant_id, actor_user_id, action, entity_type, entity_id, result, metadata)
                VALUES('platform', NULL, @actor, 'platform.template.created', 'contract_template', @templateId, 'success', jsonb_build_object('name',@name,'type',@contractType,'officialKey',@officialKey))
            ", new { templateId, versionId, name = request.Name.Trim(), description = string.IsNullOrWhiteSpace(request.Description) ? null : request.Description.Trim(), contractType = request.ContractType.ToLowerInvariant(), actor = actor.Value, officialKey = officialKey, purpose = string.IsNullOrWhiteSpace(request.DocumentPurpose) ? null : request.DocumentPurpose.Trim(), content = request.Content, fields = request.Fields }, tx, cancellationToken: ct));
            await tx.CommitAsync(ct);
        }
        catch { await tx.RollbackAsync(ct); throw; }

        return Created($"api/v1/platform/templates/{templateId}", new { id = templateId, officialKey, status = "draft", message = "Modelo criado em rascunho. Publique para torná-lo disponível." });
    }

    private static async Task EnsurePlatformGuc(NpgsqlConnection c, Guid actor, CancellationToken ct) =>
        await c.ExecuteAsync(new CommandDefinition("SELECT set_config('odca.user_id',@actor::text,false)", new { actor }, cancellationToken: ct));

    private Guid? Actor() => Guid.TryParse(User.FindFirstValue("sub"), out var id) ? id : null;

    private sealed record TemplateCatalogRow(Guid TemplateId, string OfficialKey, string Name, string Description, string ContractType, string Status, int CurrentRevision, DateTime CreatedAt, DateTime? PublishedAt);
    private sealed record TemplateCatalogVersionRow(Guid TemplateId, int VersionNumber, string? CreatedAt, string? PublishedAt);
    private sealed record PlatformUserSearchRow(Guid UserId, string Email, string DisplayName, bool IsPlatformAdministrator, bool IsDeleted, Guid? TenantId, string? TenantName, string? MembershipStatus, string? RoleCode);
}
