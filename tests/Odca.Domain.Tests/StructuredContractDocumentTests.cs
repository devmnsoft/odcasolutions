using System.Globalization;
using System.Text;
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
            [definition], [new("field", value, true)], ContractValidationMode.Confirmed));
    }

    [Fact]
    public void StructuralModeSavesIncompleteDraftsButStillRejectsMalformedPresentValues()
    {
        StructuredContractDocument.ValidateValues([Amount], [], ContractValidationMode.Structural);
        StructuredContractDocument.ValidateValues([Amount], [new("amount", null, false)], ContractValidationMode.Structural);
        StructuredContractDocument.ValidateValues([Amount], [new("amount", "1000.00", false)], ContractValidationMode.Structural);
        Assert.Throws<InvalidDataException>(() =>
            StructuredContractDocument.ValidateValues([Amount], [new("amount", "150,00", false)], ContractValidationMode.Structural));
    }

    [Fact]
    public void CompleteModeRequiresFilledValuesWithoutConfirmation()
    {
        Assert.Throws<InvalidDataException>(() =>
            StructuredContractDocument.ValidateValues([Amount], [], ContractValidationMode.Complete));
        Assert.Throws<InvalidDataException>(() =>
            StructuredContractDocument.ValidateValues([Amount], [new("amount", " ", false)], ContractValidationMode.Complete));
        Assert.Throws<InvalidDataException>(() =>
            StructuredContractDocument.ValidateValues([Amount], [new("amount", "150,00", false)], ContractValidationMode.Complete));
        StructuredContractDocument.ValidateValues([Amount], [new("amount", "1000.00", false)], ContractValidationMode.Complete);
    }

    [Fact]
    public void ConfirmedModeRequiresExplicitConfirmationOnRequiredFields()
    {
        Assert.Throws<InvalidDataException>(() =>
            StructuredContractDocument.ValidateValues([Amount], [new("amount", "1000.00", false)], ContractValidationMode.Confirmed));
        Assert.Throws<InvalidDataException>(() =>
            StructuredContractDocument.ValidateValues([Amount], [new("amount", null, true)], ContractValidationMode.Confirmed));
        StructuredContractDocument.ValidateValues([Amount], [new("amount", "1000.00", true)], ContractValidationMode.Confirmed);
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

    [Fact]
    public void AcceptsPageBreakInCanonicalDocument()
    {
        var document = StructuredContractDocument.Parse(
            """{"type":"document","content":[{"type":"pageBreak"}]}""", []);

        Assert.Empty(document.FieldOccurrences);
    }

    [Fact]
    public void RendererEscapesTextAndResolvesEveryRepeatedFieldFromSnapshot()
    {
        var fields="""[{"id":"amount","label":"Valor","type":"Currency","required":true}]""";
        var values="""[{"fieldId":"amount","value":"1000.00","confirmed":true}]""";

        var html=ContractDocumentRenderer.ToHtml(Content.Replace("Contrato","<Contrato & seguro>"),fields,values);

        Assert.Contains("&lt;Contrato &amp; seguro&gt;",html);
        Assert.Equal(2,html.Split("1.000,00",StringSplitOptions.None).Length-1);
        Assert.DoesNotContain("<Contrato",html);
    }

    [Fact]
    public void PdfIsSelectableTextWithAccentsTablesAndPageNumbers()
    {
        const string content="""{"type":"document","content":[{"type":"heading","level":1,"content":[{"type":"text","text":"Cláusula médica"}]},{"type":"table","content":[{"type":"tableRow","content":[{"type":"tableCell","content":[{"type":"text","text":"Descrição"}]},{"type":"tableCell","content":[{"type":"text","text":"Atenção"}]}]}]},{"type":"pageBreak"},{"type":"paragraph","content":[{"type":"text","text":"Fim"}]}]}""";

        var pdf=ContractDocumentRenderer.ToPdf(content,"[]","[]","Documento clínico",3);
        var latin=System.Text.Encoding.Latin1.GetString(pdf);

        Assert.StartsWith("%PDF-1.4",latin);
        Assert.Contains("Cláusula médica",latin);
        Assert.Contains("Página 1 de 2",latin);
        Assert.Contains("Página 2 de 2",latin);
    }

    [Fact]
    public void RendererShowsOptionalEmptyFieldsWithoutInventingData()
    {
        const string content="""{"type":"document","content":[{"type":"paragraph","content":[{"type":"field","fieldId":"note"}]}]}""";
        const string fields="""[{"id":"note","label":"Observação","type":"ShortText","required":false}]""";

        var html=ContractDocumentRenderer.ToHtml(content,fields,"[]");

        Assert.Contains("Não informado",html);
    }

    [Fact]
    public void RendererRejectsContentThatIsNotAnArray()
    {
        const string malformed = """{"type":"document","content":{"type":"paragraph"}}""";

        Assert.Throws<InvalidDataException>(() => ContractDocumentRenderer.ToHtml(malformed, "[]", "[]"));
    }

    [Fact]
    public void PdfSerializationIsCultureIndependentAndCrossReferencesPointToObjects()
    {
        const string content = """{"type":"document","content":[{"type":"paragraph","content":[{"type":"text","text":"Conteúdo"}]}]}""";
        var originalCulture = CultureInfo.CurrentCulture;
        byte[] ptBr;
        byte[] enUs;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("pt-BR");
            ptBr = ContractDocumentRenderer.ToPdf(content, "[]", "[]", "Documento", 1);
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("en-US");
            enUs = ContractDocumentRenderer.ToPdf(content, "[]", "[]", "Documento", 1);
        }
        finally
        {
            CultureInfo.CurrentCulture = originalCulture;
        }

        Assert.Equal(ptBr, enUs);
        var pdf = Encoding.Latin1.GetString(ptBr);
        var startXrefMarker = "startxref\n";
        var startXref = int.Parse(
            pdf.AsSpan(pdf.LastIndexOf(startXrefMarker, StringComparison.Ordinal) + startXrefMarker.Length)
                .Slice(0, pdf.AsSpan(pdf.LastIndexOf(startXrefMarker, StringComparison.Ordinal) + startXrefMarker.Length).IndexOf('\n')),
            CultureInfo.InvariantCulture);
        Assert.StartsWith("xref\n", pdf[startXref..]);

        var xrefLines = pdf[startXref..].Split('\n');
        var objectCount = int.Parse(xrefLines[1].Split(' ')[1], CultureInfo.InvariantCulture) - 1;
        for (var i = 0; i < objectCount; i++)
        {
            var offset = int.Parse(xrefLines[i + 3].AsSpan(0, 10), CultureInfo.InvariantCulture);
            Assert.StartsWith($"{i + 1} 0 obj\n", pdf[offset..]);
        }
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void RejectsValuesWithMissingOrWhitespaceFieldId(string? missingId)
    {
        var definition = new ContractFieldDefinition("field_a", "Campo A", ContractFieldType.ShortText, false);
        var ex = Assert.Throws<InvalidDataException>(() =>
            StructuredContractDocument.ValidateValues([definition], [new(missingId!, "valor", true)], ContractValidationMode.Structural));
        Assert.Contains("ausente", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void RejectsDuplicateFieldDefinitionIdentifiers()
    {
        var def1 = new ContractFieldDefinition("client_name", "Nome do Cliente", ContractFieldType.ShortText, true);
        var def2 = new ContractFieldDefinition("client_name", "Nome da Empresa", ContractFieldType.ShortText, true);
        var docJson = """{"type":"document","content":[{"type":"paragraph","content":[{"type":"field","fieldId":"client_name"}]}]}""";

        var ex = Assert.Throws<InvalidDataException>(() =>
            StructuredContractDocument.Parse(docJson, [def1, def2]));
        Assert.Contains("únicos", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void DistinctFieldTypesAreRenderedAndIsolatedInOutput()
    {
        var defs = new ContractFieldDefinition[]
        {
            new("therapies", "Terapias", ContractFieldType.Choice, true, ["Neuropsicologia", "Psicologia"]),
            new("session_fee", "Valor da Sessão", ContractFieldType.Currency, true),
            new("start_date", "Data de Início", ContractFieldType.Date, true)
        };

        var docJson = """
            {"type":"document","content":[
                {"type":"heading","level":2,"content":[{"type":"text","text":"Cláusula de Terapias"}]},
                {"type":"paragraph","alignment":"justify","content":[
                    {"type":"text","text":"Terapia: "},{"type":"field","fieldId":"therapies"},
                    {"type":"text","text":", com valor de R$ "},{"type":"field","fieldId":"session_fee"},
                    {"type":"text","text":" a partir de "},{"type":"field","fieldId":"start_date"}
                ]}
            ]}
            """;

        var values = new ContractFieldValue[]
        {
            new("therapies", "Neuropsicologia", true),
            new("session_fee", "250.00", true),
            new("start_date", "2026-10-01", true)
        };

        StructuredContractDocument.ValidateValues(defs, values, ContractValidationMode.Confirmed);
        var html = ContractDocumentRenderer.ToHtml(docJson,
            OfficialContractTemplates.SerializeFields(defs),
            System.Text.Json.JsonSerializer.Serialize(values));

        Assert.Contains("Neuropsicologia", html);
        Assert.Contains("250,00", html);
        Assert.Contains("2026-10-01", html);

        var pdfBytes = ContractDocumentRenderer.ToPdf(docJson,
            OfficialContractTemplates.SerializeFields(defs),
            System.Text.Json.JsonSerializer.Serialize(values),
            "Contrato de Terapias", 1);

        Assert.NotEmpty(pdfBytes);
        var pdfStr = Encoding.Latin1.GetString(pdfBytes);
        Assert.Contains("Neuropsicologia", pdfStr);
        Assert.Contains("250,00", pdfStr);
        Assert.Contains("2026-10-01", pdfStr);
    }
}
