namespace Odca.Application.Renewals;

public enum RenewalNoticeUnit { CalendarDays, CalendarMonths }

public static class RenewalRules
{
    public static DateOnly? NoticeDueOn(DateOnly? endDate, int? noticeAmount, RenewalNoticeUnit unit)
    {
        if (endDate is null || noticeAmount is null or < 0) return null;
        return unit == RenewalNoticeUnit.CalendarMonths
            ? endDate.Value.AddMonths(-noticeAmount.Value)
            : endDate.Value.AddDays(-noticeAmount.Value);
    }

    public static DateOnly ThreeMonthWindowEnd(DateOnly today) => today.AddMonths(3);

    public static void ValidateProposal(DateOnly? currentEnd, DateOnly effectiveOn, DateOnly? proposedStart, DateOnly? proposedEnd) =>
        ValidateProposal(DateOnly.FromDateTime(DateTime.UtcNow), currentEnd, effectiveOn, proposedStart, proposedEnd);

    public static void ValidateProposal(DateOnly today, DateOnly? currentEnd, DateOnly effectiveOn, DateOnly? proposedStart, DateOnly? proposedEnd)
    {
        if (effectiveOn < today)
            throw new ArgumentException("Datas retroativas não são aceitas nesta entrega.", nameof(effectiveOn));
        if (proposedStart.HasValue && proposedEnd.HasValue && proposedEnd < proposedStart)
            throw new ArgumentException("A vigência proposta é inválida.", nameof(proposedEnd));
        if (currentEnd.HasValue && proposedEnd.HasValue && proposedEnd == currentEnd)
            throw new ArgumentException("A proposta deve conter uma alteração.", nameof(proposedEnd));
    }
}
