using Odca.Contracts.Patients;

namespace Odca.Application.Patients;

public enum PatientMutationStatus { Success, Forbidden, NotFound, Conflict, Duplicate }
public sealed record PatientMutation(PatientMutationStatus Status, PatientDetails? Patient = null);

public interface IPatientRepository
{
    Task<PatientPage?> ListAsync(Guid actorId, Guid tenantId, string? search, bool includeInactive, int page, int pageSize, CancellationToken ct);
    Task<PatientDetails?> GetAsync(Guid actorId, Guid tenantId, Guid patientId, CancellationToken ct);
    Task<PatientMutation> CreateAsync(Guid actorId, Guid tenantId, SavePatientRequest request, CancellationToken ct);
    Task<PatientMutation> UpdateAsync(Guid actorId, Guid tenantId, Guid patientId, SavePatientRequest request, CancellationToken ct);
    Task<PatientMutationStatus> SetActiveAsync(Guid actorId, Guid tenantId, Guid patientId, bool active, long expectedVersion, CancellationToken ct);
    Task<PatientArchivePage?> ArchiveAsync(Guid actorId, Guid tenantId, Guid patientId, int page, int pageSize, CancellationToken ct);
}
