using System.Security.Claims;
using Dapper;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Npgsql;
using Odca.Api.Localization;

namespace Odca.Api.Controllers;

/// <summary>
/// B.1 (D-OC31): central de notificações do superadministrador. A listagem e o detalhe
/// expõem apenas campos seguros (organização, tipo, destino, status, tentativas e código
/// de erro estável) — nunca o payload protegido nem links com tokens; o retry é restrito
/// a falhas definitivas e é auditado em escopo platform (platform.notifications.retried).
/// </summary>
[ApiController]
[Authorize(Policy = "PlatformAdministrator")]
public sealed class PlatformNotificationsController(NpgsqlDataSource dataSource) : ControllerBase
{
    [HttpGet("api/v1/platform/notifications")]
    public async Task<IActionResult> List([FromQuery] string? status, [FromQuery] string? kind, [FromQuery] string? search,
        [FromQuery] int page = 1, [FromQuery] int pageSize = 25, CancellationToken ct = default)
    {
        var actor = Actor(); if (actor is null) return Unauthorized();
        await using var c = await dataSource.OpenConnectionAsync(ct);
        await EnsurePlatformGuc(c, actor.Value, ct);
        page = Math.Max(1, page); pageSize = Math.Clamp(pageSize, 1, 100);
        var args = new { actor = actor.Value, status = TrimOrNull(status), kind = TrimOrNull(kind), search = TrimOrNull(search), page, pageSize };
        var total = await c.ExecuteScalarAsync<int>(new CommandDefinition(
            "SELECT odca.notification_ops_count(@actor,@status,@kind,@search)", args, cancellationToken: ct));
        var rows = await c.QueryAsync<OpsRow>(new CommandDefinition("""
            SELECT message_id AS MessageId, organization_name AS OrganizationName, kind AS Kind, destination AS Destination,
                   status AS Status, attempt_count AS AttemptCount, last_error_code AS LastErrorCode,
                   created_at AS CreatedAt, sent_at AS SentAt, invitation_id AS InvitationId
            FROM odca.notification_ops_page(@actor,@status,@kind,@search,@page,@pageSize)
            """, args, cancellationToken: ct));
        return Ok(new
        {
            items = rows.Select(r => new OpsItem(
                r.MessageId, r.OrganizationName, r.Kind, r.Destination, r.Status, r.AttemptCount, r.LastErrorCode,
                ToUtcOffset(r.CreatedAt), r.SentAt is null ? null : ToUtcOffset(r.SentAt.Value), r.InvitationId)).ToArray(),
            page, pageSize, total
        });
    }

    [HttpPost("api/v1/platform/notifications/{messageId:guid}/retry")]
    public async Task<IActionResult> Retry(Guid messageId, CancellationToken ct)
    {
        var actor = Actor(); if (actor is null) return Unauthorized();
        await using var c = await dataSource.OpenConnectionAsync(ct);
        await EnsurePlatformGuc(c, actor.Value, ct);
        var ok = await c.ExecuteScalarAsync<bool>(new CommandDefinition(
            "SELECT odca.retry_notification(@messageId,@actor)", new { messageId, actor = actor.Value }, cancellationToken: ct));
        if (!ok) return Conflict(new { title = this.T("api.platform_notifications.retry_unavailable"), code = "notification_retry_unavailable" });
        return Ok(new { retried = true });
    }

    private static async Task EnsurePlatformGuc(NpgsqlConnection c, Guid actor, CancellationToken ct) =>
        await c.ExecuteAsync(new CommandDefinition("SELECT set_config('odca.user_id',@actor::text,false)", new { actor }, cancellationToken: ct));

    private static string? TrimOrNull(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private Guid? Actor() => Guid.TryParse(User.FindFirstValue("sub"), out var id) ? id : null;

    private static DateTimeOffset ToUtcOffset(DateTime dt) => dt.Kind == DateTimeKind.Utc ? new DateTimeOffset(dt, TimeSpan.Zero) : new DateTimeOffset(DateTime.SpecifyKind(dt, DateTimeKind.Utc), TimeSpan.Zero);

    internal sealed record OpsRow(Guid MessageId, string OrganizationName, string Kind, string Destination, string Status, int AttemptCount, string? LastErrorCode, DateTime CreatedAt, DateTime? SentAt, Guid? InvitationId);
    internal sealed record OpsItem(Guid MessageId, string OrganizationName, string Kind, string Destination, string Status, int AttemptCount, string? LastErrorCode, DateTimeOffset CreatedAt, DateTimeOffset? SentAt, Guid? InvitationId);
}
