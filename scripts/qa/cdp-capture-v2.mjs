// D-OC31 fase C v2 — capturas headless (msedge CDP) com login FRESO via HTTP por sessao.
// Correcao: __Host-odca-session exige secure:true no Network.setCookies; sessao obsoleta era revogada.
import { spawn } from 'node:child_process';
import { writeFileSync, mkdirSync, rmSync, readFileSync } from 'node:fs';
import crypto from 'node:crypto';

const cfgPath = process.argv[2];
let cfgText = readFileSync(cfgPath, 'utf8');
if (cfgText.charCodeAt(0) === 0xFEFF) cfgText = cfgText.slice(1);
const cfg = JSON.parse(cfgText);
const CDP_PORT = 9336;
const sleep = (ms) => new Promise((r) => setTimeout(r, ms));
const esc = encodeURIComponent;

const userDataDir = `C:\\Users\\NCELL-DEV-020\\AppData\\Local\\Temp\\opencode\\edge-profile-${Date.now()}`;
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
    // replay-protection: o passo TOTP aceito na API nao vale de novo -> espera passo novo ao repetir.
    let done = false; let postedStep = -1; let lastBody = '';
    for (let posts = 1; posts <= 4 && !done; posts++) {
      // anti-replay: o passo TOTP aceito na confirmacao da inscricao nao vale de novo;
      // espera o fim da janela atual antes de reenviar com codigo fresco.
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
      const code = totp(mfaKey);
      const rm = await fetch(cfg.base + '/mfa/desafio', {
        method: 'POST', redirect: 'manual',
        headers: { 'content-type': 'application/x-www-form-urlencoded', cookie: cookieHeader(jar), requestverificationtoken: af2 },
        body: 'Code=' + esc(code) + '&ReturnUrl=&__RequestVerificationToken=' + esc(af2),
      });
      const rmText = await rm.text().catch(() => '');
      for (const c of parseCookies(rm)) jar.push(c);
      if (rm.status === 302) { done = true; }
      else {
        lastBody = rmText.replace(/[\s]+/g, ' ').slice(0, 200);
        console.log('MFA attempt post=' + posts + ' step=' + step + ' status=' + rm.status + ' (aguardando passo novo se repetido)');
      }
    }
    if (!done) throw new Error('desafio MFA nao confirmado para ' + email + (lastBody ? ' ultimo=' + lastBody : ''));
    if (!done) throw new Error('desafio MFA nao confirmado para ' + email);
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
let idc = 0; const pending = new Map(); let handlers = [];
ws.addEventListener('message', (ev) => {
  const msg = JSON.parse(typeof ev.data === 'string' ? ev.data : ev.data.toString());
  if (msg.id && pending.has(msg.id)) { pending.get(msg.id)(msg); pending.delete(msg.id); }
  else if (msg.method) { for (const h of handlers) h(msg); }
});
function send(method, params = {}) {
  return new Promise((res) => {
    const id = ++idc;
    pending.set(id, res);
    ws.send(JSON.stringify({ id, method, params }));
  });
}
function once(methodName, timeoutMs = 45000) {
  return new Promise((res) => {
    const h = (msg) => {
      if (msg.method === methodName) { handlers = handlers.filter((x) => x !== h); clearTimeout(t); res(msg.params); }
    };
    const t = setTimeout(() => { handlers = handlers.filter((x) => x !== h); res(null); }, timeoutMs);
    handlers.push(h);
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
let pidc = 0; const ppending = new Map(); let phandlers = [];
pws.addEventListener('message', (ev) => {
  const msg = JSON.parse(typeof ev.data === 'string' ? ev.data : ev.data.toString());
  if (msg.id && ppending.has(msg.id)) { ppending.get(msg.id)(msg); ppending.delete(msg.id); }
  else if (msg.method) { for (const h of phandlers) h(msg); }
});
function psend(method, params = {}) {
  return new Promise((res) => {
    const id = ++pidc;
    ppending.set(id, res);
    pws.send(JSON.stringify({ id, method, params }));
  });
}
function ponce(methodName, timeoutMs = 45000) {
  return new Promise((res) => {
    const h = (msg) => {
      if (msg.method === methodName) { phandlers = phandlers.filter((x) => x !== h); clearTimeout(t); res(msg.params); }
    };
    const t = setTimeout(() => { phandlers = phandlers.filter((x) => x !== h); res(null); }, timeoutMs);
    phandlers.push(h);
  });
}
await psend('Page.enable');
await psend('Network.enable');

// ---------- sessoes fresas ----------
const jars = {};
try {
  const cCli = cfg.creds.cliente;
  jars.cliente = await httpLogin(cCli.email, cCli.pass, '');
  console.log('LOGIN cliente ok (' + jars.cliente.length + ' cookies)');
  const cOp = cfg.creds.operador;
  jars.operador = await httpLogin(cOp.email, cOp.pass, cOp.mfaKey || '');
  console.log('LOGIN operador ok (' + jars.operador.length + ' cookies)');

  let okCount = 0, failCount = 0; const fails = [];
  for (const screen of cfg.screens) {
    for (const lang of cfg.langs) {
      for (const vp of cfg.viewports) {
        const ok = await shoot(screen, lang, vp[0], vp[1], 1, `${screen.id}_${lang}_${vp[0]}x${vp[1]}.png`);
        ok ? okCount++ : (failCount++, fails.push(`${screen.id}/${lang}/${vp[0]}x${vp[1]}`));
      }
    }
    const z = cfg.zoomAt;
    for (const lang of cfg.langs) {
      const ok = await shoot(screen, lang, z[0], z[1], 2, `${screen.id}_${lang}_${z[0]}x${z[1]}_2x.png`);
      ok ? okCount++ : (failCount++, fails.push(`${screen.id}/${lang}/${z[0]}x${z[1]}_2x`));
    }
  }
  console.log(`CAPTURES-DONE ok=${okCount} fail=${failCount}` + (fails.length ? ' fails=' + fails.join('|') : ''));

  async function shoot(screen, lang, width, height, dsf, fileName) {
    const tag = `${screen.id}/${lang}/${width}x${height}@${dsf}x`;
    try {
      const url = cfg.base + screen.path;
      for (let attempt = 0; attempt < 2; attempt++) {
        const jar = screen.session === 'anon' ? [] : (jars[screen.session] || []);
        await psend('Network.clearBrowserCookies');
        const cookies = jar.map((c) => ({ name: c.name, value: c.value, domain: 'localhost', path: '/', secure: true }));
        cookies.push({ name: 'odca_lang', value: lang, domain: 'localhost', path: '/' });
        await psend('Network.setCookies', { cookies });
        await psend('Emulation.setDeviceMetricsOverride', {
          width, height, deviceScaleFactor: dsf, mobile: width < 500, screenWidth: width, screenHeight: height,
        });
        const loaded = ponce('Page.loadEventFired');
        await psend('Page.navigate', { url });
        await Promise.all([loaded, sleep(4500)]);
        const locRes = await psend('Runtime.evaluate', { expression: 'location.href' });
        const loc = (locRes.result && locRes.result.result && locRes.result.result.value) || '';
        if (screen.session === 'anon' || !loc.includes('/entrar')) break;
        if (attempt === 0) {
          const cr = cfg.creds[screen.session];
          console.log('RELOGIN ' + tag + ' (sessao expirada)');
          jars[screen.session] = await httpLogin(cr.email, cr.pass, cr.mfaKey || '');
          continue;
        }
        console.log('STILL-REDIRECTED ' + tag + ' url=' + loc);
      }
      await sleep(1500);
      const shot = await psend('Page.captureScreenshot', { format: 'png', captureBeyondViewport: true });
      if (shot.result && shot.result.data) {
        writeFileSync(`${cfg.outDir}\\${fileName}`, Buffer.from(shot.result.data, 'base64'));
        console.log('OK   ' + tag);
        return true;
      }
      console.log('FAIL ' + tag + ' (sem dados) ' + JSON.stringify(shot.error || {}));
      return false;
    } catch (e) {
      console.log('FAIL ' + tag + ' : ' + (e && e.message));
      return false;
    }
  }

  pws.close();
  edge.kill();
  // Windows: o processo do Edge ainda segura handles do profile ao morrer; tenta algumas vezes.
  for (let i = 0; i < 10; i++) {
    try { rmSync(userDataDir, { recursive: true, force: true }); break; }
    catch { await sleep(700); }
  }
  process.exit(failCount > 0 ? 1 : 0);
} catch (e) {
  console.error('ERRO-FATAL: ' + (e && e.message));
  try { pws.close(); } catch {}
  try { edge.kill(); } catch {}
  try { rmSync(userDataDir, { recursive: true, force: true }); } catch {}
  process.exit(2);
}
