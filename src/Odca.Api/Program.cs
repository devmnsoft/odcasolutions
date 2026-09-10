using System.Security.Claims;
using System.Text;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.IdentityModel.Tokens;
using Microsoft.Extensions.Options;
using Odca.Application.Identity;
using Odca.Infrastructure;
using Odca.Infrastructure.Database;
using Odca.Infrastructure.Identity;
using Odca.Configuration;

var builder = WebApplication.CreateBuilder(args);

var localConfiguration = LocalRuntimeConfiguration.Add(builder.Configuration, builder.Environment, args);
Console.WriteLine($"Configuração: ambiente={localConfiguration.Environment}; caminho={localConfiguration.Path ?? "não utilizado"}.");

builder.Logging.AddJsonConsole(options => options.IncludeScopes = true);
builder.Services.AddProblemDetails(options =>
{
    options.CustomizeProblemDetails = context =>
        context.ProblemDetails.Extensions["traceId"] = context.HttpContext.TraceIdentifier;
});
builder.Services.AddControllers();
builder.Services.AddOpenApi();
builder.Services.AddOdcaInfrastructure(builder.Configuration);
builder.Services.AddHostedService<Odca.Api.StartupValidationService>();
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
                if (!Guid.TryParse(context.Principal?.FindFirstValue("sub"), out var userId) ||
                    !Guid.TryParse(context.Principal?.FindFirstValue("sid"), out var sessionId) ||
                    !int.TryParse(context.Principal?.FindFirstValue("security_version"), out var securityVersion))
                {
                    context.Fail("Token sem identidade de sessão válida.");
                    return;
                }

                var repository = context.HttpContext.RequestServices.GetRequiredService<IIdentityRepository>();
                var valid = await repository.IsSessionValidAsync(
                    userId,
                    sessionId,
                    securityVersion,
                    DateTimeOffset.UtcNow,
                    context.HttpContext.RequestAborted);
                if (!valid)
                {
                    context.Fail("Sessão revogada ou expirada.");
                }
            }
        };
    });
builder.Services.AddAuthorization(options =>
{
    options.AddPolicy("PasswordChanged", policy => policy
        .RequireAuthenticatedUser()
        .RequireClaim("must_change_password", "false"));
    options.AddPolicy("PlatformAdministrator", policy => policy
        .RequireAuthenticatedUser()
        .RequireRole("SuperAdministrator")
        .RequireClaim("must_change_password", "false")
        .RequireClaim("mfa_verified", "true"));
});

var app = builder.Build();

if (!app.Environment.IsEnvironment("Testing"))
{
    app.UseExceptionHandler();
}
app.UseStatusCodePages();
app.UseHttpsRedirection();
app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
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
