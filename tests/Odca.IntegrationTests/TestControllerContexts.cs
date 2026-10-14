using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Odca.Configuration.Localization;

namespace Odca.IntegrationTests;

/// <summary>
/// D-OC27: HttpContext para invocação direta de controllers fora do pipeline HTTP.
/// As fábricas de erro resolvem o <see cref="LocalizationService"/> via
/// HttpContext.RequestServices; aqui se fornece a DI mínima com os catálogos reais
/// (copiados ao output do teste) e a ProblemDetailsFactory padrão do MVC, espelhando
/// o que o host registra em produção.
/// </summary>
internal static class TestControllerContexts
{
    public static DefaultHttpContext Create(Guid actorId, params Claim[] extraClaims)
    {
        var context = new DefaultHttpContext();
        var claims = new List<Claim>(extraClaims)
        {
            new("sub", actorId.ToString()),
            new(ClaimTypes.NameIdentifier, actorId.ToString())
        };
        context.User = new ClaimsPrincipal(new ClaimsIdentity(claims, "test"));

        var services = new ServiceCollection();
        services.AddSingleton(new LocalizationService());
        services.AddSingleton<ProblemDetailsFactory>(new DefaultProblemDetailsFactory(
            Options.Create(new ApiBehaviorOptions()),
            Options.Create(new ProblemDetailsOptions())));
        context.RequestServices = services.BuildServiceProvider();
        return context;
    }
}
