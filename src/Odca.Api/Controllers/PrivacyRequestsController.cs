using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Odca.Application.Privacy;
using Odca.Contracts.Privacy;

namespace Odca.Api.Controllers;

[ApiController]
[Route("api/v1/privacy/requests")]
[AllowAnonymous]
public sealed class PrivacyRequestsController(PrivacyRequestService service) : ControllerBase
{
    [HttpPost]
    [EnableRateLimiting("privacy-intake")]
    public async Task<ActionResult<PrivacyRequestCreated>> Create(
        CreatePrivacyRequest request,
        CancellationToken cancellationToken)
    {
        var protocol = await service.SubmitAsync(
            request.Email,
            request.RequestType,
            request.Details,
            cancellationToken);
        return Accepted(new PrivacyRequestCreated(
            protocol,
            "Solicitação recebida. O protocolo não confirma a existência de dados ou contratos."));
    }
}
