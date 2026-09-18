using System.Security.Claims;
using Dapper;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Npgsql;
using Odca.Application.Operations;
using Odca.Contracts.Operations;

namespace Odca.Api.Controllers;

[ApiController]
[Authorize(Policy = "PasswordChanged")]
[Route("api/v1/organizations/{tenantId:guid}")]
public sealed class OperationalInboxController(
    OperationalInboxService inbox,
    MonthlyAgendaService agenda,
    ContractSheetService sheet,
    NpgsqlDataSource dataSource) : ControllerBase
{
    [HttpGet("inbox")]
    public async Task<IActionResult> Inbox(
        Guid tenantId,
        [FromQuery] string scope = "mine",
        [FromQuery] string? kind = null,
        [FromQuery] string? urgency = null,
        [FromQuery] Guid? contractId = null,
        [FromQuery] Guid? ownerId = null,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        CancellationToken ct = default)
    {
        var actor = Actor();
        if (actor is null) return Unauthorized();

        await using var connection = await dataSource.OpenConnectionAsync(ct);
        if (!await Allowed(connection, actor.Value, tenantId, "tenant.obligations.read", ct))
            return Forbid();

        var readAllObligations = await Allowed(connection, actor.Value, tenantId, "tenant.obligations.read_all", ct);
        var readAllReviews = await Allowed(connection, actor.Value, tenantId, "tenant.reviews.read", ct)
            || await Allowed(connection, actor.Value, tenantId, "tenant.reviews.read_all", ct);
        var readAllRenewals = await Allowed(connection, actor.Value, tenantId, "tenant.renewals.read", ct);

        var organization = scope.Equals("organization", StringComparison.OrdinalIgnoreCase);
        if (organization && !readAllObligations)
            return Forbid();

        var pageDto = await inbox.QueryAsync(
            tenantId,
            actor.Value,
            organization && readAllObligations,
            organization && readAllReviews,
            organization && readAllRenewals,
            new OperationalInboxQuery(scope, kind, urgency, contractId, ownerId, page, pageSize),
            ct);
        return Ok(pageDto);
    }

    [HttpGet("agenda")]
    public async Task<IActionResult> Agenda(
        Guid tenantId,
        [FromQuery] int year,
        [FromQuery] int month,
        [FromQuery] string scope = "mine",
        [FromQuery] Guid? ownerId = null,
        CancellationToken ct = default)
    {
        var actor = Actor();
        if (actor is null) return Unauthorized();

        await using var connection = await dataSource.OpenConnectionAsync(ct);
        if (!await Allowed(connection, actor.Value, tenantId, "tenant.obligations.read", ct))
            return Forbid();

        var organization = scope.Equals("organization", StringComparison.OrdinalIgnoreCase);
        var canReadTenant = false;
        if (organization)
        {
            canReadTenant = await Allowed(connection, actor.Value, tenantId, "tenant.obligations.read_all", ct);
            if (!canReadTenant)
                return Forbid();
        }

        try
        {
            var page = await agenda.QueryAsync(
                tenantId,
                actor.Value,
                canReadTenant,
                new MonthlyAgendaQuery(year, month, scope, ownerId),
                ct);
            return Ok(page);
        }
        catch (ArgumentOutOfRangeException)
        {
            return ValidationProblem("Informe um ano e um mês civis válidos.");
        }
        catch (ArgumentException exception)
        {
            return ValidationProblem(exception.Message);
        }
    }

    [HttpGet("contracts/{contractId:guid}/sheet")]
    public async Task<IActionResult> Sheet(Guid tenantId, Guid contractId, CancellationToken ct)
    {
        var actor = Actor();
        if (actor is null) return Unauthorized();

        await using var connection = await dataSource.OpenConnectionAsync(ct);
        if (!await Allowed(connection, actor.Value, tenantId, "tenant.obligations.read", ct))
            return Forbid();

        var canReadTenant = await Allowed(connection, actor.Value, tenantId, "tenant.obligations.read_all", ct);
        var dto = await sheet.GetAsync(tenantId, contractId, actor.Value, canReadTenant, ct);
        return dto is null ? NotFound() : Ok(dto);
    }

    private Guid? Actor() =>
        Guid.TryParse(User.FindFirstValue("sub"), out var id) ? id : null;

    private static Task<bool> Allowed(
        NpgsqlConnection connection,
        Guid actor,
        Guid tenant,
        string permission,
        CancellationToken cancellationToken) =>
        connection.ExecuteScalarAsync<bool>(new CommandDefinition(
            "SELECT odca.tenant_actor_has_permission(@actor,@tenant,@permission)",
            new { actor, tenant, permission },
            cancellationToken: cancellationToken));
}
