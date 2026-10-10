using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Odca.Web.Models;
using Odca.Web.Services;

namespace Odca.Web.Controllers;

[Authorize(Roles = "SuperAdministrator")]
[Route("administracao/auditoria")]
public sealed class PlatformAuditController(OdcaApiClient api) : Controller
{
    [HttpGet]
    public async Task<IActionResult> Index(string? search, Guid? tenantId, int page = 1, int pageSize = 25, CancellationToken ct = default)
    {
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 10, 100);
        var token = await HttpContext.GetTokenAsync("access_token");
        if (token is null) return Challenge();
        var result = await api.GetPlatformAuditEventsAsync(token, search, tenantId, page, pageSize, ct);
        if (!result.Succeeded)
        {
            var forbidden = result.Status == ApiCallStatus.Forbidden;
            Response.StatusCode = forbidden ? 403 : 503;
            return View(forbidden ? "AccessDenied" : "ServiceUnavailable");
        }
        var customers = await api.GetCustomersAsync(token, null, null, null, ct);
        var tenantOptions = customers.Succeeded && customers.Value is not null
            ? customers.Value.Select(customer => new PlatformAuditTenantOption(customer.TenantId, customer.Name)).ToArray()
            : Array.Empty<PlatformAuditTenantOption>();
        return View(new PlatformAuditListViewModel(result.Value!, search, tenantId, pageSize, tenantOptions, !customers.Succeeded));
    }
}
