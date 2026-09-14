namespace Odca.Application.Obligations;

public enum ObligationCategory { Delivery, Document, Renewal, Communication, Financial, Other }
public enum ObligationPriority { Low, Normal, High, Critical }
public enum ObligationStatus { Open, InProgress, Fulfilled, Cancelled }
public enum ObligationOrigin { Manual, ReviewedSuggestion }

public sealed class ContractObligation
{
    public ContractObligation(Guid id, Guid tenantId, Guid contractId, string title, ObligationCategory category,
        string obligatedParty, Guid ownerId, DateOnly dueDate, ObligationPriority priority,
        ObligationOrigin origin, decimal? amount = null, string? currency = null)
    {
        if (id == Guid.Empty || tenantId == Guid.Empty || contractId == Guid.Empty || ownerId == Guid.Empty)
            throw new ObligationRuleException("Os identificadores da obrigação são obrigatórios.");
        if (string.IsNullOrWhiteSpace(title) || title.Trim().Length > 160)
            throw new ObligationRuleException("O título deve conter de 1 a 160 caracteres.");
        if (string.IsNullOrWhiteSpace(obligatedParty))
            throw new ObligationRuleException("Informe a parte responsável pelo cumprimento.");
        if (category == ObligationCategory.Financial && (amount is null or <= 0 || !ValidCurrency(currency)))
            throw new ObligationRuleException("Obrigação financeira exige valor positivo e moeda ISO de três letras.");
        if (category != ObligationCategory.Financial && (amount is not null || currency is not null))
            throw new ObligationRuleException("Valor e moeda são exclusivos de obrigação financeira.");
        Id=id; TenantId=tenantId; ContractId=contractId; Title=title.Trim(); Category=category;
        ObligatedParty=obligatedParty.Trim(); OwnerId=ownerId; DueDate=dueDate; Priority=priority; Origin=origin;
        Amount=amount; Currency=currency?.ToUpperInvariant();
    }

    public Guid Id { get; }
    public Guid TenantId { get; }
    public Guid ContractId { get; }
    public string Title { get; }
    public ObligationCategory Category { get; }
    public string ObligatedParty { get; }
    public Guid OwnerId { get; private set; }
    public DateOnly DueDate { get; private set; }
    public ObligationPriority Priority { get; }
    public ObligationOrigin Origin { get; }
    public decimal? Amount { get; }
    public string? Currency { get; }
    public ObligationStatus Status { get; private set; }
    public long Version { get; private set; } = 1;
    public DateTimeOffset? FulfilledAt { get; private set; }
    public bool IsOverdue(DateOnly today) => Status is ObligationStatus.Open or ObligationStatus.InProgress && DueDate < today;

    public void Start(long expectedVersion) { EnsureVersion(expectedVersion); EnsureActive(); Status=ObligationStatus.InProgress; Version++; }
    public void Fulfill(long expectedVersion, DateTimeOffset effectiveAt, string? note, bool evidenceRequired, Guid? evidenceVersionId)
    {
        EnsureVersion(expectedVersion); EnsureActive();
        if (effectiveAt == default) throw new ObligationRuleException("Informe a data efetiva do cumprimento.");
        if (evidenceRequired && evidenceVersionId is null) throw new ObligationRuleException("Esta obrigação exige uma evidência documental.");
        if (note?.Length > 2000) throw new ObligationRuleException("A observação deve ter no máximo 2.000 caracteres.");
        Status=ObligationStatus.Fulfilled; FulfilledAt=effectiveAt; Version++;
    }
    public void Cancel(long expectedVersion, string reason) { EnsureVersion(expectedVersion); EnsureActive(); RequireReason(reason,"cancelamento"); Status=ObligationStatus.Cancelled; Version++; }
    public void Reopen(long expectedVersion, bool allowed, string reason)
    {
        EnsureVersion(expectedVersion); if (!allowed) throw new ObligationRuleException("O usuário não pode reabrir esta obrigação.");
        if (Status is not (ObligationStatus.Fulfilled or ObligationStatus.Cancelled)) throw new ObligationRuleException("Somente obrigação cumprida ou cancelada pode ser reaberta.");
        RequireReason(reason,"reabertura"); Status=ObligationStatus.Open; FulfilledAt=null; Version++;
    }
    public void Reschedule(long expectedVersion, DateOnly dueDate, string reason) { EnsureVersion(expectedVersion); EnsureActive(); RequireReason(reason,"mudança de prazo"); DueDate=dueDate; Version++; }
    public void Reassign(long expectedVersion, Guid ownerId) { EnsureVersion(expectedVersion); EnsureActive(); if(ownerId==Guid.Empty) throw new ObligationRuleException("Informe o novo responsável."); OwnerId=ownerId; Version++; }
    private void EnsureActive() { if(Status is ObligationStatus.Fulfilled or ObligationStatus.Cancelled) throw new ObligationRuleException("A obrigação encerrada precisa ser reaberta antes desta ação."); }
    private void EnsureVersion(long expected) { if(expected!=Version) throw new ObligationConflictException(expected,Version); }
    private static void RequireReason(string value,string action) { if(string.IsNullOrWhiteSpace(value)) throw new ObligationRuleException($"A {action} exige motivo."); }
    private static bool ValidCurrency(string? value) => value?.Length==3 && value.All(char.IsLetter);
}

public static class MonthlyRecurrence
{
    public static DateOnly Occurrence(DateOnly baseDate, int offset)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(offset);
        var month=baseDate.AddMonths(offset); return new DateOnly(month.Year,month.Month,Math.Min(baseDate.Day,DateTime.DaysInMonth(month.Year,month.Month)));
    }
    public static IReadOnlyList<DateOnly> Materialize(DateOnly baseDate, int count, DateOnly? endsOn=null, int maximumWindow=24)
    {
        if(count is <1 or >120 || maximumWindow is <1 or >24) throw new ObligationRuleException("A recorrência deve ter entre 1 e 120 ocorrências e janela de até 24 meses.");
        return Enumerable.Range(0,Math.Min(count,maximumWindow)).Select(i=>Occurrence(baseDate,i)).TakeWhile(x=>endsOn is null || x<=endsOn).ToArray();
    }
}

public sealed class ObligationRuleException(string message) : InvalidOperationException(message);
public sealed class ObligationConflictException(long expected,long actual) : InvalidOperationException($"Versão esperada {expected}; versão atual {actual}.") { public long Expected {get;}=expected; public long Actual {get;}=actual; }
