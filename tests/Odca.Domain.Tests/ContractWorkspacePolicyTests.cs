using Odca.Application.Operations;

namespace Odca.Domain.Tests;

public sealed class ContractWorkspacePolicyTests
{
    [Fact]
    public void RenewalWindowRequiresAmendmentAndBlocksNewPrimaryDraft()
    {
        var items = ContractWorkspacePolicy.Recommend(
            "services",
            hasOpenReview: false,
            inThreeMonthWindow: true,
            currentEnd: new DateOnly(2026, 12, 31),
            proposedEnd: null);

        Assert.Contains(items, item => item.Key == ContractWorkspacePolicy.AmendmentKey);
        Assert.Contains(items, item => item.Key == ContractWorkspacePolicy.ServicesKey);
    }

    [Fact]
    public void OpenReviewSuppressesPrimaryTemplates()
    {
        var items = ContractWorkspacePolicy.Recommend(
            "nda",
            hasOpenReview: true,
            inThreeMonthWindow: false,
            currentEnd: null,
            proposedEnd: null);

        Assert.Empty(items);
    }

    [Fact]
    public void OpenReviewStillSurfacesAmendmentInsideRenewalWindow()
    {
        var items = ContractWorkspacePolicy.Recommend(
            "lease",
            hasOpenReview: true,
            inThreeMonthWindow: true,
            currentEnd: new DateOnly(2026, 10, 1),
            proposedEnd: new DateOnly(2027, 10, 1));

        Assert.Equal(ContractWorkspacePolicy.AmendmentKey, Assert.Single(items).Key);
    }

    [Theory]
    [InlineData("nda", ContractWorkspacePolicy.NdaUnilateralKey)]
    [InlineData("supply", ContractWorkspacePolicy.SupplyKey)]
    [InlineData("lease", ContractWorkspacePolicy.LeaseKey)]
    [InlineData("services", ContractWorkspacePolicy.ServicesKey)]
    public void TypeSelectsOfficialStarter(string type, string expectedKey)
    {
        var items = ContractWorkspacePolicy.Recommend(type, false, false, null, null);
        Assert.Contains(items, item => item.Key == expectedKey);
    }

    [Fact]
    public void UnknownTypeFallsBackToServices()
    {
        var items = ContractWorkspacePolicy.Recommend("script", false, false, null, null);
        Assert.Contains(items, item => item.Key == ContractWorkspacePolicy.ServicesKey);
    }

    [Fact]
    public void LibraryInstallRequiresManagePermissionAndMissingOfficials()
    {
        Assert.True(ContractWorkspacePolicy.CanInstallOfficialLibrary(true, 0));
        Assert.False(ContractWorkspacePolicy.CanInstallOfficialLibrary(false, 0));
        Assert.False(ContractWorkspacePolicy.CanInstallOfficialLibrary(true, 6));
    }

    [Fact]
    public void ChangedEndDateRequiresAmendmentEvenOutsideWindow()
    {
        Assert.True(ContractWorkspacePolicy.RequiresAmendment(
            false, new DateOnly(2027, 1, 1), new DateOnly(2027, 6, 1)));
        Assert.False(ContractWorkspacePolicy.RequiresAmendment(
            false, new DateOnly(2027, 1, 1), new DateOnly(2027, 1, 1)));
    }
}
