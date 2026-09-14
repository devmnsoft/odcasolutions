using System.Globalization;
using System.IO.Compression;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml;

namespace Odca.Application.Documents;

public enum SupportedDocumentType { Pdf, Png, Jpeg, Docx }

public sealed record ExtractedText(string Text, string Method, int? Pages = null);
public sealed record ExtractionSuggestion(
    string Field, string ExtractedValue, string? NormalizedValue,
    string Evidence, int? Page, string Method, bool IsAmbiguous);

public static partial class DocumentContent
{
    public const long MaximumBytes = 25 * 1024 * 1024;
    public const long MaximumDocxExpandedBytes = 100 * 1024 * 1024;
    public const int MaximumImageDimension = 12_000;

    public static SupportedDocumentType Detect(ReadOnlySpan<byte> prefix, string fileName)
    {
        if (prefix.Length >= 5 && prefix[..5].SequenceEqual("%PDF-"u8)) return SupportedDocumentType.Pdf;
        if (prefix.Length >= 8 && prefix[..8].SequenceEqual(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 })) return SupportedDocumentType.Png;
        if (prefix.Length >= 3 && prefix[0] == 0xff && prefix[1] == 0xd8 && prefix[2] == 0xff) return SupportedDocumentType.Jpeg;
        if (prefix.Length >= 4 && prefix[..4].SequenceEqual("PK\u0003\u0004"u8) && Path.GetExtension(fileName).Equals(".docx", StringComparison.OrdinalIgnoreCase))
            return SupportedDocumentType.Docx;
        throw new InvalidDataException("Formato indisponível. Envie PDF, PNG, JPEG ou DOCX.");
    }

    public static async Task<ExtractedText> ExtractDocxAsync(Stream input, CancellationToken cancellationToken)
    {
        using var archive = new ZipArchive(input, ZipArchiveMode.Read, leaveOpen: true);
        if (archive.Entries.Sum(x => x.Length) > MaximumDocxExpandedBytes)
            throw new InvalidDataException("O conteúdo descompactado excede 100 MB.");
        var document = archive.GetEntry("word/document.xml")
            ?? throw new InvalidDataException("DOCX corrompido: documento principal ausente.");
        await using var stream = document.Open();
        using var reader = XmlReader.Create(stream, new XmlReaderSettings
        {
            Async = true, DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null,
            MaxCharactersInDocument = MaximumDocxExpandedBytes
        });
        var text = new StringBuilder();
        while (await reader.ReadAsync())
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (reader.NodeType == XmlNodeType.Text) text.Append(reader.Value);
            else if (reader.NodeType == XmlNodeType.EndElement && reader.LocalName is "p" or "tr") text.AppendLine();
        }
        return new ExtractedText(text.ToString().Trim(), "docx-structure");
    }

    public static IReadOnlyList<ExtractionSuggestion> Suggest(string text, string method)
    {
        if (string.IsNullOrWhiteSpace(text)) return [];
        var result = new List<ExtractionSuggestion>();
        AddLabel(result, text, TitleRegex(), "title", method, static x => x.Trim(), false);
        AddLabel(result, text, ReferenceRegex(), "reference", method, static x => x.Trim(), false);
        AddLabel(result, text, StartDateRegex(), "start_date", method, NormalizeDate, true);
        AddLabel(result, text, EndDateRegex(), "end_date", method, NormalizeDate, true);
        AddLabel(result, text, NoticeRegex(), "renewal_notice_days", method, static x => int.TryParse(x, out var n) && n is > 0 and <= 3650 ? n.ToString(CultureInfo.InvariantCulture) : null, false);
        AddLabel(result, text, ValueRegex(), "value", method, NormalizeMoney, true);
        foreach (Match match in DocumentRegex().Matches(text))
        {
            var raw = match.Value;
            var normalized = new string(raw.Where(char.IsDigit).ToArray());
            if (normalized.Length is 11 or 14)
                result.Add(new("party_document", raw, normalized, Evidence(text, match.Index, match.Length), null, method, false));
        }
        return result;
    }

    private static void AddLabel(List<ExtractionSuggestion> target, string text, Regex regex, string field, string method, Func<string, string?> normalize, bool ambiguous)
    {
        var matches = regex.Matches(text);
        foreach (Match match in matches.Cast<Match>().Take(3))
        {
            var raw = match.Groups[1].Value.Trim();
            var normalized = normalize(raw);
            target.Add(new(field, raw, normalized, Evidence(text, match.Index, match.Length), null, method, ambiguous || normalized is null || matches.Count > 1));
        }
    }

    private static string Evidence(string text, int index, int length)
    {
        var start = Math.Max(0, index - 50);
        var end = Math.Min(text.Length, index + length + 50);
        return Regex.Replace(text[start..end], @"\s+", " ").Trim();
    }

    private static string? NormalizeDate(string value)
        => DateOnly.TryParseExact(value, ["dd/MM/yyyy", "dd-MM-yyyy", "yyyy-MM-dd"], CultureInfo.InvariantCulture, DateTimeStyles.None, out var date)
            ? date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) : null;

    private static string? NormalizeMoney(string value)
    {
        var cleaned = value.Replace(".", "", StringComparison.Ordinal).Replace(',', '.');
        return decimal.TryParse(cleaned, NumberStyles.Number, CultureInfo.InvariantCulture, out var amount)
            ? amount.ToString("0.00", CultureInfo.InvariantCulture) : null;
    }

    [GeneratedRegex(@"(?im)^\s*(?:t[ií]tulo|objeto)\s*:\s*([^\r\n]{2,160})")]
    private static partial Regex TitleRegex();
    [GeneratedRegex(@"(?im)^\s*(?:refer[eê]ncia|contrato\s+n[ºo°.]*)\s*:\s*([A-Z0-9./_-]{2,80})")]
    private static partial Regex ReferenceRegex();
    [GeneratedRegex(@"(?im)(?:in[ií]cio\s+(?:da\s+)?vig[eê]ncia|vig[eê]ncia\s+inicial)\s*:\s*(\d{2}[/\-]\d{2}[/\-]\d{4}|\d{4}-\d{2}-\d{2})")]
    private static partial Regex StartDateRegex();
    [GeneratedRegex(@"(?im)(?:t[eé]rmino\s+(?:da\s+)?vig[eê]ncia|fim\s+da\s+vig[eê]ncia)\s*:\s*(\d{2}[/\-]\d{2}[/\-]\d{4}|\d{4}-\d{2}-\d{2})")]
    private static partial Regex EndDateRegex();
    [GeneratedRegex(@"(?im)(?:aviso\s+(?:de\s+)?renova[cç][aã]o)\s*:\s*(\d{1,4})\s*dias?")]
    private static partial Regex NoticeRegex();
    [GeneratedRegex(@"(?im)(?:valor\s+(?:total\s+)?do\s+contrato)\s*:\s*(?:R\$\s*)?([0-9.]+(?:,[0-9]{2})?)")]
    private static partial Regex ValueRegex();
    [GeneratedRegex(@"(?<!\d)(?:\d{3}[.]?\d{3}[.]?\d{3}-?\d{2}|\d{2}[.]?\d{3}[.]?\d{3}/?\d{4}-?\d{2})(?!\d)")]
    private static partial Regex DocumentRegex();
}
