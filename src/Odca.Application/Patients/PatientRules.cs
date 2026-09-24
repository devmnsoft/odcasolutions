using System.Text.RegularExpressions;

namespace Odca.Application.Patients;

public static partial class PatientRules
{
    private static readonly HashSet<string> IdentifierTypes = new(StringComparer.Ordinal) { "cpf", "passport", "other" };

    public static IReadOnlyDictionary<string, string[]> Validate(string? fullName, string? email, string? identifierType, string? identifierValue)
    {
        var errors = new Dictionary<string, string[]>();
        if (string.IsNullOrWhiteSpace(fullName) || fullName.Trim().Length is < 2 or > 160)
            errors["fullName"] = ["Informe o nome completo (2 a 160 caracteres)."];
        if (!string.IsNullOrWhiteSpace(email) && !EmailPattern().IsMatch(email.Trim()))
            errors["email"] = ["Informe um e-mail válido."];
        if ((identifierType is null) != (identifierValue is null))
            errors["identifier"] = ["Informe juntos o tipo e o valor do identificador."];
        else if (identifierType is not null && (!IdentifierTypes.Contains(identifierType.Trim().ToLowerInvariant()) || string.IsNullOrWhiteSpace(identifierValue)))
            errors["identifier"] = ["O identificador informado não é suportado."];
        return errors;
    }

    public static string? NormalizeIdentifier(string? type, string? value)
        => value is null ? null : type?.Trim().ToLowerInvariant() == "cpf"
            ? NonDigits().Replace(value, string.Empty)
            : value.Trim().ToUpperInvariant();

    [GeneratedRegex(@"^[^\s@]+@[^\s@]+\.[^\s@]+$", RegexOptions.CultureInvariant)]
    private static partial Regex EmailPattern();
    [GeneratedRegex(@"\D", RegexOptions.CultureInvariant)]
    private static partial Regex NonDigits();
}
