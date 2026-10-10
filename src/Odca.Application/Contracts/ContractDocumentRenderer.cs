using System.Globalization;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Odca.Application.Contracts;

/// <summary>Creates safe derived representations of the validated canonical document.</summary>
public static class ContractDocumentRenderer
{
    public const string PdfRendererVersion = "odca-pdf-2";

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
        var lines = new List<SimplePdf.Line>();
        // A capa só existe quando o documento é adequado a ela (HasFrontMatter); as
        // metadados de data do /Info seguem o mesmo critério (D-A4).
        var hasCover = cover is not null && HasFrontMatter(model.Root);
        if (hasCover)
        {
            var c = cover!;
            for (var blank = 0; blank < 8; blank++) lines.Add(SimplePdf.Line.Blank());
            // D-C4: maiúsculas sempre com cultura invariante (estável para pt-BR).
            lines.Add(new SimplePdf.Line(c.OrganizationName.ToUpperInvariant(), SimplePdf.Style.Org));
            if (!string.IsNullOrWhiteSpace(c.OrganizationTaxId)) lines.Add(new SimplePdf.Line("CNPJ " + FormatTaxId(c.OrganizationTaxId), SimplePdf.Style.MetaCentered));
            lines.Add(SimplePdf.Line.Blank()); lines.Add(SimplePdf.Line.Blank());
            lines.Add(new SimplePdf.Line(c.Title.ToUpperInvariant(), SimplePdf.Style.Title));
            lines.Add(new SimplePdf.Line($"Versao {c.Version} - Emitido em {c.IssuedAt.ToOffset(TimeSpan.FromHours(-3)).ToString("dd/MM/yyyy", CultureInfo.InvariantCulture)}", SimplePdf.Style.MetaCentered));
            if (!string.IsNullOrWhiteSpace(c.Author)) lines.Add(new SimplePdf.Line("Emitido por " + c.Author, SimplePdf.Style.MetaCentered));
            lines.Add(SimplePdf.Line.PageBreak());
            lines.Add(new SimplePdf.Line("SUMARIO", SimplePdf.Style.TocTitle));
            foreach (var entry in CollectTocEntries(model)) lines.Add(new SimplePdf.Line("  " + entry, SimplePdf.Style.Normal));
            lines.Add(SimplePdf.Line.PageBreak());
        }
        lines.Add(new SimplePdf.Line(title, SimplePdf.Style.DocTitle));
        lines.Add(new SimplePdf.Line($"Versão {version}", SimplePdf.Style.MetaLeft));
        Flatten(model.Root, model.Values, model.Definitions, lines, 0);
        return SimplePdf.Create(lines, title, hasCover ? cover!.IssuedAt : null);
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

    private static void Flatten(JsonElement node, IReadOnlyDictionary<string, string?> values, IReadOnlyDictionary<string, ContractFieldDefinition> definitions, List<SimplePdf.Line> lines, int depth, bool inCallout = false)
    {
        var type = node.GetProperty("type").GetString();
        if (type == "pageBreak") { lines.Add(SimplePdf.Line.PageBreak()); return; }
        if (type == "callout")
        {
            var label = node.TryGetProperty("variant", out var calloutVariant) && calloutVariant.GetString() == "info" ? "INFORMAÇÃO:" : "ATENÇÃO:";
            lines.Add(new SimplePdf.Line(label, SimplePdf.Style.CalloutLabel, true));
            if (TryGetChildren(node, out var calloutChildren)) foreach (var calloutChild in calloutChildren) Flatten(calloutChild, values, definitions, lines, depth + 1, true);
            return;
        }
        if (type is "paragraph" or "heading" or "listItem" or "tableRow")
        {
            var line = new StringBuilder(type == "listItem" ? "\u2022 " : "");
            CollectText(node, values, definitions, line);
            var style = inCallout
                ? SimplePdf.Style.CalloutBody
                : type switch
                {
                    "heading" => SimplePdf.Style.Heading,
                    "listItem" => SimplePdf.Style.Bullet,
                    _ => SimplePdf.Style.Normal
                };
            lines.Add(new SimplePdf.Line(line.ToString(), style, inCallout));
            return;
        }
        if (TryGetChildren(node, out var children)) foreach (var child in children) Flatten(child, values, definitions, lines, depth + 1, inCallout);
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

    /// <summary>
    /// Renderer PDF mínimo e determinístico. Texto codificado em WinAnsi/CP1252
    /// (mesmo espaço do /Encoding /WinAnsiEncoding da fonte): acentos, travessão
    /// (U+2014) e bullet (U+2022) sobrevivem como bytes selecionáveis.
    /// </summary>
    private static class SimplePdf
    {
        private const int PageWidth = 595;
        private const int MarginX = 54;
        private const int TopY = 790;
        private const int Leading = 14;
        private const int MaxLinesPerPage = 52;

        public enum Style { PageBreak, Blank, Normal, Bullet, Heading, DocTitle, MetaLeft, MetaCentered, Org, Title, TocTitle, CalloutLabel, CalloutBody }

        public sealed record Line(string Text, Style Style, bool InCallout = false)
        {
            public static Line Blank() => new("", Style.Blank);
            public static Line PageBreak() => new("", Style.PageBreak);
        }

        // Codificação WinAnsi/CP1252 embutida (sem provider de codepages, determinística):
        // ASCII e Latin-1 em identidade; os símbolos CP1252 da faixa 0x80–0x9F por mapa;
        // o restante cai para '?' selecionável.
        private static readonly Dictionary<char, byte> WinAnsiSpecial = new()
        {
            ['\u20AC'] = 0x80, ['\u201A'] = 0x82, ['\u0192'] = 0x83, ['\u201E'] = 0x84,
            ['\u2026'] = 0x85, ['\u2020'] = 0x86, ['\u2021'] = 0x87, ['\u02C6'] = 0x88,
            ['\u2030'] = 0x89, ['\u0160'] = 0x8A, ['\u2039'] = 0x8B, ['\u0152'] = 0x8C,
            ['\u017D'] = 0x8E, ['\u2018'] = 0x91, ['\u2019'] = 0x92, ['\u201C'] = 0x93,
            ['\u201D'] = 0x94, ['\u2022'] = 0x95, ['\u2013'] = 0x96, ['\u2014'] = 0x97,
            ['\u02DC'] = 0x98, ['\u2122'] = 0x99, ['\u0161'] = 0x9A, ['\u203A'] = 0x9B,
            ['\u0153'] = 0x9C, ['\u017E'] = 0x9E, ['\u0178'] = 0x9F,
        };

        public static byte[] WinAnsiEncode(string value)
        {
            var bytes = new byte[value.Length];
            for (var i = 0; i < value.Length; i++)
            {
                var ch = value[i];
                if (ch < 0x80) bytes[i] = (byte)ch;
                else if (ch >= 0xA0 && ch <= 0xFF) bytes[i] = (byte)ch;
                else bytes[i] = WinAnsiSpecial.TryGetValue(ch, out var b) ? b : (byte)'?';
            }
            return bytes;
        }

        public static byte[] Create(IReadOnlyList<Line> source, string title, DateTimeOffset? issuedAt)
        {
            var pages = new List<List<Line>>
            {
                new()
            };
            foreach (var logical in source)
            {
                if (logical.Style == Style.PageBreak)
                {
                    if (pages[^1].Count > 0) pages.Add([]);
                    continue;
                }
                var (_, size, _) = Metrics(logical.Style);
                foreach (var wrapped in Wrap(logical.Text, size, Indent(logical.Style)))
                {
                    if (pages[^1].Count >= MaxLinesPerPage) pages.Add([]);
                    pages[^1].Add(new Line(wrapped, logical.Style, logical.InCallout));
                }
            }
            var objects = new List<byte[]>();
            objects.Add(ToBytes("<< /Type /Catalog /Pages 2 0 R >>"));
            var firstPageId = 5;
            var pageIds = Enumerable.Range(0, pages.Count).Select(i => firstPageId + i * 2).ToArray();
            objects.Add(ToBytes(FormattableString.Invariant($"<< /Type /Pages /Kids [{string.Join(' ', pageIds.Select(x => FormattableString.Invariant($"{x} 0 R")))}] /Count {pages.Count} >>")));
            objects.Add(ToBytes("<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica /Encoding /WinAnsiEncoding >>"));
            objects.Add(ToBytes("<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica-Bold /Encoding /WinAnsiEncoding >>"));
            for (var p = 0; p < pages.Count; p++)
            {
                var bytes = BuildPageContent(pages[p], p + 1, pages.Count);
                objects.Add(ToBytes(FormattableString.Invariant($"<< /Type /Page /Parent 2 0 R /MediaBox [0 0 595 842] /Resources << /Font << /F1 3 0 R /F2 4 0 R >> >> /Contents {pageIds[p] + 1} 0 R >>")));
                objects.Add(Concat(ToBytes(FormattableString.Invariant($"<< /Length {bytes.Length} >>\nstream\n")), bytes, ToBytes("\nendstream")));
            }
            // D-A4: metadados vêm do snapshot da versão; sem capa não há data estável,
            // e o artefato continua determinístico para o mesmo documento.
            var info = new StringBuilder("<< /Title (").Append(Escape(title)).Append(") /Producer (ODCA Solutions (").Append(PdfRendererVersion).Append(")) /Creator (ODCA Studio)");
            if (issuedAt is DateTimeOffset versionDate)
            {
                var date = versionDate.ToUniversalTime().ToString("yyyyMMddHHmmss", CultureInfo.InvariantCulture);
                info.Append(" /CreationDate (D:").Append(date).Append("Z) /ModDate (D:").Append(date).Append("Z)");
            }
            info.Append(" >>");
            objects.Add(ToBytes(info.ToString()));
            using var output = new MemoryStream(); output.Write(ToBytes("%PDF-1.4\n%\u00E2\u00E3\u00CF\u00D3\n")); var offsets = new List<long> { 0 };
            for (var i = 0; i < objects.Count; i++) { offsets.Add(output.Position); output.Write(ToBytes(FormattableString.Invariant($"{i + 1} 0 obj\n"))); output.Write(objects[i]); output.Write(ToBytes("\nendobj\n")); }
            var xref = output.Position; output.Write(ToBytes(FormattableString.Invariant($"xref\n0 {objects.Count + 1}\n0000000000 65535 f \n")));
            foreach (var offset in offsets.Skip(1)) output.Write(ToBytes(offset.ToString("0000000000", CultureInfo.InvariantCulture) + " 00000 n \n"));
            output.Write(ToBytes(FormattableString.Invariant($"trailer << /Size {objects.Count + 1} /Root 1 0 R /Info {objects.Count} 0 R >>\nstartxref\n{xref}\n%%EOF"))); return output.ToArray();
        }

        private static byte[] BuildPageContent(List<Line> page, int pageNumber, int pageCount)
        {
            var sb = new StringBuilder();
            // Caixa do callout legal: fundo claro + barra escura à esquerda, antes do texto.
            for (var i = 0; i < page.Count; i++)
            {
                if (page[i].Style != Style.CalloutLabel) continue;
                var end = i;
                while (end + 1 < page.Count && page[end + 1].InCallout) end++;
                var topY = TopY - i * Leading + 5;
                var bottomY = TopY - end * Leading - 10;
                sb.Append(FormattableString.Invariant($"0.94 g\n{MarginX - 8} {bottomY} {PageWidth - 2 * MarginX + 16} {topY - bottomY} re f\n"));
                sb.Append(FormattableString.Invariant($"0.55 g\n{MarginX - 12} {bottomY} 4 {topY - bottomY} re f\n"));
                i = end;
            }
            for (var i = 0; i < page.Count; i++)
            {
                var line = page[i];
                if (line.Style is Style.Blank or Style.PageBreak || string.IsNullOrEmpty(line.Text)) continue;
                var (font, size, centered) = Metrics(line.Style);
                var y = TopY - i * Leading;
                var textWidth = line.Text.Length * size * 0.52;
                var x = centered ? Math.Max(MarginX, (PageWidth - textWidth) / 2) : MarginX + Indent(line.Style);
                sb.Append(FormattableString.Invariant($"BT /{font} {size} Tf {x:0.#} {y} Td ({Escape(line.Text)}) Tj ET\n"));
            }
            var footer = FormattableString.Invariant($"P\u00E1gina {pageNumber} de {pageCount}");
            var footerX = Math.Max(MarginX, (PageWidth - footer.Length * 9 * 0.52) / 2);
            sb.Append(FormattableString.Invariant($"BT /F1 9 Tf {footerX:0.#} 28 Td ({Escape(footer)}) Tj ET\n"));
            return ToBytes(sb.ToString());
        }

        private static (string Font, int Size, bool Centered) Metrics(Style style) => style switch
        {
            Style.Org => ("F2", 14, true),
            Style.Title => ("F2", 16, true),
            Style.TocTitle => ("F2", 12, true),
            Style.DocTitle => ("F2", 12, false),
            Style.Heading => ("F2", 10, false),
            Style.CalloutLabel => ("F2", 10, false),
            Style.MetaCentered => ("F1", 10, true),
            Style.MetaLeft => ("F1", 9, false),
            _ => ("F1", 10, false)
        };

        private static int Indent(Style style) => style is Style.CalloutLabel or Style.CalloutBody ? 10 : 0;

        private static List<string> Wrap(string text, int size, int indent)
        {
            if (text.Length == 0) return [string.Empty];
            var usable = PageWidth - 2 * MarginX - indent;
            var width = Math.Max(8, (int)(usable / (size * 0.52)));
            var words = text.Split(' ');
            var result = new List<string>();
            var line = "";
            foreach (var word in words)
            {
                if (line.Length > 0 && line.Length + word.Length + 1 > width) { result.Add(line); line = word; }
                else line += line.Length == 0 ? word : " " + word;
            }
            result.Add(line);
            return result;
        }

        private static string Escape(string text) => text.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("(", "\\(", StringComparison.Ordinal).Replace(")", "\\)", StringComparison.Ordinal).Replace("\r", " ", StringComparison.Ordinal).Replace("\n", " ", StringComparison.Ordinal);
        private static byte[] ToBytes(string value) => WinAnsiEncode(value);
        private static byte[] Concat(params byte[][] arrays) { var result=new byte[arrays.Sum(x=>x.Length)];var offset=0;foreach(var array in arrays){Buffer.BlockCopy(array,0,result,offset,array.Length);offset+=array.Length;}return result; }
    }
}
