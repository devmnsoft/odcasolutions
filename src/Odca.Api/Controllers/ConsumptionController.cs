using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
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
    public async Task<IActionResult> Request(Guid tenantId,[FromBody]CreateStorageRequest request,CancellationToken ct){var actor=Actor();if(actor is null)return Unauthorized();if(request.Quantity is<1 or>100||request.IdempotencyKey==Guid.Empty)return ValidationProblem();var value=await repository.RequestStorageAsync(actor.Value,tenantId,request,ct);return value is null?Forbid():Created($"api/v1/organizations/{tenantId}/storage-requests/{value.Id}",value);}

    [Authorize(Policy="PlatformAdministrator")]
    [HttpGet("api/v1/platform/customers")]
    public async Task<IActionResult> Customers([FromQuery]string? search,CancellationToken ct){var actor=Actor();return actor is null?Unauthorized():Ok(await repository.ListCustomersAsync(actor.Value,search,ct));}
    [Authorize(Policy="PlatformAdministrator")]
    [HttpGet("api/v1/platform/customers/{tenantId:guid}")]
    public async Task<IActionResult> Customer(Guid tenantId,CancellationToken ct){var actor=Actor();if(actor is null)return Unauthorized();var summary=await repository.GetSummaryAsync(actor.Value,tenantId,true,ct);if(summary is null)return NotFound();var requests=await repository.ListRequestsAsync(actor.Value,tenantId,true,ct);return Ok(new PlatformCustomerDetail(summary,requests));}
    [Authorize(Policy="PlatformAdministrator")]
    [HttpPost("api/v1/platform/customers/{tenantId:guid}/storage-requests/{requestId:guid}/decision")]
    public async Task<IActionResult> Decide(Guid tenantId,Guid requestId,[FromBody]DecideStorageRequest request,CancellationToken ct){var actor=Actor();if(actor is null)return Unauthorized();if(!await repository.DecideRequestAsync(actor.Value,tenantId,requestId,request,ct))return NotFound();return NoContent();}
    [Authorize(Policy="PlatformAdministrator")]
    [HttpPost("api/v1/platform/customers/{tenantId:guid}/storage-grants")]
    public async Task<IActionResult> Grant(Guid tenantId,[FromBody]ManualStorageGrant request,CancellationToken ct){var actor=Actor();if(actor is null)return Unauthorized();if(!await repository.GrantStorageAsync(actor.Value,tenantId,request,ct))return ValidationProblem();return NoContent();}
    private Guid? Actor()=>Guid.TryParse(User.FindFirstValue("sub"),out var id)?id:null;
}
