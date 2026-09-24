using System.Text.RegularExpressions;
using Odca.Application.Onboarding;
using Odca.Contracts.Patients;

namespace Odca.Application.Patients;

public static partial class PatientRules
{
    private static readonly HashSet<string> IdentifierTypes = new(StringComparer.Ordinal) { "cpf", "passport", "other" };

    public static IReadOnlyDictionary<string, string[]> Validate(SavePatientRequest request, DateOnly today)
    {
        var errors = new Dictionary<string, string[]>();
        AddLengthError(errors, "fullName", request.FullName, 2, 160, true, "Informe o nome completo (2 a 160 caracteres).");
        AddLengthError(errors, "preferredName", request.PreferredName, 0, 120);
        AddLengthError(errors, "email", request.Email, 0, 254);
        AddLengthError(errors, "phone", request.Phone, 0, 40);
        AddLengthError(errors, "address", request.Address, 0, 500);

        if (!string.IsNullOrWhiteSpace(request.Email) && !EmailPattern().IsMatch(request.Email.Trim()))
            errors["email"] = ["Informe um e-mail válido."];
        if (request.BirthDate is { } birthDate && birthDate > today)
            errors["birthDate"] = ["A data de nascimento não pode estar no futuro."];

        ValidateIdentifier(errors, "identifier", request.Identifier?.Type, request.Identifier?.Value);
        if (request.Representative is { } representative)
        {
            AddLengthError(errors, "representative.fullName", representative.FullName, 2, 160, true,
                "Informe o nome do representante (2 a 160 caracteres).");
            AddLengthError(errors, "representative.relationship", representative.Relationship, 1, 80, true,
                "Informe a relação do representante (até 80 caracteres).");
            ValidateIdentifier(errors, "representative.identifier", representative.IdentifierType, representative.IdentifierValue);
        }

        return errors;
    }

    // Compatibility overload for callers that validate only the minimum registration fields.
    public static IReadOnlyDictionary<string, string[]> Validate(string? fullName, string? email, string? identifierType, string? identifierValue)
        => Validate(new SavePatientRequest(fullName ?? string.Empty, null, null, email, null, null,
            identifierType is null && identifierValue is null ? null : new(identifierType!, identifierValue!), null), DateOnly.MaxValue);

    public static SavePatientRequest Normalize(SavePatientRequest request) => request with
    {
        FullName = request.FullName.Trim(),
        PreferredName = Null(request.PreferredName),
        Email = Null(request.Email),
        Phone = Null(request.Phone),
        Address = Null(request.Address),
        Identifier = NormalizeIdentifierInput(request.Identifier),
        Representative = request.Representative is null ? null : request.Representative with
        {
            FullName = request.Representative.FullName.Trim(),
            Relationship = request.Representative.Relationship.Trim(),
            IdentifierType = Null(request.Representative.IdentifierType)?.ToLowerInvariant(),
            IdentifierValue = NormalizeIdentifier(request.Representative.IdentifierType, request.Representative.IdentifierValue)
        }
    };

    public static string? NormalizeIdentifier(string? type, string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        return type?.Trim().ToLowerInvariant() == "cpf"
            ? NonDigits().Replace(value.Trim(), string.Empty)
            : value.Trim().ToUpperInvariant();
    }

    private static PatientIdentifierInput? NormalizeIdentifierInput(PatientIdentifierInput? identifier) => identifier is null ? null :
        new PatientIdentifierInput(identifier.Type.Trim().ToLowerInvariant(), NormalizeIdentifier(identifier.Type, identifier.Value) ?? string.Empty);

    private static void ValidateIdentifier(Dictionary<string, string[]> errors, string key, string? type, string? value)
    {
        var hasType = !string.IsNullOrWhiteSpace(type);
        var hasValue = !string.IsNullOrWhiteSpace(value);
        if (hasType != hasValue) { errors[key] = ["Informe juntos o tipo e o valor do identificador."]; return; }
        if (!hasType) return;

        var normalizedType = type!.Trim().ToLowerInvariant();
        if (!IdentifierTypes.Contains(normalizedType)) { errors[key] = ["O identificador informado não é suportado."]; return; }
        var trimmed = value!.Trim();
        if (trimmed.Length > 80) { errors[key] = ["O identificador deve ter no máximo 80 caracteres."]; return; }
        if (normalizedType == "cpf")
        {
            if (!CpfInputPattern().IsMatch(trimmed) || BrazilianDocument.NormalizeAndValidate(trimmed).Type != "cpf")
                errors[key] = ["Informe um CPF válido."];
        }
        else if (trimmed.Length < 2)
            errors[key] = ["O identificador deve ter ao menos 2 caracteres."];
    }

    private static void AddLengthError(Dictionary<string, string[]> errors, string key, string? value, int min, int max,
        bool required = false, string? message = null)
    {
        var trimmed = value?.Trim();
        if ((required && string.IsNullOrEmpty(trimmed)) || (!string.IsNullOrEmpty(trimmed) && trimmed.Length < min) || trimmed?.Length > max)
            errors[key] = [message ?? $"O campo deve ter no máximo {max} caracteres."];
    }

    private static string? Null(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    [GeneratedRegex(@"^[^\s@]+@[^\s@]+\.[^\s@]+$", RegexOptions.CultureInvariant)]
    private static partial Regex EmailPattern();
    [GeneratedRegex(@"^\d{3}\.?\d{3}\.?\d{3}-?\d{2}$", RegexOptions.CultureInvariant)]
    private static partial Regex CpfInputPattern();
    [GeneratedRegex(@"\D", RegexOptions.CultureInvariant)]
    private static partial Regex NonDigits();
}
