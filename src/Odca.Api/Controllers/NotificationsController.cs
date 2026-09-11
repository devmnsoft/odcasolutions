using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Odca.Application.Contracts;
using Odca.Application.Tenancy;
using Odca.Contracts.Contracts;
using Odca.Contracts.Tenancy;

namespace Odca.Api.Controllers;

[ApiController]
[Route("api/v1/organizations/{tenantId:guid}/notifications")]
[Authorize(Policy = "PasswordChanged")]
public sealed class NotificationsController(UserNotificationService service) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<PaginatedResponse<UserNotificationResponse>>> List(
        Guid tenantId,
        [FromQuery] bool unreadOnly = false,
        [FromQuery] int? page = null,
        [FromQuery] int? pageSize = null,
        CancellationToken cancellationToken = default)
    {
        if (!Actor(out var actor))
        {
            return Unauthorized();
        }

        var result = await service.ListAsync(actor, tenantId, unreadOnly, page, pageSize, cancellationToken);
        if (result.Status == QueryAccessStatus.Forbidden)
        {
            return Forbid();
        }

        var pageResult = result.Value!;
        return Ok(new PaginatedResponse<UserNotificationResponse>(
            pageResult.Items.Select(Map).ToList(),
            pageResult.TotalCount,
            pageResult.Page,
            pageResult.PageSize));
    }

    [HttpGet("unread-count")]
    public async Task<ActionResult<UnreadNotificationsResponse>> UnreadCount(Guid tenantId, CancellationToken cancellationToken)
    {
        if (!Actor(out var actor))
        {
            return Unauthorized();
        }

        var result = await service.UnreadCountAsync(actor, tenantId, cancellationToken);
        if (result.Status == QueryAccessStatus.Forbidden)
        {
            return Forbid();
        }

        return Ok(new UnreadNotificationsResponse(result.Value));
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<UserNotificationResponse>> Get(Guid tenantId, Guid id, CancellationToken cancellationToken)
    {
        if (!Actor(out var actor))
        {
            return Unauthorized();
        }

        var result = await service.GetAsync(actor, tenantId, id, cancellationToken);
        if (result.Status == QueryAccessStatus.Forbidden)
        {
            return Forbid();
        }

        return result.Value is null ? NotFound() : Ok(Map(result.Value));
    }

    [HttpPost("{id:guid}/read")]
    public async Task<IActionResult> MarkRead(Guid tenantId, Guid id, CancellationToken cancellationToken)
    {
        if (!Actor(out var actor))
        {
            return Unauthorized();
        }

        var result = await service.MarkReadAsync(actor, tenantId, id, cancellationToken);
        return result.Status switch
        {
            MutationStatus.Succeeded => NoContent(),
            MutationStatus.Forbidden => Forbid(),
            MutationStatus.NotFound => NotFound(),
            _ => Conflict()
        };
    }

    private static UserNotificationResponse Map(UserNotificationRecord row)
        => new(
            row.Id,
            row.Category,
            row.Title,
            row.Body,
            row.ResourceType,
            row.ResourceId,
            row.EventKey,
            row.Status,
            row.IsObsolete,
            row.CreatedAt,
            row.RelevantDate,
            row.ReadAt);

    private bool Actor(out Guid actor) => Guid.TryParse(User.FindFirstValue("sub"), out actor);
}
