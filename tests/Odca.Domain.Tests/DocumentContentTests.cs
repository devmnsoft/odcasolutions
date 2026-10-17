using System.IO.Compression;
using System.Text;
using Odca.Application.Documents;

namespace Odca.Domain.Tests;

public sealed class DocumentContentTests
{
    [Fact]
    public void ClassifiesPageSegmentsAndSplicesWithoutShiftingPageAttribution()
    {
        var text = string.Join("\f", ["curta", "Título: Contrato de Teste ABC", "fora de alcance"]);
        var segments = DocumentContent.SplitPageSegments(text);
        Assert.Equal(3, segments.Count);
        Assert.False(DocumentContent.PageHasUsableText(segments[0]));
        Assert.True(DocumentContent.PageHasUsableText(segments[1]));
        Assert.False(DocumentContent.PageHasUsableText(segments[2]));

        var spliced = DocumentContent.SplicePageSegments(segments, 1, "Título: Contrato Digitalizado XYZ");
        var resplit = DocumentContent.SplitPageSegments(spliced);
        Assert.Equal(3, resplit.Count);
        Assert.Contains("Contrato de Teste ABC", resplit[1]);

        // A paginação das sugestoes (1-based, por form feed) sobrevive a costura: o titulo da
        // pagina OCRizada aponta para a pagina 1 e o original continua na pagina 2.
        var suggestions = DocumentContent.Suggest(spliced, "pdf-mixed");
        var titles = suggestions.Where(s => s.Field == "title").ToList();
        Assert.Contains(titles, s => s.ExtractedValue == "Contrato Digitalizado XYZ" && s.Page == 1);
        Assert.Contains(titles, s => s.ExtractedValue == "Contrato de Teste ABC" && s.Page == 2);
    }

    [Fact]
    public void ExposesHonestPageScopeInExtractionResult()
    {
        var docx = new ExtractedText("texto", "docx-structure");
        Assert.Null(docx.PagesTotal);
        Assert.Null(docx.PagesProcessed);
        Assert.False(docx.Truncated);
        Assert.Equal(0, docx.OcrPages);

        var scanned = new ExtractedText("a\fb", "ocr", 25, 20, true, 20);
        Assert.Equal(25, scanned.PagesTotal);
        Assert.Equal(20, scanned.PagesProcessed);
        Assert.True(scanned.Truncated);
        Assert.Equal(20, scanned.OcrPages);
    }

    [Fact]
    public void RequiresMatchingContentSignatureAndExtension()
    {
        Assert.Equal(SupportedDocumentType.Pdf, DocumentContent.Detect("%PDF-1.7"u8, "contrato.pdf"));
        Assert.Throws<InvalidDataException>(() => DocumentContent.Detect("%PDF-1.7"u8, "malicioso.exe"));
        Assert.Throws<InvalidDataException>(() => DocumentContent.Detect("texto"u8, "contrato.pdf"));
    }

    [Fact]
    public async Task RejectsPngAbovePixelLimit()
    {
        var png = new byte[24];
        new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 }.CopyTo(png, 0);
        "IHDR"u8.CopyTo(png.AsSpan(12));
        System.Buffers.Binary.BinaryPrimitives.WriteInt32BigEndian(png.AsSpan(16, 4), DocumentContent.MaximumImageDimension + 1);
        System.Buffers.Binary.BinaryPrimitives.WriteInt32BigEndian(png.AsSpan(20, 4), 100);
        await Assert.ThrowsAsync<InvalidDataException>(() => DocumentContent.ValidateImageDimensionsAsync(new MemoryStream(png), SupportedDocumentType.Png, default));
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

    // D-OC33 B: texto com separador de pagina (\f) recebe page_number 1-based por segmento.
    [Fact]
    public void AttributesPageNumbersOnlyWhenTheExtractedTextHasFormFeeds()
    {
        var page1 = "Título: Contrato de Prestação de Serviços\nReferência: CT-2026-042\nInício da vigência: 15/10/2026";
        var page2 = "Termino da vigência: 14/10/2027\nValor total do contrato: R$ 12.345,67";
        var result = DocumentContent.Suggest(page1 + "\f" + page2, "ocr");
        Assert.Contains(result, x => x.Field == "title" && x.Page == 1);
        Assert.Contains(result, x => x.Field == "reference" && x.ExtractedValue == "CT-2026-042" && x.Page == 1);
        Assert.Contains(result, x => x.Field == "start_date" && x.NormalizedValue == "2026-10-15" && x.Page == 1);
        Assert.Contains(result, x => x.Field == "end_date" && x.NormalizedValue == "2027-10-14" && x.Page == 2);
        Assert.Contains(result, x => x.Field == "value" && x.NormalizedValue == "12345.67" && x.Page == 2 && x.IsAmbiguous);
        Assert.All(result, x => Assert.NotNull(x.Page));
    }

    [Fact]
    public void SinglePageTextKeepsPageUnset()
    {
        var result = DocumentContent.Suggest("Título: Serviço sintético\nInício da vigência: 14/09/2026", "pdf-native");
        Assert.NotEmpty(result);
        Assert.All(result, x => Assert.Null(x.Page));
    }

    [Fact]
    public void BlankPageSegmentsAreCountedWithoutCreatingGhostSuggestions()
    {
        // Separador final e pagina em branco no meio: a numeracao segue a ordem real das paginas.
        var result = DocumentContent.Suggest(
            "Início da vigência: 01/01/2026\f\fTermino da vigência: 02/02/2026\f", "ocr");
        Assert.Contains(result, x => x.Field == "start_date" && x.Page == 1);
        Assert.Contains(result, x => x.Field == "end_date" && x.Page == 3);
        Assert.Equal(2, result.Count);
    }

    // D-OC33 C: limiar honesto para considerar camada de texto no PDF.
    [Fact]
    public void PdfTextLayerThresholdDiscardsWhitespaceOnlyOutput()
    {
        Assert.False(DocumentContent.PdfHasUsableTextLayer(null!));
        Assert.False(DocumentContent.PdfHasUsableTextLayer(""));
        Assert.False(DocumentContent.PdfHasUsableTextLayer("   "));
        Assert.False(DocumentContent.PdfHasUsableTextLayer(new string('a', DocumentContent.MinimumPdfTextLayerChars - 1)));
        Assert.True(DocumentContent.PdfHasUsableTextLayer(new string('a', DocumentContent.MinimumPdfTextLayerChars)));
        Assert.True(DocumentContent.PdfHasUsableTextLayer("   " + new string('b', DocumentContent.MinimumPdfTextLayerChars) + "  "));
    }
}
