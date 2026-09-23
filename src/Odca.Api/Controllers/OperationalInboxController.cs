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
        var canReadObligations = await Allowed(connection, actor.Value, tenantId, "tenant.obligations.read", ct);
        var canReadReviews = await Allowed(connection, actor.Value, tenantId, "tenant.reviews.read", ct);
        var canReadRenewals = await Allowed(connection, actor.Value, tenantId, "tenant.renewals.read", ct);
        if (!canReadObligations && !canReadReviews && !canReadRenewals)
            return Forbid();

        var readAllObligations = await Allowed(connection, actor.Value, tenantId, "tenant.obligations.read_all", ct);

        var organization = scope.Equals("organization", StringComparison.OrdinalIgnoreCase);

        var pageDto = await inbox.QueryAsync(
            tenantId,
            actor.Value,
            canReadObligations,
            organization && readAllObligations,
            canReadReviews,
            organization && canReadReviews,
            canReadRenewals,
            organization && canReadRenewals,
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
        [FromQuery] string? kind = null,
        CancellationToken ct = default)
    {
        var actor = Actor();
        if (actor is null) return Unauthorized();

        await using var connection = await dataSource.OpenConnectionAsync(ct);
        var canReadObligations = await Allowed(connection, actor.Value, tenantId, "tenant.obligations.read", ct);
        var canReadReviews = await Allowed(connection, actor.Value, tenantId, "tenant.reviews.read", ct);
        var canReadRenewals = await Allowed(connection, actor.Value, tenantId, "tenant.renewals.read", ct);
        if (!canReadObligations && !canReadReviews && !canReadRenewals)
            return Forbid();

        var organization = scope.Equals("organization", StringComparison.OrdinalIgnoreCase);
        var canReadAllObligations = canReadObligations
            && await Allowed(connection, actor.Value, tenantId, "tenant.obligations.read_all", ct);

        try
        {
            var page = await agenda.QueryAsync(
                tenantId,
                actor.Value,
                canReadObligations,
                organization && canReadAllObligations,
                canReadReviews,
                organization && canReadReviews,
                canReadRenewals,
                organization && canReadRenewals,
                new MonthlyAgendaQuery(year, month, scope, ownerId, kind),
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
        var canReadObligations = await Allowed(connection, actor.Value, tenantId, "tenant.obligations.read", ct);
        var canReadReviews = await Allowed(connection, actor.Value, tenantId, "tenant.reviews.read", ct);
        var canReadRenewals = await Allowed(connection, actor.Value, tenantId, "tenant.renewals.read", ct);
        var canReadDocuments = await Allowed(connection, actor.Value, tenantId, "tenant.contract_drafts.read", ct)
            || await Allowed(connection, actor.Value, tenantId, "tenant.documents.download", ct);
        if (!canReadObligations && !canReadReviews && !canReadRenewals && !canReadDocuments)
            return Forbid();

        var canReadTenant = canReadObligations
            && await Allowed(connection, actor.Value, tenantId, "tenant.obligations.read_all", ct);
        await using var transaction = await connection.BeginTransactionAsync(ct);
        await connection.ExecuteAsync(new CommandDefinition(
            """
            SELECT set_config('odca.tenant_id', @tenant, true),
                   set_config('odca.actor_id', @actor, true);
            """,
            new { tenant = tenantId.ToString(), actor = actor.Value.ToString() },
            transaction,
            cancellationToken: ct));
        var assignment = await connection.QuerySingleAsync<SheetAssignment>(new CommandDefinition(
            """
            SELECT
                EXISTS(SELECT 1 FROM odca.contracts c
                        WHERE c.tenant_id=@tenant AND c.id=@contract AND c.owner_id=@actor) AS OwnsContract,
                EXISTS(SELECT 1 FROM odca.contract_obligations o
                        WHERE o.tenant_id=@tenant AND o.contract_id=@contract
                          AND o.owner_id=@actor AND o.deleted_at IS NULL) AS OwnsObligation,
                EXISTS(SELECT 1 FROM odca.contract_reviews r
                        JOIN odca.contract_review_steps s ON s.tenant_id=r.tenant_id AND s.review_id=r.id
                        WHERE r.tenant_id=@tenant AND r.contract_id=@contract
                          AND s.reviewer_id=@actor AND s.status='current') AS IsAssignedReviewer,
                EXISTS(SELECT 1 FROM odca.contract_change_requests r
                        WHERE r.tenant_id=@tenant AND r.contract_id=@contract
                          AND r.responsible_id=@actor AND r.status <> 'cancelled') AS OwnsRenewal
            """,
            new { tenant = tenantId, contract = contractId, actor = actor.Value },
            transaction,
            cancellationToken: ct));
        await transaction.CommitAsync(ct);

        var purposeAllowed = ContractSheetAccessPolicy.CanOpen(
            assignment.OwnsContract,
            canReadTenant,
            canReadObligations && assignment.OwnsObligation,
            canReadReviews && assignment.IsAssignedReviewer,
            canReadRenewals && assignment.OwnsRenewal,
            canReadDocuments && assignment.OwnsContract);
        if (!purposeAllowed) return Forbid();

        var dto = await sheet.GetAsync(
            tenantId, contractId, actor.Value, purposeAllowed, canReadTenant, ct);
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

    private sealed record SheetAssignment(
        bool OwnsContract,
        bool OwnsObligation,
        bool IsAssignedReviewer,
        bool OwnsRenewal);
}
