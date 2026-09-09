using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Odca.Application.Dashboard;
using Odca.Contracts.Identity;

namespace Odca.Api.Controllers;

[ApiController]
[Route("api/v1/platform/dashboard")]
[Authorize(Policy = "PlatformAdministrator")]
public sealed class DashboardController(IPlatformDashboardRepository repository) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<DashboardResponse>> Get(CancellationToken cancellationToken)
    {
        if (!Guid.TryParse(User.FindFirstValue("sub"), out var userId))
        {
            return Unauthorized();
        }

        var snapshot = await repository.GetAsync(userId, cancellationToken);
        return Ok(new DashboardResponse(
            User.FindFirstValue(ClaimTypes.Name) ?? "Administrador",
            snapshot.ActiveTenants,
            snapshot.ActiveUsers,
            snapshot.PendingPrivacyItems));
    }
}
