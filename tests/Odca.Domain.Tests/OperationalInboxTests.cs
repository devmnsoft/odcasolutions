using Odca.Application.Operations;

namespace Odca.Domain.Tests;

public sealed class OperationalInboxTests
{
    private static readonly Guid Tenant = Guid.Parse("10000000-0000-0000-0000-000000000001");
    private static readonly Guid Owner = Guid.Parse("20000000-0000-0000-0000-000000000001");
    private static readonly Guid Other = Guid.Parse("20000000-0000-0000-0000-000000000002");
    private static readonly DateOnly Today = new(2026, 9, 17);

    [Fact]
    public void OwnOnlyReaderDoesNotSeeSomeoneElsessItem()
    {
        var items = new[]
        {
            new OperationalWorkItem(OperationalWorkKind.Obligation, Tenant, Guid.NewGuid(), Other, Today)
        };

        var ranked = OperationalInbox.Rank(items, Today, Owner, canReadTenant: false);

        Assert.Empty(ranked);
    }

    [Fact]
    public void TenantReaderSeesOwnedAndUnownedItems()
    {
        var owned = new OperationalWorkItem(OperationalWorkKind.Review, Tenant, Guid.NewGuid(), Owner, Today);
        var other = new OperationalWorkItem(OperationalWorkKind.Renewal, Tenant, Guid.NewGuid(), Other, Today.AddDays(3));

        var ranked = OperationalInbox.Rank([owned, other], Today, Owner, canReadTenant: true);

        Assert.Equal(2, ranked.Count);
    }

    [Fact]
    public void RankPutsOverdueBeforeTodayBeforeWeek()
    {
        var week = new OperationalWorkItem(OperationalWorkKind.Obligation, Tenant, Guid.Parse("30000000-0000-0000-0000-000000000003"), Owner, Today.AddDays(4));
        var overdue = new OperationalWorkItem(OperationalWorkKind.Obligation, Tenant, Guid.Parse("30000000-0000-0000-0000-000000000001"), Owner, Today.AddDays(-2));
        var today = new OperationalWorkItem(OperationalWorkKind.Review, Tenant, Guid.Parse("30000000-0000-0000-0000-000000000002"), Owner, Today);

        var ranked = OperationalInbox.Rank([week, today, overdue], Today, Owner, canReadTenant: true);

        Assert.Equal(new[] { overdue.SourceId, today.SourceId, week.SourceId }, ranked.Select(item => item.SourceId));
    }

    [Theory]
    [InlineData(2026, 9, 1, 2026, 9, 30)]
    [InlineData(2024, 2, 1, 2024, 2, 29)]
    [InlineData(2026, 2, 1, 2026, 2, 28)]
    public void VisibleMonthIsInclusiveCalendarMonth(int y, int m, int df, int ty, int tm, int td)
    {
        var window = MonthlyAgendaWindow.ForMonth(y, m);
        Assert.Equal(new DateOnly(y, m, df), window.From);
        Assert.Equal(new DateOnly(ty, tm, td), window.To);
        Assert.True(MonthlyAgendaWindow.Includes(window.From, window.From, window.To));
        Assert.True(MonthlyAgendaWindow.Includes(window.To, window.From, window.To));
    }

    [Fact]
    public void AgendaRejectsInvertedRange() =>
        Assert.Throws<ArgumentException>(() =>
            MonthlyAgendaWindow.EnsureInclusiveRange(new DateOnly(2026, 9, 30), new DateOnly(2026, 9, 1)));
}
