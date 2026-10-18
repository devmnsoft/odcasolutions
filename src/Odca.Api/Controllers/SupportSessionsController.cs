using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Odca.Api.Localization;
using Odca.Application.Identity;
using Odca.Application.Support;
using Odca.Contracts.Support;

namespace Odca.Api.Controllers;

[ApiController]
[Route("api/v1/platform/support-sessions")]
// D-OC43 (Sprint 9/F-1): políticas movidas por ação — o encerramento da impersonação
// pode chegar vindo do próprio token da visão do cliente (ator resolvido por imp_op).
[Authorize]
public sealed class SupportSessionsController(
    ISupportSessionRepository repository, IIdentityRepository identity, ITokenService tokenService) : ControllerBase
{
    [HttpGet]
    [Authorize(Policy = "PlatformAdministrator")]
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
    [Authorize(Policy = "PlatformAdministrator")]
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
    [Authorize(Policy = "PlatformAdministrator")]
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
    [Authorize(Policy = "PlatformAdministrator")]
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
    [Authorize(Policy = "PlatformAdministrator")]
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
    [Authorize(Policy = "PlatformAdministrator")]
    [Authorize(Policy = "RecentMfa")]
    public async Task<IActionResult> End(Guid id, CancellationToken cancellationToken)
        => await CloseAsync(id, revoke: false, cancellationToken);

    [HttpPost("{id:guid}/revoke")]
    [Authorize(Policy = "PlatformAdministrator")]
    [Authorize(Policy = "RecentMfa")]
    public async Task<IActionResult> Revoke(Guid id, CancellationToken cancellationToken)
        => await CloseAsync(id, revoke: true, cancellationToken);

    // D-OC43 (Sprint 9/F-1): impersonação profunda — membros elegíveis do tenant da janela,
    // abertura da visão do cliente (MFA recente) e encerramento da concessão.
    [HttpGet("{id:guid}/members")]
    [Authorize(Policy = "PlatformAdministrator")]
    public async Task<IActionResult> Members(Guid id, CancellationToken cancellationToken)
    {
        if (!Actor(out var actorId))
        {
            return Unauthorized();
        }

        var session = await repository.GetAsync(actorId, id, cancellationToken);
        if (session is null)
        {
            return NotFound(new ProblemDetails { Title = this.T("api.support_session.not_found"), Extensions = { ["code"] = "support_session.not_found" } });
        }

        var members = await repository.ListMembersAsync(actorId, session.TenantId, cancellationToken);
        return Ok(members.Select(m => new SupportSessionMemberResponse(m.UserId, m.DisplayName, m.Email)).ToArray());
    }

    [HttpPost("{id:guid}/impersonation")]
    [Authorize(Policy = "PlatformAdministrator")]
    [Authorize(Policy = "RecentMfa")]
    public async Task<IActionResult> StartImpersonation(
        Guid id, [FromBody] StartSupportImpersonationRequest request, CancellationToken cancellationToken)
    {
        if (!Actor(out var actorId))
        {
            return Unauthorized();
        }

        if (request.TargetUserId == Guid.Empty || request.Minutes is < 5 or > 60)
        {
            return this.Invalid("api.support_impersonation.validation");
        }

        var result = await repository.StartImpersonationAsync(actorId, id, request.TargetUserId, request.Minutes, cancellationToken);
        return result.Error switch
        {
            SupportImpersonationStartError.None when result.Ok => await IssuedAsync(actorId, result),
            SupportImpersonationStartError.AlreadyActive => Conflict(new ProblemDetails
            {
                Title = this.T("api.support_impersonation.active_exists"),
                Status = StatusCodes.Status409Conflict,
                Extensions = { ["code"] = "support_impersonation.active_exists" }
            }),
            SupportImpersonationStartError.Validation => this.Invalid("api.support_impersonation.validation"),
            _ => this.Status(503, "api.platform_audit.unavailable")
        };
    }

    // O ator do encerramento é sempre o operador real: vindo da visão do cliente o token
    // carrega imp_op (o usuário-alvo não é superadministrador); vindo da plataforma é o sub.
    [HttpPost("{id:guid}/impersonation/{grantId:guid}/end")]
    public async Task<IActionResult> EndImpersonation(Guid id, Guid grantId, CancellationToken cancellationToken)
    {
        if (!Actor(out var subject) && string.IsNullOrEmpty(User.FindFirstValue("imp_op")))
        {
            return Unauthorized();
        }

        var operatorId = Guid.TryParse(User.FindFirstValue("imp_op"), out var bound) ? bound : subject;
        var ended = await repository.EndImpersonationAsync(operatorId, grantId, revoked: false, cancellationToken);
        return ended
            ? NoContent()
            : NotFound(new ProblemDetails { Title = this.T("api.support_impersonation.not_found"), Extensions = { ["code"] = "support_impersonation.not_found" } });
    }

    private async Task<IActionResult> IssuedAsync(Guid actorId, SupportImpersonationStart result)
    {
        // O alvo existe e tem membership ativa (garantia da função SQL); a linha de sessão
        // dedicada foi criada na mesma transação com security_version 1 e nível mfa.
        var target = await identity.FindByIdAsync(result.TargetUserId, default);
        if (target is null || target.IsDeleted)
        {
            return this.Status(503, "api.platform_audit.unavailable");
        }

        var targetSession = new SessionRecord(result.TargetSessionId, target.Id, 1, result.ExpiresAt, "mfa", DateTimeOffset.UtcNow);
        var issued = tokenService.IssueImpersonation(target, targetSession, actorId, result.GrantId);
        return Ok(new StartSupportImpersonationResponse(result.GrantId, result.ExpiresAt, issued.AccessToken));
    }

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
