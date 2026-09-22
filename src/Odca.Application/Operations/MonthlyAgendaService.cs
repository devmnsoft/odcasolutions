using Odca.Contracts.Operations;

namespace Odca.Application.Operations;

public sealed class MonthlyAgendaService(
    IMonthlyAgendaRepository repository,
    IOperationalInboxRepository calendar)
{
    public async Task<MonthlyAgendaPageDto> QueryAsync(
        Guid tenantId,
        Guid viewerId,
        bool canReadObligations,
        bool canReadTenantObligations,
        bool canReadReviews,
        bool canReadTenantReviews,
        bool canReadRenewals,
        bool canReadTenantRenewals,
        MonthlyAgendaQuery query,
        CancellationToken cancellationToken)
    {
        ArgumentOutOfRangeException.ThrowIfEqual(tenantId, Guid.Empty);
        ArgumentOutOfRangeException.ThrowIfEqual(viewerId, Guid.Empty);

        var (from, to) = MonthlyAgendaWindow.ForMonth(query.Year, query.Month);
        var today = (await calendar.ReadCalendarAsync(tenantId, cancellationToken)).Today;

        var rows = await repository.ListWindowAsync(
            tenantId, viewerId,
            canReadObligations, canReadTenantObligations,
            canReadReviews, canReadTenantReviews,
            canReadRenewals, canReadTenantRenewals,
            query.OwnerId, from, to, cancellationToken);

        var kind = Enum.TryParse<OperationalWorkKind>(query.Kind, true, out var parsedKind)
            ? parsedKind
            : (OperationalWorkKind?)null;

        var ranked = OperationalInbox.Rank(
            rows.Where(row => OperationalInboxService.CanRead(
                    row.Kind, canReadObligations, canReadReviews, canReadRenewals))
                .Where(row => OperationalInboxService.CanReadTenant(
                        row.Kind, canReadTenantObligations, canReadTenantReviews, canReadTenantRenewals)
                    || row.OwnerId == viewerId)
                .Where(row => kind is null || row.Kind == kind)
                .Select(row => new OperationalWorkItem(row.Kind, row.TenantId, row.SourceId, row.OwnerId, row.DueOn)),
            today,
            viewerId,
            canReadTenant: true);

        var allowed = ranked.Select(item => item.SourceId).ToHashSet();
        var days = rows
            .Where(row => allowed.Contains(row.SourceId)
                && row.DueOn is not null
                && MonthlyAgendaWindow.Includes(row.DueOn.Value, from, to))
            .GroupBy(row => row.DueOn!.Value)
            .OrderBy(group => group.Key)
            .Select(group => new MonthlyAgendaDayDto(
                group.Key,
                group.Select(row => OperationalInboxService.Map(row, today, tenantId)).ToArray()))
            .ToArray();

        return new MonthlyAgendaPageDto(
            query.Year,
            query.Month,
            from,
            to,
            today,
            days,
            days.Sum(day => day.Items.Count));
    }
}
