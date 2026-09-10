using System.Text;
using Odca.Infrastructure.Identity;

namespace Odca.Api;

public static class StartupValidation
{
    public static void Validate(IConfiguration configuration, IHostEnvironment environment, JwtOptions jwt)
    {
        var invalid = new List<string>();
        if (string.IsNullOrWhiteSpace(configuration.GetConnectionString("Database"))) invalid.Add("ConnectionStrings:Database (ausente)");
        if (string.IsNullOrWhiteSpace(jwt.Issuer)) invalid.Add("Jwt:Issuer (ausente)");
        if (string.IsNullOrWhiteSpace(jwt.Audience)) invalid.Add("Jwt:Audience (ausente)");
        if (string.IsNullOrWhiteSpace(jwt.SigningKey)) invalid.Add("Jwt:SigningKey (ausente)");
        else if (Encoding.UTF8.GetByteCount(jwt.SigningKey) < 32) invalid.Add("Jwt:SigningKey (tamanho insuficiente; mínimo 32 bytes)");
        if (invalid.Count > 0)
        {
            throw new InvalidOperationException($"Configuração inválida no ambiente {environment.EnvironmentName}: {string.Join(", ", invalid)}.");
        }

        if (jwt.AccessTokenMinutes is < 5 or > 30)
        {
            throw new InvalidOperationException("Jwt:AccessTokenMinutes deve ficar entre 5 e 30 minutos.");
        }

        if (!environment.IsDevelopment())
        {
            if (!configuration.GetValue<bool>("Security:MfaRequiredForSuperAdmin"))
            {
                throw new InvalidOperationException("Produção exige MFA para SuperAdministrador.");
            }

            if (configuration.GetValue<bool>("Security:AllowDevelopmentBootstrap"))
            {
                throw new InvalidOperationException("O bootstrap de desenvolvimento não pode ser habilitado fora de Development.");
            }
        }
    }
}
