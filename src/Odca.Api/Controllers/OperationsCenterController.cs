using System.Security.Claims;
using Dapper;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Npgsql;

namespace Odca.Api.Controllers;

/// <summary>
/// Central de Ação — visão orientada à ação que transforma contratos, assinaturas,
/// obrigações e solicitações em itens concretos para o usuário atuar.
/// Reutiliza dados existentes; não cria estado paralelo.
/// </summary>
[ApiController]
[Authorize(Policy = "PasswordChanged")]
[Route("api/v1/organizations/{tenantId:guid}/operations")]
public sealed class OperationsCenterController(NpgsqlDataSource dataSource) : ControllerBase
{
    /// <summary>
    /// GET /api/v1/organizations/{tenantId}/operations/action-center
    /// Returns aggregated action items grouped by category.
    /// </summary>
    [HttpGet("action-center")]
    public async Task<IActionResult> ActionCenter(Guid tenantId, [FromQuery] bool mine = true, CancellationToken ct = default)
    {
        var actor = Actor(); if (actor is null) return Unauthorized();
        await using var c = await dataSource.OpenConnectionAsync(ct);
        if (!await c.ExecuteScalarAsync<bool>(new CommandDefinition("SELECT odca.tenant_actor_has_permission(@actor,@tenant,'tenant.obligations.read') OR odca.tenant_actor_has_permission(@actor,@tenant,'tenant.renewals.read') OR odca.tenant_actor_has_permission(@actor,@tenant,'tenant.solicitations.read')", new { actor, tenant = tenantId }, cancellationToken: ct)))
            return Forbid();
        await using var tx = await c.BeginTransactionAsync(ct);
        await c.ExecuteAsync(new CommandDefinition("SELECT set_config('odca.tenant_id',@t,true),set_config('odca.actor_id',@a,true)", new { t = tenantId.ToString(), a = actor.ToString() }, tx, cancellationToken: ct));
        var today = await c.ExecuteScalarAsync<DateOnly>(new CommandDefinition("SELECT (now() AT TIME ZONE timezone)::date FROM odca.tenants WHERE id=@tenantId", new { tenantId }, tx, cancellationToken: ct));

        const string query = """
            WITH
            -- Ações sobre contratos de alteração (minha ação ou aguardando)
            change_actions AS (
                SELECT
                    'contract_change' AS source_type,
                    r.id::text AS source_id,
                    CASE
                        WHEN r.status='draft' AND (r.responsible_id=@actor OR @mine=false) THEN 'my_action'
                        WHEN r.status='in_review' AND r.responsible_id IS DISTINCT FROM @actor THEN 'waiting_other'
                        WHEN r.status='formalized' AND r.application_status='not_applied' AND r.effective_on<=@today AND (r.responsible_id=@actor OR @mine=false) THEN 'my_action'
                        WHEN r.status='formalized' AND r.application_status='scheduled' AND r.effective_on>@today THEN 'upcoming'
                        WHEN r.status='conflict' THEN 'conflict'
                        ELSE 'waiting_other'
                    END AS category,
                    c.title AS context,
                    r.reason AS reason,
                    u.display_name AS responsible,
                    CASE WHEN r.status='draft' THEN r.effective_on
                         WHEN r.status='formalized' THEN r.effective_on
                         ELSE NULL END AS deadline,
                    CASE
                        WHEN r.status='draft' THEN 'Submeter proposta'
                        WHEN r.status='in_review' THEN 'Aguardando formalização'
                        WHEN r.status='formalized' AND r.application_status='not_applied' AND r.effective_on<=@today THEN 'Aplicar alteração'
                        WHEN r.status='formalized' AND r.application_status='scheduled' THEN 'Aguardar efetivação'
                        WHEN r.status='conflict' THEN 'Resolver conflito'
                        ELSE 'Acompanhar'
                    END AS next_action,
                    r.priority AS priority,
                    r.created_at AS created_at
                FROM odca.contract_change_requests r
                JOIN odca.contracts c ON c.tenant_id=r.tenant_id AND c.id=r.contract_id
                LEFT JOIN odca.users u ON u.id=r.responsible_id
                WHERE r.tenant_id=@tenantId
                  AND r.status NOT IN ('cancelled')
                  AND (@mine=false OR r.responsible_id=@actor)
            ),
            -- Assinaturas pendentes
            signature_actions AS (
                SELECT
                    'signature_prep' AS source_type,
                    sp.id::text AS source_id,
                    CASE
                        WHEN sp.status='confirmed' AND @mine=true THEN 'waiting_other'
                        WHEN sp.status='confirmed' AND @mine=false THEN 'my_action'
                        ELSE 'waiting_other'
                    END AS category,
                    dv.display_name AS context,
                    'Assinatura em andamento' AS reason,
                    NULL::text AS responsible,
                    NULL::date AS deadline,
                    CASE WHEN sp.status='confirmed' THEN 'Aguardar signatários' ELSE 'Preparar assinatura' END AS next_action,
                    'normal' AS priority,
                    sp.created_at AS created_at
                FROM odca.signature_preparations sp
                LEFT JOIN odca.generated_contract_versions dv ON dv.id=sp.generated_version_id
                WHERE sp.tenant_id=@tenantId AND sp.status IN ('draft','confirmed','partially_signed')
            ),
            -- Obrigações com prazo
            obligation_actions AS (
                SELECT
                    'obligation' AS source_type,
                    o.id::text AS source_id,
                    CASE
                        WHEN o.due_date < @today AND o.status NOT IN ('completed','waived') THEN 'overdue'
                        WHEN o.due_date >= @today AND o.due_date <= @today + 30 AND o.status NOT IN ('completed','waived') THEN 'upcoming'
                        ELSE 'my_action'
                    END AS category,
                    o.title AS context,
                    COALESCE(o.description, '') AS reason,
                    NULL::text AS responsible,
                    o.due_date AS deadline,
                    'Cumprir obrigação' AS next_action,
                    CASE WHEN o.due_date < @today THEN 'high' ELSE 'normal' END AS priority,
                    o.created_at AS created_at
                FROM odca.contract_obligations o
                WHERE o.tenant_id=@tenantId AND o.status NOT IN ('completed','waived','cancelled')
            ),
            -- Solicitações ODCA (aguardando cliente ou em atendimento)
            solicitation_actions AS (
                SELECT
                    'solicitation' AS source_type,
                    s.id::text AS source_id,
                    CASE
                        WHEN s.status='awaiting_customer' THEN 'my_action'
                        WHEN s.status='in_progress' OR s.status='open' THEN 'waiting_other'
                        WHEN s.status='paused' THEN 'upcoming'
                        ELSE 'waiting_other'
                    END AS category,
                    s.subject AS context,
                    s.service_type AS reason,
                    NULL::text AS responsible,
                    s.sla_due_on AS deadline,
                    CASE WHEN s.status='awaiting_customer' THEN 'Responder solicitação'
                         WHEN s.status='in_progress' THEN 'Aguardar plataforma'
                         WHEN s.status='paused' THEN 'Solicitação pausada'
                         ELSE 'Aguardando triagem' END AS next_action,
                    s.priority AS priority,
                    s.created_at AS created_at
                FROM odca.solicitations s
                WHERE s.tenant_id=@tenantId AND s.status NOT IN ('closed','resolved','cancelled')
                  AND (@mine=false OR s.created_by=@actor OR EXISTS(SELECT 1 FROM odca.memberships m WHERE m.tenant_id=s.tenant_id AND m.user_id=@actor))
            ),
            -- Contratos expirando/vencidos
            contract_deadlines AS (
                SELECT
                    'contract_expiry' AS source_type,
                    c.id::text AS source_id,
                    CASE WHEN c.end_date < @today THEN 'overdue' ELSE 'upcoming' END AS category,
                    c.title AS context,
                    CASE WHEN c.end_date < @today THEN 'Contrato vencido' ELSE 'Contrato expira em breve' END AS reason,
                    cu.display_name AS responsible,
                    c.end_date AS deadline,
                    CASE WHEN c.end_date < @today THEN 'Analisar renovação' ELSE 'Decidir sobre renovação' END AS next_action,
                    'critical' AS priority,
                    c.created_at AS created_at
                FROM odca.contracts c
                LEFT JOIN odca.users cu ON cu.id=c.owner_id
                WHERE c.tenant_id=@tenantId AND c.status='active' AND c.end_date IS NOT NULL
                  AND c.end_date <= @today + 90
            )
            SELECT * FROM change_actions
            UNION ALL SELECT * FROM signature_actions
            UNION ALL SELECT * FROM obligation_actions
            UNION ALL SELECT * FROM solicitation_actions
            UNION ALL SELECT * FROM contract_deadlines
            ORDER BY
                CASE priority WHEN 'critical' THEN 0 WHEN 'high' THEN 1 WHEN 'normal' THEN 2 ELSE 3 END,
                COALESCE(deadline, '9999-12-31'::date),
                created_at DESC
            LIMIT 200
            """;

        var items = (await c.QueryAsync<ActionItemRow>(new CommandDefinition(query, new { actor, tenantId, today, mine }, tx, cancellationToken: ct))).AsList();
        await tx.CommitAsync(ct);

        // Group by category
        var myAction = items.Where(i => i.Category == "my_action").ToList();
        var waitingOther = items.Where(i => i.Category == "waiting_other").ToList();
        var upcoming = items.Where(i => i.Category == "upcoming").ToList();
        var overdue = items.Where(i => i.Category == "overdue").ToList();
        var conflicts = items.Where(i => i.Category == "conflict").ToList();

        return Ok(new ActionCenterResponse(
            MyAction: myAction.Select(ToDto).ToList(),
            WaitingOther: waitingOther.Select(ToDto).ToList(),
            UpcomingDeadlines: upcoming.Select(ToDto).ToList(),
            Overdue: overdue.Select(ToDto).ToList(),
            ConflictsAndFailures: conflicts.Select(ToDto).ToList(),
            Total: items.Count,
            Today: today));
    }

    private Guid? Actor() => Guid.TryParse(User.FindFirstValue("sub"), out var id) ? id : null;

    private static OperationActionDto ToDto(ActionItemRow r) => new(
        r.SourceType, r.SourceId, r.Context, r.Reason, r.Responsible,
        r.Deadline, r.NextAction, r.Priority);

    private sealed record ActionItemRow(
        string SourceType, string SourceId, string Category,
        string Context, string Reason, string? Responsible,
        DateOnly? Deadline, string NextAction, string Priority, DateTime CreatedAt);

    public sealed record OperationActionDto(
        string SourceType, string SourceId, string Context, string Reason,
        string? Responsible, DateOnly? Deadline, string NextAction, string Priority);

    public sealed record ActionCenterResponse(
        IReadOnlyList<OperationActionDto> MyAction,
        IReadOnlyList<OperationActionDto> WaitingOther,
        IReadOnlyList<OperationActionDto> UpcomingDeadlines,
        IReadOnlyList<OperationActionDto> Overdue,
        IReadOnlyList<OperationActionDto> ConflictsAndFailures,
        int Total, DateOnly Today);
}
