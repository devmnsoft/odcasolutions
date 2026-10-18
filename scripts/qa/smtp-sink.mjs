// D-OC44 (Sprint 10): sink SMTP mínimo para o ensaio do transporte real de notificações.
// Uso: node scripts/qa/smtp-sink.mjs --port 2537 --inbox <diretorio> [--max 3]
// Implementa apenas o protocolo necessário para o SmtpClient (.NET) sem AUTH/STARTTLS:
// 220/250/354/221. Cada mensagem recebida é salva como <inbox>/<seq>.eml e toda a
// conversa de protocolo é registrada em <inbox>/../protocol.log (transcript).
// Termina sozinho após --max mensagens ou SIGTERM/SIGINT. Nenhum dado é persistido fora
// da pasta informada; os endereços são sintéticos (rehearsal.test).
import net from 'node:net';
import fs from 'node:fs';
import path from 'node:path';

function arg(name, fallback = undefined) {
  const i = process.argv.indexOf(`--${name}`);
  return i >= 0 && i + 1 < process.argv.length ? process.argv[i + 1] : fallback;
}

const port = Number(arg('port', '2537'));
const inboxDir = path.resolve(arg('inbox', '.local/smtp-sink-inbox'));
const maxMessages = Number(arg('max', '3'));
const logPath = path.join(path.dirname(inboxDir), 'protocol.log');

fs.mkdirSync(inboxDir, { recursive: true });
let received = 0;
let seq = 0;
let server = null;
let finishing = false;

function log(line) {
  const stamped = `[${new Date().toISOString()}] ${line}`;
  fs.appendFileSync(logPath, stamped + '\n');
  console.log(stamped);
}

function respond(socket, text) {
  socket.write(text.endsWith('\r\n') ? text : text + '\r\n');
  log(`S: ${text.replace(/\r?\n/g, ' | ')}`);
}

function finish() {
  if (finishing) return;
  finishing = true;
  try { server?.close(); } catch { /* já encerrado */ }
  log(`SINK-DONE received=${received} max=${maxMessages}`);
  fs.writeFileSync(path.join(inboxDir, '..', 'sink-done.txt'), `received=${received}\n`);
  setTimeout(() => process.exit(0), 100).unref();
}

server = net.createServer((socket) => {
  const peer = `${socket.remoteAddress}:${socket.remotePort}`;
  let state = 'cmd';
  let dataBuf = '';
  let mailFrom = '';
  const rcptTo = [];
  log(`C nova conexão ${peer}`);
  // Saudação inicial obrigatória: sem o 220 o cliente fica esperando e morre no
  // timeout (foi exatamente isso que a primeira rodada do ensaio expôs).
  respond(socket, '220 odca-smtp-sink ESMTP ready');

  socket.on('data', (chunk) => {
    let text = chunk.toString('binary');
    if (state === 'data') {
      // Acumulador unico: re-acrescentar o historico a cada chunk duplicava o conteudo.
      dataBuf += text;
      const idx = dataBuf.indexOf('\r\n.\r\n');
      if (idx === -1) return;
      const message = dataBuf.slice(0, idx);
      dataBuf = '';
      state = 'cmd';
      seq += 1;
      received += 1;
      const file = path.join(inboxDir, `${String(seq).padStart(2, '0')}.eml`);
      // O corpo chega em base64/quoted-printable conforme Content-Transfer-Encoding;
      // preserva os bytes recebidos tal qual para inspecao fiel no ensaio.
      fs.writeFileSync(file, Buffer.from(message, 'binary'));
      log(`SINK-MSG seq=${seq} from=${mailFrom} to=[${rcptTo.join(',')}] bytes=${message.length} file=${path.basename(file)}`);
      respond(socket, '250 OK: message queued');
      if (received >= maxMessages) finish();
      return;
    }

    let newlineIdx;
    while ((newlineIdx = text.indexOf('\r\n')) !== -1) {
      const line = text.slice(0, newlineIdx);
      text = text.slice(newlineIdx + 2);
      if (line.length === 0) continue;
      log(`C: ${line}`);
      const up = line.toUpperCase();
      if (up.startsWith('EHLO')) {
        respond(socket, '250-odca-smtp-sink ESMTP\r\n250 8BITMIME');
      } else if (up.startsWith('HELO')) {
        respond(socket, '250 odca-smtp-sink');
      } else if (up.startsWith('MAIL FROM:')) {
        mailFrom = line.slice(10).trim().replace(/^<|>$/g, '');
        rcptTo.length = 0;
        dataBuf = '';
        respond(socket, '250 OK');
      } else if (up.startsWith('RCPT TO:')) {
        rcptTo.push(line.slice(8).trim().replace(/^<|>$/g, ''));
        respond(socket, '250 OK');
      } else if (up === 'DATA') {
        state = 'data';
        dataBuf = '';
        respond(socket, '354 End data with <CR><LF>.<CR><LF>');
      } else if (up === 'RSET' || up === 'NOOP') {
        respond(socket, '250 OK');
      } else if (up.startsWith('QUIT')) {
        respond(socket, '221 Bye');
        socket.end();
      } else {
        respond(socket, '250 OK');
      }
    }
  });

  socket.on('error', (err) => log(`C erro ${peer}: ${err.message}`));
  socket.on('close', () => log(`C fechou ${peer}`));
});

server.listen(port, '127.0.0.1', () => log(`SINK-READY port=${port} inbox=${inboxDir}`));
process.on('SIGINT', finish);
process.on('SIGTERM', finish);
