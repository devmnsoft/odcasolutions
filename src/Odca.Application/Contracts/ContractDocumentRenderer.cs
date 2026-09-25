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

    public static string ToHtml(string contentJson, string fieldsJson, string valuesJson)
    {
        var model = Read(contentJson, fieldsJson, valuesJson);
        var html = new StringBuilder("<article class=\"legal-document\">");
        RenderHtml(model.Root, model.Values, html);
        return html.Append("</article>").ToString();
    }

    public static byte[] ToPdf(string contentJson, string fieldsJson, string valuesJson, string title, int version)
    {
        var model = Read(contentJson, fieldsJson, valuesJson);
        var lines = new List<string> { title, $"Versão {version}" };
        Flatten(model.Root, model.Values, lines, 0);
        return SimplePdf.Create(lines);
    }

    private static RenderModel Read(string contentJson, string fieldsJson, string valuesJson)
    {
        var options=new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
        options.Converters.Add(new JsonStringEnumConverter());
        var definitions = JsonSerializer.Deserialize<ContractFieldDefinition[]>(fieldsJson, options) ?? [];
        var values = JsonSerializer.Deserialize<ContractFieldValue[]>(valuesJson,
            options) ?? [];
        StructuredContractDocument.Parse(contentJson, definitions);
        StructuredContractDocument.ValidateValues(definitions, values, false);
        var valueMap = values.ToDictionary(x => x.FieldId, x => x.Value, StringComparer.Ordinal);
        using var parsed = JsonDocument.Parse(contentJson, new JsonDocumentOptions { MaxDepth = 64 });
        return new(parsed.RootElement.Clone(), valueMap);
    }

    private static void RenderHtml(JsonElement node, IReadOnlyDictionary<string, string?> values, StringBuilder output)
    {
        var type = node.GetProperty("type").GetString();
        if (type == "text") { output.Append(WebUtility.HtmlEncode(node.GetProperty("text").GetString())); return; }
        if (type == "field")
        {
            var id = node.GetProperty("fieldId").GetString()!;
            output.Append("<span class=\"document-field").Append(string.IsNullOrWhiteSpace(values.GetValueOrDefault(id)) ? " document-field-empty" : "")
                .Append("\">").Append(WebUtility.HtmlEncode(values.GetValueOrDefault(id) ?? "Não informado")).Append("</span>");
            return;
        }
        if (type == "pageBreak") { output.Append("<hr class=\"document-page-break\" aria-label=\"Quebra de página\">"); return; }
        var tag = type switch { "document" => "div", "heading" => $"h{node.GetProperty("level").GetInt32()}", "paragraph" => "p", "bulletList" => "ul", "orderedList" => "ol", "listItem" => "li", "table" => "table", "tableRow" => "tr", "tableCell" => "td", _ => "div" };
        output.Append('<').Append(tag);
        if (type == "paragraph" && node.TryGetProperty("alignment", out var alignment))
            output.Append(" class=\"align-").Append(alignment.GetString()).Append("\"");
        output.Append('>');
        if (TryGetChildren(node, out var children)) foreach (var child in children)
        {
            if (child.GetProperty("type").GetString() == "text" && child.TryGetProperty("marks", out var marks))
            {
                var markNames = marks.EnumerateArray().Select(x => x.GetString()).ToHashSet(StringComparer.Ordinal);
                if (markNames.Contains("bold")) output.Append("<strong>");
                if (markNames.Contains("italic")) output.Append("<em>");
                if (markNames.Contains("underline")) output.Append("<u>");
                RenderHtml(child, values, output);
                if (markNames.Contains("underline")) output.Append("</u>");
                if (markNames.Contains("italic")) output.Append("</em>");
                if (markNames.Contains("bold")) output.Append("</strong>");
            }
            else RenderHtml(child, values, output);
        }
        output.Append("</").Append(tag).Append('>');
    }

    private static void Flatten(JsonElement node, IReadOnlyDictionary<string, string?> values, List<string> lines, int depth)
    {
        var type = node.GetProperty("type").GetString();
        if (type == "pageBreak") { lines.Add("\f"); return; }
        if (type is "paragraph" or "heading" or "listItem" or "tableRow")
        {
            var line = new StringBuilder(type == "listItem" ? "• " : "");
            CollectText(node, values, line);
            lines.Add(line.ToString());
            return;
        }
        if (TryGetChildren(node, out var children)) foreach (var child in children) Flatten(child, values, lines, depth + 1);
    }

    private static void CollectText(JsonElement node, IReadOnlyDictionary<string, string?> values, StringBuilder line)
    {
        var type = node.GetProperty("type").GetString();
        if (type == "text") line.Append(node.GetProperty("text").GetString());
        else if (type == "field") line.Append(values.GetValueOrDefault(node.GetProperty("fieldId").GetString()!) ?? "Não informado");
        else if (TryGetChildren(node, out var children)) foreach (var child in children) CollectText(child, values, line);
        if (type == "tableCell") line.Append("  |  ");
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

    private sealed record RenderModel(JsonElement Root, IReadOnlyDictionary<string, string?> Values);

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
