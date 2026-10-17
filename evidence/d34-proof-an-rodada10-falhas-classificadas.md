# D-OC34 — Rodada 10 da prova A–N: 31 falhas classificadas (17/10/2026)

Rodada 10 (`fails=31`, fim-marker `PROVA-FIM fails=31 ts=2026-10-17T08:05:45`). Nenhuma falha de
produto: as 31 caem em três grupos com causa raiz comprovada e cura aplicada no script antes da
rodada 11. Evidências locais: `%TEMP%\opencode\evidence\d34-proof-a.txt`, `d34-a1-trace.log`
(marcas WC-inicio/WC-done por curl), `d34-proof-run-err.log`, `d34-suite-freshdb-r2.log`.

## Grupo A — 6 falhas: causas raiz próprias (curadas na rodada 11)

| Check | Causa | Cura |
|---|---|---|
| P0b | `odca.support_session_events` e `odca.privacy_request_events` não têm coluna `tenant_id`; o DELETE genérico falhava nelas (auditoria completa das 49 tabelas da lista de purga via `information_schema` confirmou que só essas duas) | deletes dedicados por referência: `session_id IN (… support_sessions …)` e `privacy_request_id IN (… privacy_requests …)`, executados antes do loop |
| B6-1, B6-3, B6-4, B6-5 | a purga apaga `odca.patients`, mas o paciente fixture (`75c87013-…`, "Ana Paula Ferreira da Costa") foi criado via API em D-OC17/GATE3 e o `ODCA_SETUP_ON_START` não recria pacientes → picker vazio, vincular sem alvo | reinsert determinístico no P0b com dados sintéticos (CPF `11144777735`, `created_by/updated_by` = conta cliente do tenant, `ON CONFLICT (id) DO NOTHING`) + assert count=1 dentro do check |
| J0 | bug de quoting de argumentos nativos do PowerShell 5.1: argumento de `psql -c` contendo **aspas duplas** (o JSON dos campos NDA) chega corrompido ao psql (`aviso: argumento extra de linha de comando "Alfa" ignorado`); o erro do cast `::jsonb` era engolido pelo `Out-Null` → UPDATE silenciosamente nulo (`values=[]`) | novo helper `DbFile` (escreve a SQL em `.sql` no disco e roda `psql -v ON_ERROR_STOP=1 -f arquivo`); o check J0 agora reporta `upd=` e `err=` para nunca mais haver no-op silencioso |

Reprodução independente (rodada em curso, sem afetar estado): `d34-j0test.sql` via `psql -f`
aplica o UPDATE corretamente; `d34-diag4.ps1` reproduz a criação do docx isoladamente.

## Grupo B — 15 falhas: cascata única do J0

J0 sem efeito → gerar bloqueado ("O campo obrigatório 'Parte A' está pendente") → `$vidB` vazio →
tudo que depende da versão falha com 405/uuid vazio: J1, J2, J2b, J4, J5 (`uuid: ""`), J5a, J6,
J7, B8, B9-1, B9-2, B10-1…B10-5. Com o J0 curado, a cadeia inteira é reavaliada na rodada 11.

## Grupo C — 10 falhas: fase OCR/X/T/N — ambiente + 2 bugs de script

| Check | Causa | Cura |
|---|---|---|
| X0b | o script carregava só `System.IO.Compression.FileSystem`; o enum `[IO.Compression.ZipArchiveMode]` vive no assembly **base** `System.IO.Compression`, não resolvido no PS 5.1 → `New-Object ZipArchive` falhava (erro visível em `d34-proof-run-err.log`: "Não é possível localizar o tipo [IO.Compression.ZipArchiveMode]") e o zip ficou com 0 bytes | `Add-Type` do assembly base antes do FileSystem + `$fsZip.Dispose()` após `$za.Dispose()`; reprodução isolada gera 610 bytes |
| X1-1, X1-2, X2-1, X2-2, X3, T1, N1, N2 | **autoinfligido**: a suíte de integração (`run-integration-tests.ps1 -FreshDb`) foi lançada em paralelo à prova; ela mata todo `dotnet.exe` cujo CommandLine contém a raiz do repo antes do build → derrubou API **e WEB** da prova às ~07:48. O início da fase OCR usava só `Ensure-Api` (não garantia a WEB), e com a web morta todas as subidas a `:5144` voltaram connection refused (trace: `WC-done exit=7` às 07:49 e 08:04–08:05; os 6 uploads com `n_args=19`). A espera cega de `Wait-Ready` (90×2 s) ainda escondeu ~15 min entre o kill e o primeiro upload | fase OCR agora usa `Ensure-Stack` (API + WEB); `Wait-Ready` reduzido a 45×2 s (~90 s de teto); **regra de processo: nunca rodar a suíte de integração em paralelo à prova A–N** |

Além disso, dois checks consultavam o banco com concatenação no ponto de chamada
(`Db "…preparation_id='" + $var + "'"`, onde o `+` liga depois da invocação) — mesmo padrão de
precedência que já havia quebrado o `-join` do J0. Corrigidos com parênteses: L. J7 e L. X1-2.

## Repetibilidade

- Toda rodada parte do zero: purga determinística (P0b) + boot com `ODCA_SETUP_ON_START=true`.
- Comando da prova: `powershell -NoProfile -ExecutionPolicy Bypass -File scripts\qa\validate-d34-an-web.ps1`
  (cópia sincronizada deste script); saída em `evidence\d34-proof-a.txt` + fim-marker.
- A rodada 11 aplica todas as curas acima e roda **sozinha** (sem suíte em paralelo).

## Adendo — releituras do mesmo dia (rodadas 11 e 12 interrompidas)

- **P0b**: `odca.privacy_request_events` também não tinha `tenant_id`. Auditoria completa das
  49 tabelas da lista de purga via `information_schema` confirmou que só essas duas tabelas
  exigem delete dedicado (já aplicado). P0b passou limpo na sequência.
- **J0 passou** (`upd=UPDATE 1`, campos=8), mas o gerar continuou bloqueado com
  `"O valor de 'Documento da Parte B' é inválido."`: o campo de documento (tipo 5) valida
  **dígito verificador** CPF/CNPJ e o valor sintético `11144777735` não é um CPF válido. O
  fixture do J0 agora usa documentos sintéticos válidos (CPF `52998224725`; o CNPJ
  `12345678000195` já era válido) — validação dos dígitos verificadores reproduzida localmente
  antes do relançamento.
- **A1 MFA (rodada FINAL-2 abortada no login web)**: o POST do desafio voltou 400. A fonte
  (`MfaService.VerifyChallengeAsync`) revelou anti-replay: qualquer código cujo timestep seja
  `<=` o último timestep aceito — a confirmação da inscrição do A0b conta — é rejeitado como
  "Código de autenticação já utilizado". Quando confirmação da inscrição e desafio caem na
  **mesma janela TOTP de 30 s** (rodadas rápidas), o código idêntico é rejeitado. Corrigido:
  (a) o retry agora espera de fato a próxima janela TOTP antes de tentar (código fresco) — o
  loop antigo desperdiçava 5 de 6 tentativas num guarda de 30 s que não avançava;
  (b) o corpo da resposta de falha é capturado (`mfa-challenge-resp.html`) e a mensagem de erro
  entra no trace e na exceção; (c) o intervalo de polling do `Watch-Curl` caiu de 5 s para
  300 ms — os ~5,1 s por curl eram artefato desse poler, não da rede (medidos ~50–70 ms), e
  mascaravam o timing real nos traces.

## Adendo — rodadas finais FINAL-3 → FINAL-6 (convergência para fails=0) e capturas

**FINAL-3 (`fails=15`, ts 08:50:52)** — primeiro `vidB` real; T/N 100% limpo pela primeira vez
(T1 `extraction-timeout`, N1 `pdf-no-text-layer`, N2 `extractor-not-configured`).

- **J2b `plan_restricted`**: o tenant da prova está em `basic v2`, que declara
  `module.reviews = f` no `plan_entitlements`; só o `enterprise v2` habilita o módulo de
  revisões. O gate lê a assinatura ativa via `odca.organization_feature_state` → sem plano
  adequado o BFF redireciona de volta à versão sem criar revisão (302 enganoso de sucesso).
  Cura: fixture determinística no P0b atualiza `odca.subscriptions` para o plan version
  published do enterprise v2 + assert `plan-now = 'enterprise v2'` dentro do check (ativação ≠
  pagamento; catálogo de planos intacto).
- **X1-2**: wkhtmltopdf criava página vazia extra quando o último bloco tem
  `page-break-after: always` (PDF com 26 páginas em vez de 25) e o check hardcodeava 25.
  Cura: quebra só entre páginas (PDF exatamente 25) + comparação dinâmica contra o total
  medido pelo próprio check.
- Demais 13 = cascata do J2b (J4, J5, J5a, J6, J7, B8, B9-1, B9-2, B10-1…B10-5). B9-2 não era
  bug próprio: o badge "Concluído" depende de `review_status = externally_signed`, produto da
  cadeia J.

**FINAL-4 (`fails=12`, ts 09:10:22)** — J0→J4 verdes pela primeira vez (versão
`internally_approved`). Restante = contato do participante + cascata.

- **J5**: a API `PUT …/signature-preparation` valida contato do participante — sem e-mail nem
  telefone volta 400 `participants[0].contact` ("Informe e-mail ou telefone.") e o BFF faz
  redirect back; o formulário da prova enviava e-mail vazio → `row=` vazio. Validação offline:
  chamada direta com token de raspagem + e-mail sintético (`profissional.responsavel.prova@odca.local`)
  e `expectedVersion=0` → 200 `confirmed`/`version=1`. Corrigido no formulário.
  Nota de contaminação: durante a janela B10 daquela rodada um diagnóstico offline criou a
  preparação e assinou a versão (estado purgado na rodada seguinte; não afeta a evidência final).

**FINAL-5 (`fails=5`, ts 09:25:54)** — jornada completa J0→J7 + B8/B9 verdes; só B10-1…B10-5
falharam, todos por defeitos de check/fixture, não de produto:

1. A query atribuía a linha por `payload::text LIKE '%<versionId>%'` — **não existe coluna
   `payload`** (o erro do psql virava count=0 silencioso). O payload real
   (`protected_payload`) é protegido por DataProtection e não contém os ids em texto puro.
   Atribuição corrigida pela **`source_id` determinística**
   `SHA-256('signature.completed|<preparação>|<versão>')[0..16]`
   (`NotificationEnqueue.DeterministicSource`), reproduzida no script em PS e verificada
   byte a byte contra a linha real (`bd86375f-…`).
2. Diretório de pickup do transporte local hardcoded em `src\Odca.Worker\.local\notifications`
   (legado do caminho relativo do `appsettings.Development.json`); a runtime config em uso
   (`development-runtime.json`) sobrescreve `Notifications:DevelopmentPickupDirectory` com
   caminho absoluto sob `%TEMP%` — a notificação despachada (`sent`, attempt 1) ficava lá. O
   check agora lê o diretório do JSON efetivamente carregado, e o P0b limpa artefatos `*.txt`
   do diretório (a purga de banco não alcança arquivo; rodada anterior cascaria com o assunto).
3. O canal de e-mail por tipo é **opt-in**: `odca.notification_channel_state` dá
   `channel_email = false` como padrão — sem preferência explícita o ato commita evento +
   auditoria mas a outbox fica vazia *sem erro* (apenas warning de log). Fixture determinística
   no P0b dá opt-in do criador (conta cliente) para `signature_completed`
   (upsert sobre PK `(tenant_id,user_id,kind)`) + assert da função `(t,t,t)`.

**FINAL-6 (`fails=0`, ts 09:42:59)** — todos os 61 checks PASS. Transcript completo arquivado
no repositório: `evidence/d34-proof-an-rodada-final.txt` (marker `PROVA-FIM fails=0`).

**Execução de capturas (100 PNGs)** — o driver CDP falhou na 1ª execução pelo mesmo anti-replay
TOTP, com causa própria: o loop de retry fazia 12 iterações de skip de 2 s (~22 s) — menos que
uma janela TOTP de 30 s se o primeiro POST saísse no começo da janela — esgotando tentativas
sem jamais ver um passo novo. Cura no `scripts/qa/cdp-capture-v2.mjs`: esperar o fim da janela
atual antes de reenviar (código fresco), no máximo 4 passos novos, e o corpo da última resposta
de falha entra na exceção. Com a cura: `CAPTURES-DONE ok=100 fail=0` (5 telas × 360/768/1280/1440
+ zoom 200% × 4 idiomas, sessões MFA reais). Secundário: a limpeza do perfil Edge temporário
passa a repetir após `edge.kill()` (EPERM no Windows enquanto o processo morre).
