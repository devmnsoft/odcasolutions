using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace Odca.AI;

public static class OdcaAiServiceCollectionExtensions
{
    /// <summary>
    /// Registra <see cref="IAiService"/> conforme a seção "Ai": provedor "http" só é ativado com
    /// Endpoint absoluto e ApiKey presentes — caso contrário cai em "none" (fallback honesto),
    /// sem exceção nem configuração parcial funcionando às cegas.
    /// </summary>
    public static IServiceCollection AddOdcaAi(this IServiceCollection services, IConfiguration configuration)
    {
        var section = configuration.GetSection(AiOptions.Section);
        services.Configure<AiOptions>(section);

        var candidate = section.Get<AiOptions>() ?? new AiOptions();
        var provider = string.IsNullOrWhiteSpace(candidate.Provider) ? "none" : candidate.Provider.Trim().ToLowerInvariant();

        if (provider == "http" &&
            !string.IsNullOrWhiteSpace(candidate.Endpoint) &&
            !string.IsNullOrWhiteSpace(candidate.ApiKey) &&
            Uri.TryCreate(candidate.Endpoint.Trim(), UriKind.Absolute, out var baseUrl))
        {
            // Garante barra final: a chamada usa URI relativa "chat/completions".
            var baseUri = baseUrl.OriginalString.EndsWith('/')
                ? baseUrl
                : new Uri(baseUrl.ToString().TrimEnd('/') + "/");
            services.AddHttpClient<HttpAiService>(client =>
            {
                client.BaseAddress = baseUri;
                client.Timeout = TimeSpan.FromSeconds(Math.Clamp(candidate.TimeoutSeconds, 5, 120));
            });
            services.TryAddScoped<IAiService>(sp => sp.GetRequiredService<HttpAiService>());
        }
        else
        {
            services.TryAddSingleton<IAiService, NullAiService>();
        }

        return services;
    }
}
