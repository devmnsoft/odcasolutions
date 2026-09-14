using Odca.Application.Contracts;

namespace Odca.Domain.Tests;

public sealed class StructuredContractDocumentTests
{
    private static readonly ContractFieldDefinition Amount = new("amount", "Valor", ContractFieldType.Currency, true);
    private const string Content = """
        {"type":"document","content":[{"type":"heading","level":1,"content":[{"type":"text","text":"Contrato"}]},{"type":"paragraph","alignment":"justify","content":[{"type":"text","text":"Valor: ","marks":["bold"]},{"type":"field","fieldId":"amount"},{"type":"text","text":" e novamente "},{"type":"field","fieldId":"amount"}]}]}
        """;

    [Fact]
    public void RepeatedOccurrencesAreLinkedByStableFieldId()
    {
        var document = StructuredContractDocument.Parse(Content, [Amount]);
        Assert.Equal(2, document.FieldOccurrences["amount"]);
    }

    [Theory]
    [InlineData("<script>alert(1)</script>")]
    [InlineData("{\"type\":\"document\",\"onclick\":\"steal()\",\"content\":[]}")]
    [InlineData("{\"type\":\"document\",\"content\":[{\"type\":\"image\",\"src\":\"https://evil.invalid/a\"}]}")]
    public void RejectsMarkupHandlersAndExternalResources(string malicious)
        => Assert.ThrowsAny<Exception>(() => StructuredContractDocument.Parse(malicious, []));

    [Theory]
    [InlineData(ContractFieldType.Date, "31/12/2026")]
    [InlineData(ContractFieldType.Currency, "1.234,56")]
    [InlineData(ContractFieldType.BrazilianDocument, "111.111.111-11")]
    public void RejectsNonCanonicalOrInvalidFieldValues(ContractFieldType type, string value)
    {
        var definition = new ContractFieldDefinition("field", "Campo", type, true);
        Assert.Throws<InvalidDataException>(() => StructuredContractDocument.ValidateValues(
            [definition], [new("field", value, true)], requireConfirmed: true));
    }

    [Fact]
    public void DetectsConcurrentSaveAndPreservesLocalCandidate()
    {
        var initial = StructuredContractDocument.Parse(Content, [Amount]);
        var draft = new ContractDraft(initial);
        draft.Save(0, initial);
        var localCandidate = StructuredContractDocument.Parse(Content.Replace("Contrato", "Cópia local"), [Amount]);

        var conflict = Assert.Throws<ContractRevisionConflictException>(() => draft.Save(0, localCandidate));

        Assert.Equal(1, conflict.Actual);
        Assert.Contains("Cópia local", localCandidate.CanonicalJson);
        Assert.DoesNotContain("Cópia local", draft.Content.CanonicalJson);
    }

    [Fact]
    public void PublishingRequiresConfirmedFieldsAndSnapshotsAreImmutable()
    {
        var draft = new ContractDraft(StructuredContractDocument.Parse(Content, [Amount]));
        Assert.Throws<InvalidDataException>(() => draft.Publish([Amount], [new("amount", null, false)], DateTimeOffset.UtcNow));

        var published = draft.Publish([Amount], [new("amount", "1000.00", true, "manual")], DateTimeOffset.UtcNow);
        draft.Save(0, StructuredContractDocument.Parse(Content.Replace("Contrato", "Alterado"), [Amount]));

        Assert.DoesNotContain("Alterado", published.Content);
        Assert.Equal("1000.00", published.Values.Single().Value);
    }

    [Fact]
    public void RestoreCreatesANewDraftRevisionWithoutChangingHistory()
    {
        var draft = new ContractDraft(StructuredContractDocument.Parse(Content, [Amount]));
        draft.Publish([Amount], [new("amount", "1.00", true)], DateTimeOffset.UtcNow);
        draft.Save(0, StructuredContractDocument.Parse(Content.Replace("Contrato", "Novo"), [Amount]));

        draft.Restore(1, [Amount]);

        Assert.Equal(2, draft.Revision);
        Assert.Single(draft.Versions);
        Assert.DoesNotContain("Novo", draft.Content.CanonicalJson);
    }
}
