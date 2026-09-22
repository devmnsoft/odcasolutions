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
    public async Task<IActionResult> List(Guid tenantId, [FromQuery] string? status, [FromQuery] bool mine = false,
        [FromQuery] int page = 1, [FromQuery] int pageSize = 20, CancellationToken ct = default)
    {
        var actor = Actor(); if (actor is null) return Unauthorized();
        await using var connection = await dataSource.OpenConnectionAsync(ct);
        if (!await Allowed(connection, actor.Value, tenantId, "tenant.reviews.read", ct)) return Forbid();
        if (!mine && !await Allowed(connection, actor.Value, tenantId, "tenant.reviews.decide", ct)) return Forbid();
        await SetTenant(connection, tenantId, actor.Value, ct);
        page = Math.Max(1, page); pageSize = Math.Clamp(pageSize, 1, 100);
        const string where = """
            r.tenant_id=@tenantId
            AND (CAST(@status AS text) IS NULL OR r.status=CAST(@status AS text))
            AND (NOT @mine OR r.requested_by=@actor OR EXISTS(SELECT 1 FROM odca.contract_review_steps ms WHERE ms.tenant_id=r.tenant_id AND ms.review_id=r.id AND ms.reviewer_id=@actor AND ms.status='current'))
            """;
        var args = new { tenantId, actor, status = string.IsNullOrWhiteSpace(status) ? null : status, mine, offset = (page - 1) * pageSize, pageSize };
        var total = await connection.ExecuteScalarAsync<int>(new CommandDefinition($"SELECT count(*)::integer FROM odca.contract_review_requests r WHERE {where}", args, cancellationToken: ct));
        var rows = await connection.QueryAsync<QueueRow>(new CommandDefinition($"""
            SELECT r.id AS Id,r.contract_id AS ContractId,r.requested_by AS RequestedBy,c.title AS Contract,r.status AS Status,requester.display_name AS Requester,
              assignee.display_name AS Assignee,r.opened_at AS OpenedAt,r.updated_at AS UpdatedAt,r.due_at AS DueAt,r.row_version AS Version,
              count(cm.id) FILTER(WHERE cm.visibility='client')::integer AS PublicMessages,
              count(cm.id) FILTER(WHERE cm.resolved_at IS NULL)::integer AS PendingComments
            FROM odca.contract_review_requests r JOIN odca.contracts c ON c.tenant_id=r.tenant_id AND c.id=r.contract_id
            JOIN odca.users requester ON requester.id=r.requested_by
            LEFT JOIN odca.contract_review_steps s ON s.tenant_id=r.tenant_id AND s.review_id=r.id AND s.status='current'
            LEFT JOIN odca.users assignee ON assignee.id=s.reviewer_id
            LEFT JOIN odca.contract_review_comments cm ON cm.tenant_id=r.tenant_id AND cm.review_id=r.id
            WHERE {where} GROUP BY r.id,c.title,requester.display_name,assignee.display_name
            ORDER BY r.updated_at DESC,r.id LIMIT @pageSize OFFSET @offset
            """, args, cancellationToken: ct));
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
              r.due_at AS DueAt,r.row_version AS Version
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
        return Ok(new ReviewDetail(row.Id,row.ContractId,row.Contract,row.Status,row.Requester,row.Assignee,row.Instructions,row.OpenedAt,row.UpdatedAt,row.DueAt,row.Version,messages.ToArray(),history.ToArray()));
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
        if (inserted is null && !await connection.ExecuteScalarAsync<bool>(new CommandDefinition("SELECT EXISTS(SELECT 1 FROM odca.contract_review_comments WHERE tenant_id=@tenantId AND review_id=@reviewId AND id=@id)",new{tenantId,reviewId,id},tx,cancellationToken:ct))) return Conflict(new { title="A solicitação não aceita novas mensagens." });
        if (inserted == true) await connection.ExecuteAsync(new CommandDefinition("INSERT INTO odca.contract_review_events(tenant_id,review_id,actor_id,event_type,details) VALUES(@tenantId,@reviewId,@actor,'review.message_added',jsonb_build_object('visibility',@visibility))",new{tenantId,reviewId,actor,request.Visibility},tx,cancellationToken:ct));
        await tx.CommitAsync(ct); return Ok(new { id });
    }

    private Guid? Actor()=>Guid.TryParse(User.FindFirstValue("sub"),out var id)?id:null;
    private static async Task<bool> Allowed(NpgsqlConnection c,Guid actor,Guid tenant,string permission,CancellationToken ct)=>await c.ExecuteScalarAsync<bool>(new CommandDefinition("SELECT odca.has_tenant_permission(@actor,@tenant,@permission)",new{actor,tenant,permission},cancellationToken:ct));
    private static Task SetTenant(NpgsqlConnection c,Guid tenant,Guid actor,CancellationToken ct)=>c.ExecuteAsync(new CommandDefinition("SELECT set_config('odca.tenant_id',@tenant::text,false),set_config('odca.user_id',@actor::text,false)",new{tenant,actor},cancellationToken:ct));
    private static Task SetTenant(NpgsqlConnection c,Guid tenant,Guid actor,NpgsqlTransaction tx,CancellationToken ct)=>c.ExecuteAsync(new CommandDefinition("SELECT set_config('odca.tenant_id',@tenant::text,true),set_config('odca.user_id',@actor::text,true)",new{tenant,actor},tx,cancellationToken:ct));
    private static ReviewQueueItem Map(QueueRow r)=>new(r.Id,r.ContractId,r.Contract,r.Status,r.Requester,r.Assignee,r.OpenedAt,r.UpdatedAt,r.DueAt,r.Version,r.PublicMessages,r.PendingComments);
    private sealed record QueueRow(Guid Id,Guid ContractId,string Contract,string Status,string Requester,string? Assignee,DateTimeOffset OpenedAt,DateTimeOffset UpdatedAt,DateTimeOffset? DueAt,long Version,int PublicMessages,int PendingComments);
    private sealed record DetailRow(Guid Id,Guid ContractId,Guid RequestedBy,string Contract,string Status,string Requester,string? Assignee,string? Instructions,DateTimeOffset OpenedAt,DateTimeOffset UpdatedAt,DateTimeOffset? DueAt,long Version);
}
