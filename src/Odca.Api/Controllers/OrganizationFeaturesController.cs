using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Odca.Application.Administration;
using Odca.Contracts.Administration;

namespace Odca.Api.Controllers;

[ApiController]
[Authorize(Policy = "PasswordChanged")]
[Route("api/v1/organizations/{tenantId:guid}/features")]
public sealed class OrganizationFeaturesController(IOrganizationFeatureRepository repository) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> List(Guid tenantId, CancellationToken cancellationToken)
    {
        if (!Guid.TryParse(User.FindFirstValue("sub"), out var actorId))
        {
            return Unauthorized();
        }

        var result = await repository.ListAsync(actorId, tenantId, cancellationToken);
        return result.Access switch
        {
            OrganizationFeatureAccess.Ok when result.Catalog is not null => Ok(result.Catalog),
            OrganizationFeatureAccess.Missing => NotFound(),
            OrganizationFeatureAccess.Forbidden => Forbid(),
            _ => Problem(statusCode: StatusCodes.Status503ServiceUnavailable, title: "Não foi possível consultar as funcionalidades da organização.")
        };
    }

    [Authorize(Policy = "PlatformAdministrator")]
    [HttpPut("{featureCode}")]
    public async Task<IActionResult> Set(
        Guid tenantId,
        string featureCode,
        [FromBody] SetOrganizationFeatureRequest request,
        CancellationToken cancellationToken)
    {
        if (!Guid.TryParse(User.FindFirstValue("sub"), out var actorId))
        {
            return Unauthorized();
        }

        if (request.Reason is null)
        {
            return ValidationProblem(new ValidationProblemDetails(new Dictionary<string, string[]>
            {
                ["reason"] = ["Informe a justificativa do bloqueio ou da liberação."]
            }));
        }

        var result = await repository.SetAsync(actorId, tenantId, featureCode, request.Blocked, request.Reason, cancellationToken);
        return result.Access switch
        {
            OrganizationFeatureAccess.Ok when result.Feature is not null => Ok(result.Feature),
            OrganizationFeatureAccess.Invalid => ValidationProblem(new ValidationProblemDetails(new Dictionary<string, string[]>
            {
                ["feature"] = ["Informe uma funcionalidade conhecida e uma justificativa entre 5 e 500 caracteres."]
            })),
            OrganizationFeatureAccess.Missing => NotFound(),
            OrganizationFeatureAccess.Forbidden => Forbid(),
            _ => Problem(statusCode: StatusCodes.Status503ServiceUnavailable, title: "Não foi possível registrar a decisão administrativa.")
        };
    }
}
