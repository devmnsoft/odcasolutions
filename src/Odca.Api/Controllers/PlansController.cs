using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Npgsql;
using Odca.Application.Plans;
using Odca.Contracts.Plans;
using Odca.Contracts.Tenancy;

namespace Odca.Api.Controllers;

[ApiController]
[Route("api/v1/platform/plans")]
[Authorize(Policy = "PlatformAdministrator")]
public sealed class PlansController(IPlanCatalogRepository repository) : ControllerBase
{
    private const string BillingLimitation =
        "O faturamento não está operacional. A alteração de plano não registra pagamento nem quitação.";

    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<PlanCatalogResponse>>> List(CancellationToken cancellationToken)
    {
        var plans = await repository.ListPublishedAsync(cancellationToken);
        return Ok(plans.Select(ToResponse));
    }

    [HttpPost("changes/preview")]
    public async Task<ActionResult<PlanChangePreviewResponse>> Preview(
        [FromBody] PlanChangeRequest request,
        CancellationToken cancellationToken)
    {
        if (!Actor(out var actor) || request.TenantId == Guid.Empty || string.IsNullOrWhiteSpace(request.PlanCode))
        {
            return ValidationProblem();
        }

        try
        {
            var preview = await repository.PreviewOrganizationPlanChangeAsync(
                actor, request.TenantId, request.PlanCode.Trim(), cancellationToken);
            return preview is null
                ? NotFound()
                : Ok(ToPreview(preview));
        }
        catch (PostgresException exception) when (exception.SqlState == PostgresErrorCodes.InsufficientPrivilege)
        {
            return Forbid();
        }
    }

    [HttpPost("changes")]
    public async Task<IActionResult> Apply(
        [FromBody] PlanChangeRequest request,
        CancellationToken cancellationToken)
    {
        if (!Actor(out var actor) || request.TenantId == Guid.Empty || string.IsNullOrWhiteSpace(request.PlanCode))
        {
            return ValidationProblem();
        }

        var result = await repository.ApplyOrganizationPlanChangeAsync(
            actor,
            request.TenantId,
            request.PlanCode.Trim(),
            request.Justification?.Trim() ?? string.Empty,
            cancellationToken);
        return result switch
        {
            "applied" => NoContent(),
            "missing" or "missing_plan" => NotFound(),
            "forbidden" => Forbid(),
            "unchanged" => Conflict(new ProblemDetails
            {
                Title = "Plano inalterado",
                Detail = "A organização já está vinculada a essa versão vigente.",
                Extensions = { ["code"] = "unchanged" }
            }),
            "conflict" => Conflict(new ProblemDetails
            {
                Title = "Versões vigentes conflitantes",
                Detail = "Há mais de uma versão publicada vigente para o plano solicitado.",
                Extensions = { ["code"] = "conflicting_plan_versions" }
            }),
            _ => ValidationProblem(new ValidationProblemDetails(new Dictionary<string, string[]>
            {
                ["justification"] = ["Informe uma justificativa entre 5 e 500 caracteres e um plano vigente."]
            }))
        };
    }

    private static PlanCatalogResponse ToResponse(PlanCatalogItem plan) => new(
        plan.Code,
        plan.Version,
        plan.DisplayName,
        plan.ActiveSeats,
        plan.StorageBytes,
        plan.UserStorageBytes,
        plan.FileBytes,
        plan.OcrPagesMonthly,
        plan.SignatureEnvelopesMonthly,
        plan.EnabledModules);

    private static PlanChangePreviewResponse ToPreview(PlanChangePreview preview) => new(
        preview.CurrentCode,
        preview.CurrentVersion,
        preview.CurrentPlanVersionId,
        preview.TargetCode,
        preview.TargetVersion,
        preview.TargetPlanVersionId,
        preview.ActiveMembers,
        preview.ReservedInvitations,
        preview.CurrentSeatLimit,
        preview.TargetSeatLimit,
        preview.SeatsOverLimit,
        preview.PatientCount,
        preview.DocumentCount,
        preview.Policy,
        BillingLimitation);

    private bool Actor(out Guid actor) => Guid.TryParse(User.FindFirstValue("sub"), out actor);
}
