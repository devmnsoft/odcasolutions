using System.Security.Claims;
using Dapper;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Npgsql;

namespace Odca.Api.Controllers;

/// <summary>
/// Central de Ação — visão orientada à ação que transforma contratos, assinaturas,
/// obrigações e solicitações em itens concretos para o usuário atuar.
/// Cada fonte é autorizada separadamente (item 7). "Minhas" usa o dono/responsável;
/// "Organização" requer permissão ampliada quando disponível (item 8).
/// </summary>
[ApiController]
[Authorize(Policy = "PasswordChanged")]
[Route("api/v1/organizations/{tenantId:guid}/operations")]
public sealed class OperationsCenterController(NpgsqlDataSource dataSource) : ControllerBase
{
    [HttpGet("action-center")]
    public async Task<IActionResult> ActionCenter(
        Guid tenantId,
        [FromQuery] string scope = "mine",
        [FromQuery] string? source = null,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 50,
        CancellationToken ct = default)
    {
        if (pageSize is < 1 or > 100) pageSize = 50;
        if (page < 1) page = 1;
        var mine = scope != "organization";
        var actorNullable = Actor();
        if (actorNullable is null) return Unauthorized();
        var actorId = actorNullable.Value;

        await using var c = await dataSource.OpenConnectionAsync(ct);
        await using var tx = await c.BeginTransactionAsync(ct);
        await c.ExecuteAsync(new CommandDefinition(
            "SELECT set_config('odca.tenant_id',@t,true),set_config('odca.actor_id',@a,true)",
            new { t = tenantId.ToString(), a = actorId.ToString() }, tx, cancellationToken: ct));
        var today = await c.ExecuteScalarAsync<DateOnly>(new CommandDefinition(
            "SELECT (now() AT TIME ZONE timezone)::date FROM odca.tenants WHERE id=@tenantId",
            new { tenantId }, tx, cancellationToken: ct));

        // PER-SOURCE AUTHORIZATION (item 7): each source requires its own permission.
        // A restricted user sees ONLY sources they can read.
        var perms = new
        {
            renewals      = await Perm(c, tx, actorId, tenantId, "tenant.renewals.read", ct),
            obligations   = await Perm(c, tx, actorId, tenantId, mine ? "tenant.obligations.read" : "tenant.obligations.read_all", ct),
            solicitations = await Perm(c, tx, actorId, tenantId, "tenant.solicitations.read", ct),
            signatures    = await Perm(c, tx, actorId, tenantId, "tenant.contracts.read", ct),
            contracts     = await Perm(c, tx, actorId, tenantId, "tenant.contracts.read", ct)
        };

        if (!perms.renewals && !perms.obligations && !perms.solicitations && !perms.signatures && !perms.contracts)
            return Ok(new ActionCenterResponse([], [], [], [], [], [], 0, today, page, pageSize));

        bool Include(string src) => source is null || source == src;
        var ctes = new List<string>();
        var names = new List<string>();

        // 1. Contract changes (renewals/amendments/revisions)
        if (perms.renewals && Include("renewal"))
        {
            ctes.Add(@"
            change_actions AS (
                SELECT 'renewal' AS source_type, r.id::text AS source_id,
                    CASE
                        WHEN r.status='draft' AND (r.responsible_id=@actor OR @mine=false) THEN 'my_action'
                        WHEN r.status='in_review' AND r.responsible_id IS DISTINCT FROM @actor THEN 'waiting_other'
                        WHEN r.status='formalized' AND r.application_status='not_applied' AND r.effective_on<=@today
                             AND (r.responsible_id=@actor OR @mine=false) THEN 'my_action'
                        WHEN r.status='formalized' AND r.application_status IN ('scheduled','applied') THEN 'info'
                        WHEN r.status='conflict' THEN 'conflict'
                        ELSE 'waiting_other'
                    END AS category,
                    c.title AS context, r.reason AS reason, u.display_name AS responsible,
                    CASE WHEN r.status IN ('draft','formalized') THEN r.effective_on ELSE NULL END AS deadline,
                    r.priority AS priority, r.created_at AS created_at
                FROM odca.contract_change_requests r
                JOIN odca.contracts c ON c.tenant_id=r.tenant_id AND c.id=r.contract_id
                LEFT JOIN odca.users u ON u.id=r.responsible_id
                WHERE r.tenant_id=@tenantId AND r.status NOT IN ('cancelled')
                  AND (@mine=false OR r.responsible_id=@actor))");
            names.Add("change_actions");
        }

        // 2. Signatures (item 8: actual participation via membership)
        if (perms.signatures && Include("signature"))
        {
            ctes.Add(@"
            signature_actions AS (
                SELECT 'signature' AS source_type, sp.id::text AS source_id,
                    CASE
                        WHEN sp.status='draft' AND @mine=true THEN 'my_action'
                        ELSE 'waiting_other'
                    END AS category,
                    COALESCE(sc.title, 'Documento gerado') AS context,
                    'Assinatura em andamento' AS reason,
                    NULL::text AS responsible, NULL::date AS deadline,
                    'normal' AS priority, sp.created_at AS created_at
                FROM odca.signature_preparations sp
                LEFT JOIN odca.generated_contract_versions dv ON dv.id=sp.generated_version_id
                LEFT JOIN odca.contracts sc ON sc.id=dv.contract_id AND sc.tenant_id=sp.tenant_id
                WHERE sp.tenant_id=@tenantId AND sp.status IN ('draft','confirmed')
                  AND (@mine=false OR EXISTS (
                      SELECT 1 FROM odca.signature_participants spart
                      WHERE spart.preparation_id=sp.id AND spart.identity_membership_id=(
                          SELECT m.id FROM odca.memberships m WHERE m.tenant_id=sp.tenant_id AND m.user_id=@actor LIMIT 1))))");
            names.Add("signature_actions");
        }

        // 3. Obligations (item 8: owner_id based ownership)
        if (perms.obligations && Include("obligation"))
        {
            ctes.Add(@"
            obligation_actions AS (
                SELECT 'obligation' AS source_type, o.id::text AS source_id,
                    CASE
                        WHEN o.due_date < @today THEN 'overdue'
                        WHEN o.due_date >= @today AND o.due_date <= @today + 30 THEN 'upcoming'
                        ELSE 'my_action'
                    END AS category,
                    o.title AS context, COALESCE(o.description, '') AS reason,
                    ou.display_name AS responsible, o.due_date AS deadline,
                    CASE WHEN o.due_date < @today THEN 'critical' ELSE o.priority END AS priority,
                    o.created_at AS created_at
                FROM odca.contract_obligations o
                LEFT JOIN odca.users ou ON ou.id=o.owner_id
                WHERE o.tenant_id=@tenantId AND o.status NOT IN ('completed','waived','cancelled') AND o.deleted_at IS NULL
                  AND (@mine=false OR o.owner_id=@actor))");
            names.Add("obligation_actions");
        }

        // 4. Solicitations (item 8: opened_by for mine — NOT any member of org)
        if (perms.solicitations && Include("solicitation"))
        {
            ctes.Add(@"
            solicitation_actions AS (
                SELECT 'solicitation' AS source_type, s.id::text AS source_id,
                    CASE
                        WHEN s.status='aguardando_cliente' THEN 'my_action'
                        WHEN s.status IN ('aberta','triagem','em_atendimento') THEN 'waiting_other'
                        ELSE 'waiting_other'
                    END AS category,
                    s.subject AS context, s.service AS reason,
                    NULL::text AS responsible,
                    CASE WHEN s.status IN ('aberta','triagem','em_atendimento','aguardando_cliente')
                         THEN s.resolution_due_at::date ELSE NULL END AS deadline,
                    s.priority AS priority, s.opened_at AS created_at
                FROM odca.solicitations s
                WHERE s.tenant_id=@tenantId AND s.status NOT IN ('resolvida','encerrada','cancelada')
                  AND (@mine=false OR s.opened_by=@actor))");
            names.Add("solicitation_actions");
        }

        // 5. Contract expiry deadlines
        if (perms.contracts && Include("contract"))
        {
            ctes.Add(@"
            contract_deadlines AS (
                SELECT 'contract' AS source_type, c.id::text AS source_id,
                    CASE WHEN c.end_date < @today THEN 'overdue' ELSE 'upcoming' END AS category,
                    c.title AS context,
                    CASE WHEN c.end_date < @today THEN 'Contrato vencido' ELSE 'Contrato expira em breve' END AS reason,
                    cu.display_name AS responsible, c.end_date AS deadline,
                    'critical' AS priority, c.created_at AS created_at
                FROM odca.contracts c
                LEFT JOIN odca.users cu ON cu.id=c.owner_id
                WHERE c.tenant_id=@tenantId AND c.archived_at IS NULL AND c.closed_at IS NULL
                  AND c.end_date IS NOT NULL AND c.end_date <= @today + 90
                  AND (@mine=false OR c.owner_id=@actor))");
            names.Add("contract_deadlines");
        }

        if (ctes.Count == 0)
            return Ok(new ActionCenterResponse([], [], [], [], [], [], 0, today, page, pageSize));

        var allItems = string.Join("\nUNION ALL\n", names.Select(n => $"SELECT * FROM {n}"));

        var countQuery = $"WITH {string.Join(",\n", ctes)}\nSELECT COUNT(*) FROM ({allItems}) all_items";
        var total = await c.ExecuteScalarAsync<int>(new CommandDefinition(
            countQuery, new { actor = actorId, tenantId, today, mine }, tx, cancellationToken: ct));

        var dataQuery = $@"WITH {string.Join(",\n", ctes)}
SELECT * FROM ({allItems}) all_items
ORDER BY
    CASE priority WHEN 'critical' THEN 0 WHEN 'high' THEN 1 WHEN 'normal' THEN 2 ELSE 3 END,
    COALESCE(deadline, '9999-12-31'::date),
    created_at DESC
OFFSET @offset LIMIT @limit";

        var items = (await c.QueryAsync<ActionItemRow>(new CommandDefinition(
            dataQuery,
            new { actor = actorId, tenantId, today, mine, offset = (page - 1) * pageSize, limit = pageSize },
            tx, cancellationToken: ct))).AsList();
        await tx.CommitAsync(ct);

        // Group by category (item 9: proper state classification)
        return Ok(new ActionCenterResponse(
            items.Where(i => i.category == "my_action").Select(ToDto).ToList(),
            items.Where(i => i.category == "waiting_other").Select(ToDto).ToList(),
            items.Where(i => i.category == "upcoming").Select(ToDto).ToList(),
            items.Where(i => i.category == "overdue").Select(ToDto).ToList(),
            items.Where(i => i.category == "conflict").Select(ToDto).ToList(),
            items.Where(i => i.category == "info").Select(ToDto).ToList(),
            total, today, page, pageSize));
    }

    private static async Task<bool> Perm(NpgsqlConnection c, NpgsqlTransaction tx, Guid actor, Guid tenant, string permission, CancellationToken ct) =>
        await c.ExecuteScalarAsync<bool>(new CommandDefinition(
            "SELECT odca.tenant_actor_has_permission(@actor,@tenant,@perm)",
            new { actor, tenant, perm = permission }, tx, cancellationToken: ct));

    private Guid? Actor() => Guid.TryParse(User.FindFirstValue("sub"), out var id) ? id : null;

    private static OperationActionDto ToDto(ActionItemRow r) => new(
        r.source_type, r.source_id, r.context, r.reason, r.responsible,
        r.deadline, GetNextAction(r.source_type, r.category), r.priority, r.category);

    private static string GetNextAction(string sourceType, string category) =>
        (sourceType, category) switch
        {
            ("renewal", "my_action") => "Tratar alteracao contratual",
            ("renewal", "conflict") => "Resolver conflito de alteracao",
            ("renewal", "waiting_other") => "Aguardando formalizacao/aprovacao",
            ("renewal", "info") => "Alteracao programada - acompanhar",
            ("signature", "my_action") => "Assinar documento",
            ("signature", "waiting_other") => "Aguardando demais signatarios",
            ("obligation", "overdue") => "Cumprir obrigacao atrasada",
            ("obligation", "upcoming") => "Preparar cumprimento de obrigacao",
            ("obligation", "my_action") => "Cumprir obrigacao",
            ("solicitation", "my_action") => "Responder solicitacao",
            ("solicitation", "waiting_other") => "Aguardando atendimento ODCA",
            ("solicitation", "info") => "Solicitacao pausada",
            ("contract", "overdue") => "Analisar contrato vencido",
            ("contract", "upcoming") => "Decidir sobre renovacao",
            _ => "Consultar detalhes"
        };

    private sealed record ActionItemRow(
        string source_type, string source_id, string category,
        string context, string reason, string? responsible,
        DateOnly? deadline, string priority, DateTime created_at);

    public sealed record OperationActionDto(
        string SourceType, string SourceId, string Context, string Reason,
        string? Responsible, DateOnly? Deadline, string NextAction, string Priority,
        string Category);

    public sealed record ActionCenterResponse(
        IReadOnlyList<OperationActionDto> MyAction,
        IReadOnlyList<OperationActionDto> WaitingOther,
        IReadOnlyList<OperationActionDto> UpcomingDeadlines,
        IReadOnlyList<OperationActionDto> Overdue,
        IReadOnlyList<OperationActionDto> ConflictsAndFailures,
        IReadOnlyList<OperationActionDto> Info,
        int Total, DateOnly Today, int Page, int PageSize);
}
