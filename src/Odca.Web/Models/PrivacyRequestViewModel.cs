using System.ComponentModel.DataAnnotations;

namespace Odca.Web.Models;

public sealed class PrivacyRequestViewModel
{
    [Required(ErrorMessage = "Informe seu e-mail."), EmailAddress(ErrorMessage = "Informe um e-mail válido."), StringLength(254)]
    [Display(Name = "E-mail para retorno")]
    public string Email { get; set; } = string.Empty;

    [Required(ErrorMessage = "Selecione o tipo de solicitação.")]
    [Display(Name = "Direito ou assunto")]
    public string RequestType { get; set; } = string.Empty;

    [StringLength(2000, ErrorMessage = "O relato deve ter até 2.000 caracteres.")]
    [Display(Name = "Contexto adicional (opcional)")]
    public string? Details { get; set; }

    public string? Protocol { get; set; }

    public string? ConfirmationMessage { get; set; }
}
