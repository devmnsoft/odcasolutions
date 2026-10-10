using System.ComponentModel.DataAnnotations;
using Odca.Contracts.Administration;
using Odca.Contracts.Consumption;
namespace Odca.Web.Models;
public sealed record ConsumptionViewModel(ConsumptionSummary Summary,IReadOnlyList<StoragePackage> Packages,IReadOnlyList<AdditionalStorageRequest> Requests);
public sealed record CustomerListViewModel(IReadOnlyList<PlatformCustomer> Customers,string? Search,string? Plan,string? Status);
public sealed record UserSearchViewModel(IReadOnlyList<PlatformUserRow> Users,string? Search);
public sealed class CreateCustomerViewModel
{
    [Required(ErrorMessage = "Informe o nome da organização.")]
    [Display(Name = "Nome da organização")]
    public string OrganizationName { get; set; } = string.Empty;

    [Required(ErrorMessage = "Informe o CPF ou CNPJ.")]
    [Display(Name = "CPF ou CNPJ")]
    public string Document { get; set; } = string.Empty;

    [Required(ErrorMessage = "Selecione um plano.")]
    [Display(Name = "Plano")]
    public string PlanCode { get; set; } = "odca-pro";

    [Required(ErrorMessage = "Informe o nome do administrador.")]
    [Display(Name = "Nome do administrador")]
    public string AdminName { get; set; } = string.Empty;

    [Required(ErrorMessage = "Informe o e-mail do administrador.")]
    [EmailAddress(ErrorMessage = "Informe um e-mail válido.")]
    [Display(Name = "E-mail do administrador")]
    public string AdminEmail { get; set; } = string.Empty;

    [Required(ErrorMessage = "Informe a senha inicial.")]
    [MinLength(8, ErrorMessage = "A senha deve ter pelo menos 8 caracteres.")]
    [DataType(DataType.Password)]
    [Display(Name = "Senha inicial")]
    public string InitialPassword { get; set; } = string.Empty;

    [Display(Name = "Ativar imediatamente para operação")]
    public bool ActivateDirectly { get; set; } = true;
}

