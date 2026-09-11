using Odca.Application.Onboarding;

namespace Odca.Application.Contracts;

public static class ContractDocumentRules
{
    public static (bool Ok, string DocumentType, string? Normalized, string? Display, string? Error) NormalizeDocument(
        string documentType,
        string? documentRaw)
    {
        var type = documentType.Trim().ToLowerInvariant();
        if (type is not ("cpf" or "cnpj" or "none"))
        {
            return (false, type, null, null, "document_type_invalid");
        }

        if (type == "none")
        {
            if (!string.IsNullOrWhiteSpace(documentRaw))
            {
                return (false, type, null, null, "document_not_allowed");
            }

            return (true, type, null, null, null);
        }

        if (string.IsNullOrWhiteSpace(documentRaw))
        {
            return (false, type, null, null, "document_required");
        }

        var (normalized, detected) = BrazilianDocument.NormalizeAndValidate(documentRaw);
        if (detected == "invalid" || !string.Equals(detected, type, StringComparison.Ordinal))
        {
            return (false, type, null, null, "document_invalid");
        }

        var display = documentRaw.Trim();
        return (true, type, normalized, display, null);
    }
}
