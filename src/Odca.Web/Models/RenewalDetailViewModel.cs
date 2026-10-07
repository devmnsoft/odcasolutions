using Odca.Contracts.Renewals;

namespace Odca.Web.Models;

public sealed record RenewalDetailViewModel(
    RenewalRequestDetails Request,
    DateOnly Today,
    bool CanSubmit,
    bool CanCancel,
    bool CanFormalize,
    bool CanApply);
