// S7 (D-OC41): mesclador determinístico de deltas de i18n nos catálogos flat.
// Uso (raiz do repositório): node scripts/i18n/merge-i18n-delta.mjs
// Lê todos os scripts/i18n/d-s7-*.json (ordem alfabética), valida unicidade e
// paridade 4/4, insere as chaves novas no final de cada catálogo preservando BOM,
// indentação de 2 espaços e ausência de nova linha final, e revalida o resultado.
import { readFileSync, readdirSync, writeFileSync } from 'node:fs';
import path from 'node:path';

const root = process.cwd();
const locDir = path.join(root, 'src', 'Odca.Configuration', 'Localization');
const scriptsDir = path.join(root, 'scripts', 'i18n');
const cultures = ['pt-BR', 'en-US', 'es-ES', 'fr-FR'];

function fail(message) {
  console.error('ERRO: ' + message);
  process.exit(1);
}

const deltaFiles = readdirSync(scriptsDir)
  .filter((f) => f.startsWith('d-s7') && f.endsWith('.json'))
  .sort();
if (deltaFiles.length === 0) fail('nenhum delta d-s7-*.json encontrado em ' + scriptsDir);

const merged = new Map(); // chave -> {pt-BR,en-US,es-ES,fr-FR}
for (const file of deltaFiles) {
  const raw = readFileSync(path.join(scriptsDir, file), 'utf8');
  let parsed;
  try {
    parsed = JSON.parse(raw);
  } catch (e) {
    fail(`JSON inválido em ${file}: ${e.message}`);
  }
  for (const [key, values] of Object.entries(parsed)) {
    if (!/^[a-z][a-z0-9]*(\.[a-z0-9_\-]+)+$/i.test(key)) fail(`chave com formato inesperado: ${key} em ${file}`);
    for (const culture of cultures) {
      if (typeof values[culture] !== 'string' || values[culture].length === 0) {
        fail(`valor vazio ou ausente: ${key}[${culture}] em ${file}`);
      }
    }
    if (merged.has(key)) {
      const prev = merged.get(key);
      const same = cultures.every((c) => prev[c] === values[c]);
      if (!same) fail(`conflito de valores entre deltas para a chave ${key}`);
    } else {
      merged.set(key, values);
    }
  }
}

// Paridade pré-mescla: o mesmo conjunto de chaves nas 4 culturas antes de alterar tudo.
const parityRef = {};
for (const culture of cultures) {
  const text = readFileSync(path.join(locDir, culture + '.json'), 'utf8').replace(/^\uFEFF/, '');
  parityRef[culture] = Object.keys(JSON.parse(text)).sort().join('|');
}
for (const culture of cultures) {
  if (parityRef[culture] !== parityRef[cultures[0]]) fail(`paridade 4/4 já não vale antes da mescla (diferença em ${culture})`);
}

for (const culture of cultures) {
  const file = path.join(locDir, culture + '.json');
  let text = readFileSync(file, 'utf8');
  const hadBom = text.startsWith('\uFEFF');
  if (hadBom) text = text.slice(1);

  let current;
  try {
    current = JSON.parse(text);
  } catch (e) {
    fail(`catálogo corrompido antes da mescla: ${culture}.json (${e.message})`);
  }
  const existingSet = new Set(Object.keys(current));
  const toAdd = [...merged.keys()].filter((k) => !existingSet.has(k));
  const lines = toAdd.map((k) => '  ' + JSON.stringify(k) + ': ' + JSON.stringify(merged.get(k)[culture]));
  const braceIdx = text.lastIndexOf('}');
  if (braceIdx < 0 || text.slice(braceIdx + 1).trim() !== '') fail(`${culture}: formato do catálogo mudou (fecha inesperado)`);
  const prefix = text.slice(0, braceIdx).replace(/\s+$/, '');
  if (!prefix.endsWith('"')) fail(`${culture}: última entrada do catálogo não termina com string`);
  const next = prefix + ',\n' + lines.join(',\n') + '\n}';
  const check = JSON.parse(next);
  if (Object.keys(check).length !== Object.keys(current).length + toAdd.length) {
    fail(`${culture}: contagem de chaves inconsistente após a mescla`);
  }
  writeFileSync(file, (hadBom ? '\uFEFF' : '') + next, 'utf8');
  console.log(`${culture}: +${toAdd.length} chaves -> total ${Object.keys(check).length}`);
}

// Paridade pós-mescla final.
const totals = {};
for (const culture of cultures) {
  const text = readFileSync(path.join(locDir, culture + '.json'), 'utf8').replace(/^\uFEFF/, '');
  totals[culture] = Object.keys(JSON.parse(text)).length;
}
const distinct = new Set(Object.values(totals));
if (distinct.size !== 1) fail('paridade 4/4 violada após a mescla: ' + JSON.stringify(totals));
console.log('paridade 4/4 ok; delta total incorporado: ' + merged.size + ' chaves');
