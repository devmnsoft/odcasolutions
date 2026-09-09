using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Odca.Application.Plans;
using Odca.Contracts.Plans;

namespace Odca.Api.Controllers;

[ApiController]
[Route("api/v1/platform/plans")]
[Authorize(Policy = "PlatformAdministrator")]
public sealed class PlansController(IPlanCatalogRepository repository) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<PlanCatalogResponse>>> List(CancellationToken cancellationToken)
    {
        var plans = await repository.ListPublishedAsync(cancellationToken);
        return Ok(plans.Select(plan => new PlanCatalogResponse(
            plan.Code,
            plan.Version,
            plan.DisplayName,
            plan.ActiveSeats,
            plan.StorageBytes,
            plan.UserStorageBytes,
            plan.FileBytes,
            plan.OcrPagesMonthly,
            plan.SignatureEnvelopesMonthly)));
    }
}
