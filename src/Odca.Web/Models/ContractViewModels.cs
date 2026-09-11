using System.ComponentModel.DataAnnotations;
using Odca.Contracts.Contracts;
using Odca.Contracts.Tenancy;

namespace Odca.Web.Models;

public sealed class ContractListPageViewModel
{
    public required OrganizationSummary Organization { get; init; }
    public ContractOverviewResponse? Overview { get; init; }
    public PaginatedResponse<ContractListItemResponse>? Items { get; init; }
    public IReadOnlyList<ContractTypeResponse> Types { get; init; } = [];
    public IReadOnlyList<CounterpartyResponse> Counterparties { get; init; } = [];
    public IReadOnlyList<TeamMemberResponse> Members { get; init; } = [];
    public string? Title { get; init; }
    public Guid? CounterpartyId { get; init; }
    public Guid? TypeId { get; init; }
    public Guid? OwnerUserId { get; init; }
    public string? OperationalStatus { get; init; }
    public string? TemporalStatus { get; init; }
    public DateOnly? EndFrom { get; init; }
    public DateOnly? EndTo { get; init; }
    public bool Mine { get; init; }
    public bool Approaching { get; init; }
    public bool Unassigned { get; init; }
    public int Page { get; init; } = 1;
    public int PageSize { get; init; } = 20;
    public bool LoadError { get; init; }
    public bool CanManage { get; init; }
    public bool CanLifecycle { get; init; }
}

public sealed class ContractFormPageViewModel
{
    public required OrganizationSummary Organization { get; init; }
    public ContractFormViewModel Form { get; set; } = new();
    public IReadOnlyList<ContractTypeResponse> Types { get; init; } = [];
    public IReadOnlyList<CounterpartyResponse> Counterparties { get; init; } = [];
    public IReadOnlyList<TeamMemberResponse> Members { get; init; } = [];
    public bool IsEdit { get; init; }
    public bool CanManage { get; init; }
    public string? ReturnQuery { get; init; }
}

public sealed class ContractFormViewModel
{
    public Guid? Id { get; set; }

    [StringLength(80)]
    public string? ReferenceNumber { get; set; }

    [Required(ErrorMessage = "Informe o título."), StringLength(240, MinimumLength = 2)]
    public string Title { get; set; } = string.Empty;

    [StringLength(4000)]
    public string? Summary { get; set; }

    [Required(ErrorMessage = "Selecione o tipo.")]
    public Guid TypeId { get; set; }

    [Required(ErrorMessage = "Selecione a contraparte principal.")]
    public Guid PrimaryCounterpartyId { get; set; }

    public Guid? OwnerUserId { get; set; }

    [Required(ErrorMessage = "Informe a data de início.")]
    public DateOnly StartDate { get; set; } = DateOnly.FromDateTime(DateTime.Today);

    public DateOnly? EndDate { get; set; }

    public bool IsIndefinite { get; set; }

    public decimal? Amount { get; set; }

    [StringLength(3)]
    public string? Currency { get; set; }

    public string? AmountPeriodicity { get; set; }

    [Range(0, 3650)]
    public int? RenewalNoticeDays { get; set; }

    public string RenewalDecision { get; set; } = "pending";

    public Guid[]? AdditionalCounterpartyIds { get; set; }

    public long? Version { get; set; }

    public bool VersionConflict { get; set; }

    public string? ReturnQuery { get; set; }
}

public sealed class ContractDetailPageViewModel
{
    public required OrganizationSummary Organization { get; init; }
    public required ContractDetailResponse Contract { get; init; }
    public string Tab { get; init; } = "resumo";
    public bool CanManage { get; init; }
    public bool CanLifecycle { get; init; }
    public string? ReturnQuery { get; init; }
    public RenewContractFormViewModel Renew { get; init; } = new();
    public SoftDeleteContractFormViewModel SoftDelete { get; init; } = new();
}

public sealed class RenewContractFormViewModel
{
    [Required]
    public long Version { get; set; }

    [Required(ErrorMessage = "Informe a nova data de término.")]
    public DateOnly NewEndDate { get; set; }

    [StringLength(500)]
    public string? Reason { get; set; }
}

public sealed class SoftDeleteContractFormViewModel
{
    [Required]
    public long Version { get; set; }

    [Required(ErrorMessage = "Informe o motivo."), StringLength(500, MinimumLength = 3)]
    public string Reason { get; set; } = string.Empty;
}

public sealed class ContractVersionActionViewModel
{
    [Required]
    public long Version { get; set; }
}

public sealed class CounterpartyListPageViewModel
{
    public required OrganizationSummary Organization { get; init; }
    public PaginatedResponse<CounterpartyResponse>? Items { get; init; }
    public string? Search { get; init; }
    public string? Status { get; init; }
    public int Page { get; init; } = 1;
    public int PageSize { get; init; } = 20;
    public bool LoadError { get; init; }
    public bool CanManage { get; init; }
}

public sealed class CounterpartyFormPageViewModel
{
    public required OrganizationSummary Organization { get; init; }
    public CounterpartyFormViewModel Form { get; set; } = new();
    public bool IsEdit { get; init; }
    public bool CanManage { get; init; }
}

public sealed class CounterpartyFormViewModel
{
    public Guid? Id { get; set; }

    [Required(ErrorMessage = "Selecione o tipo de pessoa.")]
    public string PersonType { get; set; } = "organization";

    [Required(ErrorMessage = "Informe a razão social ou nome completo."), StringLength(200, MinimumLength = 2)]
    public string LegalName { get; set; } = string.Empty;

    [Required(ErrorMessage = "Informe o nome de exibição."), StringLength(200, MinimumLength = 2)]
    public string DisplayName { get; set; } = string.Empty;

    [Required(ErrorMessage = "Selecione o tipo de documento.")]
    public string DocumentType { get; set; } = "cnpj";

    [StringLength(32)]
    public string? Document { get; set; }

    public string? DocumentDisplay { get; set; }

    [EmailAddress(ErrorMessage = "Informe um e-mail válido."), StringLength(254)]
    public string? Email { get; set; }

    [StringLength(40)]
    public string? Phone { get; set; }

    public bool IsClient { get; set; }
    public bool IsSupplier { get; set; }
    public bool IsPartner { get; set; }
    public bool IsProvider { get; set; }

    [StringLength(200)]
    public string? AddressLine1 { get; set; }

    [StringLength(200)]
    public string? AddressLine2 { get; set; }

    [StringLength(120)]
    public string? AddressCity { get; set; }

    [StringLength(80)]
    public string? AddressState { get; set; }

    [StringLength(20)]
    public string? AddressPostalCode { get; set; }

    [StringLength(80)]
    public string? AddressCountry { get; set; } = "BR";

    public string? Status { get; set; }
    public long? Version { get; set; }
    public bool VersionConflict { get; set; }
}

public sealed class ContractTypeListPageViewModel
{
    public required OrganizationSummary Organization { get; init; }
    public IReadOnlyList<ContractTypeResponse> Items { get; init; } = [];
    public string? Status { get; init; }
    public bool LoadError { get; init; }
    public bool CanManage { get; init; }
}

public sealed class ContractTypeFormPageViewModel
{
    public required OrganizationSummary Organization { get; init; }
    public ContractTypeFormViewModel Form { get; set; } = new();
    public bool IsEdit { get; init; }
    public bool CanManage { get; init; }
}

public sealed class ContractTypeFormViewModel
{
    public Guid? Id { get; set; }

    [Required(ErrorMessage = "Informe o código."), StringLength(40, MinimumLength = 2)]
    public string Code { get; set; } = string.Empty;

    [Required(ErrorMessage = "Informe o nome."), StringLength(120, MinimumLength = 2)]
    public string Name { get; set; } = string.Empty;

    [StringLength(500)]
    public string? Description { get; set; }

    [StringLength(1000)]
    public string? Guidance { get; set; }

    public string? Status { get; set; }
    public long? Version { get; set; }
    public bool VersionConflict { get; set; }
    public bool IsSystemDemo { get; set; }
}

public sealed class NotificationsPageViewModel
{
    public required OrganizationSummary Organization { get; init; }
    public PaginatedResponse<UserNotificationResponse>? Items { get; init; }
    public bool UnreadOnly { get; init; }
    public int Page { get; init; } = 1;
    public int PageSize { get; init; } = 20;
    public bool LoadError { get; init; }
    public int UnreadCount { get; init; }
}

public sealed class CustomerHomePageViewModel
{
    public required Odca.Contracts.Onboarding.CustomerHomeResponse Home { get; init; }
    public OrganizationOverviewResponse? Overview { get; init; }
    public ContractOverviewResponse? ContractOverview { get; init; }
}
