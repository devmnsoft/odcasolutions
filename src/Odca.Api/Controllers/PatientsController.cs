using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Odca.Application.Patients;
using Odca.Contracts.Patients;

namespace Odca.Api.Controllers;

[ApiController, Authorize(Policy = "PasswordChanged")]
[Route("api/v1/organizations/{tenantId:guid}/patients")]
public sealed class PatientsController(IPatientRepository repository) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> List(Guid tenantId,[FromQuery]string? search,[FromQuery]bool includeInactive=false,[FromQuery]int page=1,[FromQuery]int pageSize=20,CancellationToken ct=default)
    { if(!Actor(out var actor))return Unauthorized();page=Math.Max(1,page);pageSize=Math.Clamp(pageSize,1,50);var result=await repository.ListAsync(actor,tenantId,search,includeInactive,page,pageSize,ct);return result is null?Forbid():Ok(result); }

    [HttpGet("{patientId:guid}")]
    public async Task<IActionResult> Get(Guid tenantId,Guid patientId,CancellationToken ct)
    {if(!Actor(out var actor))return Unauthorized();var row=await repository.GetAsync(actor,tenantId,patientId,ct);return row is null?NotFound():Ok(row);}

    [HttpPost]
    public async Task<IActionResult> Create(Guid tenantId,[FromBody]SavePatientRequest request,CancellationToken ct)
    {if(!Actor(out var actor))return Unauthorized();var invalid=ValidateRequest(request);if(invalid is not null)return invalid;var result=await repository.CreateAsync(actor,tenantId,request,ct);return result.Status switch{PatientMutationStatus.Success=>CreatedAtAction(nameof(Get),new{tenantId,patientId=result.Patient!.Id},result.Patient),PatientMutationStatus.Duplicate=>Conflict(Problem("Já existe um paciente ativo com esse identificador.")),_=>Forbid()};}

    [HttpPut("{patientId:guid}")]
    public async Task<IActionResult> Update(Guid tenantId,Guid patientId,[FromBody]SavePatientRequest request,CancellationToken ct)
    {if(!Actor(out var actor))return Unauthorized();var invalid=ValidateRequest(request);if(invalid is not null)return invalid;var result=await repository.UpdateAsync(actor,tenantId,patientId,request,ct);return Mutation(result);}

    [HttpPost("{patientId:guid}/inactivate")]
    public Task<IActionResult> Inactivate(Guid tenantId,Guid patientId,[FromQuery]long expectedVersion,CancellationToken ct)=>SetActive(tenantId,patientId,false,expectedVersion,ct);
    [HttpPost("{patientId:guid}/restore")]
    public Task<IActionResult> Restore(Guid tenantId,Guid patientId,[FromQuery]long expectedVersion,CancellationToken ct)=>SetActive(tenantId,patientId,true,expectedVersion,ct);

    [HttpGet("{patientId:guid}/documents")]
    public async Task<IActionResult> Archive(Guid tenantId,Guid patientId,[FromQuery]int page=1,[FromQuery]int pageSize=20,CancellationToken ct=default)
    {if(!Actor(out var actor))return Unauthorized();page=Math.Max(1,page);pageSize=Math.Clamp(pageSize,1,50);var result=await repository.ArchiveAsync(actor,tenantId,patientId,page,pageSize,ct);return result is null?Forbid():Ok(result);}

    private async Task<IActionResult> SetActive(Guid tenantId,Guid patientId,bool active,long version,CancellationToken ct){if(!Actor(out var actor))return Unauthorized();var status=await repository.SetActiveAsync(actor,tenantId,patientId,active,version,ct);return status switch{PatientMutationStatus.Success=>NoContent(),PatientMutationStatus.Duplicate=>Conflict(Problem("Outro cadastro ativo usa o mesmo identificador.")),PatientMutationStatus.Conflict=>Conflict(Problem("O paciente foi alterado em outra sessão.")),_=>Forbid()};}
    private IActionResult Mutation(PatientMutation result)=>result.Status switch{PatientMutationStatus.Success=>Ok(result.Patient),PatientMutationStatus.NotFound=>NotFound(),PatientMutationStatus.Conflict=>Conflict(Problem("O paciente foi alterado em outra sessão.")),PatientMutationStatus.Duplicate=>Conflict(Problem("Outro paciente ativo usa esse identificador.")),_=>Forbid()};
    private IActionResult? ValidateRequest(SavePatientRequest r){var errors=PatientRules.Validate(r.FullName,r.Email,r.Identifier?.Type,r.Identifier?.Value).ToDictionary(x=>x.Key,x=>x.Value);if(r.BirthDate>DateOnly.FromDateTime(DateTime.UtcNow))errors["birthDate"]=["A data de nascimento não pode estar no futuro."];if(r.Representative is not null&&(string.IsNullOrWhiteSpace(r.Representative.FullName)||string.IsNullOrWhiteSpace(r.Representative.Relationship)))errors["representative"]=["Informe nome e relação do representante."];return errors.Count==0?null:ValidationProblem(new ValidationProblemDetails(errors));}
    private bool Actor(out Guid actor)=>Guid.TryParse(User.FindFirstValue("sub"),out actor);
}
