using System.Security.Claims;
using Dapper;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Npgsql;
using Odca.Application.Contracts;
using Odca.Contracts.Solicitations;

namespace Odca.Api.Controllers;

/// <summary>
/// Seção C: fila da plataforma (Central ODCA) para solicitações de clientes e aprovação de
/// modelos cirúrgicos oficiais. As funcoes definer validam o ator de plataforma internamente
/// (assert_platform_actor), por isso o GUC odca.user_id precisa ser definido antes das chamadas.
/// </summary>
[ApiController]
[Authorize(Policy = "PlatformAdministrator")]
public sealed class PlatformSolicitationsController(NpgsqlDataSource dataSource) : ControllerBase
{
    [HttpGet("api/v1/platform/solicitations")]
    public async Task<IActionResult> List([FromQuery] Guid? tenantId, [FromQuery] string? service, [FromQuery] string? status, [FromQuery] string? search, CancellationToken ct)
    {
        var actor = Actor(); if (actor is null) return Unauthorized();
        await using var c = await dataSource.OpenConnectionAsync(ct);
        var rows = await c.QueryAsync<SolicitationsController.SolicitationListRow>(new CommandDefinition(
            "SELECT * FROM odca.solicitations_list(@actor,@tenant,@service,@status,@search)",
            new { actor = actor.Value, tenant = tenantId, service = string.IsNullOrWhiteSpace(service) ? null : service.Trim(), status = string.IsNullOrWhiteSpace(status) ? null : status.Trim(), search = string.IsNullOrWhiteSpace(search) ? null : search.Trim() },
            cancellationToken: ct));
        return Ok(rows);
    }

    [HttpGet("api/v1/platform/solicitations/{solicitationId:guid}")]
    public async Task<IActionResult> Detail(Guid solicitationId, CancellationToken ct)
    {
        var actor = Actor(); if (actor is null) return Unauthorized();
        await using var c = await dataSource.OpenConnectionAsync(ct);
        return await SolicitationsController.EnvelopeResult(c, "SELECT odca.solicitation_detail(@actor,@solicitation)", new { actor = actor.Value, solicitation = solicitationId }, ct);
    }

    [HttpPost("api/v1/platform/solicitations/{solicitationId:guid}/messages")]
    public async Task<IActionResult> Message(Guid solicitationId, [FromBody] SolicitationMessageRequest request, CancellationToken ct)
    {
        var actor = Actor(); if (actor is null) return Unauthorized();
        if (string.IsNullOrWhiteSpace(request.Body) || request.Body.Trim().Length < 2 || request.TenantId is null) return ValidationProblem();
        await using var c = await dataSource.OpenConnectionAsync(ct);
        return await SolicitationsController.EnvelopeResult(c, "SELECT odca.solicitations_message(@tenant,@actor,@solicitation,@body,@version)",
            new { tenant = request.TenantId.Value, actor = actor.Value, solicitation = solicitationId, body = request.Body.Trim(), version = request.RowVersion }, ct);
    }

    [HttpPost("api/v1/platform/solicitations/{solicitationId:guid}/actions")]
    public async Task<IActionResult> Action(Guid solicitationId, [FromBody] SolicitationActionRequest request, CancellationToken ct)
    {
        var actor = Actor(); if (actor is null) return Unauthorized();
        if (string.IsNullOrWhiteSpace(request.Action) || request.TenantId is null) return ValidationProblem();
        await using var c = await dataSource.OpenConnectionAsync(ct);
        return await SolicitationsController.EnvelopeResult(c, "SELECT odca.solicitations_transition(@tenant,@actor,@solicitation,@action,@text,@priority,@assignee,@version)",
            new { tenant = request.TenantId.Value, actor = actor.Value, solicitation = solicitationId, action = request.Action.Trim(), text = string.IsNullOrWhiteSpace(request.Text) ? null : request.Text.Trim(), priority = string.IsNullOrWhiteSpace(request.Priority) ? null : request.Priority.Trim(), assignee = request.AssigneeUserId, version = request.RowVersion }, ct);
    }

    [HttpGet("api/v1/platform/template-approvals")]
    public async Task<IActionResult> ApprovalsQueue(CancellationToken ct)
    {
        var actor = Actor(); if (actor is null) return Unauthorized();
        await using var c = await dataSource.OpenConnectionAsync(ct);
        await EnsurePlatformGuc(c, actor.Value, ct);
        var rows = await c.QueryAsync<ApprovalQueueRow>(new CommandDefinition(
            "SELECT * FROM odca.template_approvals_queue(@actor,@keys)", new { actor = actor.Value, keys = OfficialContractTemplates.ApprovalRequiredKeys.ToArray() }, cancellationToken: ct));
        return Ok(rows);
    }

    [HttpPost("api/v1/platform/template-approvals/{tenantId:guid}/{key}/decision")]
    public async Task<IActionResult> Decide(Guid tenantId, string key, [FromBody] ApprovalDecisionRequest request, CancellationToken ct)
    {
        var actor = Actor(); if (actor is null) return Unauthorized();
        if (string.IsNullOrWhiteSpace(request.Decision)) return ValidationProblem();
        await using var c = await dataSource.OpenConnectionAsync(ct);
        await EnsurePlatformGuc(c, actor.Value, ct);
        return await SolicitationsController.EnvelopeResult(c, "SELECT odca.template_approval_decide(@actor,@tenant,@key,@decision,@note)",
            new { actor = actor.Value, tenant = tenantId, key = key.Trim(), decision = request.Decision.Trim(), note = string.IsNullOrWhiteSpace(request.Note) ? null : request.Note.Trim() }, ct);
    }

    private static async Task EnsurePlatformGuc(NpgsqlConnection c, Guid actor, CancellationToken ct) =>
        await c.ExecuteAsync(new CommandDefinition("SELECT set_config('odca.user_id',@actor::text,false)", new { actor }, cancellationToken: ct));

    private Guid? Actor() => Guid.TryParse(User.FindFirstValue("sub"), out var id) ? id : null;

    // Asse: os parâmetros seguem exatamente a ordem das colunas do RETURNS TABLE
    // de odca.template_approvals_queue (materialização Dapper por nome; sem opcionais).
    internal sealed record ApprovalQueueRow(Guid TenantId, string OrganizationName, string? ActivityProfile, string OfficialKey,
        string? Decision, string? DecisionNote, DateTime? DecidedAt, string? DecidedByName, int? ApprovedRevision, long GeneratedCount, DateTime? LastGeneratedAt);
}
