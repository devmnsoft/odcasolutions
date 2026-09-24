using Dapper;
using Npgsql;
using Odca.Application.Contracts;
using Odca.Application.Operations;
using Odca.Application.Renewals;
using Odca.Contracts.Operations;

namespace Odca.Infrastructure.Operations;

public sealed class ContractSheetRepository(NpgsqlDataSource dataSource) : IContractSheetRepository
{
    public async Task<ContractSheetDto?> GetAsync(
        Guid tenantId,
        Guid contractId,
        Guid viewerId,
        bool purposeAuthorized,
        bool canReadTenant,
        DateOnly today,
        CancellationToken cancellationToken)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        await connection.ExecuteAsync(new CommandDefinition(
            """
            SELECT set_config('odca.tenant_id', @tenant, true),
                   set_config('odca.actor_id', @actor, true);
            """,
            new { tenant = tenantId.ToString(), actor = viewerId.ToString() },
            transaction,
            cancellationToken: cancellationToken));

        var header = await connection.QuerySingleOrDefaultAsync<SheetHeader>(new CommandDefinition(
            """
            SELECT c.id AS ContractId, c.title AS Title,
                   c.start_date AS StartsOn, c.end_date AS EndsOn, c.version AS Version,
                   c.contract_type AS ContractType,
                   c.owner_id AS OwnerId, u.display_name AS OwnerName,
                   c.renewal_notice_amount AS NoticeAmount,
                   c.renewal_notice_unit AS NoticeUnit
              FROM odca.contracts c
              LEFT JOIN odca.users u ON u.id = c.owner_id
             WHERE c.tenant_id = @tenantId AND c.id = @contractId
            """,
            new { tenantId, contractId },
            transaction,
            cancellationToken: cancellationToken));
        if (header is null)
        {
            await transaction.RollbackAsync(cancellationToken);
            return null;
        }

        if (!purposeAuthorized)
        {
            await transaction.RollbackAsync(cancellationToken);
            return null;
        }

        // Opening the workspace for an assigned obligation/review is not a grant
        // to every section. Each projection is independently capability-gated.
        var canReadDocuments = await connection.ExecuteScalarAsync<bool>(new CommandDefinition(
            "SELECT odca.tenant_actor_has_permission(@viewerId,@tenantId,'tenant.documents.download') OR odca.tenant_actor_has_permission(@viewerId,@tenantId,'tenant.documents.manage') OR odca.tenant_actor_has_permission(@viewerId,@tenantId,'tenant.contract_drafts.read')",
            new { viewerId, tenantId }, transaction, cancellationToken: cancellationToken));

        var documents = (await connection.QueryAsync<ContractSheetDocumentDto>(new CommandDefinition(
            """
            SELECT d.id AS DocumentId, v.id AS VersionId, d.title AS Name,
                   v.security_status AS SafetyState, v.created_at AS UpdatedAt
              FROM odca.contract_documents d
              JOIN odca.document_versions v
                ON v.document_id = d.id AND v.tenant_id = d.tenant_id
               AND v.version_number = (
                    SELECT max(x.version_number)
                      FROM odca.document_versions x
                     WHERE x.document_id = d.id AND x.tenant_id = d.tenant_id)
             WHERE @canReadDocuments AND d.tenant_id = @tenantId AND d.contract_id = @contractId AND d.deleted_at IS NULL
             ORDER BY v.created_at DESC
             LIMIT 8
            """,
            new { tenantId, contractId, canReadDocuments },
            transaction,
            cancellationToken: cancellationToken))).AsList();

        var review = await connection.QuerySingleOrDefaultAsync<ContractSheetReviewDto>(new CommandDefinition(
            """
            SELECT r.id AS ReviewId, r.status AS Status, s.reviewer_id AS CurrentReviewerId,
                   u.display_name AS CurrentReviewerName, r.due_at AS DueAt
              FROM odca.contract_reviews r
              LEFT JOIN odca.contract_review_steps s
                ON s.review_id = r.id AND s.tenant_id = r.tenant_id AND s.status = 'current'
              LEFT JOIN odca.users u ON u.id = s.reviewer_id
             WHERE r.tenant_id = @tenantId AND r.contract_id = @contractId
               AND r.status IN ('in_review','changes_requested')
             ORDER BY r.opened_at DESC
             LIMIT 1
            """,
            new { tenantId, contractId },
            transaction,
            cancellationToken: cancellationToken));

        var obligations = (await connection.QueryAsync<OperationalInboxRow>(new CommandDefinition(
            """
            SELECT 'Obligation'::text AS Kind, o.id AS SourceId, o.tenant_id AS TenantId,
                   o.contract_id AS ContractId, c.title AS ContractTitle, o.title AS Title,
                   o.owner_id AS OwnerId, u.display_name AS OwnerName, o.due_date AS DueOn, o.status AS Status,
                   o.row_version::bigint AS Version
              FROM odca.contract_obligations o
              JOIN odca.contracts c ON c.id = o.contract_id AND c.tenant_id = o.tenant_id
              JOIN odca.users u ON u.id = o.owner_id
             WHERE o.tenant_id = @tenantId AND o.contract_id = @contractId
               AND o.deleted_at IS NULL AND o.status IN ('open','in_progress')
               AND (@canReadTenant OR o.owner_id = @viewerId)
             ORDER BY o.due_date, o.id
            """,
            new { tenantId, contractId, viewerId, canReadTenant },
            transaction,
            cancellationToken: cancellationToken))).AsList();

        var importId = await connection.ExecuteScalarAsync<Guid?>(new CommandDefinition(
            """
            SELECT i.id
              FROM odca.contract_imports i
              JOIN odca.document_versions v ON v.id = i.document_version_id AND v.tenant_id = i.tenant_id
             WHERE @canReadDocuments AND i.tenant_id = @tenantId AND i.status = 'awaiting_review'
               AND (v.contract_id = @contractId OR i.result_contract_id = @contractId)
             ORDER BY i.created_at DESC
             LIMIT 1
            """,
            new { tenantId, contractId, canReadDocuments },
            transaction,
            cancellationToken: cancellationToken));
        var hasImportAwaitingReview = importId.HasValue;

        var canManageTemplates = await connection.ExecuteScalarAsync<bool>(new CommandDefinition(
            """
            SELECT odca.tenant_actor_has_permission(@viewerId, @tenantId, 'tenant.templates.manage')
                   OR odca.tenant_actor_has_permission(@viewerId, @tenantId, 'tenant.contract_drafts.manage')
            """,
            new { tenantId, viewerId },
            transaction,
            cancellationToken: cancellationToken));

        var publishedCount = await connection.ExecuteScalarAsync<int>(new CommandDefinition(
            """
            SELECT count(DISTINCT CASE 
                WHEN t.contract_type IN ('non_disclosure_agreement', 'nda') AND t.fields::text LIKE '%"discloser_name"%' THEN 'nda-unilateral'
                WHEN t.contract_type IN ('non_disclosure_agreement', 'nda') THEN 'nda-mutual'
                WHEN t.contract_type IN ('service_agreement', 'services') THEN 'services-agreement'
                WHEN t.contract_type IN ('amendment') THEN 'contract-amendment'
                WHEN t.contract_type IN ('supply_agreement', 'supply') THEN 'supply-agreement'
                WHEN t.contract_type IN ('lease_agreement', 'lease') THEN 'lease-agreement'
                ELSE NULL END)::int
              FROM odca.contract_templates t
             WHERE t.status = 'published'
               AND (t.scope = 'global' OR (t.scope = 'private' AND t.owner_tenant_id = @tenantId))
            """,
            new { tenantId },
            transaction,
            cancellationToken: cancellationToken));

        var draftId = await connection.ExecuteScalarAsync<Guid?>(new CommandDefinition(
            """
            SELECT d.id
              FROM odca.contract_drafts d
             WHERE @canReadDocuments AND d.tenant_id = @tenantId AND d.contract_id = @contractId
             ORDER BY d.updated_at DESC
             LIMIT 1
            """,
            new { tenantId, contractId, canReadDocuments },
            transaction,
            cancellationToken: cancellationToken));

        await transaction.CommitAsync(cancellationToken);

        var unit = string.Equals(header.NoticeUnit, "calendar_months", StringComparison.Ordinal)
            ? RenewalNoticeUnit.CalendarMonths
            : RenewalNoticeUnit.CalendarDays;
        var noticeDue = RenewalRules.NoticeDueOn(header.EndsOn, header.NoticeAmount, unit);
        var windowEnd = RenewalRules.ThreeMonthWindowEnd(today);
        var renewal = header.EndsOn is null
            ? null
            : new ContractSheetRenewalDto(
                header.EndsOn,
                noticeDue,
                header.EndsOn.Value <= windowEnd,
                header.EndsOn.Value < today ? "expired" : "expiring");

        var open = obligations
            .Select(row => OperationalInboxService.Map(row, today, tenantId))
            .ToArray();

        var inWindow = renewal?.InThreeMonthWindow == true;
        var recommendations = ContractWorkspacePolicy.Recommend(
                contractType: header.ContractType,
                hasOpenReview: review is not null,
                inThreeMonthWindow: inWindow,
                currentEnd: header.EndsOn,
                proposedEnd: null)
            .Select(item => new OfficialTemplateRecommendationDto(item.Key, item.Name, item.ContractType, item.Reason))
            .ToArray();

        var canInstallOfficialLibrary = ContractWorkspacePolicy.CanInstallOfficialLibrary(canManageTemplates, publishedCount);

        return new ContractSheetDto(
            header.ContractId,
            header.Title,
            header.EndsOn.HasValue && header.EndsOn.Value < today ? "expired" : "active",
            header.StartsOn,
            header.EndsOn,
            documents,
            review,
            open,
            renewal,
            header.Version,
            header.ContractType,
            header.OwnerName,
            recommendations,
            canInstallOfficialLibrary,
            hasImportAwaitingReview,
            draftId,
            Today: today,
            ImportId: importId);
    }

    private sealed record SheetHeader(
        Guid ContractId,
        string Title,
        DateOnly? StartsOn,
        DateOnly? EndsOn,
        long Version,
        string? ContractType,
        Guid? OwnerId,
        string? OwnerName,
        int? NoticeAmount,
        string? NoticeUnit);
}
