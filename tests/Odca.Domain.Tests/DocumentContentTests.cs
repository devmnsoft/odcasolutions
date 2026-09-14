using System.IO.Compression;
using System.Text;
using Odca.Application.Documents;

namespace Odca.Domain.Tests;

public sealed class DocumentContentTests
{
    [Fact]
    public void DetectsContentInsteadOfTrustingExtension()
    {
        Assert.Equal(SupportedDocumentType.Pdf, DocumentContent.Detect("%PDF-1.7"u8, "malicioso.exe"));
        Assert.Throws<InvalidDataException>(() => DocumentContent.Detect("texto"u8, "contrato.pdf"));
    }

    [Fact]
    public async Task ExtractsRealDocxXmlWithoutExecutingEmbeddedContent()
    {
        await using var buffer = new MemoryStream();
        using (var archive = new ZipArchive(buffer, ZipArchiveMode.Create, true))
        {
            var entry = archive.CreateEntry("word/document.xml");
            await using var output = entry.Open();
            await output.WriteAsync(Encoding.UTF8.GetBytes("<w:document xmlns:w=\"x\"><w:body><w:p><w:r><w:t>Título: Contrato sintético</w:t></w:r></w:p></w:body></w:document>"));
        }
        buffer.Position = 0;
        var result = await DocumentContent.ExtractDocxAsync(buffer, default);
        Assert.Contains("Contrato sintético", result.Text);
        Assert.Equal("docx-structure", result.Method);
    }

    [Fact]
    public void SuggestionsAreEvidenceBasedAndAmbiguityIsExplicit()
    {
        var result = DocumentContent.Suggest("Título: Serviço sintético\nInício da vigência: 14/09/2026\nValor total do contrato: R$ 1.234,56", "pdf-native");
        Assert.Contains(result, x => x.Field == "title" && x.ExtractedValue == "Serviço sintético");
        Assert.Contains(result, x => x.Field == "start_date" && x.NormalizedValue == "2026-09-14" && x.IsAmbiguous);
        Assert.Contains(result, x => x.Field == "value" && x.NormalizedValue == "1234.56" && x.IsAmbiguous);
        Assert.DoesNotContain(result, x => x.Field == "end_date");
    }
}
