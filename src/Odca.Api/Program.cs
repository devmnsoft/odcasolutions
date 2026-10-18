using System.Security.Claims;
using System.Text;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.Http;
using Microsoft.IdentityModel.Tokens;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Odca.Application.Identity;
using Odca.Infrastructure;
using Odca.Infrastructure.Database;
using Odca.Infrastructure.Identity;
using Odca.Api;
using Odca.Configuration;

var builder = WebApplication.CreateBuilder(args);

var localConfiguration = LocalRuntimeConfiguration.Add(builder.Configuration, builder.Environment, args);
Console.WriteLine($"Configuração: ambiente={localConfiguration.Environment}; caminho={localConfiguration.Path ?? "não utilizado"}.");

builder.Logging.AddJsonConsole(options => options.IncludeScopes = true);
builder.Services.AddSingleton<Odca.Configuration.Localization.LocalizationService>();
builder.Services.AddProblemDetails(options =>
{
    options.CustomizeProblemDetails = context =>
    {
        context.ProblemDetails.Extensions["traceId"] = context.HttpContext.TraceIdentifier;
        // D-OC27: erros 500 (exceção não tratada) ganham título e código estáveis;
        // a identidade técnica segue em traceId. Cultura via X-Odca-Culture.
        if (context.HttpContext.Response.StatusCode is 500 or 502 or 503)
        {
            var localizer = context.HttpContext.RequestServices.GetRequiredService<Odca.Configuration.Localization.LocalizationService>();
            var culture = Odca.Api.Localization.ApiCulture.For(context.HttpContext);
            context.ProblemDetails.Title = localizer.T("api.internal.error", culture);
            context.ProblemDetails.Extensions["code"] = "api.internal.error";
        }
        // D-OC30: o 429 do rate limiter também passa por este hook — no .NET 10 o
        // middleware gera o problem+json pela fábrica registrada (a presença de
        // traceId no corpo o confirma). Título/detalhe na cultura do usuário e
        // código estável rate_limited (mesma convenção dos gates de transporte).
        else if (context.HttpContext.Response.StatusCode == StatusCodes.Status429TooManyRequests)
        {
            var localizer = context.HttpContext.RequestServices.GetRequiredService<Odca.Configuration.Localization.LocalizationService>();
            var culture = Odca.Api.Localization.ApiCulture.For(context.HttpContext);
            context.ProblemDetails.Title = localizer.T("api.rate_limited", culture);
            context.ProblemDetails.Detail = localizer.T("api.rate_limited.detail", culture);
            context.ProblemDetails.Extensions["code"] = "rate_limited";
        }
    };
});
builder.Services.AddControllers();
builder.Services.AddTransient<OrganizationFeatureGate>();
// O ReleaseFeatureGate implementa IMiddleware e é resolvido pelo container, então precisa de registro explícito.
builder.Services.AddTransient<ReleaseFeatureGate>();
builder.Services.AddOpenApi();
builder.Services.AddOdcaInfrastructure(builder.Configuration);
// D-OC31 B.1: protetor dos títulos de notificação, compartilhado com o Worker pelo
// mesmo repositório de chaves (dev: filesystem do runtime config; produção: store
// Postgres v054). Resolvido sob demanda nos controladores via RequestServices.
builder.Services.AddSingleton(sp => sp.GetRequiredService<IDataProtectionProvider>().CreateProtector("ODCA.UserNotification.v1"));
builder.Services.AddHostedService<Odca.Api.StartupValidationService>();
builder.Services.AddHostedService<Odca.Api.OfficialCatalogSeedingService>();
builder.Services.AddHealthChecks()
    .AddCheck<PostgresReadinessHealthCheck>("postgresql", tags: ["ready"]);
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.AddPolicy("login", context => RateLimitPartition.GetFixedWindowLimiter(
        context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
        _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = 5,
            Window = TimeSpan.FromMinutes(1),
            QueueLimit = 0,
            AutoReplenishment = true
        }));
    options.AddPolicy("privacy-intake", context => RateLimitPartition.GetFixedWindowLimiter(
        context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
        _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = 5,
            Window = TimeSpan.FromMinutes(10),
            QueueLimit = 0,
            AutoReplenishment = true
        }));
});
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer();
builder.Services.AddOptions<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme)
    .Configure<IOptions<JwtOptions>>((options, jwtOptions) =>
    {
        var jwt = jwtOptions.Value;
        options.MapInboundClaims = false;
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = jwt.Issuer,
            ValidateAudience = true,
            ValidAudience = jwt.Audience,
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwt.SigningKey)),
            ValidateLifetime = true,
            RequireExpirationTime = true,
            RequireSignedTokens = true,
            ClockSkew = TimeSpan.FromSeconds(30),
            ValidAlgorithms = [SecurityAlgorithms.HmacSha256],
            NameClaimType = ClaimTypes.Name,
            RoleClaimType = ClaimTypes.Role
        };
        options.Events = new JwtBearerEvents
        {
            OnTokenValidated = async context =>
            {
                // D-OC28: razões de falha de sessão resolvidas por catálogo na cultura do usuário.
                var sessionLocalizer = context.HttpContext.RequestServices.GetRequiredService<Odca.Configuration.Localization.LocalizationService>();
                var sessionCulture = Odca.Api.Localization.ApiCulture.For(context.HttpContext);
                if (!Guid.TryParse(context.Principal?.FindFirstValue("sub"), out var userId) ||
                    !Guid.TryParse(context.Principal?.FindFirstValue("sid"), out var sessionId) ||
                    !int.TryParse(context.Principal?.FindFirstValue("security_version"), out var securityVersion))
                {
                    context.Fail(sessionLocalizer.T("api.auth.session_identity_invalid", sessionCulture));
                    return;
                }

                var repository = context.HttpContext.RequestServices.GetRequiredService<IIdentityRepository>();
                var path = context.HttpContext.Request.Path.Value ?? string.Empty;
                var situationOnly = HttpMethods.IsGet(context.HttpContext.Request.Method)
                    && path.Contains("/organizations/", StringComparison.OrdinalIgnoreCase)
                    && path.TrimEnd('/').EndsWith("/features", StringComparison.OrdinalIgnoreCase);
                var valid = situationOnly
                    ? await repository.IsEstablishedSessionAsync(
                        userId,
                        sessionId,
                        securityVersion,
                        DateTimeOffset.UtcNow,
                        context.HttpContext.RequestAborted)
                    : await repository.IsSessionValidAsync(
                        userId,
                        sessionId,
                        securityVersion,
                        DateTimeOffset.UtcNow,
                        context.HttpContext.RequestAborted);
                if (!valid)
                {
                    context.Fail(sessionLocalizer.T("api.auth.session_revoked_expired", sessionCulture));
                    return;
                }

                // O requisito de MFA para superadministradores é aplicado na camada de
                // autorização (políticas PasswordChanged/PlatformAdministrator), de modo
                // que uma sessão autenticada sem MFA recebe 403 Forbidden nas operações
                // administrativas, sem invalidar o próprio token.
            }
        };
    });
builder.Services.AddSingleton<IAuthorizationHandler, MfaVerifiedForSuperAdministratorHandler>();
// D-OC39 (Sprint 5/F-1): reautenticacao MFA recente para operacoes sensiveis.
builder.Services.Configure<Odca.Api.RecentMfaOptions>(
    builder.Configuration.GetSection(Odca.Api.RecentMfaOptions.Section));
builder.Services.AddSingleton<IAuthorizationHandler, Odca.Api.RecentMfaHandler>();
builder.Services.AddAuthorization(options =>
{
    options.AddPolicy("PasswordChanged", policy => policy
        .RequireAuthenticatedUser()
        .RequireClaim("must_change_password", "false")
        .Requirements.Add(new MfaVerifiedForSuperAdministratorRequirement()));
    options.AddPolicy("PlatformAdministrator", policy => policy
        .RequireAuthenticatedUser()
        .RequireRole("SuperAdministrator")
        .RequireClaim("must_change_password", "false")
        .RequireClaim("mfa_verified", "true"));
    options.AddPolicy("RecentMfa", policy => policy
        .RequireAuthenticatedUser()
        .Requirements.Add(new RecentMfaRequirement()));
});

var app = builder.Build();

if (!app.Environment.IsEnvironment("Testing"))
{
    app.UseExceptionHandler();
}
// Gate do ciclo de liberação: recursos ainda não disponíveis nesta versão (importações,
// upload de documentos e extração/OCR) respondem 503 antes de suspensão, plano ou permissão.
// Registrado antes de UseStatusCodePages para preservar o corpo problem+json.
app.UseMiddleware<ReleaseFeatureGate>();
app.UseStatusCodePages();
app.UseHttpsRedirection();
app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();
app.UseMiddleware<OrganizationFeatureGate>();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
    // Diagnóstico Development: exercita o caminho 500 (exceção não tratada) de forma
    // determinística para homologação do problema+json em ambiente de desenvolvimento.
    app.MapGet("/api/v1/diagnostics/fault", (Microsoft.AspNetCore.Http.HttpContext context) => throw new InvalidOperationException("D-OC30 diagnostico de erro nao tratado"));
}

app.MapHealthChecks("/health/live", new HealthCheckOptions
{
    Predicate = _ => false,
    ResponseWriter = static (context, _) => context.Response.WriteAsJsonAsync(new { status = "ok" })
});
app.MapHealthChecks("/health/ready", new HealthCheckOptions
{
    Predicate = check => check.Tags.Contains("ready"),
    ResponseWriter = static (context, report) =>
        context.Response.WriteAsJsonAsync(new { status = report.Status.ToString().ToLowerInvariant() })
});
app.MapControllers();
app.Run();

#pragma warning disable CA1050 // Required entry point marker for WebApplicationFactory.
public partial class Program;
#pragma warning restore CA1050
