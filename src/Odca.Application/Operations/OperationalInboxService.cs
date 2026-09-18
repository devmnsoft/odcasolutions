using Odca.Application.Renewals;
using Odca.Application.Tenancy;
using Odca.Contracts.Operations;

namespace Odca.Application.Operations;

public sealed class OperationalInboxService(IOperationalInboxRepository repository)
{
    public async Task<OperationalInboxPageDto> QueryAsync(
        Guid tenantId,
        Guid viewerId,
        bool canReadTenantObligations,
        bool canReadTenantReviews,
        bool canReadTenantRenewals,
        OperationalInboxQuery query,
        CancellationToken cancellationToken)
    {
        ArgumentOutOfRangeException.ThrowIfEqual(tenantId, Guid.Empty);
        ArgumentOutOfRangeException.ThrowIfEqual(viewerId, Guid.Empty);

        var (page, pageSize) = Pagination.Normalize(query.Page, query.PageSize);
        var calendar = await repository.ReadCalendarAsync(tenantId, cancellationToken);
        var windowEnd = RenewalRules.ThreeMonthWindowEnd(calendar.Today);

        var rows = await repository.ListCandidatesAsync(
            tenantId,
            viewerId,
            canReadTenantObligations,
            canReadTenantReviews,
            canReadTenantRenewals,
            query.ContractId,
            query.OwnerId,
            calendar.Today,
            windowEnd,
            cancellationToken);

        var canReadTenant = canReadTenantObligations || canReadTenantReviews || canReadTenantRenewals;
        var kind = ParseKind(query.Kind);
        var urgencyFilter = ParseUrgency(query.Urgency);

        var ranked = OperationalInbox.Rank(
            rows.Select(row => new OperationalWorkItem(row.Kind, row.TenantId, row.SourceId, row.OwnerId, row.DueOn)),
            calendar.Today,
            viewerId,
            canReadTenant);

        var allowed = ranked.Select(item => item.SourceId).ToHashSet();
        var mapped = rows
            .Where(row => allowed.Contains(row.SourceId))
            .Where(row => kind is null || row.Kind == kind)
            .Select(row => Map(row, calendar.Today, tenantId))
            .Where(item => urgencyFilter is null || item.Urgency == urgencyFilter.Value.ToString())
            .OrderBy(item => (int)Enum.Parse<OperationalUrgency>(item.Urgency))
            .ThenBy(item => item.DueOn ?? DateOnly.MaxValue)
            .ThenBy(item => item.SourceId)
            .ToArray();

        var pageItems = mapped.Skip(Pagination.Offset(page, pageSize)).Take(pageSize).ToArray();
        return new OperationalInboxPageDto(
            pageItems,
            page,
            pageSize,
            mapped.Length,
            mapped.Count(item => item.Urgency == nameof(OperationalUrgency.Overdue)),
            mapped.Count(item => item.Urgency == nameof(OperationalUrgency.DueToday)),
            mapped.Count(item => item.Urgency == nameof(OperationalUrgency.DueThisWeek)));
    }

    public static OperationalInboxItemDto Map(OperationalInboxRow row, DateOnly today, Guid tenantId)
    {
        var urgency = OperationalInbox.Classify(row.DueOn, today);
        var path = row.Kind switch
        {
            OperationalWorkKind.Obligation => $"/organizacoes/{tenantId}/contratos/{row.ContractId}?obrigacao={row.SourceId}&from=caixa#obrigacoes",
            OperationalWorkKind.Review => $"/organizacoes/{tenantId}/contratos/{row.ContractId}?from=caixa#revisao",
            _ => $"/organizacoes/{tenantId}/contratos/{row.ContractId}?from=caixa#renovacao"
        };
        return new OperationalInboxItemDto(
            row.Kind.ToString(),
            row.SourceId,
            row.ContractId,
            row.ContractTitle,
            row.Title,
            row.OwnerId,
            row.OwnerName,
            row.DueOn,
            urgency.ToString(),
            row.Status,
            path);
    }

    private static OperationalWorkKind? ParseKind(string? value) =>
        Enum.TryParse<OperationalWorkKind>(value, ignoreCase: true, out var kind) ? kind : null;

    private static OperationalUrgency? ParseUrgency(string? value) =>
        Enum.TryParse<OperationalUrgency>(value, ignoreCase: true, out var urgency) ? urgency : null;
}
