using System.Security.Claims;
using System.Text.Json;
using Dapper;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Npgsql;
using Odca.Contracts.SavedViews;

namespace Odca.Api.Controllers;

[ApiController]
[Authorize(Policy = "PasswordChanged")]
[Route("api/v1/organizations/{tenantId:guid}/saved-views")]
public sealed class SavedViewsController(NpgsqlDataSource dataSource) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> List(Guid tenantId, [FromQuery] string listingType = "obligations", CancellationToken ct = default)
    {
        var actor = Actor();
        if (actor is null) return Unauthorized();
        if (!SavedViewFilterPolicy.IsListingType(listingType)) return ValidationProblem("Tipo de listagem inválido.");
        await using var connection = await dataSource.OpenConnectionAsync(ct);
        if (!await Allowed(connection, actor.Value, tenantId, ct)) return Forbid();
        await using var transaction = await connection.BeginTransactionAsync(ct);
        await SetContext(connection, tenantId, actor.Value, transaction, ct);
        var rows = await connection.QueryAsync<Row>(new CommandDefinition("""
            SELECT id AS "Id",name AS "Name",listing_type AS "ListingType",filters::text AS "Filters",sort AS "Sort",
                   is_default AS "IsDefault",row_version AS "Version",created_at AS "CreatedAt",updated_at AS "UpdatedAt"
              FROM odca.saved_work_views
             WHERE tenant_id=@tenantId AND owner_id=@actor AND listing_type=@listingType AND inactive_at IS NULL
             ORDER BY is_default DESC,name,id
            """, new { tenantId, actor, listingType }, transaction, cancellationToken: ct));
        await transaction.CommitAsync(ct);
        return Ok(rows.Select(ToItem));
    }

    [HttpGet("{id:guid}")]
    public async Task<IActionResult> Get(Guid tenantId, Guid id, CancellationToken ct)
    {
        var actor = Actor();
        if (actor is null) return Unauthorized();
        await using var connection = await dataSource.OpenConnectionAsync(ct);
        if (!await Allowed(connection, actor.Value, tenantId, ct)) return Forbid();
        await using var transaction = await connection.BeginTransactionAsync(ct);
        await SetContext(connection, tenantId, actor.Value, transaction, ct);
        var row = await connection.QuerySingleOrDefaultAsync<Row>(new CommandDefinition("""
            SELECT id AS "Id",name AS "Name",listing_type AS "ListingType",filters::text AS "Filters",sort AS "Sort",
                   is_default AS "IsDefault",row_version AS "Version",created_at AS "CreatedAt",updated_at AS "UpdatedAt"
              FROM odca.saved_work_views
             WHERE tenant_id=@tenantId AND owner_id=@actor AND id=@id AND inactive_at IS NULL
            """, new { tenantId, actor, id }, transaction, cancellationToken: ct));
        if (row is null) return NotFound();
        var filters = ParseAndValidate(row.ListingType, row.Filters, row.Sort);
        if (!await IdentifiersAvailable(connection, tenantId, filters, transaction, ct))
            return Conflict(new ProblemDetails { Title = "A vista usa um contrato ou responsável que não está mais disponível." });
        await transaction.CommitAsync(ct);
        return Ok(ToItem(row));
    }

    [HttpPost]
    public async Task<IActionResult> Create(Guid tenantId, [FromBody] SaveViewRequest request, CancellationToken ct)
    {
        var actor = Actor();
        if (actor is null) return Unauthorized();
        var validation = Validate(request, out var normalizedFilters, out var normalizedSort);
        if (validation is not null) return ValidationProblem(validation);
        await using var connection = await dataSource.OpenConnectionAsync(ct);
        if (!await Allowed(connection, actor.Value, tenantId, ct)) return Forbid();
        await using var transaction = await connection.BeginTransactionAsync(ct);
        await SetContext(connection, tenantId, actor.Value, transaction, ct);
        if (!await IdentifiersAvailable(connection, tenantId, normalizedFilters, transaction, ct))
            return ValidationProblem("Contrato ou responsável indisponível para esta organização.");
        var row = await connection.QuerySingleAsync<Row>(new CommandDefinition("""
            INSERT INTO odca.saved_work_views(tenant_id,owner_id,name,listing_type,filters,sort)
            VALUES(@tenantId,@actor,@name,@listingType,CAST(@filters AS jsonb),@sort)
            RETURNING id AS "Id",name AS "Name",listing_type AS "ListingType",filters::text AS "Filters",sort AS "Sort",
                      is_default AS "IsDefault",row_version AS "Version",created_at AS "CreatedAt",updated_at AS "UpdatedAt"
            """, new { tenantId, actor, name = request.Name.Trim(), request.ListingType, filters = JsonSerializer.Serialize(normalizedFilters), sort = normalizedSort }, transaction, cancellationToken: ct));
        await transaction.CommitAsync(ct);
        return CreatedAtAction(nameof(Get), new { tenantId, id = row.Id }, ToItem(row));
    }

    [HttpPut("{id:guid}")]
    public async Task<IActionResult> Update(Guid tenantId, Guid id, [FromBody] UpdateSavedViewRequest request, CancellationToken ct)
    {
        var actor = Actor();
        if (actor is null) return Unauthorized();
        if (string.IsNullOrWhiteSpace(request.Name) || request.Name.Trim().Length > 80)
            return ValidationProblem("O nome deve ter entre 1 e 80 caracteres.");
        await using var connection = await dataSource.OpenConnectionAsync(ct);
        if (!await Allowed(connection, actor.Value, tenantId, ct)) return Forbid();
        await using var transaction = await connection.BeginTransactionAsync(ct);
        await SetContext(connection, tenantId, actor.Value, transaction, ct);
        var current = await connection.QuerySingleOrDefaultAsync<(string ListingType, long Version)>(new CommandDefinition(
            "SELECT listing_type AS ListingType,row_version AS Version FROM odca.saved_work_views WHERE tenant_id=@tenantId AND owner_id=@actor AND id=@id AND inactive_at IS NULL FOR UPDATE",
            new { tenantId, actor, id }, transaction, cancellationToken: ct));
        if (current == default) return NotFound();
        if (current.Version != request.Version) return Conflict(new { title = "A vista foi alterada em outra sessão.", currentVersion = current.Version });
        Dictionary<string, string>? normalizedFilters = null;
        string? normalizedSort = null;
        if (request.Filters is not null && !SavedViewFilterPolicy.TryNormalize(current.ListingType, request.Filters, request.Sort, out normalizedFilters, out normalizedSort, out var error))
            return ValidationProblem(error);
        if (normalizedFilters is not null && !await IdentifiersAvailable(connection, tenantId, normalizedFilters, transaction, ct))
            return ValidationProblem("Contrato ou responsável indisponível para esta organização.");
        if (request.IsDefault)
            await connection.ExecuteAsync(new CommandDefinition("UPDATE odca.saved_work_views SET is_default=false,updated_at=now(),row_version=row_version+1 WHERE tenant_id=@tenantId AND owner_id=@actor AND listing_type=@listingType AND is_default", new { tenantId, actor, current.ListingType }, transaction, cancellationToken: ct));
        await connection.ExecuteAsync(new CommandDefinition("UPDATE odca.saved_work_views SET name=@name,is_default=@isDefault,filters=COALESCE(CAST(@filters AS jsonb),filters),sort=COALESCE(@sort,sort),updated_at=now(),row_version=row_version+1 WHERE tenant_id=@tenantId AND owner_id=@actor AND id=@id", new { tenantId, actor, id, name = request.Name.Trim(), request.IsDefault, filters = normalizedFilters is null ? null : JsonSerializer.Serialize(normalizedFilters), sort = normalizedSort }, transaction, cancellationToken: ct));
        await transaction.CommitAsync(ct);
        return NoContent();
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Deactivate(Guid tenantId, Guid id, CancellationToken ct)
    {
        var actor = Actor();
        if (actor is null) return Unauthorized();
        await using var connection = await dataSource.OpenConnectionAsync(ct);
        if (!await Allowed(connection, actor.Value, tenantId, ct)) return Forbid();
        await using var transaction = await connection.BeginTransactionAsync(ct);
        await SetContext(connection, tenantId, actor.Value, transaction, ct);
        var changed = await connection.ExecuteAsync(new CommandDefinition("UPDATE odca.saved_work_views SET inactive_at=now(),is_default=false,updated_at=now(),row_version=row_version+1 WHERE tenant_id=@tenantId AND owner_id=@actor AND id=@id AND inactive_at IS NULL", new { tenantId, actor, id }, transaction, cancellationToken: ct));
        if (changed == 0) return NotFound();
        await transaction.CommitAsync(ct);
        return NoContent();
    }

    private static string? Validate(SaveViewRequest request, out Dictionary<string, string> filters, out string sort)
    {
        filters = [];
        sort = request.Sort ?? "due_date";
        if (string.IsNullOrWhiteSpace(request.Name) || request.Name.Trim().Length > 80) return "O nome deve ter entre 1 e 80 caracteres.";
        return SavedViewFilterPolicy.TryNormalize(request.ListingType, request.Filters, request.Sort, out filters, out sort, out var error) ? null : error;
    }

    private static Dictionary<string, string> ParseAndValidate(string listingType, string json, string sort)
    {
        var filters = JsonSerializer.Deserialize<Dictionary<string, string>>(json) ?? [];
        if (!SavedViewFilterPolicy.TryNormalize(listingType, filters, sort, out var normalized, out _, out _))
            throw new InvalidDataException("Definição de vista inválida.");
        return normalized;
    }

    private static async Task<bool> IdentifiersAvailable(NpgsqlConnection connection, Guid tenantId, Dictionary<string, string> filters, NpgsqlTransaction transaction, CancellationToken ct)
    {
        if (filters.TryGetValue("contractId", out var contractText) && (!Guid.TryParse(contractText, out var contractId) || !await connection.ExecuteScalarAsync<bool>(new CommandDefinition("SELECT EXISTS(SELECT 1 FROM odca.contracts WHERE tenant_id=@tenantId AND id=@contractId AND deleted_at IS NULL)", new { tenantId, contractId }, transaction, cancellationToken: ct)))) return false;
        if (filters.TryGetValue("ownerId", out var ownerText) && (!Guid.TryParse(ownerText, out var ownerId) || !await connection.ExecuteScalarAsync<bool>(new CommandDefinition("SELECT EXISTS(SELECT 1 FROM odca.memberships WHERE tenant_id=@tenantId AND user_id=@ownerId AND status='active')", new { tenantId, ownerId }, transaction, cancellationToken: ct)))) return false;
        return true;
    }

    private Guid? Actor() => Guid.TryParse(User.FindFirstValue("sub"), out var id) ? id : null;
    private static Task<bool> Allowed(NpgsqlConnection c, Guid actor, Guid tenant, CancellationToken ct) => c.ExecuteScalarAsync<bool>(new CommandDefinition("SELECT odca.tenant_actor_has_permission(@actor,@tenant,'tenant.saved_views.manage')", new { actor, tenant }, cancellationToken: ct));
    private static Task<int> SetContext(NpgsqlConnection c, Guid tenant, Guid actor, NpgsqlTransaction tx, CancellationToken ct) => c.ExecuteAsync(new CommandDefinition("SELECT set_config('odca.tenant_id',@tenantValue,true),set_config('odca.actor_id',@actorValue,true)", new { tenantValue = tenant.ToString(), actorValue = actor.ToString() }, tx, cancellationToken: ct));
    private static SavedViewItem ToItem(Row row) => new(row.Id, row.Name, row.ListingType, ParseAndValidate(row.ListingType, row.Filters, row.Sort), row.Sort, row.IsDefault, row.Version, new DateTimeOffset(row.CreatedAt), new DateTimeOffset(row.UpdatedAt));

    // Npgsql exposes PostgreSQL timestamp columns as DateTime. A mutable row avoids
    // Dapper requiring an exact positional constructor (including that provider type).
    private sealed class Row
    {
        public Guid Id { get; set; }
        public string Name { get; set; } = "";
        public string ListingType { get; set; } = "";
        public string Filters { get; set; } = "{}";
        public string Sort { get; set; } = "";
        public bool IsDefault { get; set; }
        public long Version { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime UpdatedAt { get; set; }
    }
}
