using Odca.Application.Renewals;

namespace Odca.Domain.Tests;

public sealed class RenewalRulesTests
{
    [Theory]
    [InlineData(2024, 11, 30, 2025, 2, 28)]
    [InlineData(2023, 11, 30, 2024, 2, 29)]
    [InlineData(2026, 10, 31, 2027, 1, 31)]
    public void ThreeMonthWindowUsesCalendarMonths(int y,int m,int d,int ey,int em,int ed)
        => Assert.Equal(new DateOnly(ey,em,ed),RenewalRules.ThreeMonthWindowEnd(new DateOnly(y,m,d)));

    [Fact]
    public void CommunicationDeadlineIsDistinctFromExpiry()
    {
        var expiry=new DateOnly(2027,3,31);
        Assert.Equal(new DateOnly(2026,12,31),RenewalRules.NoticeDueOn(expiry,3,RenewalNoticeUnit.CalendarMonths));
        Assert.Equal(new DateOnly(2027,1,30),RenewalRules.NoticeDueOn(expiry,60,RenewalNoticeUnit.CalendarDays));
    }

    [Fact]
    public void ContractWithoutEndHasNoNoticeDeadline()
        => Assert.Null(RenewalRules.NoticeDueOn(null,3,RenewalNoticeUnit.CalendarMonths));

    [Fact]
    public void ProposalEffectiveDateIsComparedToProvidedToday()
    {
        var today = new DateOnly(2026, 9, 17);
        RenewalRules.ValidateProposal(today, new DateOnly(2026, 12, 31), today, today, new DateOnly(2027, 12, 31));
        Assert.Throws<ArgumentException>(() =>
            RenewalRules.ValidateProposal(today, new DateOnly(2026, 12, 31), today.AddDays(-1), today, new DateOnly(2027, 12, 31)));
    }
}
