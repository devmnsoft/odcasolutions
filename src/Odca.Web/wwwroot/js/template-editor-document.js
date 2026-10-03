export function extractInlineText(node) {
  if (!node) return "";
  if (node.type === "text") {
    let txt = node.text || "";
    const marks = node.marks || [];
    if (marks.includes("bold")) txt = `**${txt}**`;
    if (marks.includes("italic")) txt = `*${txt}*`;
    if (marks.includes("underline")) txt = `<u>${txt}</u>`;
    return txt;
  }
  if (node.type === "field") return `{{${node.fieldId}}}`;
  if (node.content && Array.isArray(node.content)) return node.content.map(extractInlineText).join("");
  return "";
}

export function structuredJsonToText(doc) {
  if (!doc || !doc.content || !Array.isArray(doc.content)) return "";
  const blocks = [];
  for (const node of doc.content) {
    if (node.type === "heading") {
      const prefix = "#".repeat(node.level || 1);
      blocks.push(`${prefix} ${extractInlineText(node)}`);
    } else if (node.type === "pageBreak") {
      blocks.push("---");
    } else if (node.type === "bulletList") {
      const items = (node.content || []).map(item => `- ${extractInlineText(item)}`);
      blocks.push(items.join("\n"));
    } else if (node.type === "orderedList") {
      const items = (node.content || []).map((item, idx) => `${idx + 1}. ${extractInlineText(item)}`);
      blocks.push(items.join("\n"));
    } else if (node.type === "table") {
      const rows = [];
      for (const row of node.content || []) {
        if (row.type === "tableRow") {
          const cells = (row.content || []).map(cell => extractInlineText(cell).trim());
          rows.push(`| ${cells.join(" | ")} |`);
        }
      }
      blocks.push(rows.join("\n"));
    } else if (node.type === "paragraph") {
      blocks.push(extractInlineText(node));
    } else {
      blocks.push(extractInlineText(node));
    }
  }
  return blocks.join("\n\n");
}

export function parseInlineContent(text) {
  const result = [];
  const regex = /\{\{([a-zA-Z0-9_.-]+)\}\}|\*\*([^*]+)\*\*|\*([^*]+)\*|<u>(.*?)<\/u>/g;
  let lastIndex = 0;
  let match;
  while ((match = regex.exec(text)) !== null) {
    if (match.index > lastIndex) result.push({ type: "text", text: text.substring(lastIndex, match.index) });
    if (match[1] !== undefined) result.push({ type: "field", fieldId: match[1] });
    else if (match[2] !== undefined) result.push({ type: "text", text: match[2], marks: ["bold"] });
    else if (match[3] !== undefined) result.push({ type: "text", text: match[3], marks: ["italic"] });
    else if (match[4] !== undefined) result.push({ type: "text", text: match[4], marks: ["underline"] });
    lastIndex = regex.lastIndex;
  }
  if (lastIndex < text.length) result.push({ type: "text", text: text.substring(lastIndex) });
  if (result.length === 0) result.push({ type: "text", text: "" });
  return result;
}

export function convertTextToStructuredJson(text) {
  const rawBlocks = text.split(/\n\s*\n/).map(block => block.trim()).filter(Boolean);
  const contentNodes = [];
  for (const block of rawBlocks) {
    if (block === "---" || block === "===" || block === "[QUEBRA_DE_PAGINA]") {
      contentNodes.push({ type: "pageBreak" });
      continue;
    }
    const lines = block.split("\n").map(line => line.trim()).filter(Boolean);
    if (lines.length > 0 && lines.every(line => line.startsWith("|") && line.endsWith("|"))) {
      const tableRows = [];
      for (const line of lines) {
        if (/^\|[\s\-:|]+\|$/.test(line)) continue;
        const cellTexts = line.slice(1, -1).split("|").map(cell => cell.trim());
        const cells = cellTexts.map(cellText => ({
          type: "tableCell",
          content: [{ type: "paragraph", alignment: "justify", content: parseInlineContent(cellText) }]
        }));
        tableRows.push({ type: "tableRow", content: cells });
      }
      if (tableRows.length > 0) {
        contentNodes.push({ type: "table", content: tableRows });
        continue;
      }
    }
    if (lines.length > 0 && lines.every(line => line.startsWith("- ") || line.startsWith("* "))) {
      contentNodes.push({
        type: "bulletList",
        content: lines.map(line => ({
          type: "listItem",
          content: [{ type: "paragraph", alignment: "justify", content: parseInlineContent(line.replace(/^[-*]\s+/, "")) }]
        }))
      });
      continue;
    }
    if (lines.length > 0 && lines.every(line => /^\d+\.\s+/.test(line))) {
      contentNodes.push({
        type: "orderedList",
        content: lines.map(line => ({
          type: "listItem",
          content: [{ type: "paragraph", alignment: "justify", content: parseInlineContent(line.replace(/^\d+\.\s+/, "")) }]
        }))
      });
      continue;
    }
    const headingMatch = block.match(/^(#{1,3})\s+(.*)$/s);
    if (headingMatch) {
      contentNodes.push({ type: "heading", level: headingMatch[1].length, content: parseInlineContent(headingMatch[2].trim()) });
      continue;
    }
    contentNodes.push({ type: "paragraph", alignment: "justify", content: parseInlineContent(lines.join(" ")) });
  }
  return { type: "document", content: contentNodes };
}

export function findEditorLosses(doc, issues) {
  const walk = node => {
    if (!node || typeof node !== "object") return;
    if (node.type === "text") {
      const marks = node.marks || [];
      if (marks.length > 1) issues.add("marcações combinadas no mesmo trecho");
      if (marks.some(mark => !["bold", "italic", "underline"].includes(mark))) issues.add("marcação não suportada pelo editor");
      const txt = node.text || "";
      if (txt.includes("*")) issues.add("texto contém \"*\" que o editor pode reinterpretar");
      if (txt.includes("<u>")) issues.add("texto contém \"<u>\" que o editor reinterpretará");
    }
    if (node.type === "paragraph" && node.alignment && node.alignment !== "justify")
      issues.add("alinhamentos diferentes de justificado");
    if (node.type === "heading" && (node.level < 1 || node.level > 3))
      issues.add("títulos fora dos níveis 1 a 3");
    if (node.type === "tableCell" && Array.isArray(node.content) && node.content.length > 1)
      issues.add("células de tabela com mais de um bloco");
    if (node.type === "listItem" && (node.content || []).some(child => child && (child.type === "bulletList" || child.type === "orderedList")))
      issues.add("listas aninhadas");
    if (!["document", "paragraph", "heading", "text", "field", "bulletList", "orderedList", "listItem", "table", "tableRow", "tableCell", "pageBreak"].includes(node.type))
      issues.add("bloco que o editor não representa");
    for (const child of node.content || []) walk(child);
  };
  walk(doc);
}

function stable(value) {
  if (Array.isArray(value)) return `[${value.map(stable).join(",")}]`;
  if (value && typeof value === "object") {
    return `{${Object.keys(value).filter(key => value[key] !== undefined && value[key] !== null).sort()
      .map(key => `${JSON.stringify(key)}:${stable(value[key])}`).join(",")}}`;
  }
  return JSON.stringify(value);
}

export function documentRoundTrips(doc) {
  if (!doc || !Array.isArray(doc.content)) return false;
  return stable(doc) === stable(convertTextToStructuredJson(structuredJsonToText(doc)));
}

export function collectEditorLosses(doc) {
  const issues = new Set();
  findEditorLosses(doc, issues);
  if (doc && Array.isArray(doc.content) && !documentRoundTrips(doc))
    issues.add("estrutura que o editor de texto não reproduz");
  return [...issues];
}
