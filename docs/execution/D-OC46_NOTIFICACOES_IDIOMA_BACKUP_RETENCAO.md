# Sprint 12 — conteúdo das notificações no idioma do destinatário + backup pré-piloto com retenção (D-OC46) (18/10/2026)

Entrega as duas funcionalidades restantes do plano até a estreia que não exigem decisão externa: (F-1) o **conteúdo das notificações** — título in-app, corpo e assunto/corpo do e-mail — passa a ser produzido no **idioma preferido do destinatário** (`user_notification_settings.preferred_language`, default pt-BR), do enfileiramento ao transporte; e (F-2) o ensaio de backup/restore ganha **retenção do dump validado** (`-KeepDumpDir`), deixando o procedimento pré-piloto do dia 1 completo: backup → restore → paridade → cópia carimbada retida com hash.

## 1. Escopo e base

- **Base**: `codex/s00-foundation` @ `cef18e0` (Sprint 11 / D-OC45).
- **Nova migração v062** (canônico v061 intacto): coluna `language` na outbox + funções de lembrete por idioma (§3).
- **19 chaves i18n × 4 culturas** (`notif.title.*` ×10, `notif.body.*` ×9) via delta versionado `scripts/i18n/d-s12-notifications.json`, mais atualização da chave existente `notif.lang_help` → catálogos em **2746 chaves/cultura, paridade 4/4**.
- **F-2**: `-KeepDumpDir <dir>` em `scripts/qa/run-backup-restore.ps1` (§4).
- Fora do escopo, decidido e registrado: convites por e-mail permanecem pt-BR (evento anterior à conta, sem configuração de destinatário) e a dívida i18n pt-first das áreas operacionais segue aberta (D-OC42/43).

## 2. F-1 — idioma resolvido na origem do evento

Decisão central: o idioma é resolvido **onde o evento acontece** (call site da API), nunca a posteriori no worker — a preferência pode mudar entre o enfileiramento e o despacho, e o conteúdo armazenado (título in-app) deve ser consistente com o e-mail que nasce do mesmo evento.

- `NotificationEnqueue.RecipientLanguageAsync`: 2 overloads (dentro da transação do call site e sem transação, abrindo tx curta com GUC de tenant — necessário porque `user_notification_settings` tem RLS FORÇADO sob `odca_app`). **Nunca lança**: qualquer falha cai em fallback pt-BR — idioma é apresentação, não requisito de sucesso do evento de negócio.
- `EnqueueEmailAsync` resolve o idioma internamente e grava a coluna `language` no INSERT da outbox — a **mesma** string alimenta título in-app e coluna, garantindo paridade título×coluna por construção.
- Overloads legados com literais pt-BR preservados (testes que fixam o comportamento anterior continuam usando-os).
- Os 8 call sites foram migrados: `ContractStudioController` (×4: envelope enviado/pendente/concluída + assinatura), `ReviewRequestsController` (×2: reatribuída/submetida e decisão), `SolicitationsController` (×2: aberta/atualizada).
- **Workers/transports**: `OutboxItem.Language` entra no SELECT do dispatch (vazio → normaliza pt-BR) e viaja por `INotificationTransport.SendUserNotificationAsync(..., language)` — mudança de assinatura nos 4 transports: `SmtpNotificationTransport` localiza a sentença do corpo via `LocalizationService` (DI nova, singleton no `Program.cs` do Worker); `FilePickupNotificationTransport` registra "Idioma:" no artefato; `StubProductionNotificationTransport` ignora (falha honesta inalterada).
- **Datas nas notificações**: `en-US` → `MM/DD/YYYY`; demais culturas → `DD/MM/YYYY`.
- `notif.lang_help` atualizada nas 4 culturas para descrever o efeito real (texto + formatos de data/número).

## 3. F-1 — migração v062

- `odca.notification_outbox`: `language varchar(16) NOT NULL DEFAULT 'pt-BR'` + `CHECK language IN ('pt-BR','en-US','fr-FR','es-ES')`.
- `claim_notification(text,integer)` redefinida para também retornar `language`; `complete_obligation_reminder` e `complete_renewal_reminder` reescritas com **CASE por idioma** (LEFT JOIN em `user_notification_settings`, default pt-BR) — como SECURITY DEFINER donas de superuser leem a tabela de settings sem GUC, seguro para os workers.
- Strings das funções SQL mantêm a ortografia **sem acento**, consistente com o estilo existente dessas funções ("Renovacao", "Echeance le", "Vence el"); a acentuação canônica vive nos catálogos JSON (in-app UI, corpo SMTP e preferências). Decisão registrada, não acidente.
- Checksum do bloco: `11168e2afba1b6d9449696aa52e88f294c1556372631bceefa19277528465380` (ponto fixo do algoritmo do `DatabaseMigrator`: SHA-256 UTF-8 minúsculo do body com CRLF→LF e todas as ocorrências do hex declarado substituídas por `REPLACE_WITH_SHA256`; verificado após a troca draft→real — a primeira tentativa tinha o checksum sem aspas no INSERT e foi revertida/recalculada). 62 blocos no canônico; blocos 1–61 byte-invariantes.
- Convenção de release cumprida: `database/releases/odca-v062.sql` snapshot byte-idêntico ao canônico; `DatabaseSchema.CurrentVersion = 62`; `MigrationChecksumTests` ganhou o fato V062 (snapshot, checksum, blocos anteriores invariantes) e o teste V061 perdeu a asserção de igualdade total, ganhando `DoesNotContain("ODCA-MIGRATION 062")`.

## 4. F-2 — backup pré-piloto com retenção

- `run-backup-restore.ps1 -KeepDumpDir <dir>`: após **todos** os checks de paridade passarem (cópia só de dump já validado), o dump `.Fc` é copiado para `<dir>` com carimbo `yyyy-MM-dd_HHmmss`; bytes e sha256 da cópia entram no log do check `dump preservado em KeepDumpDir`. Sem o parâmetro o comportamento é **idêntico ao S10** (dump só em TEMP, removido ao final) — 26 checks inalterados + 1 novo = 27.
- A janela legal de retenção continua "a definir" (`docs/privacy/RETENTION_POLICY.md`); este parâmetro implementa a mecânica, não a política.

## 5. Evidência

### GATE sobre a árvore final (executado localmente)

| Etapa | Comando | Resultado |
| --- | --- | --- |
| Build Debug estrito | `dotnet build Odca.sln -c Debug -warnaserror` | **0W/0E** |
| Build Release estrito | `dotnet build Odca.sln -c Release -warnaserror` | **0W/0E** |
| Domain | `dotnet test tests/Odca.Domain.Tests` | **389/389** |
| Integração em DB nova | `scripts/qa/run-integration-tests.ps1 -FreshDb` | **236/236, TEST-EXIT=0** (+2 fatos novos: título salvo no idioma do destinatário + checksum/release v062) |
| Assets/i18n | `node scripts/check-assets.mjs` | **exit=0** — 10 assets, 19 chaves do conjunto d41, 4 catálogos (2746 chaves/cultura, paridade 4/4; valores S12 revalidados byte-a-byte) |
| Auditoria de design d41 | — | **não reexecutada de propósito**: nenhuma rota auditada renderiza chave nova/alterada (as 19 `notif.*` são consumidas pela API/worker no conteúdo armazenado; `lang_help` só na página de preferências, fora da lista fixa de rotas); vistas Web e assets byte-idênticas às rondas onde 204/204 é válido |

### Provas ao vivo

| Prova | Comando | Resultado |
| --- | --- | --- |
| Aplicação v062 | migration em `odca_test_disposable` | exit=0; coluna, checagem e assinaturas confirmadas |
| Funções SQL por idioma | smoke em BEGIN/ROLLBACK | en-US: `Contract obligation due: Obrigacao S12` / `Due on 01/05/2027.` · pt-BR default: `Prazo contratual: Obrigacao S12` / `Vencimento em 05/01/2027.` |
| Backup/restore + retenção | `run-backup-restore.ps1 -KeepDumpDir $TEMP\opencode\pilot-dumps` | **27/27, EXIT=0**; dump retido `odca_test_disposable-pre-pilot-2026-10-18_172328.dump` (989512 B, sha256 `f0ec4fbc…400e7`) — transcript `evidence/d44-backup-restore-rodada-final.txt` regenerado |
| SMTP pt-BR (baseline) | `rehearse-smtp-transport.ps1` | **17/17, EXIT=0** — checks idênticos aos do S10 (comportamento preservado sem o parâmetro) |
| SMTP en-US | `rehearse-smtp-transport.ps1 -RecipientLanguage en-US` | **20/20, EXIT=0** — outbox `language=en-US`, título in-app via função SQL `Contract obligation due: …`, corpo do `.eml` em inglês (worker real + sink MTA local) |
| SMTP fr-FR | `rehearse-smtp-transport.ps1 -RecipientLanguage fr-FR` | **20/20, EXIT=0** — prefixo in-app `Obligation contractuelle: `, corpo francês e **acento sobrevivendo ao transporte** (`chars-nao-ascii=1`) |

Transcript versionado commitado = rodada en-US 20/20 (`evidence/d44-smtp-transport-rodada-final.txt`); as outras rodadas deixam logs em `$TEMP\opencode\evidence\d44-smtp-rehearsal-*`.

**Classificação QA (não é do produto)**: a rodada fr-FR expôs um typo no catálogo francês escrito nesta sprint — a palavra "plateforme" estava sem o "e" médio. Corrigido nos catálogos e no delta, com os 80 valores S12 (20 chaves × 4) revalidados byte-a-byte e o delta re-confirmado em paridade com os catálogos (76/76), e a rodada refeita verde. É exatamente o papel do ensaio: conteúdo novo é prova nova.

## 6. Cobertura honesta

- **Coberto**: idioma armazenado na outbox + título in-app no idioma (fato de integração sobre HTTP/DB reais, seed en-US + canal e-mail ativo); checksum/release v062 (fato); funções SQL por idioma (smoke ao vivo); cadeia real worker→SMTP em 3 idiomas (ensaio estendido `-RecipientLanguage`, worker reconstruído pelo script).
- **Não coberto**: MTA/provedor externo real (segue decisão externa — D-OC45 §3 item 4); entrega final em caixa de entrada (aceite do MTA ≠ entrega, semântica S10 preservada); aceitação humana das 4 linguagens das notificações.

## 7. Limitações e dívidas mantidas

- **Strings SQL sem acento** (decisão registrada §3) e **convites pt-BR por default** (fora de escopo de F-1).
- **i18n das áreas operacionais pt-first** (documentos/estúdio/atendimento/obrigações/suporte) permanece dívida aberta — esta sprint resolve apenas a dívida de conteúdo de notificações registrada na S8.
- **CI oficial nunca verde publicada** (branch sem push) → nenhum marco credenciável; retenção legal de backups "a definir"; provedores externos pendentes (D-OC45 §6).

## 8. Separação de evidências e portal

- **Teste local**: GATE §5 + provas ao vivo §5 + transcripts versionados.
- **CI oficial**: **não verificada** — sem push nesta rodada; nenhum ID de execução inventado.
- **Aceite humano**: pendente (4 idiomas do conteúdo de notificação + itens D-OC45).
- **Portal / `.odca/progress.json`**: **inalterado** — baseline 40%; IDs e pesos intactos; sem CI oficial verde do SHA nem aceite real, nenhum marco sai de `pending`.
