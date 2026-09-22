using Odca.Api.Controllers;
using Odca.Contracts.DocumentImports;

namespace Odca.IntegrationTests;

public sealed class ContractImportManualReviewTests
{
    [Fact]
    public void ManualDataIsNormalizedWithoutInventingMissingValues()
    {
        var normalized = ContractImportsController.NormalizeManualData(new SaveManualContractImport(
            4, "  Contrato de fornecimento  ", "  FOR-2026/9  ",
            new DateOnly(2026, 9, 1), null, 1250.50m, " brl "));

        Assert.Equal("Contrato de fornecimento", normalized.Title);
        Assert.Equal("FOR-2026/9", normalized.Reference);
        Assert.Equal("BRL", normalized.Currency);
        Assert.Null(normalized.EndDate);
    }

    [Fact]
    public void ManualDataRequiresCurrencyWhenValueWasProvided()
    {
        var issues = ContractImportsController.Validate(new ContractImportsController.ContractForValidation(
            "Contrato", null, null, 10m, null, null));

        Assert.Contains(issues, issue => issue.Code == "missing-currency" && issue.Severity == ImportIssueSeverity.Blocking);
    }

    [Fact]
    public void ManualDataRejectsAmbiguousCurrencyAndInvertedDates()
    {
        var issues = ContractImportsController.Validate(new ContractImportsController.ContractForValidation(
            "Contrato", new DateOnly(2026, 9, 2), new DateOnly(2026, 9, 1), 10m, "R$", null));

        Assert.Contains(issues, issue => issue.Code == "end-before-start");
        Assert.Contains(issues, issue => issue.Code == "invalid-currency");
    }
}
