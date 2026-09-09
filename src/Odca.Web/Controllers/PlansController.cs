using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Odca.Web.Services;

namespace Odca.Web.Controllers;

[Authorize]
public sealed class PlansController(OdcaApiClient apiClient) : Controller
{
    [HttpGet("planos")]
    public async Task<IActionResult> Index(CancellationToken cancellationToken)
    {
        var token = await HttpContext.GetTokenAsync("access_token");
        if (token is null)
        {
            return Challenge();
        }

        var result = await apiClient.GetPlansAsync(token, cancellationToken);
        if (result.Status == ApiCallStatus.Unauthorized)
        {
            return Challenge();
        }

        if (result.Status == ApiCallStatus.Forbidden)
        {
            return Forbid();
        }

        if (!result.Succeeded)
        {
            Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
            return View("ServiceUnavailable");
        }

        return View(result.Value);
    }
}
