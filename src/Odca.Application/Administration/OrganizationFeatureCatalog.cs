namespace Odca.Application.Administration;

public static class OrganizationFeatureCatalog
{
    public const string Patients = "patients";
    public const string ContractDrafts = "contract_drafts";
    public const string Templates = "templates";
    public const string Documents = "documents";
    public const string Reviews = "reviews";
    public const string Signatures = "signatures";
    public const string Imports = "imports";

    public static readonly IReadOnlyList<string> Codes =
    [
        Patients,
        ContractDrafts,
        Templates,
        Documents,
        Reviews,
        Signatures,
        Imports
    ];

    public static string DisplayName(string code) => code switch
    {
        Patients => "Pacientes",
        ContractDrafts => "Minutas",
        Templates => "Modelos",
        Documents => "Documentos e PDF",
        Reviews => "Revisões",
        Signatures => "Assinatura",
        Imports => "Importações",
        _ => code
    };

    public static bool IsKnown(string? code) =>
        code is not null && Codes.Contains(code);

    public static string? ResolveApiPath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return null;
        }

        var value = path.Split('?', 2)[0];
        const string marker = "/api/v1/organizations/";
        var index = value.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
        if (index < 0)
        {
            return null;
        }

        var rest = value[(index + marker.Length)..].Trim('/');
        var slash = rest.IndexOf('/');
        if (slash < 0)
        {
            return null;
        }

        var tail = rest[(slash + 1)..];
        if (tail.Equals("features", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        if (tail.StartsWith("patients", StringComparison.OrdinalIgnoreCase))
        {
            return Patients;
        }

        if (tail.StartsWith("contract-imports", StringComparison.OrdinalIgnoreCase))
        {
            return Imports;
        }

        if (tail.StartsWith("reviews", StringComparison.OrdinalIgnoreCase) ||
            tail.StartsWith("studio/reviews", StringComparison.OrdinalIgnoreCase))
        {
            return Reviews;
        }

        if (tail.StartsWith("studio/templates", StringComparison.OrdinalIgnoreCase))
        {
            return Templates;
        }

        if (tail.Contains("signature-preparation", StringComparison.OrdinalIgnoreCase))
        {
            return Signatures;
        }

        if (tail.Contains("/pdf", StringComparison.OrdinalIgnoreCase))
        {
            return Documents;
        }

        if (tail.Contains("/documents", StringComparison.OrdinalIgnoreCase))
        {
            return Documents;
        }

        if (tail.StartsWith("studio/", StringComparison.OrdinalIgnoreCase))
        {
            return ContractDrafts;
        }

        return null;
    }
}
