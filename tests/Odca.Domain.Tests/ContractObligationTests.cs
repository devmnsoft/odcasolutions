using Odca.Application.Obligations;

namespace Odca.Domain.Tests;

public sealed class ContractObligationTests
{
    private static ContractObligation New(DateOnly? due=null) => new(Guid.NewGuid(),Guid.NewGuid(),Guid.NewGuid(),"Entregar relatório",ObligationCategory.Delivery,"Contratada",Guid.NewGuid(),due??new(2026,1,31),ObligationPriority.High,ObligationOrigin.Manual);
    [Fact] public void OverdueIsDerivedAndClosedItemsNeverBecomeOverdue() { var item=New(new(2026,1,1)); Assert.True(item.IsOverdue(new(2026,1,2))); item.Fulfill(1,DateTimeOffset.UtcNow,null,false,null); Assert.False(item.IsOverdue(new(2030,1,1))); }
    [Fact] public void CompletionIsOptimisticallyConcurrent() { var item=New(); item.Fulfill(1,DateTimeOffset.UtcNow,"feito",false,null); Assert.Throws<ObligationConflictException>(()=>item.Fulfill(1,DateTimeOffset.UtcNow,"repetido",false,null)); }
    [Fact] public void ReopeningRequiresPermissionAndReason() { var item=New(); item.Cancel(1,"não aplicável"); Assert.Throws<ObligationRuleException>(()=>item.Reopen(2,false,"correção")); Assert.Throws<ObligationRuleException>(()=>item.Reopen(2,true,"")); item.Reopen(2,true,"novo acordo"); Assert.Equal(ObligationStatus.Open,item.Status); }
    [Fact] public void ReschedulePreservesConcurrency() { var item=New(); item.Reschedule(1,new(2026,2,15),"aditivo"); Assert.Equal(new DateOnly(2026,2,15),item.DueDate); Assert.Throws<ObligationConflictException>(()=>item.Reschedule(1,new(2026,3,1),"antigo")); }
    [Theory] [InlineData(2025,2,28)] [InlineData(2024,2,29)] public void MonthlyRecurrenceUsesLastValidDayAndKeepsBaseDay(int year,int month,int day) { var start=new DateOnly(year,1,31); Assert.Equal(new DateOnly(year,month,day),MonthlyRecurrence.Occurrence(start,1)); Assert.Equal(new DateOnly(year,3,31),MonthlyRecurrence.Occurrence(start,2)); }
    [Fact] public void FinancialObligationRequiresExplicitCurrency() { Assert.Throws<ObligationRuleException>(()=>new ContractObligation(Guid.NewGuid(),Guid.NewGuid(),Guid.NewGuid(),"Parcela",ObligationCategory.Financial,"Contratante",Guid.NewGuid(),new(2026,1,1),ObligationPriority.Normal,ObligationOrigin.Manual,10,null)); }
}
