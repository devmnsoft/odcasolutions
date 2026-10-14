using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Odca.Api.Localization;
using Odca.Application.Consumption;
using Odca.Contracts.Consumption;

namespace Odca.Api.Controllers;

[ApiController]
[Authorize(Policy = "PasswordChanged")]
public sealed class ConsumptionController(IConsumptionRepository repository) : ControllerBase
{
    [HttpGet("api/v1/organizations/{tenantId:guid}/consumption")]
    public async Task<IActionResult> Summary(Guid tenantId,CancellationToken ct){var actor=Actor();if(actor is null)return Unauthorized();var value=await repository.GetSummaryAsync(actor.Value,tenantId,false,ct);return value is null?Forbid():Ok(value);}
    [HttpGet("api/v1/organizations/{tenantId:guid}/storage-requests")]
    public async Task<IActionResult> Requests(Guid tenantId,CancellationToken ct){var actor=Actor();if(actor is null)return Unauthorized();var value=await repository.ListRequestsAsync(actor.Value,tenantId,false,ct);return Ok(value);}
    [HttpGet("api/v1/storage-packages")]
    public async Task<IActionResult> Packages(CancellationToken ct)=>Ok(await repository.ListPackagesAsync(ct));
    [HttpPost("api/v1/organizations/{tenantId:guid}/storage-requests")]
    public async Task<IActionResult> SubmitStorageRequest(Guid tenantId,[FromBody]CreateStorageRequest request,CancellationToken ct){var actor=Actor();if(actor is null)return Unauthorized();if(request.Quantity is<1 or>100||request.IdempotencyKey==Guid.Empty)return this.Invalid("api.consumption.storage_request_invalid");var value=await repository.RequestStorageAsync(actor.Value,tenantId,request,ct);return value is null?Forbid():Created($"api/v1/organizations/{tenantId}/storage-requests/{value.Id}",value);}

    [Authorize(Policy="PlatformAdministrator")]
    [HttpGet("api/v1/platform/customers")]
    public async Task<IActionResult> Customers([FromQuery]string? search,[FromQuery]string? plan,[FromQuery]string? status,CancellationToken ct){var actor=Actor();return actor is null?Unauthorized():Ok(await repository.ListCustomersAsync(actor.Value,search,plan,status,ct));}
    [Authorize(Policy="PlatformAdministrator")]
    [HttpPost("api/v1/platform/customers")]
    public async Task<IActionResult> CreateCustomer([FromBody]CreatePlatformCustomerRequest request,CancellationToken ct)
    {
        var actor=Actor();if(actor is null)return Unauthorized();
        if(string.IsNullOrWhiteSpace(request.OrganizationName)||string.IsNullOrWhiteSpace(request.Document)||string.IsNullOrWhiteSpace(request.AdminEmail)||string.IsNullOrWhiteSpace(request.InitialPassword))
            return this.Invalid("api.consumption.customer_required_fields");
        try
        {
            var result=await repository.CreateCustomerAsync(actor.Value,request,ct);
            if(result is null)return OdcaErrors.Conflict(this, "api.consumption.customer_conflict");
            return Created($"api/v1/platform/customers/{result.TenantId}",result);
        }
        catch(ArgumentException ex)
        {
            // D-OC27: título fixo por chave; detalhe canônico dinâmico resolvido por template.
            var culture = this.Culture();
            var localizer = HttpContext.RequestServices.GetRequiredService<Odca.Configuration.Localization.LocalizationService>();
            return BadRequest(new ProblemDetails
            {
                Title = this.T("api.consumption.invalid_plan"),
                Detail = ApiMessages.Localize(localizer, ex.Message, culture) ?? ex.Message,
                Status = StatusCodes.Status400BadRequest,
                Extensions = { ["code"] = "api.consumption.invalid_plan" }
            });
        }
    }
    [Authorize(Policy="PlatformAdministrator")]
    [HttpGet("api/v1/platform/customers/{tenantId:guid}")]
    public async Task<IActionResult> Customer(Guid tenantId,CancellationToken ct){var actor=Actor();if(actor is null)return Unauthorized();var summary=await repository.GetSummaryAsync(actor.Value,tenantId,true,ct);if(summary is null)return NotFound();var requests=await repository.ListRequestsAsync(actor.Value,tenantId,true,ct);return Ok(new PlatformCustomerDetail(summary,requests));}
    [Authorize(Policy="PlatformAdministrator")]
    [HttpPost("api/v1/platform/customers/{tenantId:guid}/storage-requests/{requestId:guid}/decision")]
    public async Task<IActionResult> Decide(Guid tenantId,Guid requestId,[FromBody]DecideStorageRequest request,CancellationToken ct){var actor=Actor();if(actor is null)return Unauthorized();if(!await repository.DecideRequestAsync(actor.Value,tenantId,requestId,request,ct))return NotFound();return NoContent();}
    [Authorize(Policy="PlatformAdministrator")]
    [HttpPost("api/v1/platform/customers/{tenantId:guid}/storage-grants")]
    public async Task<IActionResult> Grant(Guid tenantId,[FromBody]ManualStorageGrant request,CancellationToken ct){var actor=Actor();if(actor is null)return Unauthorized();if(!await repository.GrantStorageAsync(actor.Value,tenantId,request,ct))return this.Invalid("api.consumption.grant_invalid");return NoContent();}
    [Authorize(Policy="PlatformAdministrator")]
    [HttpPost("api/v1/platform/customers/{tenantId:guid}/suspend")]
    public Task<IActionResult> Suspend(Guid tenantId,[FromBody]ChangeOrganizationStatusRequest request,CancellationToken ct)=>ChangeOrganizationStatus(tenantId,false,request,ct);
    [Authorize(Policy="PlatformAdministrator")]
    [HttpPost("api/v1/platform/customers/{tenantId:guid}/restore")]
    public Task<IActionResult> Restore(Guid tenantId,[FromBody]ChangeOrganizationStatusRequest request,CancellationToken ct)=>ChangeOrganizationStatus(tenantId,true,request,ct);
    [Authorize(Policy="PlatformAdministrator")]
    [HttpPost("api/v1/platform/customers/{tenantId:guid}/activate")]
    public async Task<IActionResult> Activate(Guid tenantId,[FromBody]ChangeOrganizationStatusRequest request,CancellationToken ct){var actor=Actor();if(actor is null)return Unauthorized();if(string.IsNullOrWhiteSpace(request.Reason)||request.Reason.Trim().Length<5)return this.Invalid("api.consumption.reason_required");return await repository.ActivateOrganizationAsync(actor.Value,tenantId,request.Reason.Trim(),ct)?NoContent():Conflict(new ProblemDetails{Title=this.T("api.consumption.activate_not_pending"),Detail=this.T("api.consumption.activate_not_pending.detail"),Status=409,Extensions={["code"]="api.consumption.activate_not_pending"}});}
    private async Task<IActionResult> ChangeOrganizationStatus(Guid tenantId,bool restore,ChangeOrganizationStatusRequest request,CancellationToken ct){var actor=Actor();if(actor is null)return Unauthorized();if(string.IsNullOrWhiteSpace(request.Reason)||request.Reason.Trim().Length<5)return this.Invalid("api.consumption.reason_required");return await repository.ChangeOrganizationStatusAsync(actor.Value,tenantId,restore,request.Reason.Trim(),ct)?NoContent():Conflict(new ProblemDetails{Title=this.T("api.consumption.status_change_blocked"),Status=409,Extensions={["code"]="api.consumption.status_change_blocked"}});}
    private Guid? Actor()=>Guid.TryParse(User.FindFirstValue("sub"),out var id)?id:null;
}
