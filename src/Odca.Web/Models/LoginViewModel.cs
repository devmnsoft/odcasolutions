using System.ComponentModel.DataAnnotations;

namespace Odca.Web.Models;

public sealed class LoginViewModel
{
    [Required(ErrorMessage = "Informe seu e-mail ou CPF.")]
    [Display(Name = "E-mail ou CPF")]
    public string Login { get; set; } = string.Empty;

    [Required(ErrorMessage = "Informe sua senha.")]
    [DataType(DataType.Password)]
    [Display(Name = "Senha")]
    public string Password { get; set; } = string.Empty;
}
