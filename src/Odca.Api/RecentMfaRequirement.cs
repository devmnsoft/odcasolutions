using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Options;
using Odca.Application.Common;

namespace Odca.Api;

/// <summary>D-OC39 (Sprint 5/F-1): janela configurável de reautenticação MFA para operações sensíveis.</summary>
public sealed class RecentMfaOptions
{
    public const string Section = "Identity";

    /// <summary>Janela (minutos) em que a verificação MFA da sessão conta como "recente". Padrão 15.</summary>
    public int RecentAuthMinutes { get; set; } = 15;
}

/// <summary>
/// Exige verificação de MFA RECENTE apenas para superadministradores com MFA inscrito. Os demais
/// perfis não possuem requisito de segundo fator nesta plataforma (mesma regra canônica do
/// MfaVerifiedForSuperAdministratorRequirement). Fluxo de primeira troca de senha
/// (must_change_password=true) é isento para não criar ciclo: a troca forçada precede o MFA.
/// </summary>
public sealed record RecentMfaRequirement : IAuthorizationRequirement;

public sealed class RecentMfaHandler : AuthorizationHandler<RecentMfaRequirement>
{
    private readonly IClock _clock;
    private readonly IOptions<RecentMfaOptions> _options;

    public RecentMfaHandler(IClock clock, IOptions<RecentMfaOptions> options)
    {
        _clock = clock;
        _options = options;
    }

    protected override Task HandleRequirementAsync(
        AuthorizationHandlerContext context,
        RecentMfaRequirement requirement)
    {
        var principal = context.User;
        if (!principal.IsInRole("SuperAdministrator"))
        {
            context.Succeed(requirement);
            return Task.CompletedTask;
        }

        if (principal.FindFirstValue("must_change_password") == "true")
        {
            context.Succeed(requirement);
            return Task.CompletedTask;
        }

        if (principal.FindFirstValue("mfa_enrolled") != "true")
        {
            context.Succeed(requirement);
            return Task.CompletedTask;
        }

        var verifiedAt = principal.FindFirstValue("mfa_verified_at");
        if (!long.TryParse(verifiedAt, out var seconds) || seconds <= 0)
        {
            FailRecent(context);
            return Task.CompletedTask;
        }

        var window = TimeSpan.FromMinutes(Math.Max(1, _options.Value.RecentAuthMinutes));
        if (_clock.UtcNow - DateTimeOffset.FromUnixTimeSeconds(seconds) > window)
        {
            FailRecent(context);
            return Task.CompletedTask;
        }

        context.Succeed(requirement);
        return Task.CompletedTask;
    }

    private void FailRecent(AuthorizationHandlerContext context)
        => context.Fail(new AuthorizationFailureReason(this, "api.auth.recent_auth_required"));
}
