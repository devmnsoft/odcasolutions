using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Odca.Application.Contracts;
using Odca.Application.Tenancy;
using Odca.Contracts.Contracts;
using Odca.Contracts.Tenancy;

namespace Odca.Api.Controllers;

[ApiController]
[Route("api/v1/organizations/{tenantId:guid}/counterparties")]
[Authorize(Policy = "PasswordChanged")]
public sealed class CounterpartiesController(CounterpartyService service) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<PaginatedResponse<CounterpartyResponse>>> List(
        Guid tenantId,
        [FromQuery] string? search,
        [FromQuery] string? status,
        [FromQuery] int? page,
        [FromQuery] int? pageSize,
        CancellationToken cancellationToken)
    {
        if (!Actor(out var actor))
        {
            return Unauthorized();
        }

        if (status is not null && status is not ("active" or "inactive"))
        {
            return ValidationProblem();
        }

        var result = await service.ListAsync(actor, tenantId, search, status, page, pageSize, cancellationToken);
        if (result.Status == QueryAccessStatus.Forbidden)
        {
            return Forbid();
        }

        var pageResult = result.Value!;
        return Ok(new PaginatedResponse<CounterpartyResponse>(
            pageResult.Items.Select(Map).ToList(),
            pageResult.TotalCount,
            pageResult.Page,
            pageResult.PageSize));
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<CounterpartyResponse>> Get(Guid tenantId, Guid id, CancellationToken cancellationToken)
    {
        if (!Actor(out var actor))
        {
            return Unauthorized();
        }

        var result = await service.GetAsync(actor, tenantId, id, cancellationToken);
        if (result.Status == QueryAccessStatus.Forbidden)
        {
            return Forbid();
        }

        return result.Value is null ? NotFound() : Ok(Map(result.Value));
    }

    [HttpPost]
    public async Task<ActionResult<CounterpartyResponse>> Create(
        Guid tenantId,
        [FromBody] UpsertCounterpartyRequest request,
        CancellationToken cancellationToken)
    {
        if (!Actor(out var actor))
        {
            return Unauthorized();
        }

        var result = await service.CreateAsync(actor, tenantId, ToModel(request), cancellationToken);
        return MapMutation(result);
    }

    [HttpPut("{id:guid}")]
    public async Task<ActionResult<CounterpartyResponse>> Update(
        Guid tenantId,
        Guid id,
        [FromBody] UpsertCounterpartyRequest request,
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

    [HttpPost("{id:guid}/inactivate")]
    public async Task<IActionResult> Inactivate(
        Guid tenantId,
        Guid id,
        [FromBody] ContractVersionRequest request,
        CancellationToken cancellationToken)
    {
        if (!Actor(out var actor))
        {
            return Unauthorized();
        }

        var result = await service.InactivateAsync(actor, tenantId, id, request.Version, cancellationToken);
        return result.Status switch
        {
            MutationStatus.Succeeded => NoContent(),
            MutationStatus.Forbidden => Forbid(),
            MutationStatus.NotFound => NotFound(),
            _ => Conflict()
        };
    }

    private ActionResult<CounterpartyResponse> MapMutation(MutationResult<CounterpartyRecord> result)
        => result.Status switch
        {
            MutationStatus.Succeeded => Ok(Map(result.Value!)),
            MutationStatus.Forbidden => Forbid(),
            MutationStatus.NotFound => NotFound(),
            MutationStatus.ValidationFailed => ValidationProblem(new ValidationProblemDetails
            {
                Title = "Dados inválidos",
                Extensions = { ["code"] = result.ErrorCode }
            }),
            _ => Conflict(new ProblemDetails
            {
                Title = "Conflito ao salvar contraparte",
                Extensions = { ["code"] = result.ErrorCode }
            })
        };

    private static CounterpartyWriteModel ToModel(UpsertCounterpartyRequest request)
        => new(
            request.PersonType,
            request.LegalName,
            request.DisplayName,
            request.DocumentType,
            request.Document,
            request.Email,
            request.Phone,
            request.IsClient,
            request.IsSupplier,
            request.IsPartner,
            request.IsProvider,
            request.AddressLine1,
            request.AddressLine2,
            request.AddressCity,
            request.AddressState,
            request.AddressPostalCode,
            request.AddressCountry);

    private static CounterpartyResponse Map(CounterpartyRecord row)
        => new(
            row.Id,
            row.PersonType,
            row.LegalName,
            row.DisplayName,
            row.DocumentType,
            row.DocumentNormalized,
            row.DocumentDisplay,
            row.Email,
            row.Phone,
            row.IsClient,
            row.IsSupplier,
            row.IsPartner,
            row.IsProvider,
            row.Status,
            row.AddressLine1,
            row.AddressLine2,
            row.AddressCity,
            row.AddressState,
            row.AddressPostalCode,
            row.AddressCountry,
            row.Version,
            row.CreatedAt,
            row.UpdatedAt);

    private bool Actor(out Guid actor) => Guid.TryParse(User.FindFirstValue("sub"), out actor);
}
