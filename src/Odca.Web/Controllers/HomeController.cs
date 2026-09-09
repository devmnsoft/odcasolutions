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

        var token = await HttpContext.GetTokenAsync("access_token");
        var dashboard = token is null ? null : await apiClient.GetDashboardAsync(token, cancellationToken);
        if (dashboard is null)
        {
            return RedirectToAction("Login", "Account");
        }

        return View(dashboard);
    }

    [AllowAnonymous]
    [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
    public IActionResult Error() =>
        View(new ErrorViewModel { RequestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier });
}
