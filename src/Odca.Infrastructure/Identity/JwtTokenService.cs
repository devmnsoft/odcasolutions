using System.IdentityModel.Tokens.Jwt;
using System.Globalization;
using System.Security.Claims;
using System.Text;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using Odca.Application.Identity;

namespace Odca.Infrastructure.Identity;

public sealed class JwtTokenService(IOptions<JwtOptions> options) : ITokenService
{
    private readonly JwtOptions _options = options.Value;

    public IssuedToken Issue(UserCredential user, SessionRecord session)
        => WriteToken(BuildClaims(user, session), session.ExpiresAt);

    // D-OC43 (Sprint 9/F-1): token da visão do cliente (impersonação profunda). A identidade
    // emitida é a do usuário-alvo na sua sessão dedicada; imp_op/imp_grant preservam a
    // atribuição ao operador real para auditoria e para o fechamento da janela por request.
    public IssuedToken IssueImpersonation(UserCredential target, SessionRecord session, Guid operatorUserId, Guid grantId)
    {
        var claims = BuildClaims(target, session);
        claims.Add(new Claim("imp_op", operatorUserId.ToString()));
        claims.Add(new Claim("imp_grant", grantId.ToString()));
        return WriteToken(claims, session.ExpiresAt);
    }

    private static List<Claim> BuildClaims(UserCredential user, SessionRecord session)
    {
        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, user.Id.ToString()),
            new(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString()),
            new("sid", session.Id.ToString()),
            new("security_version", session.SecurityVersion.ToString(CultureInfo.InvariantCulture)),
            new("must_change_password", user.MustChangePassword ? "true" : "false"),
            new("mfa_verified", session.AuthenticationLevel == "mfa" ? "true" : "false"),
            new("mfa_enrolled", user.MfaConfirmedAt is not null ? "true" : "false"),
            // D-OC39 (Sprint 5/F-1): instante (unix seconds) em que o MFA foi verificado na
            // sessao; a politica RecentMfa exige esse valor fresco (dentro da janela
            // Identity:RecentAuthMinutes) para operacoes sensiveis de superadministrador.
            new("mfa_verified_at", session.MfaCompletedAt is { } completed
                ? ((long)completed.ToUnixTimeSeconds()).ToString(CultureInfo.InvariantCulture)
                : "0"),
            new(ClaimTypes.Name, user.DisplayName),
            new(ClaimTypes.Email, user.Email)
        };

        if (user.IsPlatformAdministrator)
        {
            claims.Add(new Claim(ClaimTypes.Role, "SuperAdministrator"));
        }

        return claims;
    }

    private IssuedToken WriteToken(List<Claim> claims, DateTimeOffset expiresAt)
    {
        var credentials = new SigningCredentials(
            new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_options.SigningKey)),
            SecurityAlgorithms.HmacSha256);
        var token = new JwtSecurityToken(
            issuer: _options.Issuer,
            audience: _options.Audience,
            claims: claims,
            notBefore: DateTime.UtcNow,
            expires: expiresAt.UtcDateTime,
            signingCredentials: credentials);

        return new IssuedToken(new JwtSecurityTokenHandler().WriteToken(token), expiresAt);
    }
}
