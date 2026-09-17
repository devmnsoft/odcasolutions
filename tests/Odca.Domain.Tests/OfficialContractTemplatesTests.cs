using Odca.Application.Contracts;

namespace Odca.Domain.Tests;

public sealed class OfficialContractTemplatesTests
{
    [Fact]
    public void OfficialLibraryParsesAgainstCanonicalSchema()
    {
        OfficialContractTemplates.EnsureValid();
        Assert.Equal(4, OfficialContractTemplates.All.Count);
        Assert.Equal(OfficialContractTemplates.All.Count, OfficialContractTemplates.All.Select(item => item.Key).Distinct(StringComparer.Ordinal).Count());
        Assert.Equal(OfficialContractTemplates.All.Count, OfficialContractTemplates.All.Select(item => item.Name).Distinct(StringComparer.Ordinal).Count());
    }

    [Theory]
    [InlineData("nda")]
    [InlineData("services")]
    [InlineData("amendment")]
    public void KnownTypesSurviveAllowlist(string type) =>
        Assert.Equal(type, OfficialContractTemplates.NormalizeType(type));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("script")]
    [InlineData("NDA")]
    public void UnknownTypesAreDropped(string? type) =>
        Assert.Null(OfficialContractTemplates.NormalizeType(type));

    [Fact]
    public void EveryDefinedFieldAppearsInTheDocument()
    {
        foreach (var template in OfficialContractTemplates.All)
        {
            var parsed = StructuredContractDocument.Parse(template.Content, template.Fields);
            foreach (var field in template.Fields)
            {
                Assert.True(parsed.FieldOccurrences.ContainsKey(field.Id), $"{template.Key} missing {field.Id}");
                Assert.True(field.Required);
            }
        }
    }
}
