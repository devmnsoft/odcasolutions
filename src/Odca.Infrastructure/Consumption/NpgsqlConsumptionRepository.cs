using Dapper;
using Npgsql;
using Odca.Application.Consumption;
using Odca.Contracts.Consumption;

namespace Odca.Infrastructure.Consumption;

public sealed class NpgsqlConsumptionRepository(NpgsqlDataSource dataSource) : IConsumptionRepository
{
    public async Task<ConsumptionSummary?> GetSummaryAsync(Guid actorId, Guid tenantId, bool platformAccess, CancellationToken cancellationToken)
    {
        await using var c = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var tx = await c.BeginTransactionAsync(cancellationToken);
        await Context(c, tx, actorId, tenantId, cancellationToken);
        if (!platformAccess && !await Allowed(c, tx, actorId, tenantId, "tenant.billing.read", cancellationToken)) return null;
        var row = await c.QuerySingleOrDefaultAsync<SummaryRow>(new CommandDefinition(SummarySql, new { tenantId }, tx, cancellationToken: cancellationToken));
        if (row is null) return null;
        var credits = (await c.QueryAsync<ResourceBalance>(new CommandDefinition(CreditsSql, new { tenantId }, tx, cancellationToken: cancellationToken))).AsList();
        var historyRows = (await c.QueryAsync<ConsumptionEventRow>(new CommandDefinition(HistorySql, new { tenantId }, tx, cancellationToken: cancellationToken))).AsList();
        var history = historyRows.Select(row => new ConsumptionEvent(row.Id, row.Type, row.Resource, row.Quantity, row.Unit, row.Reason, row.ActorName, new DateTimeOffset(row.OccurredAt))).ToArray();
        await tx.CommitAsync(cancellationToken);
        var limit = checked(row.ContractedStorageBytes + row.AdditionalStorageBytes);
        return new(row.TenantId,row.OrganizationName,row.TenantStatus,row.SubscriptionStatus,row.PlanCode,row.PlanName,row.PlanVersion,row.PeriodStart,row.PeriodEnd,row.ContractedSeats,row.ActiveUsers,row.ReservedInvitations,Math.Max(0,row.ContractedSeats-row.ActiveUsers-row.ReservedInvitations),row.ContractedStorageBytes,row.AdditionalStorageBytes,row.UsedStorageBytes,row.ReservedStorageBytes,Math.Max(0,limit-row.UsedStorageBytes-row.ReservedStorageBytes),row.MaximumFileBytes,credits,history);
    }

    public async Task<IReadOnlyList<StoragePackage>> ListPackagesAsync(CancellationToken cancellationToken)
    { await using var c=await dataSource.OpenConnectionAsync(cancellationToken); return (await c.QueryAsync<StoragePackage>(new CommandDefinition("SELECT id AS Id,code AS Code,version AS Version,name AS Name,quantity_bytes AS QuantityBytes,unit_price AS UnitPrice,currency AS Currency,terms AS Terms FROM odca.storage_package_versions WHERE status='published' AND unit_price IS NOT NULL AND currency IS NOT NULL AND effective_from<=now() AND (effective_until IS NULL OR effective_until>now()) ORDER BY quantity_bytes", cancellationToken:cancellationToken))).AsList(); }

    public async Task<IReadOnlyList<AdditionalStorageRequest>> ListRequestsAsync(Guid actorId,Guid tenantId,bool platformAccess,CancellationToken cancellationToken)
    { await using var c=await dataSource.OpenConnectionAsync(cancellationToken);await using var tx=await c.BeginTransactionAsync(cancellationToken);await Context(c,tx,actorId,tenantId,cancellationToken);if(!platformAccess&&!await Allowed(c,tx,actorId,tenantId,"tenant.billing.read",cancellationToken))return [];var rows=(await c.QueryAsync<AdditionalStorageRequest>(new CommandDefinition(RequestSql+" WHERE r.tenant_id=@tenantId ORDER BY r.requested_at DESC",new{tenantId},tx,cancellationToken:cancellationToken))).AsList();await tx.CommitAsync(cancellationToken);return rows; }

    public async Task<AdditionalStorageRequest?> RequestStorageAsync(Guid actorId,Guid tenantId,CreateStorageRequest request,CancellationToken cancellationToken)
    { await using var c=await dataSource.OpenConnectionAsync(cancellationToken);await using var tx=await c.BeginTransactionAsync(cancellationToken);await Context(c,tx,actorId,tenantId,cancellationToken);if(!await Allowed(c,tx,actorId,tenantId,"tenant.billing.manage",cancellationToken))return null;
      await c.ExecuteAsync(new CommandDefinition("""INSERT INTO odca.additional_storage_requests(id,tenant_id,requested_by,package_version_id,package_code,package_version,package_name,quantity,unit,bytes_per_unit,unit_price,currency,terms_snapshot,idempotency_key) SELECT gen_random_uuid(),@tenantId,@actor,p.id,p.code,p.version,p.name,@quantity,'bytes',p.quantity_bytes,p.unit_price,p.currency,p.terms,@key FROM odca.storage_package_versions p WHERE p.id=@packageId AND p.status='published' AND p.unit_price IS NOT NULL AND p.currency IS NOT NULL AND p.effective_from<=now() AND (p.effective_until IS NULL OR p.effective_until>now()) ON CONFLICT(tenant_id,idempotency_key) DO NOTHING""",new{tenantId,actor=actorId,quantity=request.Quantity,packageId=request.PackageId,key=request.IdempotencyKey},tx,cancellationToken:cancellationToken));
      var result=await c.QuerySingleOrDefaultAsync<AdditionalStorageRequest>(new CommandDefinition(RequestSql+" WHERE r.tenant_id=@tenantId AND r.idempotency_key=@key AND r.package_version_id=@packageId AND r.quantity=@quantity",new{tenantId,key=request.IdempotencyKey,packageId=request.PackageId,quantity=request.Quantity},tx,cancellationToken:cancellationToken));if(result is not null)await c.ExecuteAsync(new CommandDefinition("INSERT INTO odca.audit_events(scope_type,tenant_id,actor_user_id,action,entity_type,entity_id,result) SELECT 'tenant',@tenantId,@actor,'billing.storage.requested','additional_storage_request',@id,'success' WHERE NOT EXISTS(SELECT 1 FROM odca.audit_events WHERE tenant_id=@tenantId AND action='billing.storage.requested' AND entity_id=@id)",new{tenantId,actor=actorId,id=result.Id},tx,cancellationToken:cancellationToken));await tx.CommitAsync(cancellationToken);return result; }

    public Task<bool> DecideRequestAsync(Guid actorId,Guid tenantId,Guid requestId,DecideStorageRequest request,CancellationToken cancellationToken)=>MutatePlatform(actorId,tenantId,async(c,tx,ct)=>
    { if(request.Decision is not ("approved" or "rejected")||request.Decision=="rejected"&&string.IsNullOrWhiteSpace(request.Reason))return false;return await c.ExecuteScalarAsync<bool>(new CommandDefinition("SELECT odca.decide_storage_request(@tenantId,@requestId,@actor,@decision,@reason)",new{tenantId,requestId,actor=actorId,decision=request.Decision,reason=request.Reason?.Trim()},tx,cancellationToken:ct)); },cancellationToken);
    public Task<bool> GrantStorageAsync(Guid actorId,Guid tenantId,ManualStorageGrant request,CancellationToken cancellationToken)=>MutatePlatform(actorId,tenantId,async(c,tx,ct)=>
    { if(request.QuantityBytes<=0||string.IsNullOrWhiteSpace(request.Reason))return false;await c.ExecuteAsync(new CommandDefinition("SELECT odca.grant_storage_capacity(@tenantId,@actor,@bytes,@reason,@key,@validUntil)",new{tenantId,actor=actorId,bytes=request.QuantityBytes,reason=request.Reason.Trim(),key=request.IdempotencyKey,validUntil=request.ValidUntil},tx,cancellationToken:ct));return true; },cancellationToken);

    public async Task<IReadOnlyList<PlatformCustomer>> ListCustomersAsync(Guid actorId,string? search,CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT "TenantId" AS "TenantId",
                   COALESCE("Name", '') AS "Name",
                   COALESCE("MaskedDocument", '****') AS "MaskedDocument",
                   COALESCE("PlanName", 'Sem plano') AS "PlanName",
                   COALESCE("TenantStatus", 'inactive') AS "TenantStatus",
                   COALESCE("SubscriptionStatus", 'sem_assinatura') AS "SubscriptionStatus",
                   COALESCE("ActiveUsers", 0)::int AS "ActiveUsers",
                   COALESCE("UsedBytes", 0)::bigint AS "UsedBytes",
                   COALESCE("LimitBytes", 0)::bigint AS "LimitBytes",
                   COALESCE("PendingRequests", 0)::int AS "PendingRequests",
                   COALESCE("LastActivity", NOW()) AS "LastActivity"
              FROM odca.platform_consumption_customers(@actor, @search::text);
            """;

        await using var c = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var tx = await c.BeginTransactionAsync(cancellationToken);
        await c.ExecuteAsync(new CommandDefinition(
            "SELECT set_config('odca.user_id', @actor, true)",
            new { actor = actorId.ToString() }, tx, cancellationToken: cancellationToken));
        var rows = (await c.QueryAsync<PlatformCustomerRow>(new CommandDefinition(
            sql,
            new { actor = actorId, search = string.IsNullOrWhiteSpace(search) ? null : search.Trim() },
            tx,
            cancellationToken: cancellationToken))).AsList();
        await tx.CommitAsync(cancellationToken);

        return rows.Select(row => new PlatformCustomer(
            row.TenantId, row.Name, row.MaskedDocument, row.PlanName, row.TenantStatus,
            row.SubscriptionStatus, row.ActiveUsers, row.UsedBytes, row.LimitBytes,
            row.PendingRequests, new DateTimeOffset(row.LastActivity))).ToArray();
    }

    private async Task<bool> MutatePlatform(Guid actorId,Guid tenantId,Func<NpgsqlConnection,NpgsqlTransaction,CancellationToken,Task<bool>> action,CancellationToken cancellationToken){await using var c=await dataSource.OpenConnectionAsync(cancellationToken);await using var tx=await c.BeginTransactionAsync(cancellationToken);await Context(c,tx,actorId,tenantId,cancellationToken);var ok=await action(c,tx,cancellationToken);if(ok)await tx.CommitAsync(cancellationToken);else await tx.RollbackAsync(cancellationToken);return ok;}
    private static Task<int> Context(NpgsqlConnection c,NpgsqlTransaction tx,Guid actor,Guid tenant,CancellationToken ct)=>c.ExecuteAsync(new CommandDefinition("SELECT set_config('odca.user_id',@actor,true),set_config('odca.tenant_id',@tenant,true)",new{actor=actor.ToString(),tenant=tenant.ToString()},tx,cancellationToken:ct));
    private static Task<bool> Allowed(NpgsqlConnection c,NpgsqlTransaction tx,Guid actor,Guid tenant,string permission,CancellationToken ct)=>c.ExecuteScalarAsync<bool>(new CommandDefinition("SELECT odca.tenant_actor_has_permission(@actor,@tenant,@permission)",new{actor,tenant,permission},tx,cancellationToken:ct));
    private const string SummarySql="""SELECT t.id AS TenantId,t.display_name AS OrganizationName,t.status AS TenantStatus,s.status AS SubscriptionStatus,p.code AS PlanCode,p.display_name AS PlanName,p.version AS PlanVersion,s.created_at AS PeriodStart,NULL::timestamptz AS PeriodEnd,COALESCE(max(e.limit_value) FILTER(WHERE e.entitlement_code='active_seats'),0)::int AS ContractedSeats,(SELECT count(*) FROM odca.memberships m WHERE m.tenant_id=t.id AND m.status='active')::int AS ActiveUsers,(SELECT count(*) FROM odca.tenant_invitations i WHERE i.tenant_id=t.id AND i.status IN('pending','sent') AND i.expires_at>now())::int AS ReservedInvitations,COALESCE(max(e.limit_value) FILTER(WHERE e.entitlement_code='storage_bytes'),0)::bigint AS ContractedStorageBytes,COALESCE((SELECT sum(g.quantity_bytes) FROM odca.storage_capacity_grants g WHERE g.tenant_id=t.id AND g.revoked_at IS NULL AND (g.valid_until IS NULL OR g.valid_until>now())),0)::bigint AS AdditionalStorageBytes,COALESCE(u.used_bytes,0)::bigint AS UsedStorageBytes,COALESCE(u.reserved_bytes,0)::bigint AS ReservedStorageBytes,COALESCE(max(e.limit_value) FILTER(WHERE e.entitlement_code='file_bytes'),0)::bigint AS MaximumFileBytes FROM odca.tenants t JOIN odca.subscriptions s ON s.tenant_id=t.id JOIN odca.plan_versions p ON p.id=s.plan_version_id JOIN odca.plan_entitlements e ON e.plan_version_id=p.id LEFT JOIN odca.tenant_storage_usage u ON u.tenant_id=t.id WHERE t.id=@tenantId GROUP BY t.id,s.id,p.id,u.tenant_id""";
    private const string CreditsSql="""SELECT e.entitlement_code AS Resource,'units' AS Unit,e.limit_value AS Contracted,0::bigint AS Additional,0::bigint AS Consumed,0::bigint AS Reserved,e.limit_value AS Available FROM odca.subscriptions s JOIN odca.plan_entitlements e ON e.plan_version_id=s.plan_version_id WHERE s.tenant_id=@tenantId AND e.entitlement_code IN('ocr_pages_monthly','signature_envelopes_monthly') ORDER BY e.entitlement_code""";
    private const string HistorySql="""SELECT m.id AS "Id",m.movement_type AS "Type",m.resource_type AS "Resource",m.quantity AS "Quantity",m.unit AS "Unit",m.reason AS "Reason",COALESCE(u.display_name,'Processo do sistema') AS "ActorName",m.occurred_at AS "OccurredAt" FROM odca.resource_movements m LEFT JOIN odca.users u ON u.id=m.actor_user_id WHERE m.tenant_id=@tenantId ORDER BY m.occurred_at DESC LIMIT 50""";
    private const string RequestSql="""SELECT r.id AS Id,r.package_name AS PackageName,r.package_version AS PackageVersion,r.quantity AS Quantity,(r.bytes_per_unit*r.quantity)::bigint AS TotalBytes,(r.unit_price*r.quantity) AS TotalPrice,r.currency AS Currency,r.terms_snapshot AS Terms,r.status AS Status,requester.display_name AS RequestedBy,r.requested_at AS RequestedAt,decider.display_name AS DecidedBy,r.decided_at AS DecidedAt,r.decision_reason AS DecisionReason FROM odca.additional_storage_requests r JOIN odca.users requester ON requester.id=r.requested_by LEFT JOIN odca.users decider ON decider.id=r.decided_by""";
    private sealed class SummaryRow { public Guid TenantId{get;init;} public string OrganizationName{get;init;}="";public string TenantStatus{get;init;}="";public string SubscriptionStatus{get;init;}="";public string PlanCode{get;init;}="";public string PlanName{get;init;}="";public int PlanVersion{get;init;}public DateTimeOffset? PeriodStart{get;init;}public DateTimeOffset? PeriodEnd{get;init;}public int ContractedSeats{get;init;}public int ActiveUsers{get;init;}public int ReservedInvitations{get;init;}public long ContractedStorageBytes{get;init;}public long AdditionalStorageBytes{get;init;}public long UsedStorageBytes{get;init;}public long ReservedStorageBytes{get;init;}public long MaximumFileBytes{get;init;} }
    private sealed class ConsumptionEventRow { public long Id{get;init;} public string Type{get;init;}=""; public string Resource{get;init;}=""; public long Quantity{get;init;} public string Unit{get;init;}=""; public string? Reason{get;init;} public string ActorName{get;init;}=""; public DateTime OccurredAt{get;init;} }
    private sealed class PlatformCustomerRow
    {
        public Guid TenantId { get; set; }
        public string Name { get; set; } = string.Empty;
        public string MaskedDocument { get; set; } = string.Empty;
        public string PlanName { get; set; } = string.Empty;
        public string TenantStatus { get; set; } = string.Empty;
        public string SubscriptionStatus { get; set; } = string.Empty;
        public int ActiveUsers { get; set; }
        public long UsedBytes { get; set; }
        public long LimitBytes { get; set; }
        public int PendingRequests { get; set; }
        public DateTime LastActivity { get; set; }
    }
}
