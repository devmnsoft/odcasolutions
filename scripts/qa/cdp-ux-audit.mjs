// D-OC54 - Sprint 20 / F-2: auditoria de UX em navegador real (headless msedge CDP).
// Por rota x idioma x viewport (360 e 1280) mede quatro familias:
//  (1) CONTRASTE WCAG AA: par textoxfundo efetivo de todo elemento com texto proprio
//      (fundo composto subindo a arvore ate o primeiro opaco; gradiente tratado como
//      superficie opaca branca — aprox. conservadora registrada); limiar 4.5 normal /
//      3.0 grande (>=24px ou >=18.66px bold);
//  (2) FORMS: todo input/select/textarea visivel precisa de rotulo (label[for], label
//      envolvente, aria-label ou aria-labelledby);
//  (3) ESTADOS VAZIOS: tabela visivel sem linhas precisa de estado vazio (classe *empty*)
//      na regiao mais proxima;
//  (4) LOADING: presenca de [role=status] (afordance de carregamento) por rota — informativo.
// PNG somente das falhas. Mesma disciplina de sessao/authfail/httpfail/circuit-breaker do
// cdp-design-audit.mjs (D-OC40/D-OC51). Sem capturar tela de sucesso.
import { spawn } from 'node:child_process';
import { writeFileSync, mkdirSync, rmSync, readFileSync } from 'node:fs';
import crypto from 'node:crypto';

const cfgPath = process.argv[2];
let cfgText = readFileSync(cfgPath, 'utf8');
if (cfgText.charCodeAt(0) === 0xFEFF) cfgText = cfgText.slice(1);
const cfg = JSON.parse(cfgText);
const CDP_PORT = 9338;
const sleep = (ms) => new Promise((r) => setTimeout(r, ms));
const esc = encodeURIComponent;

const userDataDir = `C:\\Users\\NCELL-DEV-020\\AppData\\Local\\Temp\\opencode\\edge-ux-${Date.now()}`;
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
function psend(method, params = {}, timeoutMs = 30000) {
  return new Promise((res, rej) => {
    const id = ++pidc;
    const t = setTimeout(() => { ppending.delete(id); rej(new Error('CDP timeout em ' + method)); }, timeoutMs);
    ppending.set(id, (m) => { clearTimeout(t); res(m); });
    pws.send(JSON.stringify({ id, method, params }));
  });
}
await psend('Page.enable');
await psend('Network.enable');

// ---------- medidor de pagina ----------
// String pura (sem template literal) para manter o Runtime.evaluate simples e auditavel.
var UX_PROBE = '(function(){'
  + 'function lum(rgb){var a=rgb.map(function(v){v/=255;return v<=0.03928?v/12.92:Math.pow((v+0.055)/1.055,2.4)});return 0.2126*a[0]+0.7152*a[1]+0.0722*a[2];}'
  + 'function parseColor(s){if(!s)return null;if(s.indexOf("rgb")===0){var m=s.match(/rgba?\\(([^)]+)\\)/);if(!m)return null;var p=m[1].split(",").map(function(x){return parseFloat(x)});return{r:p[0],g:p[1],b:p[2],a:p.length>3?p[3]:1};}return null;}'
  + 'function comp(top,base){var a=top.a;return{r:(top.r*a+base.r*(1-a)),g:(top.g*a+base.g*(1-a)),b:(top.b*a+base.b*(1-a)),a:base.a+a*(1-base.a)};}'
  + 'function shortSel(el){var s=el.tagName.toLowerCase();var i=0;while(el&&i<5){if(el.id){s="#"+el.id;break;}var c=(el.className||"").toString().trim().split("\\s+")[0];if(c)s="."+c+" > "+s;el=el.parentElement;i++;}return s;}'
  + 'function effBg(el){var cur={r:255,g:255,b:255,a:0};var n=el;'
  +   'while(n){var cs=getComputedStyle(n);var bi=cs.backgroundImage||"none";var col=parseColor(cs.backgroundColor);'
  +     'if(bi!=="none"){cur=comp({r:255,g:255,b:255,a:1},cur);}else if(col&&col.a>0){cur=comp(col,cur);}'
  +     'if(cur.a>=0.999)break;if(n===document.body)break;n=n.parentElement;}'
  +   'if(cur.a<1)cur=comp(cur,{r:255,g:255,b:255,a:1});'
  +   'return{r:Math.round(cur.r),g:Math.round(cur.g),b:Math.round(cur.b)};}'
  + 'function ratio(f,b){var L1=lum(f),L2=lum(b);var hi=Math.max(L1,L2),lo=Math.min(L1,L2);return(hi+0.05)/(lo+0.05);}'
  + 'var out={contrastMin:99,worst:"",contrastFails:[],unlabeled:[],emptyTables:0,emptyNoCta:0,statusEls:0,imgsNoAlt:0,textEls:0,sw:0,iw:0};'
  + 'var all=document.querySelectorAll("body *");'
  + 'for(var i=0;i<all.length;i++){var el=all[i];'
  +   'var hasText=false;for(var j=0;j<el.childNodes.length;j++){var nd=el.childNodes[j];if(nd.nodeType===3&&nd.nodeValue.trim().length>0){hasText=true;break;}}'
  +   'if(!hasText)continue;'
  +   'var r=el.getBoundingClientRect();if(r.width<1||r.height<1)continue;'
  +   'var cs=getComputedStyle(el);if(cs.visibility==="hidden"||parseFloat(cs.opacity)<0.05)continue;'
  +   'var f=parseColor(cs.color);if(!f||f.a<0.05)continue;'
  +   'var b=effBg(el);var rr=ratio([f.r,f.g,f.b],[b.r,b.g,b.b]);var fsz=parseFloat(cs.fontSize);var fw=parseInt(cs.fontWeight,10)||400;'
  +   'var large=fsz>=24||(fsz>=18.66&&fw>=700);var need=large?3.0:4.5;'
  +   'out.textEls++;'
  +   'if(rr<out.contrastMin){out.contrastMin=Math.round(rr*100)/100;out.worst=shortSel(el)+" :: \\"" + (el.firstChild.nodeValue||"").trim().slice(0,40) + "\\"";} '
  +   'if(rr<need&&out.contrastFails.length<12){out.contrastFails.push({sel:shortSel(el),text:(el.firstChild.nodeValue||"").trim().slice(0,40),ratio:Math.round(rr*100)/100,fg:cs.color,bg:"rgb("+b.r+","+b.g+","+b.b+")",large:large});}'
  + '}'
  + 'var fields=document.querySelectorAll("input,select,textarea");'
  + 'for(var k=0;k<fields.length;k++){var fld=fields[k];var ft=(fld.type||"").toLowerCase();if(ft==="hidden")continue;'
  +   'if(fld.offsetParent===null&&!fld.getClientRects().length)continue;'
  +   'var cs2=getComputedStyle(fld);if(cs2.display==="none"||cs2.visibility==="hidden")continue;'
  +   'var ok=!!(fld.getAttribute("aria-label")||fld.getAttribute("aria-labelledby"));'
  +   'if(!ok&&fld.closest)ok=!!fld.closest("label");'
  +   'if(!ok&&fld.id)ok=!!document.querySelector(\'label[for="\'+fld.id+\'"]\');'
  +   'if(!ok&&out.unlabeled.length<12){out.unlabeled.push({tag:fld.tagName.toLowerCase(),type:ft,name:fld.name||"",id:fld.id||"",sel:shortSel(fld)});}'
  + '}'
  + 'var tabs=document.querySelectorAll("table");'
  + 'for(var t2=0;t2<tabs.length;t2++){var tb=tabs[t2];if(tb.offsetParent===null&&!tb.getClientRects().length)continue;'
  +   'var rows=tb.querySelectorAll("tbody tr").length;if(rows>0)continue;'
  +   'out.emptyTables++;'

  +   'var found=false;zone=tb.parentElement;for(var up2=0;up2<4&&zone&&!found;up2++){if(zone.querySelector(".empty-state,[class*=empty]"))found=true;zone=zone.parentElement;}'
  +   'if(!found)out.emptyNoCta++;'
  + '}'
  + 'out.statusEls=document.querySelectorAll("[role=status],[role=alert],.spinner").length;'
  + 'out.sw=document.documentElement.scrollWidth;out.iw=window.innerWidth;'
  + 'document.querySelectorAll("img").forEach(function(im){if(!im.getAttribute("alt")&&!im.hasAttribute("aria-hidden"))out.imgsNoAlt++;});'
  + 'return JSON.stringify(out);'
  + '})()';

// ---------- sessoes fresas ----------
const jars = {};
try {
  for (const [name, cr] of Object.entries(cfg.creds || {})) {
    jars[name] = await httpLogin(cr.email, cr.pass, cr.mfaKey || '');
    console.log('LOGIN ' + name + ' ok (' + jars[name].length + ' cookies)');
  }

  const checks = [];
  let okCount = 0, failCount = 0, authFailCount = 0, httpFailCount = 0;
  let consecutiveHttpFail = 0; // circuito (padrao D-OC51): 4 falhas seguidas = servidor caido
  let worstContrast = 99; let worstTag = '';
  let totalUnlabeled = 0, totalEmptyNoCta = 0, totalImgsNoAlt = 0, totalStatusEls = 0, totalOverflow = 0;

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
      await sleep(5000);
      const locRes = await psend('Runtime.evaluate', { expression: 'location.href' });
      const loc = (locRes.result && locRes.result.result && locRes.result.result.value) || '';
      if (route.session !== 'anon' && loc.includes('/entrar')) {
        if (attempt === 0) {
          console.log('RELOGIN ' + tag + ' (sessao expirada)');
          jars[route.session] = await httpLogin(cfg.creds[route.session].email, cfg.creds[route.session].pass, cfg.creds[route.session].mfaKey || '');
          continue;
        }
        checks.push({ tag, status: 'AUTHFAIL', href: loc });
        console.log('AUTHFAIL ' + tag + ' href=' + loc);
        authFailCount++;
        return;
      }
      const stRes = await psend('Runtime.evaluate', {
        expression: '(function(){var n=performance.getEntriesByType("navigation")[0];return String(n && n.responseStatus ? n.responseStatus : "")})()',
      });
      const httpStatus = (stRes.result && stRes.result.result && stRes.result.result.value) || '';
      if (!httpStatus || Number(httpStatus) >= 400) {
        checks.push({ tag, status: 'HTTPFAIL', http: Number(httpStatus) || 0, href: loc });
        try {
          const shot = await psend('Page.captureScreenshot', { format: 'png', captureBeyondViewport: true });
          if (shot.result && shot.result.data) writeFileSync(`${cfg.outDir}\\${route.id}_${lang}_${width}x${height}_http${httpStatus || 'neterr'}.png`, Buffer.from(shot.result.data, 'base64'));
        } catch {}
        console.log(`HTTPFAIL ${tag} status=${httpStatus || '0'} href=${loc}`);
        httpFailCount++;
        if (++consecutiveHttpFail >= 4) {
          console.log('SERVIDOR-CAIU: 4 falhas de rede seguidas; encerrando rodada.');
          pws.close();
          edge.kill();
          try { rmSync(userDataDir, { recursive: true, force: true }); } catch {}
          process.exit(2);
        }
        return;
      }
      consecutiveHttpFail = 0;
      const mRes = await psend('Runtime.evaluate', { expression: UX_PROBE });
      if (mRes.result && mRes.result.exceptionDetails) throw new Error('probe exception em ' + tag + ': ' + JSON.stringify(mRes.result.exceptionDetails).slice(0, 300));
      const m = JSON.parse((mRes.result && mRes.result.result && mRes.result.result.value) || 'null');
      if (!m || typeof m.sw !== 'number' || typeof m.textEls !== 'number') throw new Error('probe sem resultado valido em ' + tag);
      const overflow = (typeof m.sw === 'number' && typeof m.iw === 'number' && m.sw > m.iw) ? 1 : 0;
      const bad = (m.contrastFails || []).length + (m.unlabeled || []).length + (m.emptyNoCta || 0) + overflow;
      if (bad > 0) {
        checks.push({ tag, status: 'UXFAIL', contrastFails: m.contrastFails, unlabeled: m.unlabeled, emptyTables: m.emptyTables, emptyNoCta: m.emptyNoCta, sw: m.sw, iw: m.iw, contrastMin: m.contrastMin, worst: m.worst, textEls: m.textEls });
        try {
          const shot = await psend('Page.captureScreenshot', { format: 'png', captureBeyondViewport: true });
          if (shot.result && shot.result.data) writeFileSync(`${cfg.outDir}\\${route.id}_${lang}_${width}x${height}_uxfail.png`, Buffer.from(shot.result.data, 'base64'));
        } catch {}
        console.log(`UX-FAIL ${tag} contrastFails=${(m.contrastFails||[]).length} unlabeled=${(m.unlabeled||[]).length} emptyNoCta=${m.emptyNoCta} overflow=${overflow} min=${m.contrastMin}`
          + (m.contrastFails && m.contrastFails[0] ? ` ex: "${m.contrastFails[0].text}" ${m.contrastFails[0].ratio}:1 @ ${m.contrastFails[0].sel}` : ''));
        failCount++;
      } else {
        okCount++;
        checks.push({ tag, status: 'OK', contrastMin: m.contrastMin, worst: m.worst, textEls: m.textEls, statusEls: m.statusEls, sw: m.sw, iw: m.iw });
        console.log(`OK     ${tag} (sw=${m.sw} iw=${m.iw} min=${m.contrastMin} textEls=${m.textEls} status=${m.statusEls})`);
      }
      if (typeof m.contrastMin === 'number' && m.contrastMin < worstContrast) { worstContrast = m.contrastMin; worstTag = tag; }
      totalUnlabeled += (m.unlabeled || []).length;
      totalEmptyNoCta += (m.emptyNoCta || 0);
      totalImgsNoAlt += (m.imgsNoAlt || 0);
      totalStatusEls += (m.statusEls || 0);
      totalOverflow += overflow;
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

  writeFileSync(`${cfg.outDir}\\ux-audit-results.json`, JSON.stringify(checks, null, 2));
  const total = checks.length;
  const bad = failCount + authFailCount + httpFailCount;
  console.log(`UX-DONE checks=${total} ok=${okCount} uxfail=${failCount} authfail=${authFailCount} httpfail=${httpFailCount} overflowTotal=${totalOverflow} worstContrast=${worstContrast}@${worstTag} unlabeledTotal=${totalUnlabeled} emptyNoCtaTotal=${totalEmptyNoCta} imgsNoAltTotal=${totalImgsNoAlt} statusElTotal=${totalStatusEls}`);
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
