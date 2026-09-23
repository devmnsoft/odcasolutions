namespace Odca.Application.Operations;

/// <summary>
/// Defines the purposes that may open a contract workspace. Possessing a module
/// permission alone is insufficient: a limited operator must also be assigned to
/// the concrete contract, obligation, review, or renewal request.
/// </summary>
public static class ContractSheetAccessPolicy
{
    public static bool CanOpen(
        bool ownsContract,
        bool managesAllObligations,
        bool ownsObligation,
        bool isAssignedReviewer,
        bool ownsRenewal,
        bool isDocumentOperator)
        => ownsContract
           || managesAllObligations
           || ownsObligation
           || isAssignedReviewer
           || ownsRenewal
           || isDocumentOperator;
}
