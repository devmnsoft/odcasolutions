using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Odca.Api.Localization;
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
            _ => this.Status(StatusCodes.Status503ServiceUnavailable, "api.org_features.unavailable")
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
            return this.Field("reason", "api.org_features.reason_required");
        }

        var result = await repository.SetAsync(actorId, tenantId, featureCode, request.Blocked, request.Reason, cancellationToken);
        return result.Access switch
        {
            OrganizationFeatureAccess.Ok when result.Feature is not null => Ok(result.Feature),
            OrganizationFeatureAccess.Invalid => this.Field("feature", "api.org_features.invalid_feature"),
            OrganizationFeatureAccess.Missing => NotFound(),
            OrganizationFeatureAccess.Forbidden => Forbid(),
            _ => this.Status(StatusCodes.Status503ServiceUnavailable, "api.org_features.update_unavailable")
        };
    }
}
