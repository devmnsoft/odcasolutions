using Odca.Application.Obligations;

namespace Odca.Domain.Tests;

public sealed class ContractObligationTests
{
    private static ContractObligation New(DateOnly? due=null) => new(Guid.NewGuid(),Guid.NewGuid(),Guid.NewGuid(),"Entregar relatório",ObligationCategory.Delivery,"Contratada",Guid.NewGuid(),due??new(2026,1,31),ObligationPriority.High,ObligationOrigin.Manual);
    [Fact] public void OverdueIsDerivedAndClosedItemsNeverBecomeOverdue() { var item=New(new(2026,1,1)); Assert.True(item.IsOverdue(new(2026,1,2))); item.Fulfill(1,DateTimeOffset.UtcNow,null,false,null); Assert.False(item.IsOverdue(new(2030,1,1))); }
    [Fact] public void CompletionIsOptimisticallyConcurrent() { var item=New(); item.Fulfill(1,DateTimeOffset.UtcNow,"feito",false,null); Assert.Throws<ObligationConflictException>(()=>item.Fulfill(1,DateTimeOffset.UtcNow,"repetido",false,null)); }
    [Fact] public void ReopeningRequiresPermissionAndReason() { var item=New(); item.Cancel(1,"não aplicável"); Assert.Throws<ObligationRuleException>(()=>item.Reopen(2,false,"correção")); Assert.Throws<ObligationRuleException>(()=>item.Reopen(2,true,"")); item.Reopen(2,true,"novo acordo"); Assert.Equal(ObligationStatus.Open,item.Status); }
    [Fact] public void ReschedulePreservesConcurrency() { var item=New(); item.Reschedule(1,new(2026,2,15),"aditivo"); Assert.Equal(new DateOnly(2026,2,15),item.DueDate); Assert.Throws<ObligationConflictException>(()=>item.Reschedule(1,new(2026,3,1),"antigo")); }
    [Fact] public void RescheduleRejectsNoOpWithoutChangingVersion() { var item=New(); Assert.Throws<ObligationRuleException>(()=>item.Reschedule(1,item.DueDate,"sem alteração")); item.Reschedule(1,new(2026,2,1),"novo acordo"); Assert.Equal(2,item.Version); }
    [Fact] public void ReassignmentRequiresReasonAndAChangedOwner() { var item=New(); var current=item.OwnerId; Assert.Throws<ObligationRuleException>(()=>item.Reassign(1,Guid.NewGuid(),"")); Assert.Throws<ObligationRuleException>(()=>item.Reassign(1,current,"substituição")); var replacement=Guid.NewGuid(); item.Reassign(1,replacement,"mudança de equipe"); Assert.Equal(replacement,item.OwnerId); Assert.Equal(2,item.Version); }
    [Theory] [InlineData(2025,2,28)] [InlineData(2024,2,29)] public void MonthlyRecurrenceUsesLastValidDayAndKeepsBaseDay(int year,int month,int day) { var start=new DateOnly(year,1,31); Assert.Equal(new DateOnly(year,month,day),MonthlyRecurrence.Occurrence(start,1)); Assert.Equal(new DateOnly(year,3,31),MonthlyRecurrence.Occurrence(start,2)); }
    [Fact] public void FinancialObligationRequiresExplicitCurrency() { Assert.Throws<ObligationRuleException>(()=>new ContractObligation(Guid.NewGuid(),Guid.NewGuid(),Guid.NewGuid(),"Parcela",ObligationCategory.Financial,"Contratante",Guid.NewGuid(),new(2026,1,1),ObligationPriority.Normal,ObligationOrigin.Manual,10,null)); }
    [Fact] public void RecurrenceRejectsNegativeOffsetAndAcceptsZero() { var date=new DateOnly(2026,1,31); Assert.Throws<ArgumentOutOfRangeException>(()=>MonthlyRecurrence.Occurrence(date,-1)); Assert.Equal(date,MonthlyRecurrence.Occurrence(date,0)); }
    [Fact] public void RecurrenceStopsAtMaximumDateWithoutOverflow() { var start=new DateOnly(9999,12,31); Assert.Equal(new[]{DateOnly.MaxValue},MonthlyRecurrence.Materialize(start,24)); Assert.Throws<ObligationRuleException>(()=>MonthlyRecurrence.Occurrence(start,1)); }
}
