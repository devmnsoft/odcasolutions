using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Odca.Web.Services;

namespace Odca.Web.Controllers;

[AllowAnonymous]
public sealed class CatalogController(OdcaApiClient apiClient) : Controller
{
    [HttpGet("conheca-os-planos")]
    public async Task<IActionResult> Plans(CancellationToken cancellationToken)
    {
        var result = await apiClient.GetPublicPlansAsync(cancellationToken);
        if (!result.Succeeded)
        {
            Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
            return View("ServiceUnavailable");
        }

        return View("~/Views/Plans/Index.cshtml", result.Value);
    }
}
