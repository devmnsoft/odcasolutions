using Odca.Application.Contracts;

namespace Odca.Domain.Tests;

public sealed class ContractStudioAnalysisTests
{
    private const string Before = """{"type":"document","content":[{"type":"paragraph","content":[{"type":"text","text":"Antes","marks":["bold"]}]}]}""";
    private const string After = """{"type":"document","content":[{"type":"paragraph","content":[{"type":"text","text":"Depois","marks":["italic"]}]}]}""";
    private const string Fields = """[{"id":"party","label":"Parte","type":"ShortText","required":true}]""";

    [Fact]
    public void ComparisonSeparatesTextFormattingFieldsAndMetadata()
    {
        var changes = ContractStudioAnalysis.Compare(Before, Fields, """[{"fieldId":"party","value":"A","confirmed":true}]""",
            After, Fields.Replace("Parte", "Contraparte"), """[{"fieldId":"party","value":"B","confirmed":true}]""");

        Assert.Contains(changes, x => x.Category == "text");
        Assert.Contains(changes, x => x.Category == "formatting");
        Assert.Contains(changes, x => x.Category == "field" && x.Reference == "field:party");
        Assert.Contains(changes, x => x.Category == "metadata" && x.Reference == "metadata:party");
    }

    [Fact]
    public void ChecklistBlocksUnsavedInvalidDraftButOpenCommentsAreWarnings()
    {
        var items = ContractStudioAnalysis.Checklist(Before, Fields, "[]", false, true, 2);

        Assert.Contains(items, x => x.Code == "draft.unsaved" && x.Severity == "blocker");
        Assert.Contains(items, x => x.Code == "draft.conflict" && x.Severity == "blocker");
        Assert.Contains(items, x => x.Code == "fields.invalid" && x.Severity == "blocker");
        Assert.Contains(items, x => x.Code == "comments.open" && x.Severity == "warning");
    }

    [Fact]
    public void ComparisonRejectsOversizedSynchronousDocuments()
    {
        var oversized = new string('x', ContractStudioAnalysis.MaximumSnapshotBytes + 1);
        Assert.Throws<InvalidDataException>(() => ContractStudioAnalysis.Compare(oversized, "[]", "[]", After, "[]", "[]"));
    }
}
