using System.Diagnostics;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Odca.Web.Models;
using Odca.Web.Services;

namespace Odca.Web.Controllers;

[Authorize]
public sealed class HomeController(OdcaApiClient apiClient) : Controller
{
    public async Task<IActionResult> Index(CancellationToken cancellationToken)
    {
        if (User.FindFirst("must_change_password")?.Value == "true")
        {
            return RedirectToAction("ChangePassword", "Account");
        }

        if (User.FindFirst("requires_mfa_enrollment")?.Value == "true")
        {
            return RedirectToAction("MfaEnrollment", "Account");
        }

        if (User.FindFirst("requires_mfa_challenge")?.Value == "true")
        {
            return RedirectToAction("MfaChallenge", "Account");
        }

        var token = await HttpContext.GetTokenAsync("access_token");
        if (token is null)
        {
            return Challenge();
        }

        var result = await apiClient.GetDashboardAsync(token, cancellationToken);
        if (result.Status == ApiCallStatus.Unauthorized)
        {
            return Challenge();
        }

        if (result.Status == ApiCallStatus.Forbidden)
        {
            var customerHome = await apiClient.GetCustomerHomeAsync(token, cancellationToken);
            return customerHome.Succeeded
                ? RedirectToAction("CustomerHome", "Onboarding")
                : Forbid();
        }

        if (!result.Succeeded)
        {
            Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
            return View("ServiceUnavailable");
        }

        return View(result.Value);
    }

    [AllowAnonymous]
    [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
    public IActionResult Error() =>
        View(new ErrorViewModel { RequestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier });
}
