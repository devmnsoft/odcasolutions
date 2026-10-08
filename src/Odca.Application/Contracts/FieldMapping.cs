using System.Text.RegularExpressions;

namespace Odca.Application.Contracts;

/// <summary>
/// Closed mapping from a field to a registration property.
/// Identifiers and property names are matched exactly. Expressions, SQL and code are rejected.
/// </summary>
public static class FieldMapping
{
    private static readonly Regex PropertyName = new("^[a-zA-Z][a-zA-Z0-9]{0,40}$", RegexOptions.CultureInvariant);

    private static readonly Dictionary<string, string[]> Properties = new(StringComparer.Ordinal)
    {
        ["patient"] = ["fullName", "preferredName", "birthDate", "email", "phone", "address", "identifierType", "identifierValue"],
        ["organization"] = ["displayName", "businessCode"],
        ["representative"] = ["fullName", "identifierValue", "relationship"],
        ["contractor"] = ["fullName", "identifierValue", "email", "phone", "address"]
    };

    public static string NormalizeOrigin(string? origin)
    {
        var value = origin?.Trim().ToLowerInvariant();
        return string.IsNullOrEmpty(value) ? "manual" : value;
    }

    public static void Validate(IReadOnlyCollection<ContractFieldDefinition> fields)
    {
        var byId = new Dictionary<string, ContractFieldDefinition>(StringComparer.Ordinal);
        foreach (var field in fields)
        {
            if (string.IsNullOrWhiteSpace(field.Id) || !byId.TryAdd(field.Id, field))
                throw new InvalidDataException("Os campos precisam de identificadores únicos e estáveis.");
            ValidateField(field);
        }

        foreach (var field in fields)
        {
            if (string.IsNullOrWhiteSpace(field.RequiredWhenFieldId))
                continue;
            if (!byId.TryGetValue(field.RequiredWhenFieldId, out var controller))
                throw new InvalidDataException($"A condição de '{field.Label}' aponta para um campo inexistente.");
            if (string.Equals(field.RequiredWhenFieldId, field.Id, StringComparison.Ordinal))
                throw new InvalidDataException($"A condição de '{field.Label}' não pode depender do próprio campo.");
            if (field.RequiredWhenAnyOf is null || field.RequiredWhenAnyOf.Count == 0 || field.RequiredWhenAnyOf.Any(string.IsNullOrWhiteSpace))
                throw new InvalidDataException($"Informe os valores que tornam '{field.Label}' obrigatório.");
            if (controller.Type == ContractFieldType.Choice &&
                field.RequiredWhenAnyOf.Any(value => controller.Choices?.Contains(value, StringComparer.Ordinal) != true))
                throw new InvalidDataException($"A condição de '{field.Label}' usa um valor que não existe em '{controller.Label}'.");
        }

        var visiting = new HashSet<string>(StringComparer.Ordinal);
        var visited = new HashSet<string>(StringComparer.Ordinal);
        foreach (var field in fields)
        {
            if (!Walk(field.Id, byId, visiting, visited))
                throw new InvalidDataException("As condições de obrigatoriedade formam um ciclo.");
        }

        foreach (var field in fields)
        {
            if (field.Type != ContractFieldType.Formula)
            {
                if (field.FormulaTerms is not null)
                    throw new InvalidDataException($"Somente campos calculados aceitam termos de fórmula ('{field.Label}').");
                continue;
            }
            if (field.FormulaTerms is not { Count: > 0 })
                throw new InvalidDataException($"A fórmula de '{field.Label}' precisa declarar ao menos um termo.");
            if (!string.IsNullOrWhiteSpace(field.SourceProperty))
                throw new InvalidDataException($"O campo calculado '{field.Label}' não aceita propriedade de cadastro.");
            foreach (var term in field.FormulaTerms)
            {
                if (!byId.TryGetValue(term.QuantityFieldId, out var quantity) || !byId.TryGetValue(term.UnitFieldId, out var unit))
                    throw new InvalidDataException($"A fórmula de '{field.Label}' aponta para um campo inexistente.");
                if (quantity.Type != ContractFieldType.Number)
                    throw new InvalidDataException($"A fórmula de '{field.Label}' só aceita quantidade em campos numéricos.");
                if (unit.Type != ContractFieldType.Currency)
                    throw new InvalidDataException($"A fórmula de '{field.Label}' só aceita valores monetários.");
                if (string.Equals(term.QuantityFieldId, field.Id, StringComparison.Ordinal) ||
                    string.Equals(term.UnitFieldId, field.Id, StringComparison.Ordinal))
                    throw new InvalidDataException($"A fórmula de '{field.Label}' não pode depender do próprio campo.");
            }
        }
    }

    public static IReadOnlyList<string> RegistrationKeys(ContractFieldDefinition field)
    {
        var origin = NormalizeOrigin(field.Origin);
        if (!string.IsNullOrWhiteSpace(field.SourceProperty))
        {
            if (origin == "representative")
                return ["representative"];
            if (origin is "patient")
                return [field.SourceProperty.Trim()];
            return [];
        }

        return field.Id switch
        {
            "patient_name" => ["fullName"],
            "patient_document" => ["identifierValue"],
            "patient_email" => ["email"],
            "patient_phone" => ["phone"],
            "patient_address" => ["address"],
            "patient_birth_date" => ["birthDate"],
            "patient_identification" => ["fullName", "identifierValue"],
            "legal_representative_info" or "representative_name" or "representative_document" => ["representative"],
            _ => []
        };
    }

    private static void ValidateField(ContractFieldDefinition field)
    {
        if (!string.IsNullOrEmpty(field.Help) && field.Help.Trim().Length > 200)
            throw new InvalidDataException($"A ajuda de '{field.Label}' deve ter no máximo 200 caracteres.");
        var origin = NormalizeOrigin(field.Origin);
        if (origin is not ("manual" or "patient" or "organization" or "representative" or "contractor"))
            throw new InvalidDataException($"A origem de '{field.Label}' não é permitida.");
        if (origin == "manual")
        {
            if (!string.IsNullOrWhiteSpace(field.SourceProperty))
                throw new InvalidDataException($"O campo manual '{field.Label}' não aceita propriedade de cadastro.");
            return;
        }

        if (string.IsNullOrWhiteSpace(field.SourceProperty))
            return;

        var property = field.SourceProperty.Trim();
        if (!PropertyName.IsMatch(property) || !Properties[origin].Contains(property, StringComparer.Ordinal))
            throw new InvalidDataException($"A propriedade de '{field.Label}' não pertence à origem selecionada.");
        if (!TypeAllows(field.Type, property))
            throw new InvalidDataException($"O tipo de '{field.Label}' não é compatível com a propriedade selecionada.");
    }

    private static bool TypeAllows(ContractFieldType type, string property)
    {
        if (property == "birthDate")
            return type is ContractFieldType.Date or ContractFieldType.ShortText;
        if (property is "identifierValue" or "businessCode")
            return type is ContractFieldType.ShortText or ContractFieldType.LongText or ContractFieldType.BrazilianDocument;
        return type is ContractFieldType.ShortText or ContractFieldType.LongText;
    }

    private static bool Walk(
        string id,
        IReadOnlyDictionary<string, ContractFieldDefinition> fields,
        HashSet<string> visiting,
        HashSet<string> visited)
    {
        if (visited.Contains(id))
            return true;
        if (!visiting.Add(id))
            return false;
        if (fields.TryGetValue(id, out var field) && !string.IsNullOrWhiteSpace(field.RequiredWhenFieldId) &&
            !Walk(field.RequiredWhenFieldId, fields, visiting, visited))
            return false;
        visiting.Remove(id);
        visited.Add(id);
        return true;
    }
}
