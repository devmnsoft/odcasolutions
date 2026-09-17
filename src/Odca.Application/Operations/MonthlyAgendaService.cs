using Odca.Contracts.Operations;

namespace Odca.Application.Operations;

public sealed class MonthlyAgendaService(
    IMonthlyAgendaRepository repository,
    IOperationalInboxRepository calendar)
{
    public async Task<MonthlyAgendaPageDto> QueryAsync(
        Guid tenantId,
        Guid viewerId,
        bool canReadTenant,
        MonthlyAgendaQuery query,
        CancellationToken cancellationToken)
    {
        ArgumentOutOfRangeException.ThrowIfEqual(tenantId, Guid.Empty);
        ArgumentOutOfRangeException.ThrowIfEqual(viewerId, Guid.Empty);

        var (from, to) = MonthlyAgendaWindow.ForMonth(query.Year, query.Month);
        var today = (await calendar.ReadCalendarAsync(tenantId, cancellationToken)).Today;

        var rows = await repository.ListWindowAsync(
            tenantId, viewerId, canReadTenant, query.OwnerId, from, to, cancellationToken);

        var ranked = OperationalInbox.Rank(
            rows.Select(row => new OperationalWorkItem(row.Kind, row.TenantId, row.SourceId, row.OwnerId, row.DueOn)),
            today,
            viewerId,
            canReadTenant);

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
            days,
            days.Sum(day => day.Items.Count));
    }
}
