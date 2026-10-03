using System.Text.Json;
using Odca.Application.Onboarding;

namespace Odca.Application.Contracts;

/// <summary>
/// Fills draft values only from explicit field identifiers and the origin declared
/// on the field. Labels are never compared.
/// </summary>
public static class DocumentFieldAutofill
{
    private static readonly Dictionary<string, string> PatientFields = new(StringComparer.Ordinal)
    {
        ["patient_name"] = "fullName",
        ["patient_document"] = "identifierValue",
        ["patient_email"] = "email",
        ["patient_phone"] = "phone",
        ["patient_address"] = "address",
        ["patient_birth_date"] = "birthDate"
    };

    private static readonly Dictionary<string, string> OrganizationFields = new(StringComparer.Ordinal)
    {
        ["contracted_name"] = "displayName",
        ["organization_name"] = "displayName",
        ["contracted_cnpj"] = "businessCode"
    };

    public static IReadOnlyList<ContractFieldValue> Apply(
        IReadOnlyList<ContractFieldDefinition> fields,
        string? patientSnapshotJson,
        string? organizationName,
        string? organizationDocument,
        string? contractorSource = null)
    {
        using var patientDocument = string.IsNullOrWhiteSpace(patientSnapshotJson) ? null : JsonDocument.Parse(patientSnapshotJson);
        var patient = patientDocument?.RootElement;
        var source = contractorSource?.Trim().ToLowerInvariant();
        var fillContractor = source is "patient" or "representative";
        var values = new List<ContractFieldValue>();
        foreach (var field in fields)
        {
            var origin = field.Origin?.Trim().ToLowerInvariant();
            if (origin is not ("organization" or "patient" or "representative") && !(origin == "contractor" && fillContractor))
                continue;
            var value = origin switch
            {
                "organization" => OrganizationValue(field.Id, organizationName, organizationDocument),
                "patient" => PatientValue(field.Id, patient),
                "representative" => RepresentativeValue(field.Id, patient),
                "contractor" => ContractorValue(field.Id, patient, source),
                _ => null
            };
            if (string.IsNullOrWhiteSpace(value))
                continue;
            if (field.Type == ContractFieldType.BrazilianDocument && BrazilianDocument.NormalizeAndValidate(value).Type == "invalid")
                continue;
            if (field.Type == ContractFieldType.Date && !DateOnly.TryParseExact(value, "yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out _))
                continue;
            values.Add(new(field.Id, value, false, origin));
        }

        return values;
    }

    private static string? OrganizationValue(string fieldId, string? name, string? document)
    {
        if (!OrganizationFields.TryGetValue(fieldId, out var source))
            return null;
        return source switch
        {
            "displayName" => BlankToNull(name),
            "businessCode" => BlankToNull(document),
            _ => null
        };
    }

    private static string? PatientValue(string fieldId, JsonElement? patient)
    {
        if (patient is null)
            return null;
        if (fieldId == "patient_identification")
        {
            var name = Read(patient.Value, "fullName");
            var identifier = Read(patient.Value, "identifierValue");
            if (name is null) return identifier;
            return identifier is null ? name : $"{name} — {identifier}";
        }

        return PatientFields.TryGetValue(fieldId, out var property) ? Read(patient.Value, property) : null;
    }

    private static string? ContractorValue(string fieldId, JsonElement? patient, string? source)
    {
        if (patient is null || source is not ("patient" or "representative"))
            return null;
        if (source == "representative" &&
            (!patient.Value.TryGetProperty("representative", out var representative) ||
             representative.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined))
            return null;
        var party = source == "patient" ? patient.Value : patient.Value.GetProperty("representative");
        return fieldId switch
        {
            "contractor_name" => Read(party, "fullName"),
            "contractor_document" => Read(party, "identifierValue"),
            "contractor_address" => source == "patient" ? Read(party, "address") : null,
            "contractor_email" => source == "patient" ? Read(party, "email") : null,
            "contractor_phone" => source == "patient" ? Read(party, "phone") : null,
            _ => null
        };
    }

    private static string? RepresentativeValue(string fieldId, JsonElement? patient)
    {
        if (patient is null || !patient.Value.TryGetProperty("representative", out var representative) || representative.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
            return null;
        if (fieldId == "legal_representative_info")
        {
            var parts = new[] { Read(representative, "fullName"), Read(representative, "identifierValue"), Read(representative, "relationship") };
            var text = string.Join(", ", parts.Where(part => !string.IsNullOrWhiteSpace(part)));
            return BlankToNull(text);
        }

        return fieldId switch
        {
            "representative_name" => Read(representative, "fullName"),
            "representative_document" => Read(representative, "identifierValue"),
            _ => null
        };
    }

    private static string? Read(JsonElement element, string property)
    {
        if (!element.TryGetProperty(property, out var value) || value.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
            return null;
        var text = value.ValueKind == JsonValueKind.String ? value.GetString() : value.ToString();
        return BlankToNull(text);
    }

    private static string? BlankToNull(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
