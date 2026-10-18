using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Odca.Contracts.Privacy;
using Odca.Configuration.Localization;
using Odca.Web.Models;
using Odca.Web.Services;

namespace Odca.Web.Controllers;

[AllowAnonymous]
public sealed class PrivacyController(OdcaApiClient apiClient, LocalizationService loc) : Controller
{
    // D-OC41 (S7): padrão canônico de cultura (CultureMiddleware) para o canal público.
    private string? Culture => HttpContext.Items[Odca.Web.Middleware.CultureMiddleware.ItemsKey] as string;

    private string L(string key, params string[] args) => loc.T(key, Culture, args);
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
                ApiCallStatus.RateLimited => L("priv.rate_limited"),
                ApiCallStatus.Timeout => L("priv.timeout"),
                _ => L("priv.unavailable")
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
