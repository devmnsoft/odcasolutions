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

        var plans = await apiClient.GetPlansAsync(token, cancellationToken);
        if (plans is null)
        {
            TempData["Error"] = "Não foi possível carregar o catálogo de planos.";
            return RedirectToAction("Index", "Home");
        }

        return View(plans);
    }
}
