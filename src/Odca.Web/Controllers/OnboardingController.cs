using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Odca.Contracts.Onboarding;
using Odca.Contracts.Tenancy;
using Odca.Web.Models;
using Odca.Web.Services;

namespace Odca.Web.Controllers;

public sealed class OnboardingController(OdcaApiClient apiClient) : Controller
{
    [AllowAnonymous]
    [HttpGet("contratar/{planCode?}")]
    public IActionResult Register(string? planCode) =>
        View(new CustomerRegistrationViewModel { PlanCode = planCode ?? string.Empty });

    [AllowAnonymous]
    [HttpPost("contratar/{planCode?}")]
    public async Task<IActionResult> Register(CustomerRegistrationViewModel model, CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid)
        {
            return View(model);
        }

        var result = await apiClient.StartCustomerRegistrationAsync(
            new StartCustomerRegistrationRequest(
                model.PlanCode,
                model.Document,
                model.ResponsibleName,
                model.Email,
                model.Password,
                model.AcceptedTerms,
                model.AcknowledgedPrivacyNotice,
                model.MarketingConsent),
            cancellationToken);
        if (!result.Succeeded)
        {
            ModelState.AddModelError(string.Empty, result.Status switch
            {
                ApiCallStatus.RateLimited => "Muitas tentativas. Aguarde e tente novamente.",
                ApiCallStatus.Timeout or ApiCallStatus.Unavailable => "O serviço está temporariamente indisponível.",
                _ => "Não foi possível iniciar o cadastro. Confira os dados informados."
            });
            return View(model);
        }

        return View("RegistrationStarted", new CustomerRegistrationStartedViewModel
        {
            RegistrationId = result.Value!.RegistrationId,
            Message = result.Value.Message,
            DevelopmentConfirmationToken = result.Value.DevelopmentConfirmationToken
        });
    }

    [AllowAnonymous]
    [HttpGet("confirmar-email")]
    public IActionResult ConfirmEmail(Guid registrationId, string? token) =>
        View(new ConfirmEmailViewModel { RegistrationId = registrationId, Token = token ?? string.Empty });

    [AllowAnonymous]
    [HttpPost("confirmar-email")]
    public async Task<IActionResult> ConfirmEmail(ConfirmEmailViewModel model, CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid)
        {
            return View(model);
        }

        var result = await apiClient.ConfirmCustomerRegistrationAsync(
            new ConfirmCustomerRegistrationRequest(model.RegistrationId, model.Token),
            cancellationToken);
        if (!result.Succeeded)
        {
            ModelState.AddModelError(string.Empty, "Token inválido, expirado ou já utilizado.");
            return View(model);
        }

        TempData["Success"] = result.Value!.Message;
        return RedirectToAction("Login", "Account");
    }

    [Authorize]
    [HttpGet("cliente")]
    public async Task<IActionResult> CustomerHome(CancellationToken cancellationToken)
    {
        var token = await HttpContext.GetTokenAsync("access_token");
        if (token is null)
        {
            return RedirectToAction("Login", "Account");
        }

        var result = await apiClient.GetCustomerHomeAsync(token, cancellationToken);
        if (!result.Succeeded)
        {
            return result.Status == ApiCallStatus.Unauthorized
                ? RedirectToAction("Login", "Account")
                : RedirectToAction("AccessDenied", "Account");
        }

        OrganizationOverviewResponse? overview = null;
        var overviewResult = await apiClient.GetOrganizationOverviewAsync(token, result.Value!.TenantId, cancellationToken);
        if (overviewResult.Succeeded)
        {
            overview = overviewResult.Value;
        }

        ViewData["OrganizationName"] = result.Value.OrganizationName;
        return View(new CustomerHomePageViewModel
        {
            Home = result.Value,
            Overview = overview
        });
    }
}
