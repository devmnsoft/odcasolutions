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
            SELECT c.id AS ContractId, c.title AS Title, c.reference AS Reference,
                   c.start_date AS StartsOn, c.end_date AS EndsOn, c.version AS Version,
                   c.contract_type AS ContractType,
                   c.owner_id AS OwnerId, u.display_name AS OwnerName,
                   c.archived_at AS ArchivedAt, c.archive_reason AS ArchiveReason,
                   c.closed_at AS ClosedAt, c.closed_on AS ClosedOn, c.closure_reason AS ClosureReason,
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

        var documentRows = (await connection.QueryAsync<ContractSheetDocumentRow>(new CommandDefinition(
            """
            SELECT d.id AS DocumentId, v.id AS VersionId, d.title AS Name,
                   v.security_status AS SafetyState, v.uploaded_at AS UpdatedAt
              FROM odca.contract_documents d
              JOIN odca.document_versions v
                ON v.document_id = d.id AND v.tenant_id = d.tenant_id
               AND v.version_number = (
                    SELECT max(x.version_number)
                      FROM odca.document_versions x
                     WHERE x.document_id = d.id AND x.tenant_id = d.tenant_id)
             WHERE @canReadDocuments AND d.tenant_id = @tenantId AND d.contract_id = @contractId AND d.deleted_at IS NULL
             ORDER BY v.uploaded_at DESC
             LIMIT 8
            """,
            new { tenantId, contractId, canReadDocuments },
            transaction,
            cancellationToken: cancellationToken))).AsList();

        var documents = documentRows
            .Select(d => new ContractSheetDocumentDto(d.DocumentId, d.VersionId, d.Name, d.SafetyState, ToUtcOffset(d.UpdatedAt)))
            .ToList();

        var reviewRow = await connection.QuerySingleOrDefaultAsync<ContractSheetReviewRow>(new CommandDefinition(
            """
            SELECT r.id AS ReviewId, r.status AS Status, s.reviewer_id AS CurrentReviewerId,
                   u.display_name AS CurrentReviewerName, r.due_at AS DueAt
              FROM odca.contract_review_requests r
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

        var review = reviewRow is null
            ? null
            : new ContractSheetReviewDto(reviewRow.ReviewId, reviewRow.Status, reviewRow.CurrentReviewerId, reviewRow.CurrentReviewerName, ToUtcOffset(reviewRow.DueAt));

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
            SELECT count(DISTINCT t.official_key)::int
              FROM odca.contract_templates t
             WHERE t.status = 'published'
               AND t.official_key IS NOT NULL
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

        // B.3.2c — newest generated version carrying a signature preparation.
        // Signing targets the confirmed revision, so its (immutable) participants are shown.
        SheetSignatureRow? signatureRow = null;
        if (canReadDocuments)
        {
            signatureRow = await connection.QuerySingleOrDefaultAsync<SheetSignatureRow>(new CommandDefinition(
                """
                SELECT g.id AS VersionId, g.version_number AS VersionNumber,
                       g.review_status AS ReviewStatus, g.pdf_status AS PdfStatus,
                       p.id AS PreparationId, p.status AS PreparationStatus,
                       p.composition_revision AS CompositionRevision,
                       p.confirmed_revision AS ConfirmedRevision,
                       p.last_reminded_at AS LastRemindedAt
                  FROM odca.signature_preparations p
                  JOIN odca.generated_contract_versions g
                    ON g.tenant_id = p.tenant_id AND g.id = p.generated_version_id
                 WHERE p.tenant_id = @tenantId AND g.contract_id = @contractId
                 ORDER BY g.created_at DESC, p.id DESC
                 LIMIT 1
                """,
                new { tenantId, contractId },
                transaction,
                cancellationToken: cancellationToken));

            if (signatureRow is not null)
            {
                var revision = signatureRow.ConfirmedRevision ?? signatureRow.CompositionRevision;
                signatureRow.Participants = (await connection.QueryAsync<SheetSignatureParticipantRow>(new CommandDefinition(
                    """
                    SELECT sp.client_id AS ClientId, sp.name AS Name, sp.role AS Role,
                           sp.participant_type AS ParticipantType, sp.signed_at AS SignedAt,
                           sp.signed_through AS SignedThrough,
                           EXISTS (
                             SELECT 1 FROM odca.memberships m
                              WHERE m.id = sp.identity_membership_id
                                AND m.tenant_id = sp.tenant_id
                                AND m.user_id = @viewerId
                           ) AS IdentityLinkedToMe
                      FROM odca.signature_participants sp
                     WHERE sp.tenant_id = @tenantId AND sp.preparation_id = @preparationId
                       AND sp.composition_revision = @revision
                     ORDER BY sp.position
                    """,
                    new { tenantId, preparationId = signatureRow.PreparationId, revision, viewerId },
                    transaction,
                    cancellationToken: cancellationToken))).AsList();
            }
        }

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

        // Estados próprios: encerrado > arquivado > vencido > ativo.
        var status = header.ClosedAt.HasValue ? "closed"
            : header.ArchivedAt.HasValue ? "archived"
            : header.EndsOn.HasValue && header.EndsOn.Value < today ? "expired"
            : "active";

        var signature = signatureRow is null ? null : new ContractSheetSignatureDto(
            signatureRow.VersionId,
            signatureRow.VersionNumber,
            signatureRow.PreparationId,
            signatureRow.PreparationStatus,
            signatureRow.CompositionRevision,
            signatureRow.ConfirmedRevision,
            signatureRow.ReviewStatus,
            signatureRow.PdfStatus,
            signatureRow.Participants
                .Select(row => new ContractSheetSignatureParticipantDto(
                    row.ClientId, row.Name, row.Role, row.ParticipantType, ToUtcOffset(row.SignedAt),
                    row.SignedThrough, row.IdentityLinkedToMe))
                .ToArray(),
            ToUtcOffset(signatureRow.LastRemindedAt));

        return new ContractSheetDto(
            header.ContractId,
            header.Title,
            status,
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
            ImportId: importId,
            OwnerId: header.OwnerId,
            Reference: header.Reference,
            ArchivedAt: ToUtcOffset(header.ArchivedAt),
            ArchiveReason: header.ArchiveReason,
            ClosedAt: ToUtcOffset(header.ClosedAt),
            ClosedOn: header.ClosedOn,
            ClosureReason: header.ClosureReason,
            Signature: signature);
    }

    private sealed record SheetHeader(
        Guid ContractId,
        string Title,
        string? Reference,
        DateOnly? StartsOn,
        DateOnly? EndsOn,
        long Version,
        string? ContractType,
        Guid? OwnerId,
        string? OwnerName,
        DateTime? ArchivedAt,
        string? ArchiveReason,
        DateTime? ClosedAt,
        DateOnly? ClosedOn,
        string? ClosureReason,
        int? NoticeAmount,
        string? NoticeUnit);

    private sealed class ContractSheetDocumentRow
    {
        public Guid DocumentId { get; set; }
        public Guid VersionId { get; set; }
        public string Name { get; set; } = string.Empty;
        public string SafetyState { get; set; } = string.Empty;
        public DateTime UpdatedAt { get; set; }
    }

    private sealed class ContractSheetReviewRow
    {
        public Guid ReviewId { get; set; }
        public string Status { get; set; } = string.Empty;
        public Guid? CurrentReviewerId { get; set; }
        public string? CurrentReviewerName { get; set; }
        public DateTime? DueAt { get; set; }
    }

    private sealed class SheetSignatureRow
    {
        public Guid VersionId { get; set; }
        public int VersionNumber { get; set; }
        public string ReviewStatus { get; set; } = string.Empty;
        public string PdfStatus { get; set; } = string.Empty;
        public Guid PreparationId { get; set; }
        public string PreparationStatus { get; set; } = string.Empty;
        public int CompositionRevision { get; set; }
        public int? ConfirmedRevision { get; set; }
        public DateTime? LastRemindedAt { get; set; }
        public List<SheetSignatureParticipantRow> Participants { get; set; } = [];
    }

    private sealed class SheetSignatureParticipantRow
    {
        public Guid ClientId { get; set; }
        public string Name { get; set; } = string.Empty;
        public string Role { get; set; } = string.Empty;
        public string ParticipantType { get; set; } = string.Empty;
        public DateTime? SignedAt { get; set; }
        // v043: modalidade registrada (session/evidence) e vínculo do espectador.
        public string? SignedThrough { get; set; }
        public bool IdentityLinkedToMe { get; set; }
    }

    private static DateTimeOffset ToUtcOffset(DateTime dt) =>
        dt.Kind == DateTimeKind.Utc
            ? new DateTimeOffset(dt, TimeSpan.Zero)
            : new DateTimeOffset(DateTime.SpecifyKind(dt, DateTimeKind.Utc), TimeSpan.Zero);

    private static DateTimeOffset? ToUtcOffset(DateTime? dt) =>
        dt.HasValue ? ToUtcOffset(dt.Value) : null;
}
