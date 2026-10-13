using Dapper;
using Npgsql;
using Odca.Application.Operations;

namespace Odca.Infrastructure.Operations;

public sealed class OperationalInboxRepository(NpgsqlDataSource dataSource)
    : IOperationalInboxRepository, IMonthlyAgendaRepository
{
    public async Task<TenantCalendarContext> ReadCalendarAsync(Guid tenantId, CancellationToken cancellationToken)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        var row = await connection.QuerySingleAsync<(string TimeZoneId, DateOnly Today)>(new CommandDefinition(
            """
            SELECT timezone AS TimeZoneId, (now() AT TIME ZONE timezone)::date AS Today
              FROM odca.tenants
             WHERE id = @tenantId AND NOT is_deleted
            """,
            new { tenantId },
            cancellationToken: cancellationToken));
        return new TenantCalendarContext(row.TimeZoneId, row.Today);
    }

    public Task<IReadOnlyList<OperationalInboxRow>> ListCandidatesAsync(
        Guid tenantId,
        Guid viewerId,
        bool canReadObligations,
        bool canReadTenantObligations,
        bool canReadReviews,
        bool canReadTenantReviews,
        bool canReadRenewals,
        bool canReadTenantRenewals,
        bool canReadSolicitations,
        bool canReadTenantSolicitations,
        bool canReadSignatures,
        bool canReadTenantSignatures,
        Guid? contractId,
        Guid? ownerId,
        DateOnly today,
        DateOnly renewalWindowEnd,
        CancellationToken cancellationToken) =>
        QueryAsync(
            tenantId,
            viewerId,
            canReadObligations,
            canReadTenantObligations,
            canReadReviews,
            canReadTenantReviews,
            canReadRenewals,
            canReadTenantRenewals,
            canReadSolicitations,
            canReadTenantSolicitations,
            canReadSignatures,
            canReadTenantSignatures,
            contractId,
            ownerId,
            today,
            renewalWindowEnd,
            windowFrom: null,
            windowTo: null,
            cancellationToken);

    public Task<IReadOnlyList<OperationalInboxRow>> ListWindowAsync(
        Guid tenantId,
        Guid viewerId,
        bool canReadObligations,
        bool canReadTenantObligations,
        bool canReadReviews,
        bool canReadTenantReviews,
        bool canReadRenewals,
        bool canReadTenantRenewals,
        bool canReadSolicitations,
        bool canReadTenantSolicitations,
        bool canReadSignatures,
        bool canReadTenantSignatures,
        Guid? ownerId,
        DateOnly windowStart,
        DateOnly windowEnd,
        CancellationToken cancellationToken) =>
        QueryAsync(
            tenantId,
            viewerId,
            canReadObligations,
            canReadTenantObligations,
            canReadReviews,
            canReadTenantReviews,
            canReadRenewals,
            canReadTenantRenewals,
            canReadSolicitations,
            canReadTenantSolicitations,
            canReadSignatures,
            canReadTenantSignatures,
            contractId: null,
            ownerId,
            today: windowStart,
            renewalWindowEnd: windowEnd,
            windowFrom: windowStart,
            windowTo: windowEnd,
            cancellationToken);

    private async Task<IReadOnlyList<OperationalInboxRow>> QueryAsync(
        Guid tenantId,
        Guid viewerId,
        bool canReadObligations,
        bool canReadTenantObligations,
        bool canReadReviews,
        bool canReadTenantReviews,
        bool canReadRenewals,
        bool canReadTenantRenewals,
        bool canReadSolicitations,
        bool canReadTenantSolicitations,
        bool canReadSignatures,
        bool canReadTenantSignatures,
        Guid? contractId,
        Guid? ownerId,
        DateOnly today,
        DateOnly renewalWindowEnd,
        DateOnly? windowFrom,
        DateOnly? windowTo,
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

        const string sql = """
            SELECT kind AS Kind, source_id AS SourceId, tenant_id AS TenantId, contract_id AS ContractId,
                   contract_title AS ContractTitle, title AS Title, owner_id AS OwnerId,
                   owner_name AS OwnerName, due_on AS DueOn, status AS Status, version AS Version,
                   anchor AS Anchor
              FROM (
                SELECT 'Obligation'::text AS kind, o.id AS source_id, o.tenant_id, o.contract_id,
                       c.title AS contract_title, o.title, o.owner_id, u.display_name AS owner_name,
                       o.due_date AS due_on, o.status, o.row_version::bigint AS version, NULL::text AS anchor
                  FROM odca.contract_obligations o
                  JOIN odca.contracts c ON c.id = o.contract_id AND c.tenant_id = o.tenant_id
                  JOIN odca.users u ON u.id = o.owner_id
                 WHERE o.tenant_id = @tenantId
                   AND o.deleted_at IS NULL
                   AND o.status IN ('open','in_progress')
                   AND @canReadObligations
                   AND (@canReadTenantObligations OR o.owner_id = @viewerId)
                   AND (CAST(@contractId AS uuid) IS NULL OR o.contract_id = @contractId)
                   AND (CAST(@ownerId AS uuid) IS NULL OR o.owner_id = @ownerId)
                   AND (CAST(@windowFrom AS date) IS NULL OR o.due_date >= CAST(@windowFrom AS date))
                   AND (CAST(@windowTo AS date) IS NULL OR o.due_date <= CAST(@windowTo AS date))
                UNION ALL
                SELECT 'Review', r.id, r.tenant_id, r.contract_id, c.title,
                       COALESCE(NULLIF(btrim(r.instructions), ''), c.title),
                       s.reviewer_id, ru.display_name,
                       (r.due_at AT TIME ZONE t.timezone)::date, r.status, r.row_version::bigint, NULL::text
                  FROM odca.contract_review_requests r
                  JOIN odca.contracts c ON c.id = r.contract_id AND c.tenant_id = r.tenant_id
                  JOIN odca.tenants t ON t.id = r.tenant_id
                  JOIN odca.contract_review_steps s
                    ON s.review_id = r.id AND s.tenant_id = r.tenant_id AND s.status = 'current'
                  JOIN odca.users ru ON ru.id = s.reviewer_id
                 WHERE r.tenant_id = @tenantId
                   AND r.status IN ('in_review','changes_requested')
                   AND @canReadReviews
                   AND (@canReadTenantReviews OR s.reviewer_id = @viewerId OR r.requested_by = @viewerId)
                   AND (CAST(@contractId AS uuid) IS NULL OR r.contract_id = @contractId)
                   AND (CAST(@ownerId AS uuid) IS NULL OR s.reviewer_id = @ownerId)
                   AND (CAST(@windowFrom AS date) IS NULL OR (r.due_at AT TIME ZONE t.timezone)::date >= CAST(@windowFrom AS date))
                   AND (CAST(@windowTo AS date) IS NULL OR (r.due_at AT TIME ZONE t.timezone)::date <= CAST(@windowTo AS date))
                UNION ALL
                SELECT 'Renewal', c.id, c.tenant_id, c.id, c.title, c.title, c.owner_id, ou.display_name,
                       CASE
                         WHEN c.renewal_notice_amount IS NULL OR c.end_date IS NULL THEN c.end_date
                         WHEN c.renewal_notice_unit = 'calendar_months'
                           THEN (c.end_date - (c.renewal_notice_amount || ' months')::interval)::date
                         ELSE (c.end_date - c.renewal_notice_amount)::date
                       END,
                       CASE WHEN c.end_date < @today THEN 'expired' ELSE 'expiring' END, c.version::bigint, NULL::text
                  FROM odca.contracts c
                  LEFT JOIN odca.users ou ON ou.id = c.owner_id
                 WHERE c.tenant_id = @tenantId
                   AND c.end_date IS NOT NULL
                   AND c.end_date <= @renewalWindowEnd
                   AND @canReadRenewals
                   AND (@canReadTenantRenewals OR c.owner_id = @viewerId)
                   AND (CAST(@contractId AS uuid) IS NULL OR c.id = @contractId)
                   AND (CAST(@ownerId AS uuid) IS NULL OR c.owner_id = @ownerId)
                   AND (
                        CAST(@windowFrom AS date) IS NULL
                        OR COALESCE(
                             CASE
                               WHEN c.renewal_notice_amount IS NULL OR c.end_date IS NULL THEN c.end_date
                               WHEN c.renewal_notice_unit = 'calendar_months'
                                 THEN (c.end_date - (c.renewal_notice_amount || ' months')::interval)::date
                               ELSE (c.end_date - c.renewal_notice_amount)::date
                             END, c.end_date) BETWEEN CAST(@windowFrom AS date) AND CAST(@windowTo AS date)
                       )
                 UNION ALL
                 SELECT 'Solicitation', s.id, s.tenant_id, COALESCE(s.contract_id, '00000000-0000-0000-0000-000000000000'::uuid),
                        COALESCE(c.title, '(sem contrato vinculado)'),
                        s.subject, s.opened_by, so.display_name,
                        CASE WHEN s.status IN ('aberta','triagem','em_atendimento','aguardando_cliente') THEN s.resolution_due_at::date ELSE NULL END,
                        s.status, s.row_version::bigint, NULL::text
                   FROM odca.solicitations s
                   LEFT JOIN odca.contracts c ON c.tenant_id = s.tenant_id AND c.id = s.contract_id
                   LEFT JOIN odca.users so ON so.id = s.opened_by
                  WHERE s.tenant_id = @tenantId
                    AND s.status NOT IN ('resolvida','encerrada','cancelada')
                    AND @canReadSolicitations
                    AND (@canReadTenantSolicitations OR s.opened_by = @viewerId)
                    AND (CAST(@contractId AS uuid) IS NULL OR s.contract_id = @contractId)
                    -- Item 20: solicitacoes em brecha de SLA saem da lista nativa e entram na fila de excecoes.
                    AND s.first_response_breach_at IS NULL AND s.resolution_breach_at IS NULL
                    AND (CAST(@ownerId AS uuid) IS NULL OR s.opened_by = @ownerId)
                    AND (CAST(@windowFrom AS date) IS NULL OR (CASE WHEN s.status IN ('aberta','triagem','em_atendimento','aguardando_cliente') THEN s.resolution_due_at::date ELSE NULL END) >= CAST(@windowFrom AS date))
                    AND (CAST(@windowTo AS date) IS NULL OR (CASE WHEN s.status IN ('aberta','triagem','em_atendimento','aguardando_cliente') THEN s.resolution_due_at::date ELSE NULL END) <= CAST(@windowTo AS date))
                 UNION ALL
                 SELECT 'Signature', sp.id, sp.tenant_id, g.contract_id,
                        COALESCE(c.title, 'Documento gerado'),
                        COALESCE(c.title, 'Documento gerado'),
                        CASE WHEN @canReadTenantSignatures THEN c.owner_id ELSE CAST(@viewerId AS uuid) END,
                        CASE WHEN @canReadTenantSignatures THEN co.display_name ELSE vu.display_name END,
                        NULL::date,
                        sp.status, sp.row_version::bigint, NULL::text
                   FROM odca.signature_preparations sp
                   JOIN odca.generated_contract_versions g ON g.tenant_id = sp.tenant_id AND g.id = sp.generated_version_id
                   LEFT JOIN odca.contracts c ON c.tenant_id = g.tenant_id AND c.id = g.contract_id
                   LEFT JOIN odca.users co ON co.id = c.owner_id
                   JOIN odca.users vu ON vu.id = @viewerId
                  WHERE sp.tenant_id = @tenantId
                    AND sp.status IN ('draft','confirmed')
                    AND @canReadSignatures
                    AND (@canReadTenantSignatures OR EXISTS (
                         SELECT 1 FROM odca.signature_participants part
                          WHERE part.preparation_id = sp.id
                            AND part.composition_revision = sp.composition_revision
                            AND part.identity_membership_id = (
                                SELECT m.id FROM odca.memberships m
                                 WHERE m.tenant_id = sp.tenant_id AND m.user_id = @viewerId LIMIT 1)))
                    AND (CAST(@contractId AS uuid) IS NULL OR g.contract_id = @contractId)
                    AND (CAST(@ownerId AS uuid) IS NULL
                         OR (@canReadTenantSignatures AND c.owner_id = CAST(@ownerId AS uuid))
                         OR (NOT @canReadTenantSignatures AND CAST(@viewerId AS uuid) = CAST(@ownerId AS uuid)))
                    AND (CAST(@windowFrom AS date) IS NULL OR FALSE)
                 UNION ALL
                 -- Item 20 (fila de excecoes): renovacao/aditivo/alteracao em conflito ou com aplicacao falhada.
                 -- Mesma porta de permissao do kind Renewal; due_on = hoje (atencao imediata => DueToday);
                 -- ausente da agenda mensal (mesmo sentinel de window do kind Signature).
                 SELECT 'Exception', r.id, r.tenant_id, r.contract_id, c.title,
                        COALESCE(NULLIF(btrim(r.reason), ''), c.title),
                        r.responsible_id, rr.display_name,
                        @today,
                        CASE WHEN r.status = 'conflict' THEN 'conflict' ELSE r.application_status END,
                        r.row_version::bigint, 'renovacao'::text
                   FROM odca.contract_change_requests r
                   JOIN odca.contracts c ON c.id = r.contract_id AND c.tenant_id = r.tenant_id
                   JOIN odca.users rr ON rr.id = r.responsible_id
                  WHERE r.tenant_id = @tenantId
                    AND (r.status = 'conflict' OR r.application_status = 'failed')
                    AND @canReadRenewals
                    AND (@canReadTenantRenewals OR r.responsible_id = @viewerId)
                    AND (CAST(@contractId AS uuid) IS NULL OR r.contract_id = @contractId)
                    AND (CAST(@ownerId AS uuid) IS NULL OR r.responsible_id = @ownerId)
                    AND (CAST(@windowFrom AS date) IS NULL OR FALSE)
                 UNION ALL
                 -- Item 20 (fila de excecoes): solicitacao ativa com estouro de SLA ja registrado.
                 -- Mesma porta de permissao do kind Solicitation; due_on = data da primeira brecha
                 -- (sempre no passado => Overdue). Contrato nulo cai no fallback do Map.
                 SELECT 'Exception', s.id, s.tenant_id, s.contract_id,
                        COALESCE(c.title, '(sem contrato vinculado)'),
                        COALESCE(NULLIF(btrim(s.subject), ''), '(sem assunto)'),
                        s.opened_by, so.display_name,
                        (CASE WHEN s.first_response_breach_at IS NOT NULL
                               AND (s.resolution_breach_at IS NULL OR s.first_response_breach_at <= s.resolution_breach_at)
                              THEN s.first_response_breach_at ELSE s.resolution_breach_at END)::date,
                        s.status, s.row_version::bigint, 'solicitacoes'::text
                   FROM odca.solicitations s
                   LEFT JOIN odca.contracts c ON c.tenant_id = s.tenant_id AND c.id = s.contract_id
                   LEFT JOIN odca.users so ON so.id = s.opened_by
                  WHERE s.tenant_id = @tenantId
                    AND s.status NOT IN ('resolvida','encerrada','cancelada')
                    AND (s.first_response_breach_at IS NOT NULL OR s.resolution_breach_at IS NOT NULL)
                    AND @canReadSolicitations
                    AND (@canReadTenantSolicitations OR s.opened_by = @viewerId)
                    AND (CAST(@contractId AS uuid) IS NULL OR s.contract_id = @contractId)
                    AND (CAST(@ownerId AS uuid) IS NULL OR s.opened_by = @ownerId)
                    AND (CAST(@windowFrom AS date) IS NULL OR FALSE)
              ) inbox
            """;

        var rows = await connection.QueryAsync<OperationalInboxRow>(new CommandDefinition(
            sql,
            new
            {
                tenantId,
                viewerId,
                contractId,
                ownerId,
                today,
                renewalWindowEnd,
                windowFrom,
                windowTo,
                canReadObligations,
                canReadTenantObligations,
                canReadReviews,
                canReadTenantReviews,
                canReadRenewals,
                canReadTenantRenewals,
                canReadSolicitations,
                canReadTenantSolicitations,
                canReadSignatures,
                canReadTenantSignatures
            },
            transaction,
            cancellationToken: cancellationToken));
        await transaction.CommitAsync(cancellationToken);
        return rows.AsList();
    }
}
