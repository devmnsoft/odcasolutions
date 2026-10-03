using Odca.Application.Administration;

namespace Odca.Domain.Tests;

public sealed class OrganizationFeatureCatalogTests
{
    [Theory]
    [InlineData("/api/v1/organizations/00000000-0000-0000-0000-000000000001/patients", OrganizationFeatureCatalog.Patients)]
    [InlineData("/api/v1/organizations/00000000-0000-0000-0000-000000000001/patients?search=ana&page=2", OrganizationFeatureCatalog.Patients)]
    [InlineData("/api/v1/organizations/00000000-0000-0000-0000-000000000001/studio/templates", OrganizationFeatureCatalog.Templates)]
    [InlineData("/api/v1/organizations/00000000-0000-0000-0000-000000000001/studio/drafts", OrganizationFeatureCatalog.ContractDrafts)]
    [InlineData("/api/v1/organizations/00000000-0000-0000-0000-000000000001/studio/documents", OrganizationFeatureCatalog.Documents)]
    [InlineData("/api/v1/organizations/00000000-0000-0000-0000-000000000001/studio/versions/00000000-0000-0000-0000-000000000002/pdf", OrganizationFeatureCatalog.Documents)]
    [InlineData("/api/v1/organizations/00000000-0000-0000-0000-000000000001/studio/versions/00000000-0000-0000-0000-000000000002/signature-preparation", OrganizationFeatureCatalog.Signatures)]
    [InlineData("/api/v1/organizations/00000000-0000-0000-0000-000000000001/reviews", OrganizationFeatureCatalog.Reviews)]
    [InlineData("/api/v1/organizations/00000000-0000-0000-0000-000000000001/contract-imports", OrganizationFeatureCatalog.Imports)]
    public void ApiPathSelectsTheCanonicalFeature(string path, string expected) =>
        Assert.Equal(expected, OrganizationFeatureCatalog.ResolveApiPath(path));

    [Theory]
    [InlineData("/api/v1/organizations/00000000-0000-0000-0000-000000000001/features")]
    [InlineData("/api/v1/platform/customers")]
    [InlineData("/health/ready")]
    public void DiagnosticAndPlatformRoutesStayOutsideTheFeatureGate(string path) =>
        Assert.Null(OrganizationFeatureCatalog.ResolveApiPath(path));
}
