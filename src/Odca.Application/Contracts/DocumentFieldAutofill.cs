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
            var origin = FieldMapping.NormalizeOrigin(field.Origin);
            if (origin is not ("organization" or "patient" or "representative") && !(origin == "contractor" && fillContractor))
                continue;
            var value = ValueFor(field, origin, patient, organizationName, organizationDocument, source);
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

    /// <summary>
    /// Copies the selected registration keys into automatic fields.
    /// Manual values stay. Changed automatic values lose confirmation.
    /// Issued versions are not part of this list and are not modified.
    /// </summary>
    public static IReadOnlyList<ContractFieldValue> ApplyRegistrationSelection(
        IReadOnlyList<ContractFieldDefinition> fields,
        IReadOnlyList<ContractFieldValue> currentValues,
        string? snapshotJson,
        IReadOnlyCollection<string> acceptedKeys)
    {
        using var document = string.IsNullOrWhiteSpace(snapshotJson) ? null : JsonDocument.Parse(snapshotJson);
        var patient = document?.RootElement;
        var accepted = new HashSet<string>(acceptedKeys.Where(key => !string.IsNullOrWhiteSpace(key)).Select(key => key.Trim()), StringComparer.Ordinal);
        var byId = currentValues.Where(value => !string.IsNullOrWhiteSpace(value.FieldId)).ToDictionary(value => value.FieldId, StringComparer.Ordinal);
        var result = new List<ContractFieldValue>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var field in fields)
        {
            seen.Add(field.Id);
            byId.TryGetValue(field.Id, out var existing);
            var source = existing?.Source?.Trim().ToLowerInvariant();
            var origin = FieldMapping.NormalizeOrigin(field.Origin);
            if (source == "manual" || (source is null && origin == "manual"))
            {
                if (existing is not null)
                    result.Add(existing);
                continue;
            }

            var keys = FieldMapping.RegistrationKeys(field);
            if (keys.Count == 0 || !keys.Any(accepted.Contains))
            {
                if (existing is not null)
                    result.Add(existing);
                continue;
            }

            var updated = ValueFor(field, origin, patient, null, null, source is "patient" or "representative" ? source : null);
            if (string.IsNullOrWhiteSpace(updated))
            {
                if (existing is not null)
                    result.Add(existing);
                continue;
            }

            var changed = existing is null || !string.Equals(existing.Value, updated, StringComparison.Ordinal);
            result.Add(changed ? new ContractFieldValue(field.Id, updated, false, origin) : existing!);
        }

        foreach (var extra in currentValues)
        {
            if (seen.Add(extra.FieldId))
                result.Add(extra);
        }

        return result;
    }

    private static string? ValueFor(
        ContractFieldDefinition field,
        string origin,
        JsonElement? patient,
        string? organizationName,
        string? organizationDocument,
        string? contractorSource)
    {
        if (!string.IsNullOrWhiteSpace(field.SourceProperty))
            return PropertyValue(origin, field.SourceProperty.Trim(), patient, organizationName, organizationDocument, contractorSource);
        return origin switch
        {
            "organization" => OrganizationValue(field.Id, organizationName, organizationDocument),
            "patient" => PatientValue(field.Id, patient),
            "representative" => RepresentativeValue(field.Id, patient),
            "contractor" => ContractorValue(field.Id, patient, contractorSource),
            _ => null
        };
    }

    private static string? PropertyValue(
        string origin,
        string property,
        JsonElement? patient,
        string? organizationName,
        string? organizationDocument,
        string? contractorSource)
    {
        if (origin == "organization")
        {
            return property switch
            {
                "displayName" => BlankToNull(organizationName),
                "businessCode" => BlankToNull(organizationDocument),
                _ => null
            };
        }

        if (patient is null)
            return null;
        if (origin == "patient")
            return Read(patient.Value, property);
        if (origin == "representative")
        {
            if (!patient.Value.TryGetProperty("representative", out var representative) || representative.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
                return null;
            return Read(representative, property);
        }

        if (origin == "contractor" && contractorSource is "patient" or "representative")
        {
            if (contractorSource == "representative" &&
                (!patient.Value.TryGetProperty("representative", out var representative) || representative.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined))
                return null;
            var party = contractorSource == "patient" ? patient.Value : patient.Value.GetProperty("representative");
            if (contractorSource == "representative" && property is "email" or "phone" or "address")
                return null;
            return Read(party, property);
        }

        return null;
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
