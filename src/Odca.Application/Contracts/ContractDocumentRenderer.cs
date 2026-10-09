using System.Globalization;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Odca.Application.Contracts;

/// <summary>Creates safe derived representations of the validated canonical document.</summary>
public static class ContractDocumentRenderer
{
    public const string PdfRendererVersion = "odca-pdf-1";

    /// <summary>Section D (D2): organization metadata used to build the cover page and the
    /// table of contents. They are emitted only when the document is "adequate" for them
    /// (at least two section headings of level 1, any level-2 section, or an explicit page
    /// break); short one-title terms stay without cover.</summary>
    public sealed record DocumentCoverInfo(
        string OrganizationName,
        string? OrganizationTaxId,
        string Title,
        int Version,
        DateTimeOffset IssuedAt,
        string? Author);

    public static string ToHtml(string contentJson, string fieldsJson, string valuesJson, DocumentCoverInfo? cover = null)
    {
        var model = Read(contentJson, fieldsJson, valuesJson);
        var html = new StringBuilder("<article class=\"legal-document\">");
        if (cover is not null && HasFrontMatter(model.Root))
        {
            RenderCoverHtml(cover, html);
            RenderTocHtml(model, html);
        }
        RenderHtml(model.Root, model.Values, model.Definitions, html);
        return html.Append("</article>").ToString();
    }

    public static byte[] ToPdf(string contentJson, string fieldsJson, string valuesJson, string title, int version, DocumentCoverInfo? cover = null)
    {
        var model = Read(contentJson, fieldsJson, valuesJson);
        var lines = new List<string>();
        if (cover is not null && HasFrontMatter(model.Root))
        {
            for (var blank = 0; blank < 8; blank++) lines.Add(string.Empty);
            lines.Add(cover.OrganizationName.ToUpper(CultureInfo.CurrentCulture));
            if (!string.IsNullOrWhiteSpace(cover.OrganizationTaxId)) lines.Add("CNPJ " + FormatTaxId(cover.OrganizationTaxId));
            lines.Add(string.Empty); lines.Add(string.Empty);
            lines.Add(cover.Title.ToUpper(CultureInfo.CurrentCulture));
            lines.Add($"Versao {cover.Version} - Emitido em {cover.IssuedAt.ToOffset(TimeSpan.FromHours(-3)).ToString("dd/MM/yyyy", CultureInfo.InvariantCulture)}");
            if (!string.IsNullOrWhiteSpace(cover.Author)) lines.Add("Emitido por " + cover.Author);
            lines.Add("\f");
            lines.Add("SUMARIO");
            foreach (var entry in CollectTocEntries(model)) lines.Add("  " + entry);
            lines.Add("\f");
        }
        lines.Add(title); lines.Add($"Versão {version}");
        Flatten(model.Root, model.Values, model.Definitions, lines, 0);
        return SimplePdf.Create(lines);
    }

    private static bool HasFrontMatter(JsonElement root)
    {
        var levelOneHeadings = 0;
        if (!root.TryGetProperty("content", out var blocks) || blocks.ValueKind != JsonValueKind.Array) return false;
        foreach (var block in blocks.EnumerateArray())
        {
            var type = block.GetProperty("type").GetString();
            if (type == "pageBreak") return true;
            if (type != "heading") continue;
            var level = block.GetProperty("level").GetInt32();
            if (level == 2) return true;
            if (level == 1) levelOneHeadings++;
        }
        return levelOneHeadings >= 2;
    }

    private static void RenderCoverHtml(DocumentCoverInfo cover, StringBuilder output)
    {
        output.Append("<header class=\"document-cover\"><p class=\"document-cover-organization\">")
            .Append(SafeHtmlEncode(cover.OrganizationName)).Append("</p>");
        if (!string.IsNullOrWhiteSpace(cover.OrganizationTaxId))
            output.Append("<p class=\"document-cover-tax-id\">CNPJ ").Append(SafeHtmlEncode(FormatTaxId(cover.OrganizationTaxId))).Append("</p>");
        output.Append("<h1 class=\"document-cover-title\">").Append(SafeHtmlEncode(cover.Title)).Append("</h1>")
            .Append("<p class=\"document-cover-meta\">Versão ").Append(cover.Version)
            .Append(" &middot; Emitido em ").Append(cover.IssuedAt.ToOffset(TimeSpan.FromHours(-3)).ToString("dd/MM/yyyy", CultureInfo.InvariantCulture)).Append("</p>");
        if (!string.IsNullOrWhiteSpace(cover.Author))
            output.Append("<p class=\"document-cover-meta\">Emitido por ").Append(SafeHtmlEncode(cover.Author)).Append("</p>");
        output.Append("</header><hr class=\"document-page-break\" aria-label=\"Fim da capa\">");
    }

    private static void RenderTocHtml(RenderModel model, StringBuilder output)
    {
        output.Append("<nav class=\"document-toc\" aria-label=\"Sumário\"><h2 class=\"document-toc-title\">Sumário</h2><ol class=\"document-toc-list\">");
        foreach (var entry in CollectTocEntries(model))
            output.Append("<li class=\"document-toc-item\">").Append(SafeHtmlEncode(entry)).Append("</li>");
        output.Append("</ol></nav><hr class=\"document-page-break\" aria-label=\"Fim do sumário\">");
    }

    private static List<string> CollectTocEntries(RenderModel model)
    {
        var entries = new List<string>();
        if (!model.Root.TryGetProperty("content", out var blocks) || blocks.ValueKind != JsonValueKind.Array) return entries;
        foreach (var block in blocks.EnumerateArray())
            if (block.GetProperty("type").GetString() == "heading")
            {
                var line = new StringBuilder();
                CollectText(block, model.Values, model.Definitions, line);
                var text = line.ToString().Trim();
                if (text.Length > 0) entries.Add(text.TrimEnd('.', ':'));
            }
        return entries;
    }

    private static string FormatTaxId(string value)
    {
        var digits = new string(value.Where(char.IsDigit).ToArray());
        return digits.Length == 14
            ? $"{digits[..2]}.{digits[2..5]}.{digits[5..8]}/{digits[8..12]}-{digits[12..]}"
            : digits.Length == 11
                ? $"{digits[..3]}.{digits[3..6]}.{digits[6..9]}-{digits[9..]}"
                : value;
    }

    private static RenderModel Read(string contentJson, string fieldsJson, string valuesJson)
    {
        var options=new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
        options.Converters.Add(new JsonStringEnumConverter());
        var definitions = JsonSerializer.Deserialize<ContractFieldDefinition[]>(fieldsJson, options) ?? [];
        var values = JsonSerializer.Deserialize<ContractFieldValue[]>(valuesJson,
            options) ?? [];
        StructuredContractDocument.Parse(contentJson, definitions);
        StructuredContractDocument.ValidateValues(definitions, values, ContractValidationMode.Complete);
        var valueMap = values.ToDictionary(x => x.FieldId, x => x.Value, StringComparer.Ordinal);
        var definitionMap = definitions.ToDictionary(x => x.Id, StringComparer.Ordinal);
        using var parsed = JsonDocument.Parse(contentJson, new JsonDocumentOptions { MaxDepth = 64 });
        return new(parsed.RootElement.Clone(), valueMap, definitionMap);
    }

    private static string Display(string fieldId, IReadOnlyDictionary<string, string?> values, IReadOnlyDictionary<string, ContractFieldDefinition> definitions)
    {
        var value = values.GetValueOrDefault(fieldId);
        // Documentos finais nunca exibem variáveis não resolvidas: campos opcionais legitimamente
        // vazios (ex.: representante legal "quando houver") saem como traço em branco, sem marcador.
        if (string.IsNullOrWhiteSpace(value)) return "\u2014";
        if (definitions.TryGetValue(fieldId, out var definition) && definition.Type is ContractFieldType.Currency or ContractFieldType.Formula && CanonicalDecimal.TryFormatPtBr(value, out var formatted))
            return formatted;
        return value;
    }

    private static void RenderHtml(JsonElement node, IReadOnlyDictionary<string, string?> values, IReadOnlyDictionary<string, ContractFieldDefinition> definitions, StringBuilder output)
    {
        var type = node.GetProperty("type").GetString();
        if (type == "text") { output.Append(SafeHtmlEncode(node.GetProperty("text").GetString())); return; }
        if (type == "field")
        {
            var id = node.GetProperty("fieldId").GetString()!;
            var shown = Display(id, values, definitions);
            output.Append("<span class=\"document-field\">")
                .Append(SafeHtmlEncode(shown)).Append("</span>");
            return;
        }
        if (type == "pageBreak") { output.Append("<hr class=\"document-page-break\" aria-label=\"Quebra de página\">"); return; }
        if (type == "callout")
        {
            var variant = node.TryGetProperty("variant", out var variantValue) && variantValue.GetString() == "info" ? "info" : "attention";
            output.Append("<aside class=\"document-callout document-callout--").Append(variant)
                .Append("\"><p class=\"document-callout-label\">").Append(variant == "info" ? "INFORMAÇÃO:" : "ATENÇÃO:").Append("</p>");
            if (TryGetChildren(node, out var calloutChildren)) foreach (var calloutChild in calloutChildren) RenderHtml(calloutChild, values, definitions, output);
            output.Append("</aside>");
            return;
        }
        var tag = type switch { "document" => "div", "heading" => $"h{node.GetProperty("level").GetInt32()}", "paragraph" => "p", "bulletList" => "ul", "orderedList" => "ol", "listItem" => "li", "table" => "table", "tableRow" => "tr", "tableCell" => "td", _ => "div" };
        output.Append('<').Append(tag);
        if (type == "paragraph" && node.TryGetProperty("alignment", out var alignment))
            output.Append(" class=\"align-").Append(alignment.GetString()).Append('"');
        output.Append('>');
        if (TryGetChildren(node, out var children)) foreach (var child in children)
        {
            if (child.GetProperty("type").GetString() == "text" && child.TryGetProperty("marks", out var marks))
            {
                var markNames = marks.EnumerateArray().Select(x => x.GetString()).ToHashSet(StringComparer.Ordinal);
                if (markNames.Contains("bold")) output.Append("<strong>");
                if (markNames.Contains("italic")) output.Append("<em>");
                if (markNames.Contains("underline")) output.Append("<u>");
                RenderHtml(child, values, definitions, output);
                if (markNames.Contains("underline")) output.Append("</u>");
                if (markNames.Contains("italic")) output.Append("</em>");
                if (markNames.Contains("bold")) output.Append("</strong>");
            }
            else RenderHtml(child, values, definitions, output);
        }
        output.Append("</").Append(tag).Append('>');
    }

    private static void Flatten(JsonElement node, IReadOnlyDictionary<string, string?> values, IReadOnlyDictionary<string, ContractFieldDefinition> definitions, List<string> lines, int depth)
    {
        var type = node.GetProperty("type").GetString();
        if (type == "pageBreak") { lines.Add("\f"); return; }
        if (type == "callout")
        {
            lines.Add(node.TryGetProperty("variant", out var calloutVariant) && calloutVariant.GetString() == "info" ? "INFORMAÇÃO:" : "ATENÇÃO:");
            if (TryGetChildren(node, out var calloutChildren)) foreach (var calloutChild in calloutChildren) Flatten(calloutChild, values, definitions, lines, depth + 1);
            return;
        }
        if (type is "paragraph" or "heading" or "listItem" or "tableRow")
        {
            var line = new StringBuilder(type == "listItem" ? "• " : "");
            CollectText(node, values, definitions, line);
            lines.Add(line.ToString());
            return;
        }
        if (TryGetChildren(node, out var children)) foreach (var child in children) Flatten(child, values, definitions, lines, depth + 1);
    }

    private static void CollectText(JsonElement node, IReadOnlyDictionary<string, string?> values, IReadOnlyDictionary<string, ContractFieldDefinition> definitions, StringBuilder line)
    {
        var type = node.GetProperty("type").GetString();
        if (type == "text") line.Append(node.GetProperty("text").GetString());
        else if (type == "field") line.Append(Display(node.GetProperty("fieldId").GetString()!, values, definitions));
        else if (TryGetChildren(node, out var children)) foreach (var child in children) CollectText(child, values, definitions, line);
        if (type == "tableCell") line.Append("  |  ");
    }

    private static string SafeHtmlEncode(string? text)
    {
        if (string.IsNullOrEmpty(text)) return string.Empty;
        return text.Replace("&", "&amp;", StringComparison.Ordinal)
            .Replace("<", "&lt;", StringComparison.Ordinal)
            .Replace(">", "&gt;", StringComparison.Ordinal)
            .Replace("\"", "&quot;", StringComparison.Ordinal)
            .Replace("'", "&#39;", StringComparison.Ordinal);
    }

    private static bool TryGetChildren(JsonElement node, out JsonElement.ArrayEnumerator children)
    {
        if (!node.TryGetProperty("content", out var content))
        {
            children = default;
            return false;
        }
        if (content.ValueKind != JsonValueKind.Array)
            throw new InvalidDataException("O conteúdo do elemento deve ser uma lista.");
        children = content.EnumerateArray();
        return true;
    }

    private sealed record RenderModel(JsonElement Root, IReadOnlyDictionary<string, string?> Values, IReadOnlyDictionary<string, ContractFieldDefinition> Definitions);

    private static class SimplePdf
    {
        public static byte[] Create(IEnumerable<string> source)
        {
            var pages = new List<List<string>>
            {
                new List<string>()
            };
            foreach (var raw in source)
            {
                if (raw == "\f") { if (pages[^1].Count > 0) pages.Add(new List<string>()); continue; }
                foreach (var line in Wrap(raw, 92)) { if (pages[^1].Count >= 52) pages.Add(new List<string>()); pages[^1].Add(line); }
            }
            var objects = new List<byte[]>();
            objects.Add(Ascii("<< /Type /Catalog /Pages 2 0 R >>"));
            var pageIds = Enumerable.Range(0, pages.Count).Select(i => 4 + i * 2).ToArray();
            objects.Add(Ascii(FormattableString.Invariant($"<< /Type /Pages /Kids [{string.Join(' ', pageIds.Select(x => FormattableString.Invariant($"{x} 0 R")))}] /Count {pages.Count} >>")));
            objects.Add(Ascii("<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica /Encoding /WinAnsiEncoding >>"));
            for (var p = 0; p < pages.Count; p++)
            {
                var content = new StringBuilder("BT /F1 10 Tf 54 790 Td 14 TL ");
                foreach (var line in pages[p]) content.Append('(').Append(Escape(line)).Append(") Tj T* ");
                content.Append(FormattableString.Invariant($"ET BT /F1 9 Tf 285 28 Td (Página {p + 1} de {pages.Count}) Tj ET"));
                var bytes = Encoding.Latin1.GetBytes(content.ToString());
                objects.Add(Ascii(FormattableString.Invariant($"<< /Type /Page /Parent 2 0 R /MediaBox [0 0 595 842] /Resources << /Font << /F1 3 0 R >> >> /Contents {pageIds[p] + 1} 0 R >>")));
                objects.Add(Concat(Ascii(FormattableString.Invariant($"<< /Length {bytes.Length} >>\nstream\n")), bytes, Ascii("\nendstream")));
            }
            using var output = new MemoryStream(); output.Write(Ascii("%PDF-1.4\n%âãÏÓ\n")); var offsets = new List<long> { 0 };
            for (var i = 0; i < objects.Count; i++) { offsets.Add(output.Position); output.Write(Ascii(FormattableString.Invariant($"{i + 1} 0 obj\n"))); output.Write(objects[i]); output.Write(Ascii("\nendobj\n")); }
            var xref = output.Position; output.Write(Ascii(FormattableString.Invariant($"xref\n0 {objects.Count + 1}\n0000000000 65535 f \n")));
            foreach (var offset in offsets.Skip(1)) output.Write(Ascii(offset.ToString("0000000000", CultureInfo.InvariantCulture) + " 00000 n \n"));
            output.Write(Ascii(FormattableString.Invariant($"trailer << /Size {objects.Count + 1} /Root 1 0 R >>\nstartxref\n{xref}\n%%EOF"))); return output.ToArray();
        }
        private static List<string> Wrap(string text, int width) { if (text.Length == 0) return new List<string> { string.Empty }; var words=text.Split(' '); var result=new List<string>(); var line=""; foreach(var word in words){if(line.Length>0&&line.Length+word.Length+1>width){result.Add(line);line=word;}else line+=line.Length==0?word:" "+word;} result.Add(line);return result; }
        private static string Escape(string text) => text.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("(", "\\(", StringComparison.Ordinal).Replace(")", "\\)", StringComparison.Ordinal).Replace("\r", " ", StringComparison.Ordinal).Replace("\n", " ", StringComparison.Ordinal);
        private static byte[] Ascii(string value) => Encoding.Latin1.GetBytes(value);
        private static byte[] Concat(params byte[][] arrays) { var result=new byte[arrays.Sum(x=>x.Length)];var offset=0;foreach(var array in arrays){Buffer.BlockCopy(array,0,result,offset,array.Length);offset+=array.Length;}return result; }
    }
}
