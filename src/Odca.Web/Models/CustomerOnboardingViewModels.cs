using System.ComponentModel.DataAnnotations;

namespace Odca.Web.Models;

public sealed class CustomerRegistrationViewModel
{
    [Required(ErrorMessage = "Escolha um plano.")]
    [Display(Name = "Plano")]
    public string PlanCode { get; set; } = string.Empty;

    [Required(ErrorMessage = "Informe CPF ou CNPJ.")]
    [Display(Name = "CPF ou CNPJ")]
    public string Document { get; set; } = string.Empty;

    [Required(ErrorMessage = "Informe o responsável.")]
    [Display(Name = "Responsável")]
    public string ResponsibleName { get; set; } = string.Empty;

    [Required(ErrorMessage = "Informe o e-mail.")]
    [EmailAddress(ErrorMessage = "Informe um e-mail válido.")]
    [Display(Name = "E-mail")]
    public string Email { get; set; } = string.Empty;

    [Required(ErrorMessage = "Informe a senha.")]
    [MinLength(12, ErrorMessage = "Use pelo menos 12 caracteres.")]
    [DataType(DataType.Password)]
    [Display(Name = "Senha")]
    public string Password { get; set; } = string.Empty;

    [Required(ErrorMessage = "Confirme a senha.")]
    [Compare(nameof(Password), ErrorMessage = "As senhas não coincidem.")]
    [DataType(DataType.Password)]
    [Display(Name = "Confirmar senha")]
    public string ConfirmPassword { get; set; } = string.Empty;

    [Range(typeof(bool), "true", "true", ErrorMessage = "Aceite os termos para continuar.")]
    [Display(Name = "Aceito os termos de uso")]
    public bool AcceptedTerms { get; set; }

    [Range(typeof(bool), "true", "true", ErrorMessage = "Confirme a ciência do aviso de privacidade.")]
    [Display(Name = "Li o aviso de privacidade")]
    public bool AcknowledgedPrivacyNotice { get; set; }

    [Display(Name = "Aceito receber comunicações opcionais")]
    public bool MarketingConsent { get; set; }
}

public sealed class CustomerRegistrationStartedViewModel
{
    public Guid RegistrationId { get; init; }

    public string Message { get; init; } = string.Empty;

    public string? DevelopmentConfirmationToken { get; init; }
}

public sealed class ConfirmEmailViewModel
{
    [Required]
    public Guid RegistrationId { get; set; }

    [Required(ErrorMessage = "Informe o token de confirmação.")]
    [Display(Name = "Token")]
    public string Token { get; set; } = string.Empty;
}
