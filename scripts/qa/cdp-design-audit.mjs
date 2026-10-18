// D-OC40 - Sprint 6 / F-2: auditoria de design das superficies do piloto (headless msedge CDP).
// Por rota x idioma x viewport mede: (1) overflow horizontal (scrollWidth > innerWidth),
// (2) redirecionamento indesejado para /entrar (autenticacao quebrada), e salva PNG apenas das
// falhas para triagem visual. Sem capturar telas de sucesso (as capturas de referencia sao das
// sprints anteriores; aqui o objetivo e a medida, nao o acervo).
import { spawn } from 'node:child_process';
import { writeFileSync, mkdirSync, rmSync, readFileSync } from 'node:fs';
import crypto from 'node:crypto';

const cfgPath = process.argv[2];
let cfgText = readFileSync(cfgPath, 'utf8');
if (cfgText.charCodeAt(0) === 0xFEFF) cfgText = cfgText.slice(1);
const cfg = JSON.parse(cfgText);
const CDP_PORT = 9337;
const sleep = (ms) => new Promise((r) => setTimeout(r, ms));
const esc = encodeURIComponent;

const userDataDir = `C:\\Users\\NCELL-DEV-020\\AppData\\Local\\Temp\\opencode\\edge-audit-${Date.now()}`;
mkdirSync(cfg.outDir, { recursive: true });
const edge = spawn(cfg.edge, [
  '--headless=new', '--remote-debugging-port=' + CDP_PORT, '--user-data-dir=' + userDataDir,
  '--no-first-run', '--no-default-browser-check', '--disable-gpu', '--hide-scrollbars',
  '--force-color-profile=srgb', '--window-size=1280,800', 'about:blank',
], { stdio: ['ignore', 'pipe', 'pipe'] });
edge.stderr.on('data', () => {});

// ---------- TOTP ----------
function base32Decode(s) {
  const A = 'ABCDEFGHIJKLMNOPQRSTUVWXYZ234567';
  let bits = 0, value = 0; const out = [];
  for (const ch of s.toUpperCase().replace(/=+$/g, '').replace(/\s/g, '')) {
    const idx = A.indexOf(ch);
    if (idx < 0) continue;
    value = (value << 5) | idx; bits += 5;
    if (bits >= 8) { bits -= 8; out.push((value >>> bits) & 0xFF); }
  }
  return Buffer.from(out);
}
function totp(secret, period = 30, digits = 6) {
  const counter = Math.floor(Date.now() / 1000 / period);
  const buf = Buffer.alloc(8);
  buf.writeBigUInt64BE(BigInt(counter));
  const h = crypto.createHmac('sha1', base32Decode(secret)).update(buf).digest();
  const off = h[h.length - 1] & 0x0F;
  const n = ((h[off] & 0x7F) << 24 | h[off + 1] << 16 | h[off + 2] << 8 | h[off + 3]) % 10 ** digits;
  return String(n).padStart(digits, '0');
}

// ---------- login via HTTP (mesmo fluxo do BFF) ----------
function parseCookies(res) {
  return (res.headers.getSetCookie ? res.headers.getSetCookie() : []).map((line) => {
    const main = line.split(';')[0];
    const i = main.indexOf('=');
    if (i <= 0) return null;
    return { name: main.slice(0, i).trim(), value: main.slice(i + 1).trim() };
  }).filter(Boolean);
}
function cookieHeader(list) {
  const seen = new Map();
  for (const c of list) seen.set(c.name, c.value);
  return [...seen].map(([n, v]) => n + '=' + v).join('; ');
}
function antitoken(html) {
  return (html.match(/<input[^>]*name="__RequestVerificationToken"[^>]*value="([^"]+)"/) || [])[1] || '';
}

async function httpLogin(email, pass, mfaKey) {
  const jar = [];
  const r0 = await fetch(cfg.base + '/entrar', { redirect: 'manual' });
  for (const c of parseCookies(r0)) jar.push(c);
  const html = await r0.text();
  const af = antitoken(html);
  if (!af) throw new Error('antiforgery ausente em /entrar');
  const body = 'Login=' + esc(email) + '&Password=' + esc(pass) + '&ReturnUrl=&__RequestVerificationToken=' + esc(af);
  const r1 = await fetch(cfg.base + '/entrar', {
    method: 'POST', redirect: 'manual',
    headers: { 'content-type': 'application/x-www-form-urlencoded', cookie: cookieHeader(jar), requestverificationtoken: af },
    body,
  });
  for (const c of parseCookies(r1)) jar.push(c);
  await r1.body.cancel().catch(() => {});
  const loc = r1.headers.get('location') || '';
  if (r1.status === 302 && loc.includes('mfa/desafio')) {
    let done = false; let postedStep = -1; let lastBody = '';
    for (let posts = 1; posts <= 4 && !done; posts++) {
      let step = Math.floor(Date.now() / 30000);
      if (step === postedStep) {
        const msLeft = 30000 - (Date.now() % 30000) + 1200;
        await sleep(Math.min(msLeft, 32000));
        step = Math.floor(Date.now() / 30000);
        if (step === postedStep) { posts--; continue; }
      }
      postedStep = step;
      const rc = await fetch(cfg.base + '/mfa/desafio', { redirect: 'manual', headers: { cookie: cookieHeader(jar) } });
      for (const c of parseCookies(rc)) jar.push(c);
      const chHtml = await rc.text();
      const af2 = antitoken(chHtml);
      await sleep(300);
      const rm = await fetch(cfg.base + '/mfa/desafio', {
        method: 'POST', redirect: 'manual',
        headers: { 'content-type': 'application/x-www-form-urlencoded', cookie: cookieHeader(jar), requestverificationtoken: af2 },
        body: 'Code=' + esc(totp(mfaKey)) + '&ReturnUrl=&__RequestVerificationToken=' + esc(af2),
      });
      const rmText = await rm.text().catch(() => '');
      for (const c of parseCookies(rm)) jar.push(c);
      if (rm.status === 302) { done = true; }
      else lastBody = rmText.replace(/\s+/g, ' ').slice(0, 200);
    }
    if (!done) throw new Error('desafio MFA nao confirmado para ' + email + (lastBody ? ' ultimo=' + lastBody : ''));
  } else if (r1.status !== 302) {
    throw new Error('login nao redirecionou para ' + email + ': status=' + r1.status);
  }
  if (!jar.some((c) => c.name === '__Host-odca-session')) throw new Error('__Host-odca-session ausente apos login de ' + email);
  return jar;
}

// ---------- CDP ----------
async function getBrowserWs(timeoutMs = 30000) {
  const t0 = Date.now();
  for (;;) {
    try {
      const r = await fetch(`http://127.0.0.1:${CDP_PORT}/json/version`);
      const j = await r.json();
      if (j.webSocketDebuggerUrl) return j.webSocketDebuggerUrl;
    } catch {}
    if (Date.now() - t0 > timeoutMs) throw new Error('CDP nao respondeu em ' + CDP_PORT);
    await sleep(500);
  }
}
const wsUrl = await getBrowserWs();
const ws = new WebSocket(wsUrl);
await new Promise((res, rej) => {
  const t = setTimeout(() => rej(new Error('ws open timeout')), 15000);
  ws.addEventListener('open', () => { clearTimeout(t); res(); }, { once: true });
});
let idc = 0; const pending = new Map();
ws.addEventListener('message', (ev) => {
  const msg = JSON.parse(typeof ev.data === 'string' ? ev.data : ev.data.toString());
  if (msg.id && pending.has(msg.id)) { pending.get(msg.id)(msg); pending.delete(msg.id); }
});
function send(method, params = {}) {
  return new Promise((res) => {
    const id = ++idc;
    pending.set(id, res);
    ws.send(JSON.stringify({ id, method, params }));
  });
}
const created = await send('Target.createTarget', { url: 'about:blank' });
const targetId = created.result.targetId;
let pageWsUrl = null;
for (let i = 0; i < 30 && !pageWsUrl; i++) {
  const r = await fetch(`http://127.0.0.1:${CDP_PORT}/json/list`);
  const list = await r.json();
  const t = list.find((x) => x.id === targetId);
  if (t && t.webSocketDebuggerUrl) pageWsUrl = t.webSocketDebuggerUrl;
  else await sleep(500);
}
const pws = new WebSocket(pageWsUrl);
await new Promise((res) => pws.addEventListener('open', res, { once: true }));
let pidc = 0; const ppending = new Map();
pws.addEventListener('message', (ev) => {
  const msg = JSON.parse(typeof ev.data === 'string' ? ev.data : ev.data.toString());
  if (msg.id && ppending.has(msg.id)) { ppending.get(msg.id)(msg); ppending.delete(msg.id); }
});
function psend(method, params = {}) {
  return new Promise((res) => {
    const id = ++pidc;
    ppending.set(id, res);
    pws.send(JSON.stringify({ id, method, params }));
  });
}
await psend('Page.enable');
await psend('Network.enable');

// ---------- sessoes fresas ----------
const jars = {};
try {
  for (const [name, cr] of Object.entries(cfg.creds || {})) {
    jars[name] = await httpLogin(cr.email, cr.pass, cr.mfaKey || '');
    console.log('LOGIN ' + name + ' ok (' + jars[name].length + ' cookies)');
  }

  const checks = [];
  let okCount = 0, overflowCount = 0, authFailCount = 0, httpFailCount = 0;

  async function probe(route, lang, width, height) {
    const tag = `${route.id}/${lang}/${width}x${height}`;
    for (let attempt = 0; attempt < 2; attempt++) {
      const jar = route.session === 'anon' ? [] : (jars[route.session] || []);
      await psend('Network.clearBrowserCookies');
      const cookies = jar.map((c) => ({ name: c.name, value: c.value, domain: 'localhost', path: '/', secure: true }));
      cookies.push({ name: 'odca_lang', value: lang, domain: 'localhost', path: '/' });
      await psend('Network.setCookies', { cookies });
      await psend('Emulation.setDeviceMetricsOverride', {
        width, height, deviceScaleFactor: 1, mobile: width < 500, screenWidth: width, screenHeight: height,
      });
      await psend('Page.navigate', { url: cfg.base + route.path });
      await sleep(5500); // settle: fontes/imagens/renderizacao lazy
      const locRes = await psend('Runtime.evaluate', { expression: 'location.href' });
      const loc = (locRes.result && locRes.result.result && locRes.result.result.value) || '';
      if (route.session !== 'anon' && loc.includes('/entrar')) {
        if (attempt === 0) {
          console.log('RELOGIN ' + tag + ' (sessao expirada)');
          jars[route.session] = await httpLogin(cfg.creds[route.session].email, cfg.creds[route.session].pass, cfg.creds[route.session].mfaKey || '');
          continue;
        }
        const rec = { tag, status: 'AUTHFAIL', href: loc };
        checks.push(rec);
        console.log('AUTHFAIL ' + tag + ' href=' + loc);
        authFailCount++;
        return;
      }
      // Status HTTP do documento principal: pagina de erro (4xx/5xx) nao e overflow nem authfail.
      // Sem isso, o erro 404 da Chromium "encaixa" em viewports >= 908px e vira OK falso
      // (1a auditoria de D-OC40: 5 rotas 404 por tenant corrompido no cfg — ver secao 5).
      const stRes = await psend('Runtime.evaluate', {
        expression: '(function(){var n=performance.getEntriesByType("navigation")[0];return String(n && n.responseStatus ? n.responseStatus : "")})()',
      });
      const httpStatus = (stRes.result && stRes.result.result && stRes.result.result.value) || '';
      if (httpStatus && Number(httpStatus) >= 400) {
        const rec = { tag, status: 'HTTPFAIL', http: Number(httpStatus), href: loc };
        checks.push(rec);
        try {
          const shot = await psend('Page.captureScreenshot', { format: 'png', captureBeyondViewport: true });
          if (shot.result && shot.result.data) writeFileSync(`${cfg.outDir}\\${route.id}_${lang}_${width}x${height}_http${httpStatus}.png`, Buffer.from(shot.result.data, 'base64'));
        } catch {}
        console.log(`HTTPFAIL ${tag} status=${httpStatus} href=${loc}`);
        httpFailCount++;
        return;
      }
      const mRes = await psend('Runtime.evaluate', {
        expression: 'JSON.stringify({sw: document.documentElement.scrollWidth, iw: window.innerWidth, sh: document.documentElement.scrollHeight, ih: window.innerHeight, title: document.title})',
      });
      const m = JSON.parse((mRes.result && mRes.result.result && mRes.result.result.value) || '{}');
      if (m.sw > (m.iw || width) + 1) {
        const rec = { tag, status: 'OVERFLOW', sw: m.sw, iw: m.iw, delta: m.sw - (m.iw || width), title: m.title };
        checks.push(rec);
        try {
          const shot = await psend('Page.captureScreenshot', { format: 'png', captureBeyondViewport: true });
          if (shot.result && shot.result.data) writeFileSync(`${cfg.outDir}\\${route.id}_${lang}_${width}x${height}_overflow.png`, Buffer.from(shot.result.data, 'base64'));
        } catch {}
        console.log(`OVERFLOW ${tag} delta=${rec.delta}px (sw=${m.sw} iw=${m.iw}) "${m.title}"`);
        overflowCount++;
      } else {
        okCount++;
        checks.push({ tag, status: 'OK', sw: m.sw, iw: m.iw });
        console.log('OK     ' + tag + ` (sw=${m.sw} iw=${m.iw})`);
      }
      return;
    }
  }

  for (const route of cfg.routes) {
    for (const lang of (route.langs || ['pt-BR'])) {
      for (const vp of cfg.viewports) {
        await probe(route, lang, vp[0], vp[1]);
      }
    }
  }

  writeFileSync(`${cfg.outDir}\\design-audit-results.json`, JSON.stringify(checks, null, 2));
  const total = checks.length;
  const bad = overflowCount + authFailCount + httpFailCount;
  console.log(`AUDIT-DONE checks=${total} ok=${okCount} overflow=${overflowCount} authfail=${authFailCount} httpfail=${httpFailCount}`);
  pws.close();
  edge.kill();
  for (let i = 0; i < 10; i++) {
    try { rmSync(userDataDir, { recursive: true, force: true }); break; }
    catch { await sleep(700); }
  }
  process.exit(bad > 0 ? 1 : 0);
} catch (e) {
  console.error('ERRO-FATAL: ' + (e && e.message));
  try { pws.close(); } catch {}
  try { edge.kill(); } catch {}
  try { rmSync(userDataDir, { recursive: true, force: true }); } catch {}
  process.exit(2);
}
