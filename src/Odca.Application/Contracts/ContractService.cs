using Odca.Application.Common;
using Odca.Application.Tenancy;

namespace Odca.Application.Contracts;

public sealed class ContractService(IContractRepository repository, IClock clock)
{
    public Task<QueryAccess<TenantPage<ContractListItem>>> ListAsync(
        Guid actorId,
        Guid tenantId,
        ContractListFilter filter,
        int? page,
        int? pageSize,
        CancellationToken cancellationToken)
    {
        var (p, s) = Pagination.Normalize(page, pageSize);
        var reference = filter.ReferenceDate == default ? Today() : filter.ReferenceDate;
        return repository.ListAsync(actorId, tenantId, filter with { ReferenceDate = reference }, p, s, cancellationToken);
    }

    public Task<QueryAccess<ContractDetail?>> GetAsync(
        Guid actorId,
        Guid tenantId,
        Guid id,
        DateOnly? referenceDate,
        CancellationToken cancellationToken)
        => repository.GetAsync(actorId, tenantId, id, referenceDate ?? Today(), cancellationToken);

    public Task<QueryAccess<ContractOverviewMetrics>> OverviewAsync(
        Guid actorId,
        Guid tenantId,
        DateOnly? referenceDate,
        CancellationToken cancellationToken)
        => repository.OverviewAsync(actorId, tenantId, referenceDate ?? Today(), cancellationToken);

    public Task<MutationResult<ContractDetail>> CreateDraftAsync(
        Guid actorId,
        Guid tenantId,
        ContractWriteModel model,
        CancellationToken cancellationToken)
    {
        if (!TryNormalize(model, out var normalized, out var error))
        {
            return Task.FromResult(new MutationResult<ContractDetail>(MutationStatus.ValidationFailed, ErrorCode: error));
        }

        return repository.CreateDraftAsync(actorId, tenantId, normalized, Today(), cancellationToken);
    }

    public Task<MutationResult<ContractDetail>> UpdateAsync(
        Guid actorId,
        Guid tenantId,
        Guid id,
        long version,
        ContractWriteModel model,
        CancellationToken cancellationToken)
    {
        if (!TryNormalize(model, out var normalized, out var error))
        {
            return Task.FromResult(new MutationResult<ContractDetail>(MutationStatus.ValidationFailed, ErrorCode: error));
        }

        return repository.UpdateAsync(actorId, tenantId, id, version, normalized, Today(), cancellationToken);
    }

    public Task<MutationResult> ActivateAsync(Guid actorId, Guid tenantId, Guid id, long version, CancellationToken cancellationToken)
        => repository.ActivateAsync(actorId, tenantId, id, version, cancellationToken);

    public Task<MutationResult> RenewAsync(
        Guid actorId,
        Guid tenantId,
        Guid id,
        long version,
        DateOnly newEndDate,
        string? reason,
        CancellationToken cancellationToken)
        => repository.RenewAsync(actorId, tenantId, id, version, newEndDate, reason, cancellationToken);

    public Task<MutationResult> CloseAsync(Guid actorId, Guid tenantId, Guid id, long version, CancellationToken cancellationToken)
        => repository.CloseAsync(actorId, tenantId, id, version, cancellationToken);

    public Task<MutationResult> CancelAsync(Guid actorId, Guid tenantId, Guid id, long version, CancellationToken cancellationToken)
        => repository.CancelAsync(actorId, tenantId, id, version, cancellationToken);

    public Task<MutationResult> SoftDeleteAsync(
        Guid actorId,
        Guid tenantId,
        Guid id,
        long version,
        string reason,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(reason) || reason.Trim().Length is < 3 or > 500)
        {
            return Task.FromResult(new MutationResult(MutationStatus.ValidationFailed, "reason_invalid"));
        }

        return repository.SoftDeleteAsync(actorId, tenantId, id, version, reason.Trim(), cancellationToken);
    }

    public Task<MutationResult> RestoreAsync(Guid actorId, Guid tenantId, Guid id, long version, CancellationToken cancellationToken)
        => repository.RestoreAsync(actorId, tenantId, id, version, cancellationToken);

    private DateOnly Today()
        => DateOnly.FromDateTime(clock.UtcNow.UtcDateTime);

    private static bool TryNormalize(ContractWriteModel model, out ContractWriteModel normalized, out string? error)
    {
        normalized = model;
        error = null;
        if (string.IsNullOrWhiteSpace(model.Title) || model.Title.Trim().Length > 240)
        {
            error = "title_invalid";
            return false;
        }

        if (model.IsIndefinite)
        {
            if (model.EndDate is not null)
            {
                error = "end_date_not_allowed";
                return false;
            }
        }
        else if (model.EndDate is null || model.EndDate < model.StartDate)
        {
            error = "end_date_invalid";
            return false;
        }

        if (model.Amount is not null)
        {
            if (model.Amount < 0)
            {
                error = "amount_invalid";
                return false;
            }

            if (string.IsNullOrWhiteSpace(model.Currency) || model.Currency.Trim().Length != 3)
            {
                error = "currency_required";
                return false;
            }
        }
        else if (!string.IsNullOrWhiteSpace(model.Currency))
        {
            error = "currency_without_amount";
            return false;
        }

        if (model.AmountPeriodicity is not null &&
            model.AmountPeriodicity is not ("once" or "monthly" or "yearly" or "other"))
        {
            error = "periodicity_invalid";
            return false;
        }

        if (model.RenewalNoticeDays is < 0)
        {
            error = "renewal_notice_invalid";
            return false;
        }

        if (model.RenewalDecision is not null &&
            model.RenewalDecision is not ("pending" or "renew" or "do_not_renew" or "not_applicable"))
        {
            error = "renewal_decision_invalid";
            return false;
        }

        normalized = model with
        {
            ReferenceNumber = string.IsNullOrWhiteSpace(model.ReferenceNumber) ? null : model.ReferenceNumber.Trim(),
            Title = model.Title.Trim(),
            Summary = string.IsNullOrWhiteSpace(model.Summary) ? null : model.Summary.Trim(),
            Currency = string.IsNullOrWhiteSpace(model.Currency) ? null : model.Currency.Trim().ToUpperInvariant(),
            AmountPeriodicity = string.IsNullOrWhiteSpace(model.AmountPeriodicity) ? null : model.AmountPeriodicity,
            RenewalDecision = string.IsNullOrWhiteSpace(model.RenewalDecision) ? "pending" : model.RenewalDecision,
            AdditionalCounterpartyIds = model.AdditionalCounterpartyIds?
                .Where(x => x != model.PrimaryCounterpartyId)
                .Distinct()
                .ToArray()
        };
        return true;
    }
}
