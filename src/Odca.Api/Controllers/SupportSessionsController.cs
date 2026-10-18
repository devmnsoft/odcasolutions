using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Odca.Api.Localization;
using Odca.Application.Support;
using Odca.Contracts.Support;

namespace Odca.Api.Controllers;

[ApiController]
[Route("api/v1/platform/support-sessions")]
[Authorize(Policy = "PlatformAdministrator")]
public sealed class SupportSessionsController(ISupportSessionRepository repository) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<SupportSessionResponse>>> List(
        [FromQuery] int limit = 50, CancellationToken cancellationToken = default)
    {
        if (!Actor(out var actorId))
        {
            return Unauthorized();
        }

        limit = Math.Clamp(limit, 1, 100);
        var result = await repository.ListRecentAsync(actorId, limit, cancellationToken);
        return result.Ok
            ? Ok(result.Sessions.Select(ToResponse).ToArray())
            : this.Status(503, "api.platform_audit.unavailable");
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<SupportSessionResponse>> Get(Guid id, CancellationToken cancellationToken)
    {
        if (!Actor(out var actorId))
        {
            return Unauthorized();
        }

        var row = await repository.GetAsync(actorId, id, cancellationToken);
        return row is null ? NotFound() : Ok(ToResponse(row));
    }

    [HttpGet("{id:guid}/events")]
    public async Task<IActionResult> Events(Guid id, CancellationToken cancellationToken)
    {
        if (!Actor(out var actorId))
        {
            return Unauthorized();
        }

        var known = await repository.GetAsync(actorId, id, cancellationToken);
        if (known is null)
        {
            return NotFound();
        }

        var rows = await repository.EventsAsync(id, cancellationToken);
        return Ok(rows.Select(r => new SupportSessionEventResponse(r.SessionId, r.ActorName, r.EventType, r.OccurredAt, r.Details)).ToArray());
    }

    [HttpPost]
    [Authorize(Policy = "RecentMfa")]
    public async Task<IActionResult> Open(
        [FromBody] OpenSupportSessionRequest request, CancellationToken cancellationToken)
    {
        if (!Actor(out var actorId))
        {
            return Unauthorized();
        }

        var reason = request.Reason?.Trim() ?? string.Empty;
        if (request.TenantId == Guid.Empty || reason.Length is < 5 or > 1000 || request.DurationMinutes is < 1 or > 480)
        {
            return this.Invalid("api.support_session.validation");
        }

        var (id, error) = await repository.OpenAsync(actorId, request.TenantId, reason, request.DurationMinutes, cancellationToken);
        return error switch
        {
            SupportSessionOpenError.None when id != Guid.Empty => await CreatedAsync(actorId, id),
            SupportSessionOpenError.AlreadyActive => Conflict(new ProblemDetails
            {
                Title = this.T("api.support_session.active_exists"),
                Status = StatusCodes.Status409Conflict,
                Extensions = { ["code"] = "support_session.active_exists" }
            }),
            SupportSessionOpenError.Validation => this.Invalid("api.support_session.validation"),
            _ => this.Status(503, "api.platform_audit.unavailable")
        };
    }

    [HttpPost("{id:guid}/extend")]
    [Authorize(Policy = "RecentMfa")]
    public async Task<IActionResult> Extend(
        Guid id, [FromBody] ExtendSupportSessionRequest request, CancellationToken cancellationToken)
    {
        if (!Actor(out var actorId))
        {
            return Unauthorized();
        }

        if (request.Minutes is < 1 or > 480)
        {
            return this.Invalid("api.support_session.validation");
        }

        var existing = await repository.GetAsync(actorId, id, cancellationToken);
        if (existing is null || !existing.Valid)
        {
            return NotFound(new ProblemDetails { Title = this.T("api.support_session.not_found"), Extensions = { ["code"] = "support_session.not_found" } });
        }

        var newExpires = await repository.ExtendAsync(actorId, id, request.Minutes, cancellationToken);
        var updated = newExpires is null ? null : await repository.GetAsync(actorId, id, cancellationToken);
        return updated is null ? NotFound() : Ok(ToResponse(updated));
    }

    [HttpPost("{id:guid}/end")]
    [Authorize(Policy = "RecentMfa")]
    public async Task<IActionResult> End(Guid id, CancellationToken cancellationToken)
        => await CloseAsync(id, revoke: false, cancellationToken);

    [HttpPost("{id:guid}/revoke")]
    [Authorize(Policy = "RecentMfa")]
    public async Task<IActionResult> Revoke(Guid id, CancellationToken cancellationToken)
        => await CloseAsync(id, revoke: true, cancellationToken);

    private async Task<IActionResult> CloseAsync(Guid id, bool revoke, CancellationToken cancellationToken)
    {
        if (!Actor(out var actorId))
        {
            return Unauthorized();
        }

        var closed = await repository.CloseAsync(actorId, id, revoke, cancellationToken);
        if (!closed)
        {
            return NotFound(new ProblemDetails { Title = this.T("api.support_session.not_found"), Extensions = { ["code"] = "support_session.not_found" } });
        }

        var updated = await repository.GetAsync(actorId, id, cancellationToken);
        return updated is null ? NotFound() : Ok(ToResponse(updated));
    }

    private async Task<IActionResult> CreatedAsync(Guid actorId, Guid id)
    {
        var row = await repository.GetAsync(actorId, id, default);
        return row is null
            ? CreatedAtAction(nameof(Get), new { id }, null)
            : CreatedAtAction(nameof(Get), new { id }, ToResponse(row));
    }

    private bool Actor(out Guid actorId)
    {
        actorId = Guid.Empty;
        return Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.FindFirstValue("sub"), out actorId);
    }

    private static SupportSessionResponse ToResponse(SupportSessionInfo row) => new(
        row.Id, row.TenantId, row.TenantName, row.SuperadminUserId, row.SuperadminName,
        row.Reason, row.Status, row.Valid, row.StartedAt, row.ExpiresAt, row.EndedAt);
}
