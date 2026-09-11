using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Odca.Application.Contracts;
using Odca.Application.Tenancy;
using Odca.Contracts.Contracts;
using Odca.Contracts.Tenancy;

namespace Odca.Api.Controllers;

[ApiController]
[Route("api/v1/organizations/{tenantId:guid}/contracts")]
[Authorize(Policy = "PasswordChanged")]
public sealed class ContractsController(ContractService service) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<PaginatedResponse<ContractListItemResponse>>> List(
        Guid tenantId,
        [FromQuery] string? title,
        [FromQuery] Guid? counterpartyId,
        [FromQuery] Guid? typeId,
        [FromQuery] Guid? ownerUserId,
        [FromQuery] string? operationalStatus,
        [FromQuery] string? temporalStatus,
        [FromQuery] DateOnly? endFrom,
        [FromQuery] DateOnly? endTo,
        [FromQuery] bool mine = false,
        [FromQuery] bool approaching = false,
        [FromQuery] bool unassigned = false,
        [FromQuery] DateOnly? referenceDate = null,
        [FromQuery] int? page = null,
        [FromQuery] int? pageSize = null,
        CancellationToken cancellationToken = default)
    {
        if (!Actor(out var actor))
        {
            return Unauthorized();
        }

        var filter = new ContractListFilter(
            title,
            counterpartyId,
            typeId,
            ownerUserId,
            operationalStatus,
            temporalStatus,
            endFrom,
            endTo,
            mine,
            approaching,
            unassigned,
            referenceDate ?? default);

        var result = await service.ListAsync(actor, tenantId, filter, page, pageSize, cancellationToken);
        if (result.Status == QueryAccessStatus.Forbidden)
        {
            return Forbid();
        }

        var pageResult = result.Value!;
        return Ok(new PaginatedResponse<ContractListItemResponse>(
            pageResult.Items.Select(MapList).ToList(),
            pageResult.TotalCount,
            pageResult.Page,
            pageResult.PageSize));
    }

    [HttpGet("overview")]
    public async Task<ActionResult<ContractOverviewResponse>> Overview(
        Guid tenantId,
        [FromQuery] DateOnly? referenceDate,
        CancellationToken cancellationToken)
    {
        if (!Actor(out var actor))
        {
            return Unauthorized();
        }

        var result = await service.OverviewAsync(actor, tenantId, referenceDate, cancellationToken);
        if (result.Status == QueryAccessStatus.Forbidden)
        {
            return Forbid();
        }

        var metrics = result.Value!;
        return Ok(new ContractOverviewResponse(
            metrics.Draft,
            metrics.Active,
            metrics.Approaching,
            metrics.Expired,
            metrics.Indefinite,
            metrics.Closed,
            metrics.Unassigned));
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<ContractDetailResponse>> Get(
        Guid tenantId,
        Guid id,
        [FromQuery] DateOnly? referenceDate,
        CancellationToken cancellationToken)
    {
        if (!Actor(out var actor))
        {
            return Unauthorized();
        }

        var result = await service.GetAsync(actor, tenantId, id, referenceDate, cancellationToken);
        if (result.Status == QueryAccessStatus.Forbidden)
        {
            return Forbid();
        }

        return result.Value is null ? NotFound() : Ok(MapDetail(result.Value));
    }

    [HttpPost]
    public async Task<ActionResult<ContractDetailResponse>> Create(
        Guid tenantId,
        [FromBody] UpsertContractRequest request,
        CancellationToken cancellationToken)
    {
        if (!Actor(out var actor))
        {
            return Unauthorized();
        }

        var result = await service.CreateDraftAsync(actor, tenantId, ToModel(request), cancellationToken);
        return MapMutation(result);
    }

    [HttpPut("{id:guid}")]
    public async Task<ActionResult<ContractDetailResponse>> Update(
        Guid tenantId,
        Guid id,
        [FromBody] UpsertContractRequest request,
        CancellationToken cancellationToken)
    {
        if (!Actor(out var actor))
        {
            return Unauthorized();
        }

        if (request.Version is null)
        {
            return ValidationProblem();
        }

        var result = await service.UpdateAsync(actor, tenantId, id, request.Version.Value, ToModel(request), cancellationToken);
        return MapMutation(result);
    }

    [HttpPost("{id:guid}/activate")]
    public Task<ActionResult> Activate(Guid tenantId, Guid id, [FromBody] ContractVersionRequest request, CancellationToken cancellationToken)
        => Lifecycle(tenantId, id, request.Version, (a, t, i, v, ct) => service.ActivateAsync(a, t, i, v, ct), cancellationToken);

    [HttpPost("{id:guid}/renew")]
    public async Task<ActionResult> Renew(
        Guid tenantId,
        Guid id,
        [FromBody] RenewContractRequest request,
        CancellationToken cancellationToken)
    {
        if (!Actor(out var actor))
        {
            return Unauthorized();
        }

        var result = await service.RenewAsync(actor, tenantId, id, request.Version, request.NewEndDate, request.Reason, cancellationToken);
        return MapLifecycle(result);
    }

    [HttpPost("{id:guid}/close")]
    public Task<ActionResult> Close(Guid tenantId, Guid id, [FromBody] ContractVersionRequest request, CancellationToken cancellationToken)
        => Lifecycle(tenantId, id, request.Version, (a, t, i, v, ct) => service.CloseAsync(a, t, i, v, ct), cancellationToken);

    [HttpPost("{id:guid}/cancel")]
    public Task<ActionResult> Cancel(Guid tenantId, Guid id, [FromBody] ContractVersionRequest request, CancellationToken cancellationToken)
        => Lifecycle(tenantId, id, request.Version, (a, t, i, v, ct) => service.CancelAsync(a, t, i, v, ct), cancellationToken);

    [HttpPost("{id:guid}/delete")]
    public async Task<ActionResult> SoftDelete(
        Guid tenantId,
        Guid id,
        [FromBody] SoftDeleteContractRequest request,
        CancellationToken cancellationToken)
    {
        if (!Actor(out var actor))
        {
            return Unauthorized();
        }

        var result = await service.SoftDeleteAsync(actor, tenantId, id, request.Version, request.Reason, cancellationToken);
        return MapLifecycle(result);
    }

    [HttpPost("{id:guid}/restore")]
    public Task<ActionResult> Restore(Guid tenantId, Guid id, [FromBody] ContractVersionRequest request, CancellationToken cancellationToken)
        => Lifecycle(tenantId, id, request.Version, (a, t, i, v, ct) => service.RestoreAsync(a, t, i, v, ct), cancellationToken);

    private async Task<ActionResult> Lifecycle(
        Guid tenantId,
        Guid id,
        long version,
        Func<Guid, Guid, Guid, long, CancellationToken, Task<MutationResult>> action,
        CancellationToken cancellationToken)
    {
        if (!Actor(out var actor))
        {
            return Unauthorized();
        }

        var result = await action(actor, tenantId, id, version, cancellationToken);
        return MapLifecycle(result);
    }

    private ActionResult MapLifecycle(MutationResult result)
        => result.Status switch
        {
            MutationStatus.Succeeded => NoContent(),
            MutationStatus.Forbidden => Forbid(),
            MutationStatus.NotFound => NotFound(),
            MutationStatus.ValidationFailed => ValidationProblem(new ValidationProblemDetails
            {
                Extensions = { ["code"] = result.ErrorCode }
            }),
            _ => Conflict(new ProblemDetails
            {
                Title = "Conflito na operação do contrato",
                Extensions = { ["code"] = result.ErrorCode }
            })
        };

    private ActionResult<ContractDetailResponse> MapMutation(MutationResult<ContractDetail> result)
        => result.Status switch
        {
            MutationStatus.Succeeded => Ok(MapDetail(result.Value!)),
            MutationStatus.Forbidden => Forbid(),
            MutationStatus.NotFound => NotFound(),
            MutationStatus.ValidationFailed => ValidationProblem(new ValidationProblemDetails
            {
                Title = "Dados inválidos",
                Extensions = { ["code"] = result.ErrorCode }
            }),
            _ => Conflict(new ProblemDetails
            {
                Title = "Conflito ao salvar contrato",
                Extensions = { ["code"] = result.ErrorCode }
            })
        };

    private static ContractWriteModel ToModel(UpsertContractRequest request)
        => new(
            request.ReferenceNumber,
            request.Title,
            request.Summary,
            request.TypeId,
            request.PrimaryCounterpartyId,
            request.OwnerUserId,
            request.StartDate,
            request.EndDate,
            request.IsIndefinite,
            request.Amount,
            request.Currency,
            request.AmountPeriodicity,
            request.RenewalNoticeDays,
            request.RenewalDecision,
            request.AdditionalCounterpartyIds);

    private static ContractListItemResponse MapList(ContractListItem row)
        => new(
            row.Id,
            row.ReferenceNumber,
            row.Title,
            row.TypeId,
            row.TypeName,
            row.PrimaryCounterpartyId,
            row.PrimaryCounterpartyName,
            row.OwnerUserId,
            row.OwnerName,
            row.StartDate,
            row.EndDate,
            row.IsIndefinite,
            row.OperationalStatus,
            row.TemporalStatus,
            row.TermCycle,
            row.Version,
            row.UpdatedAt);

    private static ContractDetailResponse MapDetail(ContractDetail row)
        => new(
            row.Id,
            row.ReferenceNumber,
            row.Title,
            row.Summary,
            row.TypeId,
            row.TypeName,
            row.PrimaryCounterpartyId,
            row.PrimaryCounterpartyName,
            row.OwnerUserId,
            row.OwnerName,
            row.StartDate,
            row.EndDate,
            row.IsIndefinite,
            row.Amount,
            row.Currency,
            row.AmountPeriodicity,
            row.RenewalNoticeDays,
            row.RenewalDecision,
            row.OperationalStatus,
            row.TemporalStatus,
            row.TermCycle,
            row.Version,
            row.CreatedAt,
            row.UpdatedAt,
            row.ClosedAt,
            row.CancelledAt,
            row.Parties.Select(p => new ContractPartyResponse(p.CounterpartyId, p.CounterpartyName, p.Role)).ToList(),
            row.Renewals.Select(r => new ContractRenewalResponse(r.Id, r.PreviousEndDate, r.NewEndDate, r.PreviousTermCycle, r.NewTermCycle, r.Reason, r.CreatedBy, r.CreatedAt)).ToList(),
            row.Events.Select(e => new ContractEventResponse(e.Id, e.Action, e.ActorUserId, e.OccurredAt, e.MetadataJson)).ToList());

    private bool Actor(out Guid actor) => Guid.TryParse(User.FindFirstValue("sub"), out actor);
}
