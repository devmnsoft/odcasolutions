using Odca.Application.Renewals;
using Odca.Application.Tenancy;
using Odca.Contracts.Operations;

namespace Odca.Application.Operations;

public sealed class OperationalInboxService(IOperationalInboxRepository repository)
{
    public async Task<OperationalInboxPageDto> QueryAsync(
        Guid tenantId,
        Guid viewerId,
        bool canReadObligations,
        bool canReadTenantObligations,
        bool canReadReviews,
        bool canReadTenantReviews,
        bool canReadRenewals,
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
            canReadObligations,
            canReadTenantObligations,
            canReadReviews,
            canReadTenantReviews,
            canReadRenewals,
            canReadTenantRenewals,
            query.ContractId,
            query.OwnerId,
            calendar.Today,
            windowEnd,
            cancellationToken);

        var kind = ParseKind(query.Kind);
        var urgencyFilter = ParseUrgency(query.Urgency);

        var ranked = OperationalInbox.Rank(
            rows.Where(row => CanRead(row.Kind, canReadObligations, canReadReviews, canReadRenewals))
                .Where(row => CanReadTenant(row.Kind, canReadTenantObligations, canReadTenantReviews, canReadTenantRenewals)
                    || row.OwnerId == viewerId)
                .Select(row => new OperationalWorkItem(row.Kind, row.TenantId, row.SourceId, row.OwnerId, row.DueOn)),
            calendar.Today,
            viewerId,
            canReadTenant: true);

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
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(row.Version);
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
            path,
            row.Version);
    }

    private static OperationalWorkKind? ParseKind(string? value) =>
        Enum.TryParse<OperationalWorkKind>(value, ignoreCase: true, out var kind) ? kind : null;

    private static OperationalUrgency? ParseUrgency(string? value) =>
        Enum.TryParse<OperationalUrgency>(value, ignoreCase: true, out var urgency) ? urgency : null;

    internal static bool CanRead(
        OperationalWorkKind kind,
        bool canReadObligations,
        bool canReadReviews,
        bool canReadRenewals) => kind switch
        {
            OperationalWorkKind.Obligation => canReadObligations,
            OperationalWorkKind.Review => canReadReviews,
            OperationalWorkKind.Renewal => canReadRenewals,
            _ => false
        };

    internal static bool CanReadTenant(
        OperationalWorkKind kind,
        bool canReadTenantObligations,
        bool canReadTenantReviews,
        bool canReadTenantRenewals) => kind switch
        {
            OperationalWorkKind.Obligation => canReadTenantObligations,
            OperationalWorkKind.Review => canReadTenantReviews,
            OperationalWorkKind.Renewal => canReadTenantRenewals,
            _ => false
        };
}
