using System.Security.Claims;
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
public sealed class ContractStudioCatalogController(NpgsqlDataSource dataSource) : ControllerBase
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() }
    };

    [HttpGet("templates/{templateId:guid}")]
    public async Task<IActionResult> Template(Guid tenantId, Guid templateId, CancellationToken ct)
    {
        var actor = Actor(); if (actor is null) return Unauthorized();
        await using var connection = await dataSource.OpenConnectionAsync(ct);
        if (!await Allowed(connection, actor.Value, tenantId, "tenant.templates.read", ct)) return Forbid();
        var row = await connection.QuerySingleOrDefaultAsync<TemplatePreviewRow>(new CommandDefinition("""
            SELECT t.id AS Id, t.name AS Name, t.description AS Description, t.contract_type AS ContractType,
                   t.scope AS Scope, t.status AS Status, t.current_version AS Version, v.fields::text AS Fields
              FROM odca.contract_templates t
              JOIN odca.contract_template_versions v ON v.template_id = t.id AND v.version_number = t.current_version
             WHERE t.id = @templateId AND t.status = 'published'
               AND (t.scope = 'global' OR t.owner_tenant_id = @tenantId OR EXISTS (
                    SELECT 1 FROM odca.contract_template_access a
                     WHERE a.template_id = t.id AND a.tenant_id = @tenantId AND a.revoked_at IS NULL))
            """, new { templateId, tenantId }, cancellationToken: ct));
        if (row is null) return NotFound();
        var fields = JsonSerializer.Deserialize<JsonElement>(row.Fields);
        var labels = JsonSerializer.Deserialize<ContractFieldDefinition[]>(row.Fields, JsonOptions)?
            .Select(item => item.Label).ToArray() ?? [];
        return Ok(new TemplatePreview(row.Id, row.Name, row.Description, row.ContractType, row.Scope, row.Status, row.Version, fields, labels));
    }

    [HttpPost("templates/official")]
    public async Task<IActionResult> InstallOfficial(Guid tenantId, CancellationToken ct)
    {
        var actor = Actor(); if (actor is null) return Unauthorized();
        OfficialContractTemplates.EnsureValid();
        await using var connection = await dataSource.OpenConnectionAsync(ct);
        if (!await Allowed(connection, actor.Value, tenantId, "tenant.templates.manage", ct)) return Forbid();
        await using var tx = await connection.BeginTransactionAsync(ct);
        await connection.ExecuteAsync(new CommandDefinition(
            "SELECT set_config('odca.tenant_id',@value,true),set_config('odca.actor_id',@actor,true)",
            new { value = tenantId.ToString(), actor = actor.Value.ToString() }, tx, cancellationToken: ct));
        var installed = 0;
        var present = 0;
        var names = new List<string>();
        foreach (var template in OfficialContractTemplates.All)
        {
            var exists = await connection.ExecuteScalarAsync<bool>(new CommandDefinition("""
                SELECT EXISTS(
                    SELECT 1 FROM odca.contract_templates
                     WHERE name = @name AND status <> 'archived'
                       AND (scope = 'global' OR owner_tenant_id = @tenantId))
                """, new { template.Name, tenantId }, tx, cancellationToken: ct));
            if (exists) { present++; continue; }
            var id = Guid.NewGuid();
            var versionId = Guid.NewGuid();
            var fields = OfficialContractTemplates.SerializeFields(template.Fields);
            await connection.ExecuteAsync(new CommandDefinition("""
                INSERT INTO odca.contract_templates(id,owner_tenant_id,name,description,contract_type,scope,status,author_id,published_at)
                VALUES(@id,@tenantId,@name,@description,@contractType,'private','published',@actor,now());
                INSERT INTO odca.contract_template_versions(id,template_id,version_number,content,fields,created_by,published_at)
                VALUES(@versionId,@id,1,@content::jsonb,@fields::jsonb,@actor,now());
                INSERT INTO odca.audit_events(scope_type,tenant_id,actor_user_id,action,entity_type,entity_id,result,metadata)
                VALUES('tenant',@tenantId,@actor,'template.official_installed','contract_template',@id,'success',jsonb_build_object('key',@key));
                """, new { id, tenantId, name = template.Name, template.Description, template.ContractType, actor = actor.Value, versionId, content = template.Content, fields, key = template.Key }, tx, cancellationToken: ct));
            installed++;
            names.Add(template.Name);
        }

        await tx.CommitAsync(ct);
        return Ok(new OfficialTemplateInstallResponse(installed, present, names));
    }

    private Guid? Actor() => Guid.TryParse(User.FindFirstValue("sub"), out var id) ? id : null;

    private static Task<bool> Allowed(NpgsqlConnection connection, Guid actor, Guid tenant, string permission, CancellationToken ct) =>
        connection.ExecuteScalarAsync<bool>(new CommandDefinition(
            "SELECT odca.tenant_actor_has_permission(@actor,@tenant,@permission)",
            new { actor, tenant, permission }, cancellationToken: ct));

    private sealed record TemplatePreviewRow(Guid Id, string Name, string? Description, string ContractType, string Scope, string Status, int Version, string Fields);
}
