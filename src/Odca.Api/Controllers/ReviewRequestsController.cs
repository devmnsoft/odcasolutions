using System.Security.Claims;
using Dapper;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Npgsql;
using Odca.Contracts.Reviews;

namespace Odca.Api.Controllers;

[ApiController]
[Authorize(Policy = "PasswordChanged")]
[Route("api/v1/organizations/{tenantId:guid}/reviews")]
public sealed class ReviewRequestsController(NpgsqlDataSource dataSource) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> List(Guid tenantId, [FromQuery] string? status, [FromQuery] string? scope = null,
        [FromQuery] Guid? assigneeId = null, [FromQuery] Guid? contractId = null, [FromQuery] DateOnly? from = null,
        [FromQuery] DateOnly? to = null, [FromQuery] int page = 1, [FromQuery] int pageSize = 20,
        CancellationToken ct = default)
    {
        var actor = Actor(); if (actor is null) return Unauthorized();
        await using var connection = await dataSource.OpenConnectionAsync(ct);
        if (!await Allowed(connection, actor.Value, tenantId, "tenant.reviews.read", ct)) return Forbid();
        var canManage = await Allowed(connection, actor.Value, tenantId, "tenant.reviews.decide", ct);
        scope = scope is "requested" or "assigned" or "all" ? scope : "requested";
        if ((scope == "all" || assigneeId.HasValue) && !canManage) return Forbid();
        if (from > to) return ValidationProblem("A data inicial deve ser anterior ou igual à data final.");
        await SetTenant(connection, tenantId, actor.Value, ct);
        page = Math.Max(1, page); pageSize = Math.Clamp(pageSize, 1, 100);
        const string where = """
            r.tenant_id=@tenantId
            AND (CAST(@status AS text) IS NULL OR r.status=CAST(@status AS text))
            AND (CAST(@contractId AS uuid) IS NULL OR r.contract_id=CAST(@contractId AS uuid))
            AND (CAST(@assigneeId AS uuid) IS NULL OR EXISTS(SELECT 1 FROM odca.contract_review_steps af WHERE af.tenant_id=r.tenant_id AND af.review_id=r.id AND af.reviewer_id=CAST(@assigneeId AS uuid) AND af.status='current'))
            AND (CAST(@from AS date) IS NULL OR r.opened_at >= CAST(@from AS date))
            AND (CAST(@to AS date) IS NULL OR r.opened_at < CAST(@to AS date) + INTERVAL '1 day')
            AND (@scope='all' OR (@scope='requested' AND r.requested_by=@actor) OR (@scope='assigned' AND EXISTS(SELECT 1 FROM odca.contract_review_steps ms WHERE ms.tenant_id=r.tenant_id AND ms.review_id=r.id AND ms.reviewer_id=@actor AND ms.status='current')))
            """;
        var args = new { tenantId, actor = actor.Value, status = string.IsNullOrWhiteSpace(status) ? null : status,
            scope, assigneeId, contractId, from, to, offset = (page - 1) * pageSize, pageSize };
        var total = await connection.ExecuteScalarAsync<int>(new CommandDefinition($"SELECT count(*)::integer FROM odca.contract_review_requests r WHERE {where}", args, cancellationToken: ct));
        var rows = await connection.QueryAsync<QueueRow>(new CommandDefinition($"""
            SELECT r.id AS Id,r.contract_id AS ContractId,r.requested_by AS RequestedBy,c.title AS Contract,r.status AS Status,requester.display_name AS Requester,
              assignee.display_name AS Assignee,r.opened_at AS OpenedAt,r.updated_at AS UpdatedAt,r.due_at AS DueAt,r.row_version AS Version,
              count(cm.id) FILTER(WHERE cm.visibility='client')::integer AS PublicMessages,
              count(cm.id) FILTER(WHERE cm.resolved_at IS NULL AND (@canManage OR cm.visibility='client'))::integer AS PendingComments
            FROM odca.contract_review_requests r JOIN odca.contracts c ON c.tenant_id=r.tenant_id AND c.id=r.contract_id
            JOIN odca.users requester ON requester.id=r.requested_by
            LEFT JOIN odca.contract_review_steps s ON s.tenant_id=r.tenant_id AND s.review_id=r.id AND s.status='current'
            LEFT JOIN odca.users assignee ON assignee.id=s.reviewer_id
            LEFT JOIN odca.contract_review_comments cm ON cm.tenant_id=r.tenant_id AND cm.review_id=r.id
            WHERE {where} GROUP BY r.id,c.title,requester.display_name,assignee.display_name
            ORDER BY r.updated_at DESC,r.id LIMIT @pageSize OFFSET @offset
            """, new { args.tenantId,args.actor,args.status,args.scope,args.assigneeId,args.contractId,args.from,args.to,args.offset,args.pageSize,canManage }, cancellationToken: ct));
        return Ok(new ReviewQueuePage(rows.Select(Map).ToArray(), page, pageSize, total));
    }

    [HttpGet("{reviewId:guid}")]
    public async Task<IActionResult> Detail(Guid tenantId, Guid reviewId, CancellationToken ct)
    {
        var actor = Actor(); if (actor is null) return Unauthorized();
        await using var connection = await dataSource.OpenConnectionAsync(ct);
        if (!await Allowed(connection, actor.Value, tenantId, "tenant.reviews.read", ct)) return Forbid();
        var internalAccess = await Allowed(connection, actor.Value, tenantId, "tenant.reviews.decide", ct);
        await SetTenant(connection, tenantId, actor.Value, ct);
        var row = await connection.QuerySingleOrDefaultAsync<DetailRow>(new CommandDefinition("""
            SELECT r.id AS Id,r.contract_id AS ContractId,r.requested_by AS RequestedBy,c.title AS Contract,r.status AS Status,requester.display_name AS Requester,
              assignee.display_name AS Assignee,r.instructions AS Instructions,r.opened_at AS OpenedAt,r.updated_at AS UpdatedAt,
              r.due_at AS DueAt,r.row_version AS Version,r.document_version_id AS DocumentVersionId,r.generated_version_id AS GeneratedVersionId
            FROM odca.contract_review_requests r JOIN odca.contracts c ON c.tenant_id=r.tenant_id AND c.id=r.contract_id
            JOIN odca.users requester ON requester.id=r.requested_by
            LEFT JOIN odca.contract_review_steps s ON s.tenant_id=r.tenant_id AND s.review_id=r.id AND s.status='current'
            LEFT JOIN odca.users assignee ON assignee.id=s.reviewer_id WHERE r.tenant_id=@tenantId AND r.id=@reviewId
            """, new { tenantId, reviewId }, cancellationToken: ct));
        if (row is null) return NotFound();
        if (!internalAccess && row.RequestedBy != actor.Value) return NotFound();
        var messages = await connection.QueryAsync<ReviewMessage>(new CommandDefinition("""
            SELECT cm.id AS Id,cm.body AS Body,cm.reference AS Reference,u.display_name AS Author,cm.visibility AS Visibility,
              cm.created_at AS CreatedAt,(cm.resolved_at IS NOT NULL) AS Resolved
            FROM odca.contract_review_comments cm JOIN odca.users u ON u.id=cm.author_id
            WHERE cm.tenant_id=@tenantId AND cm.review_id=@reviewId AND (@internalAccess OR cm.visibility='client') ORDER BY cm.created_at,cm.id
            """, new { tenantId, reviewId, internalAccess }, cancellationToken: ct));
        var history = await connection.QueryAsync<ReviewHistoryItem>(new CommandDefinition("""
            SELECT e.id AS Id,e.event_type AS Type,u.display_name AS Actor,e.occurred_at AS OccurredAt
            FROM odca.contract_review_events e JOIN odca.users u ON u.id=e.actor_id
            WHERE e.tenant_id=@tenantId AND e.review_id=@reviewId ORDER BY e.occurred_at,e.id
            """, new { tenantId, reviewId }, cancellationToken: ct));
        return Ok(new ReviewDetail(row.Id,row.ContractId,row.Contract,row.Status,row.Requester,row.Assignee,row.Instructions,row.OpenedAt,row.UpdatedAt,row.DueAt,row.Version,row.DocumentVersionId,row.GeneratedVersionId,messages.ToArray(),history.ToArray()));
    }

    [HttpPost("{reviewId:guid}/messages")]
    public async Task<IActionResult> AddMessage(Guid tenantId, Guid reviewId, AddReviewMessageRequest request, CancellationToken ct)
    {
        var actor = Actor(); if (actor is null) return Unauthorized();
        if (string.IsNullOrWhiteSpace(request.Body) || request.Body.Trim().Length > 4000 || request.Visibility is not ("client" or "internal")) return ValidationProblem("Informe mensagem e visibilidade válidas.");
        await using var connection = await dataSource.OpenConnectionAsync(ct);
        if (!await Allowed(connection, actor.Value, tenantId, "tenant.reviews.read", ct)) return Forbid();
        var canManage = await Allowed(connection, actor.Value, tenantId, "tenant.reviews.decide", ct);
        if (request.Visibility == "internal" && !canManage) return Forbid();
        await using var tx = await connection.BeginTransactionAsync(ct); await SetTenant(connection, tenantId, actor.Value, tx, ct);
        var id = request.IdempotencyKey;
        var inserted = await connection.ExecuteScalarAsync<bool?>(new CommandDefinition("""
            INSERT INTO odca.contract_review_comments(id,tenant_id,review_id,document_version_id,generated_version_id,author_id,body,reference,visibility)
            SELECT @id,@tenantId,r.id,r.document_version_id,r.generated_version_id,@actor,@body,@reference,@visibility FROM odca.contract_review_requests r
            WHERE r.tenant_id=@tenantId AND r.id=@reviewId AND (@canManage OR r.requested_by=@actor) AND r.status NOT IN('cancelled','superseded')
            ON CONFLICT(id) DO NOTHING RETURNING true
            """, new { id, tenantId, reviewId, actor, canManage, body=request.Body.Trim(), reference=request.Reference?.Trim(), request.Visibility }, tx, cancellationToken: ct));
        if (inserted is null)
        {
            var sameRequest = await connection.ExecuteScalarAsync<bool>(new CommandDefinition("""
                SELECT EXISTS(SELECT 1 FROM odca.contract_review_comments
                  WHERE tenant_id=@tenantId AND review_id=@reviewId AND id=@id AND author_id=@actor
                    AND body=@body AND visibility=@visibility AND reference IS NOT DISTINCT FROM @reference)
                """, new { tenantId,reviewId,id,actor,body=request.Body.Trim(),reference=request.Reference?.Trim(),request.Visibility },tx,cancellationToken:ct));
            if (!sameRequest) return Conflict(new { title="A chave de repetição já foi usada ou a solicitação não aceita novas mensagens." });
        }
        if (inserted == true) await connection.ExecuteAsync(new CommandDefinition("INSERT INTO odca.contract_review_events(tenant_id,review_id,actor_id,event_type,details) VALUES(@tenantId,@reviewId,@actor,'review.message_added',jsonb_build_object('visibility',@visibility))",new{tenantId,reviewId,actor,request.Visibility},tx,cancellationToken:ct));
        await tx.CommitAsync(ct); return Ok(new { id });
    }

    [HttpPost("{reviewId:guid}/decision")]
    public async Task<IActionResult> Decide(Guid tenantId, Guid reviewId, DecideReviewRequest request, CancellationToken ct)
    {
        var actor = Actor(); if (actor is null) return Unauthorized();
        if (request.Action is not ("approve" or "request_changes") || string.IsNullOrWhiteSpace(request.Justification))
            return ValidationProblem("Informe uma decisão e uma justificativa.");
        await using var connection = await dataSource.OpenConnectionAsync(ct);
        if (!await Allowed(connection, actor.Value, tenantId, "tenant.reviews.decide", ct)) return Forbid();
        await using var tx = await connection.BeginTransactionAsync(ct); await SetTenant(connection, tenantId, actor.Value, tx, ct);
        var review = await connection.QuerySingleOrDefaultAsync<DecisionRow>(new CommandDefinition("""
            SELECT r.row_version AS Version,r.status AS Status,r.generated_version_id AS GeneratedVersionId,
              s.id AS StepId,s.reviewer_id AS ReviewerId
            FROM odca.contract_review_requests r LEFT JOIN odca.contract_review_steps s
              ON s.tenant_id=r.tenant_id AND s.review_id=r.id AND s.status='current'
            WHERE r.tenant_id=@tenantId AND r.id=@reviewId FOR UPDATE OF r
            """, new { tenantId, reviewId }, tx, cancellationToken:ct));
        if (review is null) return NotFound();
        var prior = await connection.QuerySingleOrDefaultAsync<DecisionReceipt>(new CommandDefinition("""
            SELECT event_type AS EventType,details->>'status' AS Status,details->>'action' AS Action,
              (details->>'expectedVersion')::bigint AS ExpectedVersion
            FROM odca.contract_review_events
            WHERE tenant_id=@tenantId AND review_id=@reviewId AND details->>'idempotencyKey'=@key LIMIT 1
            """, new { tenantId, reviewId, key=request.IdempotencyKey.ToString() }, tx, cancellationToken:ct));
        if (prior is not null)
        {
            if (prior.Action != request.Action || prior.ExpectedVersion != request.ExpectedVersion)
                return Conflict(new { title="A chave de repetição já foi usada para outra decisão." });
            await tx.CommitAsync(ct);
            return Ok(new { status=prior.Status, version=request.ExpectedVersion+1, replayed=true });
        }
        if (review.Status != "in_review" || review.StepId is null || review.ReviewerId != actor.Value)
            return Conflict(new { title="A revisão não está disponível para decisão deste responsável." });
        if (review.Version != request.ExpectedVersion) return Conflict(new { title="A revisão foi alterada em outra sessão.", currentVersion=review.Version });
        var status=request.Action=="approve"?"internally_approved":"changes_requested";
        var stepStatus=request.Action=="approve"?"approved":"changes_requested";
        var updated = await connection.ExecuteScalarAsync<int?>(new CommandDefinition("""
            WITH decided_step AS (
              UPDATE odca.contract_review_steps SET status=@stepStatus,decided_by=@actor,decided_at=now(),justification=@reason
              WHERE tenant_id=@tenantId AND id=@stepId AND status='current' RETURNING 1
            ), decided_review AS (
              UPDATE odca.contract_review_requests SET status=@status,completed_at=CASE WHEN @status='internally_approved' THEN now() ELSE NULL END,row_version=row_version+1,updated_at=now()
              WHERE tenant_id=@tenantId AND id=@reviewId AND status='in_review' AND row_version=@expected
                AND EXISTS(SELECT 1 FROM decided_step) RETURNING 1
            ), version_update AS (
              UPDATE odca.generated_contract_versions SET review_status=CASE WHEN @status='internally_approved' THEN 'internally_approved' ELSE review_status END
              WHERE tenant_id=@tenantId AND id=@generatedVersionId AND EXISTS(SELECT 1 FROM decided_review) RETURNING 1
            ), change_update AS (
              UPDATE odca.contract_change_requests SET status=CASE WHEN @status='internally_approved' THEN 'awaiting_formalization' ELSE 'draft' END,row_version=row_version+1,updated_at=now()
              WHERE tenant_id=@tenantId AND review_id=@reviewId AND generated_version_id=@generatedVersionId AND status='in_review'
                AND EXISTS(SELECT 1 FROM decided_review) RETURNING 1
            ), inserted_event AS (
            INSERT INTO odca.contract_review_events(tenant_id,review_id,actor_id,event_type,details)
            SELECT @tenantId,@reviewId,@actor,@event,jsonb_build_object('status',@status,'action',@action,'expectedVersion',@expected,'justification',@reason,'idempotencyKey',@key)
            WHERE EXISTS(SELECT 1 FROM decided_review) RETURNING 1
            ) SELECT count(*)::integer FROM decided_review;
            """, new { tenantId, reviewId, actor, review.StepId, stepStatus, status, action=request.Action,
                reason=request.Justification.Trim(), expected=request.ExpectedVersion, review.GeneratedVersionId,
                Event=$"review.{request.Action}", key=request.IdempotencyKey.ToString() }, tx, cancellationToken:ct));
        if (updated != 1) return Conflict(new { title="A revisão foi alterada em outra sessão.", currentVersion=review.Version });
        await tx.CommitAsync(ct); return Ok(new { status, version=request.ExpectedVersion+1, replayed=false });
    }

    private Guid? Actor()=>Guid.TryParse(User.FindFirstValue("sub"),out var id)?id:null;
    private static async Task<bool> Allowed(NpgsqlConnection c,Guid actor,Guid tenant,string permission,CancellationToken ct)=>await c.ExecuteScalarAsync<bool>(new CommandDefinition("SELECT odca.has_tenant_permission(@actor,@tenant,@permission)",new{actor,tenant,permission},cancellationToken:ct));
    private static Task SetTenant(NpgsqlConnection c,Guid tenant,Guid actor,CancellationToken ct)=>c.ExecuteAsync(new CommandDefinition("SELECT set_config('odca.tenant_id',@tenant::text,false),set_config('odca.user_id',@actor::text,false)",new{tenant,actor},cancellationToken:ct));
    private static Task SetTenant(NpgsqlConnection c,Guid tenant,Guid actor,NpgsqlTransaction tx,CancellationToken ct)=>c.ExecuteAsync(new CommandDefinition("SELECT set_config('odca.tenant_id',@tenant::text,true),set_config('odca.user_id',@actor::text,true)",new{tenant,actor},tx,cancellationToken:ct));
    private static ReviewQueueItem Map(QueueRow r)=>new(r.Id,r.ContractId,r.Contract,r.Status,r.Requester,r.Assignee,r.OpenedAt,r.UpdatedAt,r.DueAt,r.Version,r.PublicMessages,r.PendingComments);
    private sealed record QueueRow(Guid Id,Guid ContractId,string Contract,string Status,string Requester,string? Assignee,DateTimeOffset OpenedAt,DateTimeOffset UpdatedAt,DateTimeOffset? DueAt,long Version,int PublicMessages,int PendingComments);
    private sealed record DetailRow(Guid Id,Guid ContractId,Guid RequestedBy,string Contract,string Status,string Requester,string? Assignee,string? Instructions,DateTimeOffset OpenedAt,DateTimeOffset UpdatedAt,DateTimeOffset? DueAt,long Version,Guid? DocumentVersionId,Guid? GeneratedVersionId);
    private sealed record DecisionRow(long Version,string Status,Guid? GeneratedVersionId,Guid? StepId,Guid? ReviewerId);
    private sealed record DecisionReceipt(string EventType,string Status,string Action,long ExpectedVersion);
}
