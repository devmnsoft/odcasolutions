using System.Security.Claims;
using System.Text.Json;
using Dapper;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Npgsql;
using Odca.Contracts.Solicitations;

namespace Odca.Api.Controllers;

/// <summary>
/// Seção C: solicitações cliente -> ODCA (revisao/adaptacao/esclarecimento) com maquina de
/// estados e relogio de SLA. Toda a logica de negocio vive nas funcoes SECURITY DEFINER
/// odca.solicitations_*; o controlador so autentica, autoriza e mapeia envelopes de erro.
/// </summary>
[ApiController]
[Authorize(Policy = "PasswordChanged")]
[Route("api/v1/organizations/{tenantId:guid}/solicitations")]
public sealed class SolicitationsController(NpgsqlDataSource dataSource) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> List(Guid tenantId, [FromQuery] string? service, [FromQuery] string? status, [FromQuery] string? search, CancellationToken ct)
    {
        var actor = Actor(); if (actor is null) return Unauthorized();
        await using var c = await dataSource.OpenConnectionAsync(ct);
        if (!await Allowed(c, actor.Value, tenantId, "tenant.solicitations.read", ct)) return Forbid();
        var rows = await c.QueryAsync<SolicitationListRow>(new CommandDefinition(
            "SELECT * FROM odca.solicitations_list(@actor,@tenant,@service,@status,@search)",
            new { actor, tenant = tenantId, service = string.IsNullOrWhiteSpace(service) ? null : service.Trim(), status = string.IsNullOrWhiteSpace(status) ? null : status.Trim(), search = string.IsNullOrWhiteSpace(search) ? null : search.Trim() },
            cancellationToken: ct));
        return Ok(rows);
    }

    [HttpGet("{solicitationId:guid}")]
    public async Task<IActionResult> Detail(Guid tenantId, Guid solicitationId, CancellationToken ct)
    {
        var actor = Actor(); if (actor is null) return Unauthorized();
        await using var c = await dataSource.OpenConnectionAsync(ct);
        if (!await Allowed(c, actor.Value, tenantId, "tenant.solicitations.read", ct)) return Forbid();
        return await EnvelopeResult(c, "SELECT odca.solicitation_detail(@actor,@solicitation)::text", new { actor, solicitation = solicitationId }, ct);
    }

    [HttpPost]
    public async Task<IActionResult> Open(Guid tenantId, [FromBody] OpenSolicitationRequest request, CancellationToken ct)
    {
        var actor = Actor(); if (actor is null) return Unauthorized();
        if (string.IsNullOrWhiteSpace(request.Service) || string.IsNullOrWhiteSpace(request.Subject) || string.IsNullOrWhiteSpace(request.Body) || request.Body.Trim().Length < 10 || request.IdempotencyKey == Guid.Empty) return ValidationProblem();
        await using var c = await dataSource.OpenConnectionAsync(ct);
        if (!await Allowed(c, actor.Value, tenantId, "tenant.solicitations.manage", ct)) return Forbid();
        return await EnvelopeResult(c, "SELECT odca.solicitations_open(@tenant,@actor,@service,@priority,@subject,@body,@key)::text",
            new { tenant = tenantId, actor, service = request.Service.Trim(), priority = string.IsNullOrWhiteSpace(request.Priority) ? "normal" : request.Priority.Trim(), subject = request.Subject.Trim(), body = request.Body.Trim(), key = request.IdempotencyKey }, ct);
    }

    [HttpPost("{solicitationId:guid}/messages")]
    public async Task<IActionResult> Message(Guid tenantId, Guid solicitationId, [FromBody] SolicitationMessageRequest request, CancellationToken ct)
    {
        var actor = Actor(); if (actor is null) return Unauthorized();
        if (string.IsNullOrWhiteSpace(request.Body) || request.Body.Trim().Length < 2) return ValidationProblem();
        await using var c = await dataSource.OpenConnectionAsync(ct);
        if (!await Allowed(c, actor.Value, tenantId, "tenant.solicitations.manage", ct)) return Forbid();
        return await EnvelopeResult(c, "SELECT odca.solicitations_message(@tenant,@actor,@solicitation,@body,@version)::text",
            new { tenant = tenantId, actor, solicitation = solicitationId, body = request.Body.Trim(), version = request.RowVersion }, ct);
    }

    [HttpPost("{solicitationId:guid}/actions")]
    public async Task<IActionResult> Action(Guid tenantId, Guid solicitationId, [FromBody] SolicitationActionRequest request, CancellationToken ct)
    {
        var actor = Actor(); if (actor is null) return Unauthorized();
        if (string.IsNullOrWhiteSpace(request.Action)) return ValidationProblem();
        await using var c = await dataSource.OpenConnectionAsync(ct);
        if (!await Allowed(c, actor.Value, tenantId, "tenant.solicitations.manage", ct)) return Forbid();
        return await EnvelopeResult(c, "SELECT odca.solicitations_transition(@tenant,@actor,@solicitation,@action,@text,@priority,@assignee,@version)::text",
            new { tenant = tenantId, actor, solicitation = solicitationId, action = request.Action.Trim(), text = string.IsNullOrWhiteSpace(request.Text) ? null : request.Text.Trim(), priority = string.IsNullOrWhiteSpace(request.Priority) ? null : request.Priority.Trim(), assignee = request.AssigneeUserId, version = request.RowVersion }, ct);
    }

    /// <summary>Executa uma funcao definer que devolve payload OU {"error","code"} e mapeia o
    /// envelope para o status HTTP correspondente.</summary>
    internal static async Task<IActionResult> EnvelopeResult(NpgsqlConnection c, string sql, object parameters, CancellationToken ct)
    {
        var payload = await c.ExecuteScalarAsync<string>(new CommandDefinition(sql + "::text", parameters, cancellationToken: ct));
        if (payload is null) return new ObjectResult(new ProblemDetails { Title = "A operacao nao produziu resultado.", Status = 500 }) { StatusCode = 500 };
        using var doc = JsonDocument.Parse(payload);
        if (doc.RootElement.ValueKind == JsonValueKind.Object && doc.RootElement.TryGetProperty("code", out var codeEl))
        {
            var code = codeEl.GetString() ?? "solicitations.error";
            var error = doc.RootElement.TryGetProperty("error", out var errEl) ? errEl.GetString() ?? "Operacao invalida." : "Operacao invalida.";
            long? current = doc.RootElement.TryGetProperty("currentRowVersion", out var cv) && cv.ValueKind == JsonValueKind.Number ? cv.GetInt64() : null;
            IActionResult Problem(int status) => new ObjectResult(new ProblemDetails { Title = error, Status = status, Detail = code }) { StatusCode = status };
            return code switch
            {
                "solicitations.not_found" => Problem(StatusCodes.Status404NotFound),
                "solicitations.permission" or "solicitations.permission_odca" or "solicitations.permission_platform" => Problem(StatusCodes.Status403Forbidden),
                "solicitations.stale" => new ObjectResult(new ProblemDetails { Title = error, Status = StatusCodes.Status409Conflict, Detail = code, Extensions = { ["currentRowVersion"] = current } }) { StatusCode = StatusCodes.Status409Conflict },
                "solicitations.plan.required" => Problem(StatusCodes.Status409Conflict),
                "solicitations.transition" => Problem(StatusCodes.Status409Conflict),
                _ => Problem(StatusCodes.Status400BadRequest)
            };
        }
        return new OkObjectResult(doc.RootElement.Clone());
    }

    internal static async Task<bool> Allowed(NpgsqlConnection c, Guid actor, Guid tenantId, string permission, CancellationToken ct) =>
        await c.ExecuteScalarAsync<bool>(new CommandDefinition("SELECT odca.tenant_actor_has_permission(@actor,@tenantId,@permission)", new { actor, tenantId, permission }, cancellationToken: ct));

    private Guid? Actor() => Guid.TryParse(User.FindFirstValue("sub"), out var id) ? id : null;

    internal sealed record SolicitationListRow(Guid Id, Guid TenantId, string OrganizationName, string Protocol, string Service, string Priority,
        string Status, string Subject, DateTime? OpenedAt, DateTime? UpdatedAt, string? AssigneeName, DateTime? FirstResponseDueAt,
        DateTime? ResolutionDueAt, DateTime? FirstResponseBreachAt, DateTime? ResolutionBreachAt, bool Paused, DateTime? ResolvedAt,
        DateTime? ClosedAt, DateTime? CancelledAt, string? CancellationReason);
}
