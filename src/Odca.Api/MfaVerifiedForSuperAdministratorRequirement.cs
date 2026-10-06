using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;

namespace Odca.Api;

/// <summary>
/// Exige verificação de MFA apenas para superadministradores.  Os demais
/// perfis não possuem requisito de segundo fator nesta plataforma.
/// </summary>
public sealed record MfaVerifiedForSuperAdministratorRequirement : IAuthorizationRequirement;

public sealed class MfaVerifiedForSuperAdministratorHandler
    : AuthorizationHandler<MfaVerifiedForSuperAdministratorRequirement>
{
    protected override Task HandleRequirementAsync(
        AuthorizationHandlerContext context,
        MfaVerifiedForSuperAdministratorRequirement requirement)
    {
        var principal = context.User;
        if (!principal.IsInRole("SuperAdministrator") ||
            principal.FindFirstValue("mfa_verified") == "true")
        {
            context.Succeed(requirement);
        }

        return Task.CompletedTask;
    }
}
