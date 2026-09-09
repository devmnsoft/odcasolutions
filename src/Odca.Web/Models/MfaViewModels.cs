using System.ComponentModel.DataAnnotations;

namespace Odca.Web.Models;

public sealed class MfaEnrollmentViewModel
{
    public string ManualKey { get; set; } = string.Empty;

    public string OtpAuthUri { get; set; } = string.Empty;

    [Required(ErrorMessage = "Informe o código do aplicativo autenticador.")]
    [StringLength(16, MinimumLength = 6, ErrorMessage = "Informe o código com 6 dígitos.")]
    [Display(Name = "Código do autenticador")]
    public string Code { get; set; } = string.Empty;
}

public sealed class MfaChallengeViewModel
{
    [Required(ErrorMessage = "Informe o código MFA ou um código de recuperação.")]
    [StringLength(32, MinimumLength = 6, ErrorMessage = "Informe um código válido.")]
    [Display(Name = "Código MFA")]
    public string Code { get; set; } = string.Empty;
}

public sealed class MfaRecoveryCodesViewModel
{
    public string[] Codes { get; init; } = [];
}
