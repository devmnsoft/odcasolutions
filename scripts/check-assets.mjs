import { access } from "node:fs/promises";
import { readFileSync } from "node:fs";
import assert from "node:assert/strict";
import { isPartialDecimal, parseDecimal } from "../src/Odca.Web/wwwroot/js/studio-decimal.js";
import { collectEditorLosses, documentRoundTrips } from "../src/Odca.Web/wwwroot/js/template-editor-document.js";

const requiredAssets = [
  "src/Odca.Web/wwwroot/css/site.css",
  "src/Odca.Web/wwwroot/js/site.js",
  "src/Odca.Web/wwwroot/js/navigation.js",
  "src/Odca.Web/wwwroot/js/forms.js",
  "src/Odca.Web/wwwroot/js/dialogs.js",
  "src/Odca.Web/wwwroot/js/team.js",
  "src/Odca.Web/wwwroot/js/studio.js",
  "src/Odca.Web/wwwroot/js/patients.js"
];

requiredAssets.push(
  "src/Odca.Web/wwwroot/js/studio-decimal.js",
  "src/Odca.Web/wwwroot/js/template-editor-document.js"
);

await Promise.all(requiredAssets.map((path) => access(path)));

const currency = (input, expected) => {
  const parsed = parseDecimal(input, true);
  assert.equal(parsed.ok, true, input);
  assert.equal(parsed.canonical, expected);
};
currency("150,00", "150.00");
currency("1.250,00", "1250.00");
currency("1250.00", "1250.00");
currency("R$ 150,00", "150.00");
assert.equal(parseDecimal("", true).ok, false);
assert.equal(parseDecimal("   ", true).ok, false);
assert.equal(isPartialDecimal("150,"), true);
assert.equal(isPartialDecimal("1."), true);
assert.equal(isPartialDecimal(""), false);
assert.equal(parseDecimal("1.250", true).ok, false);
assert.equal(parseDecimal("1,250", true).ok, false);
assert.equal(parseDecimal("-10,00", true).ok, false);
assert.equal(parseDecimal("0150,00", true).ok, false);
assert.equal(parseDecimal("abc", true).ok, false);
assert.equal(parseDecimal("-1,5", false).canonical, "-1.5");
assert.equal(parseDecimal("1.250,00", true).canonical, "1250.00");

const text = (value) => ({ type: "text", text: value });
const paragraph = (...content) => ({ type: "paragraph", alignment: "justify", content });
const compatible = {
  type: "document",
  content: [
    { type: "heading", level: 1, content: [text("Termo")] },
    paragraph(text("A organização "), { type: "field", fieldId: "organization_name" }, text(" registra.")),
    { type: "pageBreak" },
    { type: "bulletList", content: [{ type: "listItem", content: [paragraph(text("Item"))] }] },
    { type: "table", content: [{ type: "tableRow", content: [
      { type: "tableCell", content: [paragraph(text("A "), { type: "field", fieldId: "valor" })] },
      { type: "tableCell", content: [paragraph({ type: "text", text: "B", marks: ["bold"] })] }
    ] }] }
  ]
};
assert.equal(documentRoundTrips(compatible), true);
assert.equal(collectEditorLosses(compatible).length, 0);

const nested = {
  type: "document",
  content: [{ type: "bulletList", content: [{ type: "listItem", content: [
    paragraph(text("Pai")),
    { type: "bulletList", content: [{ type: "listItem", content: [paragraph(text("Filho"))] }] }
  ] }] }]
};
const multiCell = {
  type: "document",
  content: [{ type: "table", content: [{ type: "tableRow", content: [{ type: "tableCell", content: [
    paragraph(text("Um")), paragraph(text("Dois"))
  ] }] }] }]
};
const aligned = { type: "document", content: [{ type: "paragraph", alignment: "center", content: [text("Centro")] }] };
const combined = { type: "document", content: [paragraph({ type: "text", text: "Ambos", marks: ["bold", "italic"] })] };
for (const lossy of [nested, multiCell, aligned, combined]) {
  assert.equal(documentRoundTrips(lossy), false);
  assert.ok(collectEditorLosses(lossy).length > 0);
}

// As chaves de catálogo usadas pelos módulos puros precisam existir e ser não vazias
// nos 4 idiomas: os textos de erro de moeda/número e as perdas do editor aparecem
// traduzidas no navegador em qualquer uma das línguas suportadas.
const moduleSources = [
  "src/Odca.Web/wwwroot/js/studio-decimal.js",
  "src/Odca.Web/wwwroot/js/template-editor-document.js"
].map((p) => readFileSync(new URL("../" + p, import.meta.url), "utf8"));
const i18nKeys = new Set();
for (const source of moduleSources) {
  for (const match of source.matchAll(/\bt\(\s*'([a-z][a-z0-9_.]*)'\s*\)/g)) i18nKeys.add(match[1]);
}
assert.ok(i18nKeys.size >= 15, `esperadas 15+ chaves i18n nos módulos puros; encontradas ${i18nKeys.size}`);
for (const lang of ["pt-BR", "en-US", "es-ES", "fr-FR"]) {
  const raw = readFileSync(new URL(`../src/Odca.Configuration/Localization/${lang}.json`, import.meta.url), "utf8");
  const catalog = JSON.parse(raw.replace(/^\uFEFF/, ""));
  for (const key of i18nKeys) {
    assert.ok(typeof catalog[key] === "string" && catalog[key].trim().length > 0, `chave ${key} ausente ou vazia em ${lang}.json`);
  }
}

console.log(`Validated ${requiredAssets.length} web asset(s), ${i18nKeys.size} i18n key(s) across 4 catalog(s), and the decimal and template round trips.`);
