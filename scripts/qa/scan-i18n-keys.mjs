// D-OC54 (Sprint 20): scanner permanente de chaves i18n usadas pelo Web.
// Extrai chaves literais de views Razor (L.T("...", qualquer arg)), JS (t("...")) e
// do registro de navegacao em C# (Key/AriaKey = "..." na pasta Navigation) e valida
// a presenca nas 4 culturas dos catalogos flat. Fecha o gap dos keyuse ad-hoc por
// sprint: o scanner da S19 varria so studio.js; esta varredura inteira detectou as
// 6 chaves layout.* ausentes que o _Layout renderizava como texto cru no sidebar.
import { readFileSync, readdirSync, statSync } from 'node:fs';
import path from 'node:path';

const root = process.cwd();
const webDir = path.join(root, 'src', 'Odca.Web');
const locDir = path.join(root, 'src', 'Odca.Configuration', 'Localization');
const cultures = ['pt-BR', 'en-US', 'es-ES', 'fr-FR'];

const catalogs = {};
for (const c of cultures) {
  const text = readFileSync(path.join(locDir, c + '.json'), 'utf8').replace(/^\uFEFF/, '');
  catalogs[c] = new Set(Object.keys(JSON.parse(text)));
}

const files = [];
(function walk(dir) {
  for (const name of readdirSync(dir)) {
    // lib/ = bibliotecas vendorizadas (jquery etc.) — nao usam o OdcaI18n.
    if (name === 'lib') continue;
    const p = path.join(dir, name);
    const st = statSync(p);
    if (st.isDirectory()) walk(p);
    else if (/\.(cshtml|js)$/.test(name) || (name.endsWith('.cs') && p.split(path.sep).includes('Navigation'))) files.push(p);
  }
})(webDir);

const patterns = [
  { re: /L\.T\(\s*"([A-Za-z][A-Za-z0-9_.\-]*)"/g },
  { re: /\bt\(\s*"([A-Za-z][A-Za-z0-9_.\-]*)"/g },
  { re: /\b(?:Key|AriaKey)\s*=\s*"([A-Za-z][A-Za-z0-9_.\-]*)"/g },
];

const used = new Map(); // chave -> Set(arquivos)
for (const f of files) {
  const rel = path.relative(root, f).replace(/\\/g, '/');
  const text = readFileSync(f, 'utf8');
  for (const pat of patterns) {
    pat.re.lastIndex = 0;
    let m;
    while ((m = pat.re.exec(text))) {
      if (!used.has(m[1])) used.set(m[1], new Set());
      used.get(m[1]).add(rel);
    }
  }
}

const missing = [];
for (const [key, where] of [...used].sort()) {
  const absentIn = cultures.filter((c) => !catalogs[c].has(key));
  if (absentIn.length > 0) missing.push({ key, absentIn, where: [...where].sort() });
}

console.log(`chaves lidas: ${used.size} em ${files.length} arquivos (todos .cshtml/.js do Odca.Web + Navigation/*.cs)`);
if (missing.length === 0) {
  console.log('KEYUSE-OK: 0 ausentes nos 4 catalogos');
  process.exit(0);
}
for (const m of missing) console.log(`MISSING ${m.key} (ausente em: ${m.absentIn.join(',')}) usado em: ${m.where.join(' ')}`);
console.log(`KEYUSE-FAIL: ${missing.length} chaves ausentes`);
process.exit(1);
