using System.Text;
using Odca.Infrastructure.Identity;
using Microsoft.Extensions.Options;

namespace Odca.Api;

public static class StartupValidation
{
    public static void Validate(IConfiguration configuration, IHostEnvironment environment, JwtOptions jwt)
    {
        if (string.IsNullOrWhiteSpace(configuration.GetConnectionString("Database")))
        {
            throw new InvalidOperationException("ConnectionStrings:Database não foi configurada.");
        }

        if (string.IsNullOrWhiteSpace(jwt.Issuer) || string.IsNullOrWhiteSpace(jwt.Audience) ||
            Encoding.UTF8.GetByteCount(jwt.SigningKey) < 32)
        {
            throw new InvalidOperationException("Jwt exige issuer, audience e chave aleatória com pelo menos 32 bytes.");
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

public sealed class StartupValidationService(
    IConfiguration configuration,
    IHostEnvironment environment,
    IOptions<JwtOptions> jwtOptions) : IHostedService
{
    public Task StartAsync(CancellationToken cancellationToken)
    {
        StartupValidation.Validate(configuration, environment, jwtOptions.Value);
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
