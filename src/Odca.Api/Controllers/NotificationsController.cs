using System.Security.Claims;
using Dapper;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Npgsql;
using Odca.Contracts.Account;

namespace Odca.Api.Controllers;

/// <summary>Centro de notificações da conta (Bloco C, D-OC6): leitura e marcação de
/// lidas das notificações direcionadas ao usuário logado na organização. Fonte única:
/// odca.user_notifications (alimentada pelo worker de lembretes de obrigações).</summary>
[ApiController]
[Authorize(Policy = "PasswordChanged")]
[Route("api/v1/organizations/{tenantId:guid}/notifications")]
public sealed class NotificationsController(NpgsqlDataSource dataSource) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> List(Guid tenantId, CancellationToken ct)
    {
        var actor = Actor(); if (actor is null) return Unauthorized();
        await using var c = await dataSource.OpenConnectionAsync(ct);
        await using var tx = await c.BeginTransactionAsync(ct); await SetTenant(c, tenantId, actor.Value, tx, ct);
        if (!await IsMember(c, tenantId, actor.Value, tx, ct)) { await tx.CommitAsync(ct); return Forbid(); }
        var rows = (await c.QueryAsync<NotificationRow>(new CommandDefinition("""
            SELECT id AS Id, kind AS Kind, title AS Title, body AS Body, obligation_id AS ObligationId, created_at AS CreatedAt, read_at AS ReadAt
            FROM odca.user_notifications WHERE tenant_id=@tenantId AND user_id=@actor ORDER BY created_at DESC, id DESC LIMIT 200
            """, new { tenantId, actor }, tx, cancellationToken: ct))).ToList();
        var unread = await c.ExecuteScalarAsync<int>(new CommandDefinition("SELECT count(*)::int FROM odca.user_notifications WHERE tenant_id=@tenantId AND user_id=@actor AND read_at IS NULL", new { tenantId, actor }, tx, cancellationToken: ct));
        await tx.CommitAsync(ct);
        var items = rows.Select(r => new NotificationItem(r.Id, r.Kind, r.Title, r.Body, r.ObligationId, ToUtcOffset(r.CreatedAt), r.ReadAt.HasValue ? ToUtcOffset(r.ReadAt.Value) : null)).ToList();
        return Ok(new NotificationPage(items, unread));
    }

    [HttpPost("marcar-lidas")]
    public async Task<IActionResult> MarkRead(Guid tenantId, CancellationToken ct)
    {
        var actor = Actor(); if (actor is null) return Unauthorized();
        await using var c = await dataSource.OpenConnectionAsync(ct);
        await using var tx = await c.BeginTransactionAsync(ct); await SetTenant(c, tenantId, actor.Value, tx, ct);
        if (!await IsMember(c, tenantId, actor.Value, tx, ct)) { await tx.CommitAsync(ct); return Forbid(); }
        var marked = await c.ExecuteAsync(new CommandDefinition("UPDATE odca.user_notifications SET read_at=now() WHERE tenant_id=@tenantId AND user_id=@actor AND read_at IS NULL", new { tenantId, actor }, tx, cancellationToken: ct));
        await tx.CommitAsync(ct);
        return Ok(new { marked });
    }

    private Guid? Actor() => Guid.TryParse(User.FindFirstValue("sub"), out var id) ? id : null;
    private static Task<bool> IsMember(NpgsqlConnection c, Guid tenant, Guid actor, NpgsqlTransaction tx, CancellationToken ct) =>
        c.ExecuteScalarAsync<bool>(new CommandDefinition("SELECT EXISTS(SELECT 1 FROM odca.memberships WHERE tenant_id=@tenantId AND user_id=@actor)", new { tenantId = tenant, actor }, tx, cancellationToken: ct));
    private static Task<int> SetTenant(NpgsqlConnection c, Guid tenant, Guid actor, NpgsqlTransaction tx, CancellationToken ct) =>
        c.ExecuteAsync(new CommandDefinition("SELECT set_config('odca.tenant_id',@tenant,true),set_config('odca.actor_id',@actor,true)", new { tenant = tenant.ToString(), actor = actor.ToString() }, tx, cancellationToken: ct));
    private sealed record NotificationRow(Guid Id, string Kind, string Title, string Body, Guid? ObligationId, DateTime CreatedAt, DateTime? ReadAt);
    private static DateTimeOffset ToUtcOffset(DateTime dt) => dt.Kind == DateTimeKind.Utc ? new DateTimeOffset(dt, TimeSpan.Zero) : new DateTimeOffset(DateTime.SpecifyKind(dt, DateTimeKind.Utc), TimeSpan.Zero);
}
