using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using System.Text.Json;
using Odca.Api.Controllers;
using Odca.Application.Common;
using Odca.Application.Patients;
using Odca.Contracts.Patients;

namespace Odca.Domain.Tests;

public sealed class PatientsControllerTests
{
    [Fact]
    public async Task DuplicateCreationReturnsProblemDetailsWithoutNestedActionResult()
    {
        var controller = Controller(new StubRepository(new(PatientMutationStatus.Duplicate)));
        var result = await controller.Create(Guid.NewGuid(), ValidRequest(), default);
        var conflict = Assert.IsType<ObjectResult>(result);
        Assert.Equal(StatusCodes.Status409Conflict, conflict.StatusCode);
        var problem = Assert.IsType<ProblemDetails>(conflict.Value);
        Assert.Equal("patient.identifier.duplicate", problem.Extensions["code"]);
    }

    [Fact]
    public async Task ValidationUsesInjectedClockAndPreservesErrorKey()
    {
        var controller = Controller(new StubRepository(new(PatientMutationStatus.Success)));
        var result = await controller.Create(Guid.NewGuid(), ValidRequest() with { BirthDate = new DateOnly(2026, 9, 25) }, default);
        var invalid = Assert.IsAssignableFrom<ObjectResult>(result);
        var problem = Assert.IsType<ValidationProblemDetails>(invalid.Value);
        Assert.Contains("birthDate", problem.Errors);
    }

    [Fact]
    public async Task ValidationProblemSerializesAllFieldErrorsAsHttpContract()
    {
        var controller = Controller(new StubRepository(new(PatientMutationStatus.Success)));
        var request = ValidRequest() with
        {
            FullName = " ",
            BirthDate = new DateOnly(2026, 9, 25),
            Identifier = new("cpf", "123"),
            Representative = new(" ", null, null, " ")
        };

        var result = await controller.Create(Guid.NewGuid(), request, default);
        var invalid = Assert.IsAssignableFrom<ObjectResult>(result);
        Assert.Equal(StatusCodes.Status400BadRequest, invalid.StatusCode);

        var json = JsonSerializer.Serialize(invalid.Value, invalid.Value!.GetType(),
            new JsonSerializerOptions(JsonSerializerDefaults.Web));
        using var body = JsonDocument.Parse(json);
        var errors = body.RootElement.GetProperty("errors");
        Assert.Equal("Informe o nome completo (2 a 160 caracteres).", errors.GetProperty("fullName")[0].GetString());
        Assert.True(errors.TryGetProperty("birthDate", out _));
        Assert.True(errors.TryGetProperty("identifier", out _));
        Assert.True(errors.TryGetProperty("representative.fullName", out _));
        Assert.True(errors.TryGetProperty("representative.relationship", out _));
    }

    private static PatientsController Controller(IPatientRepository repository)
    {
        var controller = new PatientsController(repository, new FixedClock());
        controller.ControllerContext.HttpContext = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity([new Claim("sub", Guid.NewGuid().ToString())], "test"))
        };
        return controller;
    }

    private static SavePatientRequest ValidRequest() => new("Maria da Silva", null, null, null, null, null, null, null);
    private sealed class FixedClock : IClock { public DateTimeOffset UtcNow => new(2026, 9, 24, 12, 0, 0, TimeSpan.Zero); }
    private sealed class StubRepository(PatientMutation createResult) : IPatientRepository
    {
        public Task<PatientMutation> CreateAsync(Guid actorId, Guid tenantId, SavePatientRequest request, CancellationToken ct) => Task.FromResult(createResult);
        public Task<PatientPage?> ListAsync(Guid actorId, Guid tenantId, string? search, bool includeInactive, int page, int pageSize, CancellationToken ct) => throw new NotSupportedException();
        public Task<PatientDetails?> GetAsync(Guid actorId, Guid tenantId, Guid patientId, CancellationToken ct) => throw new NotSupportedException();
        public Task<PatientMutation> UpdateAsync(Guid actorId, Guid tenantId, Guid patientId, SavePatientRequest request, CancellationToken ct) => throw new NotSupportedException();
        public Task<PatientMutationStatus> SetActiveAsync(Guid actorId, Guid tenantId, Guid patientId, bool active, long expectedVersion, CancellationToken ct) => throw new NotSupportedException();
        public Task<PatientArchivePage?> ArchiveAsync(Guid actorId, Guid tenantId, Guid patientId, int page, int pageSize, CancellationToken ct) => throw new NotSupportedException();
    }
}
