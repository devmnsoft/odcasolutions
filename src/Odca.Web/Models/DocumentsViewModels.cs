namespace Odca.Web.Models;

using Odca.Contracts.Patients;
using Odca.Contracts.Studio;

public sealed class DocumentsIndexViewModel
{
    public Guid TenantId { get; init; }
    public StudioDocumentPage Documents { get; init; } = new([], 1, 15, 0);
    public string? Search { get; init; }
    public string? Type { get; init; }
    public string? Stage { get; init; }
    public Guid? PatientId { get; init; }
    public string? PatientSearch { get; init; }
    public IReadOnlyList<PatientSummary> PatientOptions { get; init; } = [];
    public PatientSummary? SelectedPatient { get; init; }
    public string? ErrorMessage { get; init; }
    public string? PatientErrorMessage { get; init; }
    public bool CanCreateDocument { get; init; }
    public bool CanManagePatients { get; init; }
    public bool CanReadPatients { get; init; }
    public int PatientPage { get; init; } = 1;
    public int PatientTotal { get; init; }
    public int PatientPageSize { get; init; } = 20;
}

public sealed class NewDocumentViewModel
{
    public Guid TenantId { get; init; }
    public Guid? PatientId { get; init; }
    public IReadOnlyList<PatientSummary> Patients { get; init; } = [];
    public bool CanManagePatients { get; init; }
}

