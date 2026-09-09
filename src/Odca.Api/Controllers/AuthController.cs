using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Odca.Application.Common;
using Odca.Application.Identity;
using Odca.Contracts.Identity;

namespace Odca.Api.Controllers;

[ApiController]
[Route("api/v1/auth")]
public sealed class AuthController(AuthenticationService authentication, IIdentityRepository repository, IClock clock)
    : ControllerBase
{
    [HttpPost("login")]
    [AllowAnonymous]
    [EnableRateLimiting("login")]
    public async Task<ActionResult<LoginResponse>> Login(
        LoginRequest request,
        CancellationToken cancellationToken)
    {
        var result = await authentication.LoginAsync(
            request.Login,
            request.Password,
            HttpContext.Connection.RemoteIpAddress?.ToString(),
            Request.Headers.UserAgent.ToString(),
            cancellationToken);
        if (!result.Succeeded || result.User is null || result.Token is null)
        {
            return Unauthorized(new ProblemDetails
            {
                Status = StatusCodes.Status401Unauthorized,
                Title = "Não foi possível autenticar.",
                Detail = result.FailureMessage
            });
        }

        return Ok(ToResponse(result.User, result.Token));
    }

    [HttpPost("change-password")]
    [Authorize]
    public async Task<ActionResult<LoginResponse>> ChangePassword(
        ChangePasswordRequest request,
        CancellationToken cancellationToken)
    {
        if (!TryGetIdentity(out var userId, out var sessionId))
        {
            return Unauthorized();
        }

        var result = await authentication.ChangePasswordAsync(
            userId,
            sessionId,
            request.CurrentPassword,
            request.NewPassword,
            HttpContext.Connection.RemoteIpAddress?.ToString(),
            Request.Headers.UserAgent.ToString(),
            cancellationToken);
        if (!result.Succeeded || result.User is null || result.Token is null)
        {
            return ValidationProblem(new ValidationProblemDetails(
                new Dictionary<string, string[]> { ["newPassword"] = [.. result.Errors] }));
        }

        return Ok(ToResponse(result.User, result.Token));
    }

    [HttpPost("logout")]
    [Authorize]
    public async Task<IActionResult> Logout(CancellationToken cancellationToken)
    {
        if (TryGetIdentity(out var userId, out var sessionId))
        {
            await repository.RevokeSessionAsync(userId, sessionId, clock.UtcNow, cancellationToken);
        }

        return NoContent();
    }

    private bool TryGetIdentity(out Guid userId, out Guid sessionId)
    {
        var hasUser = Guid.TryParse(User.FindFirstValue("sub"), out userId);
        var hasSession = Guid.TryParse(User.FindFirstValue("sid"), out sessionId);
        return hasUser && hasSession;
    }

    private static LoginResponse ToResponse(UserCredential user, IssuedToken token) =>
        new(token.AccessToken, token.ExpiresAt, user.DisplayName, user.MustChangePassword);
}
