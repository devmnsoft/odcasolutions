using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Odca.Web.Services;
namespace Odca.Web.Controllers;
[Authorize(Roles="SuperAdministrator")][Route("administracao/diagnostico")]
public sealed class PlatformDiagnosticsController(OdcaApiClient api, Odca.Configuration.Localization.LocalizationService loc):Controller
{
    // D-OC49 (S15): padrao canonico de cultura (CultureMiddleware) e titulo localizado.
    private string? Culture => HttpContext.Items[Odca.Web.Middleware.CultureMiddleware.ItemsKey] as string;
    [HttpGet]public async Task<IActionResult> Index(CancellationToken ct){var token=await HttpContext.GetTokenAsync("access_token");if(token is null)return Challenge();ViewData["Title"]=loc.T("platform.diag_title",Culture);var result=await api.GetPlatformDiagnosticsAsync(token,ct);if(!result.Succeeded){Response.StatusCode=503;return View("ServiceUnavailable");}return View(result.Value!);}
}
