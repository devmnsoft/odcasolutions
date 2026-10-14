using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.DependencyInjection;
using Odca.Api.Localization;
using Odca.Application.Onboarding;
using Odca.Contracts.Onboarding;

namespace Odca.Api.Controllers;

[ApiController]
[Route("api/v1/onboarding")]
public sealed class OnboardingController(
    CustomerOnboardingService onboarding,
    IWebHostEnvironment environment)
    : ControllerBase
{
    [HttpPost("registrations")]
    [AllowAnonymous]
    [EnableRateLimiting("privacy-intake")]
    public async Task<ActionResult<StartCustomerRegistrationResponse>> StartRegistration(
        StartCustomerRegistrationRequest request,
        CancellationToken cancellationToken)
    {
        var result = await onboarding.StartRegistrationAsync(
            new CustomerRegistrationSubmission(
                request.PlanCode,
                request.Document,
                request.ResponsibleName,
                request.Email,
                request.Password,
                request.AcceptedTerms,
                request.AcknowledgedPrivacyNotice,
                request.MarketingConsent),
            environment.IsDevelopment() || environment.IsEnvironment("Testing"),
            cancellationToken);
        if (!result.Succeeded || result.RegistrationId is null)
        {
            // D-OC27: textos canônicos do CustomerOnboardingService resolvidos por mapa reverso.
            var culture = this.Culture();
            var localizer = HttpContext.RequestServices.GetRequiredService<Odca.Configuration.Localization.LocalizationService>();
            return ValidationProblem(new ValidationProblemDetails
            {
                Title = this.T("api.validation.invalid_fields"),
                Extensions = { ["code"] = "api.onboarding.registration_invalid" },
                Errors = { ["registration"] = [.. result.Errors.Select(error => ApiMessages.Localize(localizer, error, culture) ?? error)] }
            });
        }

        return Accepted(new StartCustomerRegistrationResponse(
            result.RegistrationId.Value,
            "Cadastro recebido. Confirme o e-mail para liberar a organização em estado comercial pendente.",
            result.DevelopmentConfirmationToken));
    }

    [HttpPost("confirm-email")]
    [AllowAnonymous]
    public async Task<ActionResult<ConfirmCustomerRegistrationResponse>> ConfirmEmail(
        ConfirmCustomerRegistrationRequest request,
        CancellationToken cancellationToken)
    {
        var result = await onboarding.ConfirmRegistrationAsync(
            request.RegistrationId,
            request.Token,
            cancellationToken);
        if (!result.Succeeded)
        {
            var culture = this.Culture();
            var localizer = HttpContext.RequestServices.GetRequiredService<Odca.Configuration.Localization.LocalizationService>();
            return ValidationProblem(new ValidationProblemDetails
            {
                Title = this.T("api.validation.invalid_fields"),
                Extensions = { ["code"] = "api.onboarding.confirm_invalid" },
                Errors = { ["token"] = [.. result.Errors.Select(error => ApiMessages.Localize(localizer, error, culture) ?? error)] }
            });
        }

        return Ok(new ConfirmCustomerRegistrationResponse(
            "E-mail confirmado. A organização está acessível com contratação pendente."));
    }

    [HttpGet("customer-home")]
    [Authorize(Policy = "PasswordChanged")]
    public async Task<ActionResult<CustomerHomeResponse>> CustomerHome(
        [FromQuery] Guid? tenantId,
        CancellationToken cancellationToken)
    {
        if (!Guid.TryParse(User.FindFirstValue("sub"), out var userId))
        {
            return Unauthorized();
        }

        var home = await onboarding.GetHomeAsync(userId, tenantId, cancellationToken);
        if (home is null)
        {
            return Forbid();
        }

        return Ok(new CustomerHomeResponse(
            home.TenantId,
            home.OrganizationName,
            home.TenantStatus,
            home.CommercialState,
            home.Plan.Code,
            home.Plan.DisplayName,
            home.Plan.Version,
            home.Plan.ActiveSeats,
            home.Plan.StorageBytes,
            home.Timezone));
    }
}
