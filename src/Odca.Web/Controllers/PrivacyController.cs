using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Odca.Contracts.Privacy;
using Odca.Web.Models;
using Odca.Web.Services;

namespace Odca.Web.Controllers;

[AllowAnonymous]
public sealed class PrivacyController(OdcaApiClient apiClient) : Controller
{
    [HttpGet("privacidade")]
    public IActionResult Index() => View(new PrivacyRequestViewModel());

    [HttpPost("privacidade")]
    [EnableRateLimiting("privacy-intake")]
    public async Task<IActionResult> Index(PrivacyRequestViewModel model, CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid)
        {
            return View(model);
        }

        var result = await apiClient.SubmitPrivacyRequestAsync(
            new CreatePrivacyRequest(model.Email, model.RequestType, model.Details),
            cancellationToken);
        if (!result.Succeeded)
        {
            ModelState.AddModelError(string.Empty, result.Status switch
            {
                ApiCallStatus.RateLimited => "Limite temporário atingido. Aguarde antes de reenviar.",
                ApiCallStatus.Timeout => "O serviço demorou para responder. Tente novamente.",
                _ => "O canal está temporariamente indisponível. Nenhuma confirmação foi registrada."
            });
            return View(model);
        }

        ModelState.Clear();
        return View(new PrivacyRequestViewModel
        {
            Protocol = result.Value!.Protocol,
            ConfirmationMessage = result.Value.Message
        });
    }
}
