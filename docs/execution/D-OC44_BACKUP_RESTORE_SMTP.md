# Sprint 10 — backup/restore real ensaiado e transporte SMTP por protocolo real (D-OC44) (18/10/2026)

Plano pós-S9: **S10** entrega os dois ensaios que o runbook do piloto (D-OC40 §6/§7) listava como pré-condição explícita e que ainda estavam abertos: **backup/restauração real ensaiado antes de produção** (risco remanescente do THREAT_MODEL: "histórico não substitui backup") e a **prova de que o canal e-mail funciona de ponta a ponta quando SMTP é configurado**, mesmo com o fornecedor externo ainda indefinido (ensaio sobre infraestrutura local com protocolo real). A decisão do canal (SMTP real em produção vs. apenas sino in-app) e a escolha do provedor seguem pendentes como itens externos.

## 1. Escopo e base

- **Base**: `codex/s00-foundation` @ `f7810a1` (Sprint 9 / D-OC43).
- **Sem nova migração** (canônico v061 intacto) e **sem mudanças** em portal, i18n, API ou Web — o delta de produto é um único ajuste no transporte de e-mail do worker (§3).
- **F-1 — ensaio de backup/restauração real** (`scripts/qa/run-backup-restore.ps1`): `pg_dump` formato custom/comprimido (`-Fc`, `pg_dump`/`pg_restore`/`psql` do PostgreSQL 18 local) de `odca_test_disposable` (banco de prova do piloto, já migrado ao v061 com dados sintéticos) → `pg_restore --no-owner --exit-on-error` para banco novo `odca_restore_check_s10` → **26 checks** de integridade comparando fonte × restaurado: versão máxima de migração (61=61), paridade de contagem em 11 tabelas-chave, fingerprints `md5(string_agg(id …))` de `users`/`contracts`/`audit_events`, superfície de leitura sob RLS como `odca_app` sem contexto de tenant (paridade das políticas vigentes) e execução de função `SECURITY DEFINER` (`support_impersonation_is_valid`) no banco restaurado. Banco restaurado é derrubado ao final; o dump binário fica somente em TEMP; transcript versionado em `evidence/d44-backup-restore-rodada-final.txt`.
- **F-2 — ensaio do transporte SMTP por protocolo real** (worker real + MTA sintético local):
  - `scripts/qa/smtp-sink.mjs`: sink SMTP mínimo sem dependências (saudação 220; EHLO/HELO/MAIL/RCPT/DATA/QUIT/NOOP/RSET; sem AUTH/STARTTLS anunciados), grava cada mensagem como `.eml` e registra toda a conversa em `protocol.log`.
  - `scripts/qa/rehearse-smtp-transport.ps1`: banco dedicado `odca_smtp_rehearsal` migrado do canônico; cenário sintético completo (tenant + usuário + contrato + obrigação aberta + lembrete com data de hoje + preferência `channel_email=true`); executa o **`Odca.Worker` real** (mesmo binário de produção, ambiente Development com overrides SMTP) apontando para o sink em `127.0.0.1:2537`.
    - **Fase positiva**: lembrete reclamado pelo worker de lembretes → notificação in-app criada → outbox enfileirada pelo worker principal → mensagem aceita pelo sink; validações sobre o `.eml`: destinatário `ensaio-smtpp@rehearsal.test`, remetente `odca-rehearsal@rehearsal.local` (nome "ODCA Rehearsal"), header `Content-Type: text/plain; charset=utf-8`, e corpo **decodificado do base64** contendo o link `/minha-conta/notificacoes` do `PublicOrigin` com caracteres não-ASCII intactos.
    - **Fase negativa** (MTA parado): mesmo fluxo → falha **controlada** na outbox (status volta a `pending`, `attempt_count>=1`, `last_error_code` populado — sem perda, com backoff) e o canal in-app continua criado, preservando a semântica do D-OC31 B.1 ("sucesso no SMTP = aceite pelo MTA; entrega final só com recibo").
  - **Ajuste de produto exposto pelo ensaio**: `SmtpNotificationTransport` passou a definir `BodyEncoding = Encoding.UTF8`. O pipeline de texto do `System.Net.Mail` codifica o corpo em US-ASCII por padrão e substituía acentos por `?`; o ensaio agora comprova pelos bytes recebidos que os caracteres não-ASCII sobrevivem.
- **Cronograma de backup proposto** (para aprovação, não definido): diário para o banco do piloto, retenção de 30 dias, com restore periódico em banco descartável usando exatamente este script. A **janela de retenção legal dos backups permanece "a definir"** na `docs/privacy/RETENTION_POLICY.md` (valor real exige aprovação) — esta sprint define procedimento e evidência, não a política.

## 2. GATE sobre a árvore final

Executado localmente sobre `codex/s00-foundation` pós-S10, sem push (CI oficial "não verificada" neste SHA):

| Etapa | Comando | Resultado |
| --- | --- | --- |
| Build Debug estrito | `dotnet build Odca.sln -c Debug -warnaserror` | **0W/0E** |
| Build Release estrito | `dotnet build Odca.sln -c Release -warnaserror` | **0W/0E** |
| Domain | `dotnet test tests/Odca.Domain.Tests` | **389/389** |
| Integração em DB nova | `scripts/qa/run-integration-tests.ps1 -FreshDb` | **234/234, TEST-EXIT=0** (baseline S9 = 234; v061 reaplicada no bootstrap) |
| Assets/i18n | `node scripts/check-assets.mjs` | **exit=0** — 10 assets, 19 chaves em 4 catálogos, round trips decimal/template (catálogos inalterados nesta sprint) |
| F-1 backup/restore | `scripts/qa/run-backup-restore.ps1` | **26/26, EXIT=0** — dump 985.729 bytes; paridades de contagem/md5 61/61; transcript em `evidence/d44-backup-restore-rodada-final.txt` |
| F-2 transporte SMTP | `scripts/qa/rehearse-smtp-transport.ps1` | **17/17, EXIT=0** — fase positiva (aceite + `.eml` validado) e negativa (falha controlada com código); transcript em `evidence/d44-smtp-transport-rodada-final.txt` |
| Auditoria de design d41 | — | **não reexecutada de propósito**: o diff da sprint não toca `src/Odca.Web`, catálogos i18n ou assets (ver §3); as 17 rotas auditadas renderizam superfície byte-idêntica à da S9, onde o resultado foi `AUDIT-DONE checks=204 ok=204 overflow=0` (transcript `evidence/d41-design-audit-s9-rodada-final.txt`) |

## 3. Alterações (arquivos)

| Arquivo | Natureza | Descrição |
| --- | --- | --- |
| `src/Odca.Worker/Transports/SmtpNotificationTransport.cs` | modificado (única mudança de produto) | `MailMessage.BodyEncoding = Encoding.UTF8` com comentário explicando a falha de US-ASCII default do `System.Net.Mail`; sem mudanças de assinatura/configuração |
| `scripts/qa/run-backup-restore.ps1` | novo | ensaio de backup/restore versionado (F-1), transcript consolidado |
| `scripts/qa/smtp-sink.mjs` | novo | MTA sintético local sem dependências para o ensaio SMTP |
| `scripts/qa/rehearse-smtp-transport.ps1` | novo | orquestração do ensaio SMTP (banco dedicado, worker real, fases positiva/negativa, limpeza) |
| `evidence/d44-backup-restore-rodada-final.txt` | novo | transcript da rodada verde do F-1 |
| `evidence/d44-smtp-transport-rodada-final.txt` | novo | transcript da rodada verde do F-2 (inclui caudas dos logs do worker e do sink) |
| `docs/execution/D-OC44_BACKUP_RESTORE_SMTP.md` | novo | este documento |
| `docs/execution/STATUS.md` / `docs/execution/BACKLOG.md` | modificados | seção da Sprint 10 |

Nenhum outro arquivo mudou: `git status` pós-entrega lista exatamente `SmtpNotificationTransport.cs` (M) + os seis itens novos acima + os dois documentos de acompanhamento.

## 4. Prova ao vivo

- **F-1 (transcript versionado)**: `pg_dump -Fc` de `odca_test_disposable` → 985.729 bytes (sha256 registrada no transcript); `pg_restore -l` lista objetos; restore em banco novo sem erros; **todas as 26 paridades iguais fonte×restaurado** (ex.: `count.audit_events 2859=2859`, `md5.users 55c5f9ec…=55c5f9ec…`, `max_version 61=61`, superfície RLS 109=109); função DEFINER executa após o restore (retorna `false` para grant inexistente — a execução sem erro é a propriedade provada); banco restaurado removido.
- **F-2 (transcript versionado)**: `.eml` real recebido do worker: `From: "ODCA Rehearsal" <odca-rehearsal@rehearsal.local>`, `To: ensaio-smtpp@rehearsal.test`, assunto MIME-encoded UTF-8, `Content-Type: text/plain; charset=utf-8`, corpo base64 que decodifica para `https://localhost:7144/minha-conta/notificacoes\n\nAbra o link para consultar a notificação na plataforma.` com os acentos intactos (2 caracteres não-ASCII confirmados por assert); outbox `sent|attempt=1`; 2 notificações in-app (um por lembrete, canal duplo). MTA parado → `pending|1|Failure sending mail.` com in-app criado.
- **Correções reais encontradas pelo próprio ensaio** (todas corrigidas nos artefatos versionados; nenhuma é mudança de produto): (1) o PowerShell 5.1 **não propaga variáveis de ambiente vazias** para processos filhos — o override vazio da pasta de pickup dev seria ignorado e o FilePickup capturaria as mensagens em vez do SMTP; resolvido com valor de espaço branco (propagado e tratado como branco pela seleção do worker); (2) saudação `220` ausente no sink fazia o `SmtpClient` morrer no timeout de 30 s; (3) acumulador de dados em `const` derrubava o node no primeiro EHLO; (4) o corpo chega em base64 (não quoted-printable) — as asserts passaram a decodificar antes de validar conteúdo.
- **Dados sintéticos**: GUIDs fixos `b1f00000-…`, e-mails `@rehearsal.test`/remetente `@rehearsal.local`, banco dedicado derrubado ao final; nada de credencial ou dado pessoal nos transcripts.

## 5. Separação de evidências e portal

- **Teste local**: GATE completo sobre este SHA (builds estritos 0W/0E, Domain 389/389, suíte `-FreshDb` 234/234 TEST-EXIT=0, assets exit=0, F-1 26/26, F-2 17/17); logs desta rodada em `C:\Users\NCELL-DEV-020\AppData\Local\Temp` (`s10-f1.log`, `s10-f2.log`, `s10-freshdb.log`; artefatos de trabalho dos ensaios em `...\Temp\opencode\evidence\d44-*`).
- **CI oficial**: **não verificada** — branch `codex/s00-foundation` sem push nesta rodada; nenhum ID de execução inventado.
- **Aceite humano**: **pendente** — escolha do provedor SMTP real (e a decisão de canal: SMTP vs. sino in-app), infraestrutura de backup de produção (destino offsite, cronograma efetivo) e a janela de retenção de backups continuam decisões externas/jurídicas.
- **Portal / `.odca/progress.json`**: **inalterado** — baseline 40% preservado; sem CI oficial verde deste SHA nem aceite real não há marco creditável nesta sprint (nenhum peso movido, IDs/pesos/baseline intactos).
- **Bloqueios externos mantidos e registrados**: CI oficial nunca verde publicada; fornecedor de assinatura "proposto, não escolhido"; SMTP real ainda é decisão (o mecanismo está comprovado neste incremento); deploy físico multi-instância; backup/restore de produção (o ensaio cobre o procedimento sobre a instância local do piloto).
