using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Odca.Contracts.Administration;
using Odca.Web.Services;

namespace Odca.Web.Controllers;

/// <summary>
/// Operação contratual Bloco B: configurações operacionais reais — matriz de SLA vigente
/// (plano × serviço × prioridade) vinda de sla_policies. Leitura: a edição de políticas é
/// uma decisão da plataforma ainda não assumida, então a página não inventa controles.
/// </summary>
[Authorize(Roles = "SuperAdministrator")]
[Route("administracao/configuracoes")]
public sealed class PlatformOperationsController(OdcaApiClient api) : Controller
{
    [HttpGet]
    public async Task<IActionResult> Index(CancellationToken ct)
    {
        ViewData["Title"] = "Configurações operacionais";
        var token = await HttpContext.GetTokenAsync("access_token");
        if (token is null) return Challenge();
        var result = await api.GetSlaPoliciesAsync(token, ct);
        if (!result.Succeeded)
        {
            Response.StatusCode = 503;
            return View("ServiceUnavailable");
        }
        return View(result.Value!);
    }
}
