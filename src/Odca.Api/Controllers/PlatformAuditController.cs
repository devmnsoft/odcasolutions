using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Odca.Application.Administration;
using Odca.Contracts.Administration;

namespace Odca.Api.Controllers;

[ApiController]
[Route("api/v1/platform/audit")]
[Authorize(Policy = "PlatformAdministrator")]
public sealed class PlatformAuditController(IPlatformAuditRepository repository) : ControllerBase
{
    [HttpGet("events")]
    public async Task<ActionResult<PlatformAuditPageResponse>> Events(
        [FromQuery] string? search,
        [FromQuery] Guid? tenantId,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 25,
        CancellationToken cancellationToken = default)
    {
        if (!Guid.TryParse(User.FindFirstValue("sub"), out var actorId))
        {
            return Unauthorized();
        }

        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, 100);

        var result = await repository.GetPageAsync(actorId, search, tenantId, page, pageSize, cancellationToken);
        return result.Access switch
        {
            PlatformAuditAccess.Ok when result.Page is not null => Ok(result.Page),
            PlatformAuditAccess.Forbidden => Forbid(),
            _ => Problem(statusCode: StatusCodes.Status503ServiceUnavailable, title: "Não foi possível consultar a auditoria da plataforma.")
        };
    }
}
