using Microsoft.Extensions.Options;
using Odca.Infrastructure.Identity;

namespace Odca.Api;

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
