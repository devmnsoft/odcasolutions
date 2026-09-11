using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Odca.Application.Contracts;
using Odca.Application.Tenancy;
using Odca.Contracts.Contracts;

namespace Odca.Api.Controllers;

[ApiController]
[Route("api/v1/organizations/{tenantId:guid}/contract-types")]
[Authorize(Policy = "PasswordChanged")]
public sealed class ContractTypesController(ContractTypeService service) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<ContractTypeResponse>>> List(
        Guid tenantId,
        [FromQuery] string? status,
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

        var result = await service.ListAsync(actor, tenantId, status, cancellationToken);
        if (result.Status == QueryAccessStatus.Forbidden)
        {
            return Forbid();
        }

        return Ok(result.Value!.Select(Map).ToList());
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<ContractTypeResponse>> Get(Guid tenantId, Guid id, CancellationToken cancellationToken)
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
    public async Task<ActionResult<ContractTypeResponse>> Create(
        Guid tenantId,
        [FromBody] UpsertContractTypeRequest request,
        CancellationToken cancellationToken)
    {
        if (!Actor(out var actor))
        {
            return Unauthorized();
        }

        var result = await service.CreateAsync(
            actor,
            tenantId,
            new ContractTypeWriteModel(request.Code, request.Name, request.Description, request.Guidance),
            cancellationToken);
        return MapMutation(result);
    }

    [HttpPut("{id:guid}")]
    public async Task<ActionResult<ContractTypeResponse>> Update(
        Guid tenantId,
        Guid id,
        [FromBody] UpsertContractTypeRequest request,
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

        var result = await service.UpdateAsync(
            actor,
            tenantId,
            id,
            request.Version.Value,
            new ContractTypeWriteModel(request.Code, request.Name, request.Description, request.Guidance),
            cancellationToken);
        return MapMutation(result);
    }

    [HttpPost("{id:guid}/status")]
    public async Task<IActionResult> SetStatus(
        Guid tenantId,
        Guid id,
        [FromBody] SetContractTypeStatusRequest request,
        CancellationToken cancellationToken)
    {
        if (!Actor(out var actor))
        {
            return Unauthorized();
        }

        var result = await service.SetStatusAsync(actor, tenantId, id, request.Version, request.Status, cancellationToken);
        return result.Status switch
        {
            MutationStatus.Succeeded => NoContent(),
            MutationStatus.Forbidden => Forbid(),
            MutationStatus.ValidationFailed => ValidationProblem(),
            MutationStatus.NotFound => NotFound(),
            _ => Conflict()
        };
    }

    private ActionResult<ContractTypeResponse> MapMutation(MutationResult<ContractTypeRecord> result)
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
                Title = "Conflito ao salvar tipo de contrato",
                Extensions = { ["code"] = result.ErrorCode }
            })
        };

    private static ContractTypeResponse Map(ContractTypeRecord row)
        => new(row.Id, row.Code, row.Name, row.Description, row.Guidance, row.Status, row.IsSystemDemo, row.Version, row.CreatedAt, row.UpdatedAt);

    private bool Actor(out Guid actor) => Guid.TryParse(User.FindFirstValue("sub"), out actor);
}
