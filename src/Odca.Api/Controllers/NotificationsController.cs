using System.Globalization;
using System.Security.Claims;
using Dapper;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Npgsql;
using Odca.Api.Localization;
using Odca.Contracts.Account;

namespace Odca.Api.Controllers;

/// <summary>Centro de notificações da conta (Bloco C, D-OC6 + A′/5, D-OC13):
/// leitura paginada, marcação individual e global, preferências (tipo/canal/idioma)
/// e link seguro de destino com autorização por recurso. Fonte única:
/// odca.user_notifications (alimentada pelo worker de lembretes de obrigações).</summary>
[ApiController]
[Authorize(Policy = "PasswordChanged")]
[Route("api/v1/organizations/{tenantId:guid}/notifications")]
public sealed class NotificationsController(NpgsqlDataSource dataSource) : ControllerBase
{
    private static readonly string[] KnownKinds = ["obligation_due", "renewal_due", "review_assigned", "review_decided", "solicitation_opened", "solicitation_updated", "signature_pending", "signature_completed"];
    private static readonly string[] AllowedLanguages = ["pt-BR", "en-US", "fr-FR", "es-ES"];
    private const int DefaultPageSize = 25;
    private const int MaxPageSize = 100;

    [HttpGet]
    public async Task<IActionResult> List(Guid tenantId, [FromQuery] int? page, [FromQuery] int? pageSize, CancellationToken ct)
    {
        var actor = Actor(); if (actor is null) return Unauthorized();
        var p = Math.Clamp(page ?? 1, 1, int.MaxValue);
        var ps = Math.Clamp(pageSize ?? DefaultPageSize, 1, MaxPageSize);
        var offset = (p - 1) * ps;

        await using var c = await dataSource.OpenConnectionAsync(ct);
        await using var tx = await c.BeginTransactionAsync(ct); await SetTenant(c, tenantId, actor.Value, tx, ct);
        if (!await IsMember(c, tenantId, actor.Value, tx, ct)) { await tx.CommitAsync(ct); return Forbid(); }

        const string visible = "NOT EXISTS (SELECT 1 FROM odca.user_notification_preferences pf WHERE pf.tenant_id=n.tenant_id AND pf.user_id=n.user_id AND pf.kind=n.kind AND (pf.enabled=false OR pf.channel_in_app=false))";

        var rows = (await c.QueryAsync<NotificationRow>(new CommandDefinition($"""
            SELECT n.id AS Id, n.kind AS Kind, n.title AS Title, n.body AS Body, n.obligation_id AS ObligationId,
                   n.created_at AS CreatedAt, n.read_at AS ReadAt,
                   COALESCE(n.target_type, CASE WHEN n.obligation_id IS NOT NULL THEN 'obligation' END) AS TargetType,
                   COALESCE(n.target_id, n.obligation_id) AS TargetId,
                   odca.notification_target_authorized(@actor,@tenantId,
                       COALESCE(n.target_type, CASE WHEN n.obligation_id IS NOT NULL THEN 'obligation' END),
                       COALESCE(n.target_id,n.obligation_id)) AS CanOpen
            FROM odca.user_notifications n
            WHERE n.tenant_id=@tenantId AND n.user_id=@actor AND {visible}
            ORDER BY n.created_at DESC, n.id DESC
            LIMIT @pageSize OFFSET @offset
            """, new { tenantId, actor, pageSize = ps, offset }, tx, cancellationToken: ct))).ToList();

        var total = await c.ExecuteScalarAsync<int>(new CommandDefinition($"SELECT count(*)::int FROM odca.user_notifications n WHERE n.tenant_id=@tenantId AND n.user_id=@actor AND {visible}", new { tenantId, actor }, tx, cancellationToken: ct));
        var unread = await c.ExecuteScalarAsync<int>(new CommandDefinition($"SELECT count(*)::int FROM odca.user_notifications n WHERE n.tenant_id=@tenantId AND n.user_id=@actor AND {visible} AND n.read_at IS NULL", new { tenantId, actor }, tx, cancellationToken: ct));
        var lang = (await c.ExecuteScalarAsync<string?>(new CommandDefinition("SELECT preferred_language FROM odca.user_notification_settings WHERE tenant_id=@tenantId AND user_id=@actor", new { tenantId, actor }, tx, cancellationToken: ct))) ?? "pt-BR";
        await tx.CommitAsync(ct);

        CultureInfo culture;
        try { culture = CultureInfo.GetCultureInfo(lang); } catch { culture = CultureInfo.GetCultureInfo("pt-BR"); }

        var items = rows.Select(r => new NotificationItem(
            r.Id, r.Kind, r.Title, r.Body, r.ObligationId,
            ToUtcOffset(r.CreatedAt),
            r.ReadAt.HasValue ? ToUtcOffset(r.ReadAt.Value) : null,
            r.TargetType, r.TargetId, r.CanOpen,
            FormatCreated(r.CreatedAt, culture))).ToList();
        return Ok(new NotificationPage(items, unread, p, ps, total, offset + items.Count < total));
    }

    [HttpPost("marcar-lidas")]
    public async Task<IActionResult> MarkRead(Guid tenantId, CancellationToken ct)
    {
        var actor = Actor(); if (actor is null) return Unauthorized();
        await using var c = await dataSource.OpenConnectionAsync(ct);
        await using var tx = await c.BeginTransactionAsync(ct); await SetTenant(c, tenantId, actor.Value, tx, ct);
        if (!await IsMember(c, tenantId, actor.Value, tx, ct)) { await tx.CommitAsync(ct); return Forbid(); }
        var marked = await c.ExecuteAsync(new CommandDefinition("""
            UPDATE odca.user_notifications n SET read_at=now()
            WHERE n.tenant_id=@tenantId AND n.user_id=@actor AND n.read_at IS NULL
              AND NOT EXISTS (SELECT 1 FROM odca.user_notification_preferences pf WHERE pf.tenant_id=n.tenant_id AND pf.user_id=n.user_id AND pf.kind=n.kind AND (pf.enabled=false OR pf.channel_in_app=false))
            """, new { tenantId, actor }, tx, cancellationToken: ct));
        await tx.CommitAsync(ct);
        return Ok(new { marked });
    }

    [HttpPost("{id:guid}/ler")]
    public async Task<IActionResult> MarkOneRead(Guid tenantId, Guid id, CancellationToken ct)
    {
        var actor = Actor(); if (actor is null) return Unauthorized();
        await using var c = await dataSource.OpenConnectionAsync(ct);
        await using var tx = await c.BeginTransactionAsync(ct); await SetTenant(c, tenantId, actor.Value, tx, ct);
        if (!await IsMember(c, tenantId, actor.Value, tx, ct)) { await tx.CommitAsync(ct); return Forbid(); }
        var ok = await c.QuerySingleOrDefaultAsync<Guid?>(new CommandDefinition(
            "UPDATE odca.user_notifications SET read_at=now() WHERE tenant_id=@tenantId AND user_id=@actor AND id=@id AND read_at IS NULL RETURNING id",
            new { tenantId, actor, id }, tx, cancellationToken: ct));
        await tx.CommitAsync(ct);
        return ok is null ? NotFound() : Ok(new { marked = true });
    }

    [HttpGet("preferencias")]
    public async Task<IActionResult> GetPreferences(Guid tenantId, CancellationToken ct)
    {
        var actor = Actor(); if (actor is null) return Unauthorized();
        await using var c = await dataSource.OpenConnectionAsync(ct);
        await using var tx = await c.BeginTransactionAsync(ct); await SetTenant(c, tenantId, actor.Value, tx, ct);
        if (!await IsMember(c, tenantId, actor.Value, tx, ct)) { await tx.CommitAsync(ct); return Forbid(); }
        var result = await LoadPreferences(c, tx, tenantId, actor.Value, ct);
        await tx.CommitAsync(ct);
        return Ok(result);
    }

    [HttpPut("preferencias")]
    public async Task<IActionResult> UpdatePreferences(Guid tenantId, [FromBody] UpdateNotificationPreferencesRequest request, CancellationToken ct)
    {
        var actor = Actor(); if (actor is null) return Unauthorized();
        if (string.IsNullOrWhiteSpace(request.PreferredLanguage) || !AllowedLanguages.Contains(request.PreferredLanguage, StringComparer.OrdinalIgnoreCase))
            // D-OC27: código wire preservado; título por chave estável.
            return BadRequest(new { title = this.T("api.notifications.language.invalid"), code = "notifications.language.invalid", supported = AllowedLanguages });
        var kinds = request.Kinds?.Where(k => !string.IsNullOrWhiteSpace(k.Kind)).ToList();
        if (kinds is not null && kinds.Any(k => !KnownKinds.Contains(k.Kind)))
            return BadRequest(new { title = this.T("api.notifications.kind.unknown"), code = "notifications.kind.unknown" });

        await using var c = await dataSource.OpenConnectionAsync(ct);
        await using var tx = await c.BeginTransactionAsync(ct); await SetTenant(c, tenantId, actor.Value, tx, ct);
        if (!await IsMember(c, tenantId, actor.Value, tx, ct)) { await tx.CommitAsync(ct); return Forbid(); }

        await c.ExecuteAsync(new CommandDefinition("""
            INSERT INTO odca.user_notification_settings(tenant_id,user_id,preferred_language,updated_at)
            VALUES(@tenantId,@actor,@lang,now())
            ON CONFLICT(tenant_id,user_id) DO UPDATE SET preferred_language=EXCLUDED.preferred_language, updated_at=now()
            """, new { tenantId, actor, lang = request.PreferredLanguage }, tx, cancellationToken: ct));

        if (kinds is not null)
        {
            foreach (var k in kinds)
            {
                await c.ExecuteAsync(new CommandDefinition("""
                    INSERT INTO odca.user_notification_preferences(tenant_id,user_id,kind,enabled,channel_in_app,channel_email,updated_at)
                    VALUES(@tenantId,@actor,@kind,@enabled,@inApp,@email,now())
                    ON CONFLICT(tenant_id,user_id,kind) DO UPDATE SET enabled=EXCLUDED.enabled, channel_in_app=EXCLUDED.channel_in_app, channel_email=EXCLUDED.channel_email, updated_at=now()
                    """, new { tenantId, actor, kind = k.Kind, enabled = k.Enabled, inApp = k.ChannelInApp, email = k.ChannelEmail }, tx, cancellationToken: ct));
            }
        }

        var result = await LoadPreferences(c, tx, tenantId, actor.Value, ct);
        await tx.CommitAsync(ct);
        return Ok(result);
    }

    private static async Task<NotificationPreferences> LoadPreferences(NpgsqlConnection c, NpgsqlTransaction tx, Guid tenantId, Guid actor, CancellationToken ct)
    {
        var lang = (await c.ExecuteScalarAsync<string?>(new CommandDefinition(
            "SELECT preferred_language FROM odca.user_notification_settings WHERE tenant_id=@tenantId AND user_id=@actor",
            new { tenantId, actor }, tx, cancellationToken: ct))) ?? "pt-BR";
        var kinds = new List<NotificationKindPreference>(KnownKinds.Length);
        foreach (var kind in KnownKinds)
        {
            var st = await c.QuerySingleAsync<ChannelState>(new CommandDefinition(
                "SELECT enabled AS Enabled, channel_in_app AS InApp, channel_email AS Email FROM odca.notification_channel_state(@tenantId,@actor,@kind)",
                new { tenantId, actor, kind }, tx, cancellationToken: ct));
            kinds.Add(new NotificationKindPreference { Kind = kind, Enabled = st.Enabled, ChannelInApp = st.InApp, ChannelEmail = st.Email });
        }
        return new NotificationPreferences(lang, kinds);
    }

    private Guid? Actor() => Guid.TryParse(User.FindFirstValue("sub"), out var id) ? id : null;
    private static Task<bool> IsMember(NpgsqlConnection c, Guid tenant, Guid actor, NpgsqlTransaction tx, CancellationToken ct) =>
        c.ExecuteScalarAsync<bool>(new CommandDefinition("SELECT EXISTS(SELECT 1 FROM odca.memberships WHERE tenant_id=@tenantId AND user_id=@actor)", new { tenantId = tenant, actor }, tx, cancellationToken: ct));
    private static Task<int> SetTenant(NpgsqlConnection c, Guid tenant, Guid actor, NpgsqlTransaction tx, CancellationToken ct) =>
        c.ExecuteAsync(new CommandDefinition("SELECT set_config('odca.tenant_id',@tenant,true),set_config('odca.actor_id',@actor,true)", new { tenant = tenant.ToString(), actor = actor.ToString() }, tx, cancellationToken: ct));
    private static string FormatCreated(DateTime dt, CultureInfo culture) =>
        dt.ToString(culture.DateTimeFormat.ShortDatePattern + " " + culture.DateTimeFormat.ShortTimePattern, culture);
    private sealed record NotificationRow(Guid Id, string Kind, string Title, string Body, Guid? ObligationId, DateTime CreatedAt, DateTime? ReadAt, string? TargetType, Guid? TargetId, bool CanOpen);
    private sealed record ChannelState(bool Enabled, bool InApp, bool Email);
    private static DateTimeOffset ToUtcOffset(DateTime dt) => dt.Kind == DateTimeKind.Utc ? new DateTimeOffset(dt, TimeSpan.Zero) : new DateTimeOffset(DateTime.SpecifyKind(dt, DateTimeKind.Utc), TimeSpan.Zero);
}
