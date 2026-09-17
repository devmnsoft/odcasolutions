using Odca.Application.Contracts;
using Odca.Application.Renewals;

namespace Odca.Application.Operations;

/// <summary>
/// Business rules that decide which official studio templates belong on a
/// contract sheet. Recommendations are operational starters, not legal advice.
/// </summary>
public static class ContractWorkspacePolicy
{
    public const string AmendmentKey = "contract-amendment";
    public const string ServicesKey = "services-agreement";
    public const string SupplyKey = "supply-agreement";
    public const string LeaseKey = "lease-agreement";
    public const string NdaUnilateralKey = "nda-unilateral";
    public const string NdaMutualKey = "nda-mutual";

    public static bool RequiresAmendment(
        bool inThreeMonthWindow,
        DateOnly? currentEnd,
        DateOnly? proposedEnd)
    {
        if (inThreeMonthWindow) return true;
        if (currentEnd.HasValue && proposedEnd.HasValue && proposedEnd.Value != currentEnd.Value)
            return true;
        return false;
    }

    public static bool CanInstallOfficialLibrary(bool canManageDrafts, int publishedOfficialCount) =>
        canManageDrafts && publishedOfficialCount < OfficialContractTemplates.All.Count;

    public static IReadOnlyList<OfficialTemplateRecommendation> Recommend(
        string? contractType,
        bool hasOpenReview,
        bool inThreeMonthWindow,
        DateOnly? currentEnd,
        DateOnly? proposedEnd)
    {
        var type = OfficialContractTemplates.NormalizeType(contractType);
        var needsAmendment = RequiresAmendment(inThreeMonthWindow, currentEnd, proposedEnd);
        var list = new List<OfficialTemplateRecommendation>();

        if (needsAmendment)
        {
            list.Add(new OfficialTemplateRecommendation(
                AmendmentKey,
                "Termo de Aditivo Contratual",
                "amendment",
                "A vigência entra na janela de renovação ou a proposta altera o término. Use aditivo; não estenda a vigência pela intenção."));
        }

        if (hasOpenReview)
        {
            return list;
        }

        switch (type)
        {
            case "nda":
                list.Add(Recommend(NdaUnilateralKey, "Termo de Confidencialidade (unilateral)", "nda", "Divulgação a um destinatário."));
                list.Add(Recommend(NdaMutualKey, "Acordo de Confidencialidade Recíproca", "nda", "Troca bilateral de informações."));
                break;
            case "supply":
                list.Add(Recommend(SupplyKey, "Contrato de Fornecimento", "supply", "Volume, preço e prazo de aviso estruturados."));
                break;
            case "lease":
                list.Add(Recommend(LeaseKey, "Contrato de Locação Operacional", "lease", "Objeto, aluguel e denúncia estruturados."));
                break;
            case "services":
            case null:
                list.Add(Recommend(ServicesKey, "Contrato de Prestação de Serviços", "services", "Objeto, valor e vigência estruturados."));
                break;
        }

        return list
            .DistinctBy(item => item.Key, StringComparer.Ordinal)
            .ToArray();
    }

    private static OfficialTemplateRecommendation Recommend(string key, string name, string type, string reason) =>
        new(key, name, type, reason);
}

public sealed record OfficialTemplateRecommendation(
    string Key,
    string Name,
    string ContractType,
    string Reason);
