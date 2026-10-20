// S20 / D-OC54 - inventario reprodutivel de classes CSS:
// usa "definida" = presente em wwwroot/css/site.css OU em blocos <style> dos views;
// "usada" = atributo class em .cshtml + insercao dinamica nos js do wwwroot.
import { readFileSync, readdirSync, statSync } from 'node:fs';
import { join, relative } from 'node:path';

const ROOT = 'C:/MNSOFT/odcasolutions';
const walk = (dir, ext, out = []) => {
  for (const name of readdirSync(dir)) {
    const p = join(dir, name);
    const st = statSync(p);
    if (st.isDirectory()) {
      if (name === 'bin' || name === 'obj' || name === 'node_modules') continue;
      walk(p, ext, out);
    } else if (name.endsWith(ext)) out.push(p);
  }
  return out;
};

const classTokens = (s) =>
  [...s.matchAll(/[\w][\w-]*/g)].map((m) => m[0]).filter((t) => !/^[0-9]+$/.test(t));

// ---- definidas ----
const cssText = readFileSync(join(ROOT, 'src/Odca.Web/wwwroot/css/site.css'), 'utf8');
const cssSelectors = cssText.replace(/\/\*[\s\S]*?\*\//g, '').replace(/\{[^{}]*\}/g, ' ');
const defined = new Set([...cssSelectors.matchAll(/\.([\w][\w-]*)/g)].map((m) => m[1]));

// ---- usados em views ----
const viewFiles = walk(join(ROOT, 'src/Odca.Web/Views'), '.cshtml');
const inlineStyleFiles = [];
const usage = new Map(); // classe -> Set de caminhos relativos
const addUse = (cls, file) => {
  if (!usage.has(cls)) usage.set(cls, new Set());
  usage.get(cls).add(file);
};
for (const f of viewFiles) {
  const rel = relative(ROOT, f);
  const text = readFileSync(f, 'utf8');
  for (const m of text.matchAll(/\bclass\s*=\s*"([^"]*)"/g)) {
    for (const tok of classTokens(m[1])) {
      // ignora tokens que sao expressoes razor/asp ou fragmentos de concat
      if (/^[@$<]|\.|:|\)/.test(tok)) continue;
      addUse(tok, rel);
    }
  }
  for (const m of text.matchAll(/<style[^>]*>([\s\S]*?)<\/style>/g)) {
    inlineStyleFiles.push(rel);
    for (const clsTok of classTokens(m[1])) addUse(clsTok, rel + ' [style interno]');
    for (const dm of m[1].matchAll(/\.([\w][\w-]*)/g)) defined.add(dm[1]);
  }
}

// ---- usados em js (insercao dinamica) ----
const jsFiles = walk(join(ROOT, 'src/Odca.Web/wwwroot/js'), '.js');
for (const f of jsFiles) {
  const rel = relative(ROOT, f);
  const text = readFileSync(f, 'utf8');
  const pats = [
    /classList\.(?:add|remove|toggle)\(\s*(["'`])([\w\s,\-]+?)\1/g,
    /classList\.(?:add|remove|toggle)\(\s*(["'`])([\w\s,\-]+?)\1\s*,\s*([\w\s,\-]+?)\3/g,
    /className\s*\+?=\s*(["'`])([^"'`]*)\1/g,
    /className\s*=\s*(["'`])([^"'`]*)\1/g,
    /insertAdjacentHTML\(\s*'beforeend'\s*,\s*'[^']*\bclass=["'`]?([\w\s\-]+)/g,
  ];
  for (const pat of pats) {
    for (const m of text.matchAll(pat)) {
      for (const tok of classTokens(m[m.length - 1] || '')) {
        if (!/^[\w-]+$/.test(tok)) continue;
        addUse(tok, rel);
      }
    }
  }
}

const rows = [...usage.entries()]
  .map(([cls, files]) => ({ cls, defined: defined.has(cls), files: [...files].sort() }))
  .sort((a, b) => a.cls.localeCompare(b.cls));
const undefinedRows = rows.filter((r) => !r.defined);

console.log(`USADAS=${rows.length} DEFINIDAS_SITE_CSS=${defined.size} SEM_DEFINICAO=${undefinedRows.length}`);
console.log('--- SEM DEFINICAO ---');
for (const r of undefinedRows) console.log(`${r.cls}\n    ${r.files.join('\n    ')}`);
console.log('--- ARQ C/ STYLE INTERNO ---');
for (const f of inlineStyleFiles) console.log(f);
import { writeFileSync } from 'node:fs';
writeFileSync(
  'C:/MNSOFT/odcasolutions/scripts/qa/css-class-inventory.json',
  JSON.stringify({ rows, undefinedRows }, null, 1),
  'utf8'
);
console.log('JSON em scripts/qa/css-class-inventory.json');
