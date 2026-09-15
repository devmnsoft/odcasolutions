using Odca.Contracts.Renewals;

namespace Odca.Web.Models;

public sealed record RenewalWorkspaceViewModel(RenewalPage Results, DateOnly Today);
