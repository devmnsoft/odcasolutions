using System.ComponentModel.DataAnnotations;
using Odca.Contracts.Patients;

namespace Odca.Web.Models;

public sealed class PatientFormViewModel
{
    public Guid? Id { get; set; }
    [Required(ErrorMessage = "Informe o nome completo."), StringLength(160, MinimumLength = 2)] public string FullName { get; set; } = string.Empty;
    [StringLength(120)] public string? PreferredName { get; set; }
    public DateOnly? BirthDate { get; set; }
    [EmailAddress(ErrorMessage = "Informe um e-mail válido."), StringLength(254)] public string? Email { get; set; }
    [StringLength(40)] public string? Phone { get; set; }
    [StringLength(500)] public string? Address { get; set; }
    public string? IdentifierType { get; set; }
    [StringLength(80)] public string? IdentifierValue { get; set; }
    public bool HasRepresentative { get; set; }
    [StringLength(160)] public string? RepresentativeFullName { get; set; }
    public string? RepresentativeIdentifierType { get; set; }
    [StringLength(80)] public string? RepresentativeIdentifierValue { get; set; }
    [StringLength(80)] public string? RepresentativeRelationship { get; set; }
    public long ExpectedVersion { get; set; }

    public SavePatientRequest ToRequest() => new(FullName, PreferredName, BirthDate, Email, Phone, Address,
        string.IsNullOrWhiteSpace(IdentifierType) && string.IsNullOrWhiteSpace(IdentifierValue) ? null : new(IdentifierType ?? string.Empty, IdentifierValue ?? string.Empty),
        !HasRepresentative ? null : new(RepresentativeFullName ?? string.Empty, RepresentativeIdentifierType,
            RepresentativeIdentifierValue, RepresentativeRelationship ?? string.Empty), ExpectedVersion);

    public static PatientFormViewModel From(PatientDetails patient) => new()
    {
        Id = patient.Id, FullName = patient.FullName, PreferredName = patient.PreferredName, BirthDate = patient.BirthDate,
        Email = patient.Email, Phone = patient.Phone, Address = patient.Address, IdentifierType = patient.IdentifierType,
        IdentifierValue = patient.IdentifierValue, HasRepresentative = patient.Representative is not null,
        RepresentativeFullName = patient.Representative?.FullName, RepresentativeIdentifierType = patient.Representative?.IdentifierType,
        RepresentativeIdentifierValue = patient.Representative?.IdentifierValue, RepresentativeRelationship = patient.Representative?.Relationship,
        ExpectedVersion = patient.Version
    };
}

public sealed record PatientDetailsViewModel(PatientDetails Patient, PatientArchivePage Archive, string ReturnUrl);
