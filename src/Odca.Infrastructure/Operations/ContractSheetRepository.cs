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
                   v.security_status AS SafetyState, v.security_engine AS SecurityEngine, v.uploaded_at AS UpdatedAt
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
            .Select(d => new ContractSheetDocumentDto(d.DocumentId, d.VersionId, d.Name, d.SafetyState, ToUtcOffset(d.UpdatedAt), d.SecurityEngine))
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
                   o.row_version::bigint AS Version, NULL::text AS Anchor
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

        // Solicitações ODCA ativas vinculadas a este contrato (item 13: vínculo persistente)
        var solRows = await connection.QueryAsync<SheetSolicitationRow>(new CommandDefinition("""
            SELECT s.id AS Id, s.subject AS Subject, s.service AS ServiceType, s.priority AS Priority,
                   s.status AS Status, s.resolution_due_at::date AS SlaDueOn
            FROM odca.solicitations s
            WHERE s.tenant_id=@tenantId AND s.contract_id=@contractId AND s.status IN ('open','in_progress','awaiting_customer','paused')
            ORDER BY s.opened_at DESC LIMIT 10
            """, new { tenantId, contractId }, transaction, cancellationToken: cancellationToken));
        var solicitations = solRows.Select(r => new ContractSheetSolicitationDto(
            r.Id, r.Subject, r.ServiceType, r.Priority, r.Status, r.SlaDueOn,
            StatusLabel: r.Status switch { "open" => "Aberta", "in_progress" => "Em atendimento", "awaiting_customer" => "Aguardando cliente", _ => r.Status },
            ServiceLabel: r.ServiceType switch { "revisao" => "Revisão", "adaptacao" => "Adaptação", "esclarecimento" => "Esclarecimento", "suporte_tecnico" => "Suporte técnico", _ => r.ServiceType }))
            .ToArray();

        // Item 10: Alterações pendentes/agendadas (separar condições vigentes de propostas)
        var changeRows = await connection.QueryAsync<SheetChangeRow>(new CommandDefinition("""
            SELECT r.id AS Id, r.kind AS Kind, r.status AS Status, r.application_status AS ApplicationStatus,
                   r.effective_on AS EffectiveOn, r.reason AS Reason,
                   r.proposed_start_date AS ProposedStartDate, r.proposed_end_date AS ProposedEndDate,
                   r.proposed_value AS ProposedValue, r.currency AS Currency
              FROM odca.contract_change_requests r
             WHERE r.tenant_id=@tenantId AND r.contract_id=@contractId
               AND r.status NOT IN ('cancelled','applied')
               AND r.application_status NOT IN ('applied')
             ORDER BY r.created_at DESC LIMIT 10
            """, new { tenantId, contractId }, transaction, cancellationToken: cancellationToken));
        var pendingChanges = changeRows.Select(r => new ContractSheetChangeDto(
            r.Id, r.Kind, r.Status, r.ApplicationStatus, r.EffectiveOn, r.Reason,
            r.ProposedStartDate, r.ProposedEndDate, r.ProposedValue, r.Currency)).ToArray();

        // Sequência de versões geradas do contrato (item 12) com origem em alteração vinculada (r.generated_version_id).
        var derivedRows = await connection.QueryAsync<SheetDerivedDocumentRow>(new CommandDefinition("""
            SELECT v.id AS VersionId, v.version_number AS VersionNumber, v.created_at AS GeneratedAt,
                   v.review_status AS ReviewStatus, left(v.canonical_sha256, 12) AS Sha256Prefix,
                   o.kind AS DerivedFromKind, o.effective_on AS EffectiveOn, o.reason AS OriginReason,
                   o.applied_at AS AppliedAt, o.applied_by_name AS AppliedByName,
                   o.before_start AS BeforeStart, o.before_end AS BeforeEnd, o.before_value AS BeforeValue, o.before_currency AS BeforeCurrency,
                   o.after_start AS AfterStart, o.after_end AS AfterEnd, o.after_value AS AfterValue, o.after_currency AS AfterCurrency
            FROM odca.generated_contract_versions v
            LEFT JOIN LATERAL (
              SELECT r.kind AS kind, r.effective_on AS effective_on, r.reason AS reason,
                     a.applied_at AS applied_at, u.display_name AS applied_by_name,
                     (a.before_data->>'startDate')::date AS before_start,
                     (a.before_data->>'endDate')::date AS before_end,
                     (a.before_data->>'value')::numeric(18,2) AS before_value,
                     a.before_data->>'currency' AS before_currency,
                     (a.after_data->>'startDate')::date AS after_start,
                     (a.after_data->>'endDate')::date AS after_end,
                     (a.after_data->>'value')::numeric(18,2) AS after_value,
                     a.after_data->>'currency' AS after_currency
              FROM odca.contract_change_requests r
              LEFT JOIN odca.contract_change_applications a ON a.tenant_id=r.tenant_id AND a.request_id=r.id
              LEFT JOIN odca.users u ON u.id=a.applied_by
              WHERE r.tenant_id=v.tenant_id AND r.generated_version_id=v.id
              ORDER BY r.created_at DESC LIMIT 1
            ) o ON true
            WHERE v.tenant_id=@tenantId AND v.contract_id=@contractId
            ORDER BY v.version_number ASC, v.created_at ASC
            """, new { tenantId, contractId }, transaction, cancellationToken: cancellationToken));
        var derivedDocuments = derivedRows.Select(r => new ContractSheetDerivedDocumentDto(
            r.VersionId, r.VersionNumber, ToUtcOffset(r.GeneratedAt), r.ReviewStatus, r.Sha256Prefix,
            r.DerivedFromKind, r.EffectiveOn, r.OriginReason, ToUtcOffset(r.AppliedAt), r.AppliedByName,
            r.BeforeStart, r.BeforeEnd, r.BeforeValue, r.BeforeCurrency,
            r.AfterStart, r.AfterEnd, r.AfterValue, r.AfterCurrency)).ToArray();

        await transaction.CommitAsync(cancellationToken);

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
            Signature: signature,
            Solicitations: solicitations,
            PendingChanges: pendingChanges,
            DerivedDocuments: derivedDocuments);
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

    private sealed record SheetSolicitationRow(
        Guid Id, string Subject, string ServiceType, string Priority, string Status, DateOnly? SlaDueOn);

    private sealed record SheetChangeRow(
        Guid Id, string Kind, string Status, string ApplicationStatus,
        DateOnly? EffectiveOn, string Reason,
        DateOnly? ProposedStartDate, DateOnly? ProposedEndDate,
        decimal? ProposedValue, string? Currency);

    private sealed record SheetDerivedDocumentRow(
        Guid VersionId, int VersionNumber, DateTime GeneratedAt, string ReviewStatus, string? Sha256Prefix,
        string? DerivedFromKind, DateOnly? EffectiveOn, string? OriginReason,
        DateTime? AppliedAt, string? AppliedByName,
        DateOnly? BeforeStart, DateOnly? BeforeEnd, decimal? BeforeValue, string? BeforeCurrency,
        DateOnly? AfterStart, DateOnly? AfterEnd, decimal? AfterValue, string? AfterCurrency);

    private sealed class ContractSheetDocumentRow
    {
        public Guid DocumentId { get; set; }
        public Guid VersionId { get; set; }
        public string Name { get; set; } = string.Empty;
        public string SafetyState { get; set; } = string.Empty;
        public string? SecurityEngine { get; set; }
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
