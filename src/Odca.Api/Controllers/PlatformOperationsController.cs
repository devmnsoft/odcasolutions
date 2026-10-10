using System.Security.Claims;
using System.Text.Json;
using Dapper;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Npgsql;
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
        if (string.IsNullOrWhiteSpace(request.Action)) return ValidationProblem();
        await using var c = await dataSource.OpenConnectionAsync(ct);
        await EnsurePlatformGuc(c, actor.Value, ct);
        var payload = await c.ExecuteScalarAsync<string>(new CommandDefinition(
            "SELECT odca.platform_catalog_set_status(@actor,@key,@action,@reason)::text",
            new { actor = actor.Value, key = key.Trim(), action = request.Action.Trim().ToLowerInvariant(), reason = string.IsNullOrWhiteSpace(request.Reason) ? null : request.Reason.Trim() },
            cancellationToken: ct));
        if (payload is null) return StatusCode(500, new ProblemDetails { Title = "A operação não produziu resultado.", Status = 500 });
        using var doc = JsonDocument.Parse(payload);
        var root = doc.RootElement;
        if (root.TryGetProperty("code", out var codeEl))
        {
            var code = codeEl.GetString() ?? "catalog.error";
            var error = root.TryGetProperty("error", out var errEl) ? errEl.GetString() ?? "Operação inválida." : "Operação inválida.";
            return code switch
            {
                "catalog.not_found" => new ObjectResult(new ProblemDetails { Title = error, Status = 404, Detail = code }) { StatusCode = 404 },
                _ => new ObjectResult(new ProblemDetails { Title = error, Status = 400, Detail = code }) { StatusCode = 400 }
            };
        }
        return Ok(root.Clone());
    }

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

    private static async Task EnsurePlatformGuc(NpgsqlConnection c, Guid actor, CancellationToken ct) =>
        await c.ExecuteAsync(new CommandDefinition("SELECT set_config('odca.user_id',@actor::text,false)", new { actor }, cancellationToken: ct));

    private Guid? Actor() => Guid.TryParse(User.FindFirstValue("sub"), out var id) ? id : null;

    private sealed record TemplateCatalogRow(Guid TemplateId, string OfficialKey, string Name, string Description, string ContractType, string Status, int CurrentRevision, DateTime CreatedAt, DateTime? PublishedAt);
    private sealed record TemplateCatalogVersionRow(Guid TemplateId, int VersionNumber, string? CreatedAt, string? PublishedAt);
    private sealed record PlatformUserSearchRow(Guid UserId, string Email, string DisplayName, bool IsPlatformAdministrator, bool IsDeleted, Guid? TenantId, string? TenantName, string? MembershipStatus, string? RoleCode);
}
