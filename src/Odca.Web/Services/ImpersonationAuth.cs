using System.Globalization;
using System.Security.Claims;
using Microsoft.AspNetCore.Authentication.Cookies;

namespace Odca.Web.Services;

/// <summary>D-OC43 (Sprint 9/F-1): identidade da visão do cliente (impersonação profunda).
/// O BFF troca o cookie de autenticação pela identidade do usuário-alvo e preserva o token
/// original do operador para restaurá-lo ao sair. A API continua sendo autoridade: cada
/// request revalida a concessão (claim imp_grant) e o bearer do alvo.</summary>
public static class ImpersonationAuth
{
    public const string GrantClaim = "imp_grant";
    public const string OperatorNameClaim = "imp_op_name";
    public const string TenantClaim = "imp_tenant";
    public const string SupportSessionClaim = "imp_support_session";
    public const string ExpiresClaim = "imp_expires";
    public const string OperatorTokenProperty = "imp_op_token";

    private const string RoleClaimUri = "http://schemas.microsoft.com/ws/2008/06/identity/claims/role";

    /// <summary>Constrói a identidade do usuário-alvo durante a visão do cliente (sempre papel Customer).</summary>
    public static ClaimsPrincipal BuildTargetIdentity(
        string targetDisplayName, Guid tenantId, Guid supportSessionId, string grantId,
        string operatorDisplayName, DateTimeOffset expiresAt)
    {
        var claims = new List<Claim>
        {
            new(ClaimTypes.Name, targetDisplayName),
            new(ClaimTypes.Role, "Customer"),
            new("must_change_password", "false"),
            new("mfa_verified", "true"),
            new("requires_mfa_enrollment", "false"),
            new("requires_mfa_challenge", "false"),
            new(GrantClaim, grantId),
            new(OperatorNameClaim, operatorDisplayName),
            new(TenantClaim, tenantId.ToString()),
            new(SupportSessionClaim, supportSessionId.ToString()),
            new(ExpiresClaim, ((long)expiresAt.ToUnixTimeSeconds()).ToString(CultureInfo.InvariantCulture))
        };
        var identity = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);
        return new ClaimsPrincipal(identity);
    }

    /// <summary>Restaura a identidade do operador a partir do seu bearer original.
    /// Retorna null se o token não puder ser decodificado (o chamador deve desfazer o login).</summary>
    public static (ClaimsPrincipal Identity, DateTimeOffset Expires)? RestoreOperator(string? operatorToken)
    {
        var claims = RecentAuthGuard.DecodeClaims(operatorToken);
        if (claims is null) return null;

        if (!claims.TryGetValue(ClaimTypes.Name, out var name) || string.IsNullOrWhiteSpace(name))
        {
            // Alguns emissores usam "name"; tenta o alias antes de desistir.
            if (!claims.TryGetValue("name", out name) || string.IsNullOrWhiteSpace(name)) return null;
        }

        var isPlatformAdmin = HasRole(claims);
        var mustChange = claims.TryGetValue("must_change_password", out var mc) && mc == "true";
        var mfaVerified = claims.TryGetValue("mfa_verified", out var mv) && mv == "true";

        var identityClaims = new List<Claim>
        {
            new(ClaimTypes.Name, name),
            new(ClaimTypes.Role, isPlatformAdmin ? "SuperAdministrator" : "Customer"),
            new("must_change_password", mustChange ? "true" : "false"),
            new("mfa_verified", mfaVerified ? "true" : "false"),
            new("requires_mfa_enrollment", "false"),
            new("requires_mfa_challenge", "false")
        };
        var identity = new ClaimsIdentity(identityClaims, CookieAuthenticationDefaults.AuthenticationScheme);
        var principal = new ClaimsPrincipal(identity);

        DateTimeOffset expires = DateTimeOffset.UtcNow.AddHours(-1);
        if (claims.TryGetValue("exp", out var expRaw) && long.TryParse(expRaw, out var expSecs))
        {
            expires = DateTimeOffset.FromUnixTimeSeconds(expSecs);
        }
        return (principal, expires);
    }

    private static bool HasRole(Dictionary<string, string> claims)
    {
        foreach (var key in new[] { RoleClaimUri, "role" })
        {
            if (claims.TryGetValue(key, out var value) &&
                value?.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                    .Contains("SuperAdministrator", StringComparer.OrdinalIgnoreCase) == true)
            {
                return true;
            }
        }
        return false;
    }
}
