using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Odca.Web.Models;
using Odca.Web.Services;

namespace Odca.Web.Controllers;

[Authorize]
[Route("organizacoes/{tenantId:guid}/renovacoes")]
public sealed class RenewalsController(OdcaApiClient api, IUserTenantContext userTenantContext) : Controller
{
    [HttpGet]
    public async Task<IActionResult> Index(Guid tenantId,DateOnly? from=null,DateOnly? to=null,Guid? ownerId=null,string? contractType=null,string? counterparty=null,string? status=null,string? priority=null,bool mine=false,bool withoutOwner=false,int page=1,CancellationToken ct=default)
    {
        if(from.HasValue&&to.HasValue&&from>to){ModelState.AddModelError(nameof(to),"O fim do período deve ser posterior ao início.");Response.StatusCode=400;}
        var token=await HttpContext.GetTokenAsync("access_token");if(token is null)return Challenge();
        var result=await api.GetRenewalsAsync(token,tenantId,from,to,ownerId,contractType,counterparty,status,priority,mine,withoutOwner,page,20,ct);
        if (result.Status == ApiCallStatus.Unauthorized) return Challenge();
        if (result.Status == ApiCallStatus.Forbidden) return Forbid();
        if (!result.Succeeded || result.Value is null) { Response.StatusCode = 503; return View("ServiceUnavailable"); }
        ViewData["TenantId"] = tenantId;
        var today = result.Value.Today ?? DateOnly.FromDateTime(DateTime.Today);
        var access = await userTenantContext.GetAccessAsync(tenantId, ct);
        var canManageConfig = access?.HasPermission("tenant.organization.manage") ?? false;
        int[]? reminderDays = null;
        var config = await api.GetRenewalConfigAsync(token, tenantId, ct);
        if (config.Succeeded && config.Value is not null) reminderDays = config.Value.Days;
        return View(new RenewalWorkspaceViewModel(result.Value, today, reminderDays, canManageConfig));
    }

    [HttpGet("{id:guid}")]
    public async Task<IActionResult> Detail(Guid tenantId, Guid id, CancellationToken ct = default)
    {
        var token = await HttpContext.GetTokenAsync("access_token"); if (token is null) return Challenge();
        var result = await api.GetRenewalDetailAsync(token, tenantId, id, ct);
        if (result.Status == ApiCallStatus.Unauthorized) return Challenge();
        if (result.Status == ApiCallStatus.Forbidden) return Forbid();
        if (result.Status == ApiCallStatus.NotFound) { TempData["Error"] = "Proposta não encontrada."; return RedirectToAction(nameof(Index), new { tenantId }); }
        if (!result.Succeeded || result.Value is null) { Response.StatusCode = 503; return View("ServiceUnavailable"); }
        ViewData["TenantId"] = tenantId;
        var access = await userTenantContext.GetAccessAsync(tenantId, ct);
        var vm = new RenewalDetailViewModel(result.Value, DateOnly.FromDateTime(DateTime.Today),
            access?.HasPermission("tenant.renewals.submit") ?? false,
            access?.HasPermission("tenant.renewals.cancel") ?? false,
            access?.HasPermission("tenant.renewals.formalize") ?? false,
            access?.HasPermission("tenant.renewals.apply") ?? false);
        return View(vm);
    }

    [HttpPost("{id:guid}/submeter")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Submit(Guid tenantId, Guid id, long rowVersion, CancellationToken ct = default)
    {
        var token = await HttpContext.GetTokenAsync("access_token"); if (token is null) return Challenge();
        var result = await api.SubmitRenewalAsync(token, tenantId, id, new Odca.Contracts.Renewals.SubmitRenewalRequest(rowVersion), ct);
        if (result.Status == ApiCallStatus.Unauthorized) return Challenge();
        if (result.Status == ApiCallStatus.Forbidden) return Forbid();
        if (result.Status == ApiCallStatus.Conflict) { TempData["Error"] = "A proposta mudou de versão ou não está mais em rascunho. Recarregue."; return RedirectToAction(nameof(Detail), new { tenantId, id }); }
        if (result.Succeeded) TempData["Success"] = "Proposta encaminhada para revisão.";
        else TempData["Error"] = result.UserMessage("Não foi possível encaminhar a proposta para revisão.");
        return RedirectToAction(nameof(Detail), new { tenantId, id });
    }

    [HttpPost("{id:guid}/cancelar")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Cancel(Guid tenantId, Guid id, long rowVersion, string? reason, CancellationToken ct = default)
    {
        var token = await HttpContext.GetTokenAsync("access_token"); if (token is null) return Challenge();
        var result = await api.CancelRenewalAsync(token, tenantId, id, new Odca.Contracts.Renewals.CancelRenewalRequest(rowVersion, reason), ct);
        if (result.Status == ApiCallStatus.Unauthorized) return Challenge();
        if (result.Status == ApiCallStatus.Forbidden) return Forbid();
        if (result.Status == ApiCallStatus.Conflict) { TempData["Error"] = "A proposta não pode mais ser cancelada neste estado. Recarregue."; return RedirectToAction(nameof(Detail), new { tenantId, id }); }
        if (result.Succeeded) TempData["Success"] = "Proposta cancelada.";
        else TempData["Error"] = result.UserMessage("Não foi possível cancelar a proposta.");
        return RedirectToAction(nameof(Detail), new { tenantId, id });
    }

    [HttpPost("{id:guid}/formalizar")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Formalize(Guid tenantId, Guid id, Guid evidenceVersionId, DateOnly formalizedOn, string justification, long rowVersion, CancellationToken ct = default)
    {
        var token = await HttpContext.GetTokenAsync("access_token"); if (token is null) return Challenge();
        if (string.IsNullOrWhiteSpace(justification)) ModelState.AddModelError(nameof(justification), "Informe a justificativa da formalização.");
        if (!ModelState.IsValid) return RedirectToAction(nameof(Detail), new { tenantId, id });
        var result = await api.FormalizeRenewalAsync(token, tenantId, id, new Odca.Contracts.Renewals.FormalizeRenewalRequest(evidenceVersionId, formalizedOn, justification, rowVersion), ct);
        if (result.Status == ApiCallStatus.Unauthorized) return Challenge();
        if (result.Status == ApiCallStatus.Forbidden) return Forbid();
        if (result.Status == ApiCallStatus.Conflict) { TempData["Error"] = "Estado, versão ou evidência segura inválidos. Recarregue."; return RedirectToAction(nameof(Detail), new { tenantId, id }); }
        if (result.Status == ApiCallStatus.InvalidRequest) { TempData["Error"] = result.UserMessage("Verifique os dados da formalização."); return RedirectToAction(nameof(Detail), new { tenantId, id }); }
        if (result.Succeeded) TempData["Success"] = "Formalização registrada. O contrato vigente só muda quando a alteração for aplicada.";
        else TempData["Error"] = result.UserMessage("Não foi possível registrar a formalização.");
        return RedirectToAction(nameof(Detail), new { tenantId, id });
    }

    [HttpPost("config/salvar")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SaveConfig(Guid tenantId, int day1, int day2, int day3, CancellationToken ct = default)
    {
        var token = await HttpContext.GetTokenAsync("access_token"); if (token is null) return Challenge();
        var result = await api.PutRenewalConfigAsync(token, tenantId, new Odca.Contracts.Renewals.RenewalReminderConfig(new[] { day1, day2, day3 }), ct);
        if (result.Status == ApiCallStatus.Unauthorized) return Challenge();
        if (result.Status == ApiCallStatus.Forbidden) { TempData["Error"] = "Seu perfil pode consultar os lembretes, mas não alterá-los."; return RedirectToAction(nameof(Index), new { tenantId }); }
        if (result.Status == ApiCallStatus.InvalidRequest) { TempData["Error"] = result.UserMessage("Informe três dias distintos em ordem decrescente, entre 1 e 365 (ex.: 30, 15, 7)."); return RedirectToAction(nameof(Index), new { tenantId }); }
        if (result.Succeeded) TempData["Success"] = $"Lembretes atualizados para {day1}, {day2} e {day3} dias antes do vencimento.";
        else TempData["Error"] = result.UserMessage("Não foi possível salvar os lembretes.");
        return RedirectToAction(nameof(Index), new { tenantId });
    }

    [HttpPost("{requestId:guid}/aplicar")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Apply(Guid tenantId, Guid requestId, long rowVersion, CancellationToken ct = default)
    {
        var token = await HttpContext.GetTokenAsync("access_token");
        if (token is null) return Challenge();

        var result = await api.ApplyRenewalAsync(token, tenantId, requestId, new Odca.Contracts.Renewals.ApplyRenewalRequest(rowVersion), ct);
        if (result.Status == ApiCallStatus.Unauthorized) return Challenge();
        if (result.Status == ApiCallStatus.Forbidden) return Forbid();

        if (result.Status == ApiCallStatus.Conflict)
        {
            TempData["Conflict"] = "O registro mudou. Recarregue.";
            return RedirectToAction(nameof(Index), new { tenantId });
        }

        if (result.Succeeded)
        {
            TempData["Success"] = result.Value?.EndsOn is { } endsOn
                ? $"Alteração aplicada com sucesso. Nova vigência até {endsOn:dd/MM/yyyy}."
                : "Alteração de vigência aplicada com sucesso.";
        }
        else
        {
            TempData["Error"] = result.UserMessage("Não foi possível aplicar a alteração de vigência.");
        }

        return RedirectToAction(nameof(Index), new { tenantId });
    }
}
