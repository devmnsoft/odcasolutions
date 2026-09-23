using Odca.Application.Operations;

namespace Odca.Domain.Tests;

public sealed class ContractSheetAccessPolicyTests
{
    [Theory]
    [InlineData(true, false, false, false, false, false)]
    [InlineData(false, true, false, false, false, false)]
    [InlineData(false, false, true, false, false, false)]
    [InlineData(false, false, false, true, false, false)]
    [InlineData(false, false, false, false, true, false)]
    [InlineData(false, false, false, false, false, true)]
    public void AssignedPurposeAllowsWorkspace(
        bool ownsContract,
        bool managesAllObligations,
        bool ownsObligation,
        bool isAssignedReviewer,
        bool ownsRenewal,
        bool isDocumentOperator)
    {
        Assert.True(ContractSheetAccessPolicy.CanOpen(
            ownsContract, managesAllObligations, ownsObligation,
            isAssignedReviewer, ownsRenewal, isDocumentOperator));
    }

    [Fact]
    public void ModulePermissionWithoutAssignmentDoesNotAllowWorkspace()
    {
        Assert.False(ContractSheetAccessPolicy.CanOpen(false, false, false, false, false, false));
    }
}
