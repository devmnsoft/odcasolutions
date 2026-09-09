using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Odca.Application.Plans;
using Odca.Contracts.Plans;

namespace Odca.Api.Controllers;

[ApiController]
[Route("api/v1/catalog/plans")]
[AllowAnonymous]
public sealed class CatalogController(IPlanCatalogRepository repository) : ControllerBase
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
