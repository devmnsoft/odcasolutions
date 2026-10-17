# Sprint 3 — assinatura: contrato plugável de provedor de envelope externo + provas ao vivo (D-OC37) (17/10/2026)

Plano de seis sprints para candidata ao piloto (estreia 11/01/2027). Esta entrega fecha a Sprint 3.

## 1. Escopo e base

- **Base**: `codex/s00-foundation` @ `528eff4` (Sprint 2 / D-OC36). Migração nova neste incremento: **v058** (persistência tenant-scoped do envelope externo e de seus eventos — única mudança de contrato de dados).
- **F-1 (correção)**: consistência do `signatureStatus` no detalhe da versão — `GET /api/v1/organizations/{t}/studio/versions/{id}` passa a retornar `signatureStatus ∈ {not_started, preparing, confirmed}` **derivado da preparação real** (não mais valor fixo), e o readiness de preparação mantém a advertência `integration.unavailable` quando nenhum transporte externo está configurado (removida quando o local-file é configurado). Corrigido e coberto por fato de integração + checagem ao vivo (F1).
- **F-2 (correção)**: i18n em 4 idiomas (pt-BR/en-US/es-ES/fr-FR) da **superfície de assinatura** — seção `<section id="assinatura">` da ficha do contrato (status derivado, participantes com tipo, guia "como assinar", nota de imutabilidade, ações assinar/vincular/lembrete/gerar/imprimir, `aria-label` da seção) + todas as mensagens das ações de assinatura/vínculo/lembrete do `ContractsController` (web) + rótulos de estágio de assinatura em `Documents/Index`. Chaves existentes reutilizadas antes de criar novas; cookie canônico `odca_lang` preservado.
- **F-3 (triagem — NÃO-defeito)**: suspeita "readiness adverti integração indisponível quando nenhum provedor foi escolhido (`none`)". Encerrada como **não-defeito**: a advertência é fiel à decisão registrada ("proposto, não escolhido" — nenhum envelope externo é enviado e a franquia de assinatura não é consumida), é informativa (HTTP 200, não bloqueia) e o envio falha explicitamente com `409 provider_unconfigured` em vez de nada acontecer de silencioso — os dois comportamentos estão cobertos pela prova ao vivo (R1/E0). Nenhuma mudança de código atribuída ao item.
- **A-1 (avanço)**: contrato plugável de provedor de envelopes externos — `ISignatureEnvelopeProvider` em Application (a plataforma nunca assume fornecedor: `none` preserva a decisão "proposto, não escolhido"; `local-file` é o **transporte de referência** que materializa/prova a costura), migração **v058**, endpoints `POST/GET .../versions/{id}/signature-envelope` (envio idempotente por chave natural) + `POST .../signature-envelope/sync` (reconciliação manual), varredura periódica cross-tenant no worker, consumo de franquia exatamente uma vez por envelope (replay = 0; reenvio após declínio = nova unidade). Fatos de integração ×8.
- **A-2 (avanço)**: prova ao vivo reproduzível sobre base descartável + capturas CDP da seção de assinatura nos 4 idiomas.
- **Bloqueio externo (registrado)**: fornecedor real de envio indefinido — decisão no DECISIONS-LOG (envelope externo "proposto, não escolhido" na estreia). Este incremento entrega o **contrato + transporte de referência**, não a integração com fornecedor; nenhum fornecedor, credencial ou aprovação foi inventado.
- **Fora de escopo registrado**: o restante da ficha do contrato (renovação, obrigações, revisão, histórico, formulários) permanece **pt-first** (dívida anterior a S3) e os `aria-label` da navegação global (46 itens raw) seguem o padrão pré-existente → itens pendentes no BACKLOG.

## 2. GATE

Execução inicial sobre a árvore A-1 (antes da correção do defeito Dapper adjacente à F-1): builds estritos 0W/0E, Domain 364/364, assets exit=0, integração `-FreshDb` **210/210**. Mudanças posteriores àquela execução (correção Dapper + novo fato de integração + chave `sheet.sig_section_aria_label` vindas do residual de F-2 achado na prova) **invalidaram** aquela rodada, e o GATE foi **reexecutado sobre a árvore final** antes do encerramento. Números oficiais = reexecução final:

| Etapa | Resultado |
|---|---|
| Build Release estrito (`-warnaserror`) | exit=0 · **0 avisos / 0 erros** |
| Build Debug estrito (`-warnaserror`) | exit=0 · **0 avisos / 0 erros** |
| `Odca.Domain.Tests` | **364/364 PASS** (0 falhas) |
| `scripts/check-assets.mjs` | exit=0 (assets JS + chaves i18n dos módulos × 4 catálogos) |
| Suíte de integração `-FreshDb` | **211/211 PASS** (FRESH-DB-OK → BUILD-OK → TEST-EXIT=0) = baseline 202 + 8 fatos do envelope de assinatura + 1 fato de checksum da v058 |

Comandos reproduzíveis (orquestrador local `%TEMP%\opencode\d37-gate.ps1`): `dotnet build .\Odca.sln -c Release -warnaserror`; `dotnet build .\Odca.sln -c Debug -warnaserror`; `dotnet test .\tests\Odca.Domain.Tests -c Debug --no-build`; `node .\scripts\check-assets.mjs`; `scripts\qa\run-integration-tests.ps1 -FreshDb`.

## 3. Mudanças

### Migração v058

- Checksum **`4767a431f872d24c87077955a636e76272a793cc150afe72dcb010c448fc95c0`**; `CurrentVersion=58`; release `database/releases/odca-v058.sql` byte-idêntica ao canônico (`database/odca.sql`).
- Conteúdo: `odca.signature_envelopes` (um envelope por versão gerada — `UNIQUE(tenant_id,generated_version_id)`; máquina de estados `sent|accepted|declined|failed`; `provider_code IN ('local-file')`) e `odca.signature_envelope_events` (deduplicação determinística `UNIQUE(envelope_id,source_event_id)`); RLS `ENABLE+FORCE` com policy `tenant_isolation` nas duas tabelas; funções `SECURITY DEFINER` `signature_envelope_pending_list` (varredura) e `signature_envelope_apply_state` (transição idempotente + evento deduplicado, sem reescrever linhas concorrentes); grants mínimos para `odca_app`.

### Catálogos

- **+89 chaves por cultura** (`src/Odca.Configuration/Localization/{pt-BR,en-US,fr-FR,es-ES}.json`) — total **1622 chaves/cultura**, paridade exata 4/4 (confere no GATE). Inclui `sheet.sig_section_aria_label`, adicionada durante a prova ao vivo ao corrigir o residual de F-2 do §4.

### Código

| Arquivo | Mudança |
|---|---|
| `src/Odca.Application/Signatures/SignatureEnvelopeProvider.cs` (novo) | contrato plugável `ISignatureEnvelopeProvider` (Send/GetState, idempotência por referência) + registros de payload/resultado/estado |
| `src/Odca.Infrastructure/Signatures/SignatureEnvelopeProviders.cs` (novo) | transporte `local-file` de referência: materializa `.envelope.json` (participantes + hash do PDF imutável) e lê `.state.json` na pasta de drop |
| `src/Odca.Infrastructure/DependencyInjection.cs` | seleção do provedor via `Signatures__Provider` (default `none`) + `Signatures__LocalFileDirectory` |
| `src/Odca.Api/Controllers/ContractStudioController.cs` | **F-1**: `signatureStatus` derivado no detalhe da versão + readiness ajustado à decisão; **A-1**: endpoints send/sync/GET do envelope, idempotência por chave natural, consumo único de franquia, despacho EventId 3101; registro privado `ParticipantReadRow` (correção Dapper — §3.1) |
| `src/Odca.Worker/SignatureEnvelopeWorker.cs` (novo) | reconciliador periódico: varredura cross-tenant dos envelopes `sent`, consulta ao provedor, aplica estado via função SQL com evento deduplicado |
| `src/Odca.Worker/Program.cs` | hospeda o reconciliador periódico |
| `src/Odca.Web/Controllers/ContractsController.cs` | **F-2**: padrão canônico de cultura (propriedade-shadow `Culture` via `CultureMiddleware` + `L()`, precedentes `StudioController`/S2); todas as mensagens de assinar/vincular/lembrete pelo catálogo (`sheet.sigmsg_*`), códigos de erro estáveis preservados no wire |
| `src/Odca.Web/Views/Contracts/Sheet.cshtml` | **F-2**: seção de assinatura integral via `L.T(key,_culture)` (rótulos de situação de versão e tipo de participante por helpers no catálogo; `aria-label` da seção localizado) |
| `src/Odca.Web/Views/Documents/Index.cshtml` | **F-2**: nota "assinatura externa não integrada" e rótulos dos estágios de assinatura (not_started/preparing/confirmed/externally_signed) das linhas de documento pelo catálogo |
| `tests/Odca.IntegrationTests/SignatureEnvelopeTests.cs` (novo) | 8 fatos: envio sem provedor (409 `provider_unconfigured`); consumo único + replay sem novo consumo; sync → accepted uma vez com eventos deduplicados; reenvio após declined consome nova unidade; franquia esgotada (409); preparação não confirmada recusa envio; detalhe da versão deriva status e materializa participante confirmado; varredura do worker aplica estado e linhas entre tenants permanecem isoladas |
| `tests/Odca.IntegrationTests/MigrationChecksumTests.cs` | fato de checksum da v058 (ReleaseV058) |
| `database/odca.sql` + `database/releases/odca-v058.sql` | bloco 058 canônico + snapshot byte-idêntico |
| `scripts/qa/validate-d37-signature-web.ps1` (novo) | prova ao vivo (A-2) — um comando, base descartável, dados sintéticos |
| `scripts/qa/capture-d37-screens.ps1` (novo) | capturas CDP da seção de assinatura (A-2) |

### 3.1 Defeito de produto descoberto e corrigido (adjacente à F-1)

`GET versions/{id}` dava **500** sempre que a versão tinha participante: a leitura Dapper falhava ao materializar `SignatureParticipantInput` (registro público em `Odca.Contracts\Studio\StudioContracts.cs` cujo 12º parâmetro positional tem default — `bool IdentityLinkedToMe = false`) contra a query de 12 colunas. Latente até aqui porque a lista chegava vazia nos fluxos existentes (o deserializer gerado por linha mascarava). Correção no padrão da casa: novo `private sealed record ParticipantReadRow` no controller (12 campos, nullability explícita) + projeção `.Select()`; o registro público do contrato permanece intocado. Coberto pelo fato `VersionDetailDerivesSignatureStatusAndMaterializesConfirmedParticipant` e pelas checagens F1/S-* da prova ao vivo.

## 4. Prova ao vivo (A-2)

- Script reproduzível: **`scripts/qa/validate-d37-signature-web.ps1`** — base descartável `odca_test_disposable` (migra até v058 via setup-on-start), seed determinístico com GUIDs fixos no tenant demo (`Paciente Prova D-OC37` — sintético, sem segredos), api `:5143` + web `:5144`; **fase 1** com provider `none` (padrão de desenvolvimento), **fase 2** reinicia a API com `Signatures__Provider=local-file` via env (reproduzível sem tocar em arquivo gitignored). Worker ausente na prova (varredura coberta pelo fato de integração). Sessão `cliente.teste@odca.local` (papel com `contract_drafts.read/manage`, sem MFA).
- **Rodada final fails=0 — 24 checks** (transcript `evidence/d37-proof.txt`); convergência registrada em `evidence/d37-proof-fim.txt`: **fails 8 → 4 → 3 → 0**. Conteúdo da rodada final: pré-condições determinísticas (binários, purga, seed v=1 p=1 u=1 env=0); readiness nas duas fases (R1 c/ advertência `integration.unavailable`, R2 sem); **F1** `GET versions/{id}` → `signatureStatus=confirmed` (consistência derivada); seção de assinatura da ficha nos **4 idiomas** (positivos completos + negativos de vazamento pt-BR no escopo da seção); ciclo completo do envelope **E1–E8** (404 antes → `POST` 200 `sent/replayed=false` → artefato materializado na pasta de drop → reenvio em andamento `replayed=true` **sem** novo consumo → sync sem estado `updated=false` → sync `accepted` `updated=true` → sync repetido deduplicado → GET accepted com auditoria sent+accepted → envio após accepted 409 `envelope.accepted`); estado final determinístico (envelopes=1 accepted, events=2, movimentos de franquia=1, arquivos-drop exatos).
- **Classificação das falhas das rodadas intermediárias**:
  1. **Defeito de produto (único)**: o 500 Dapper do §3.1 — corrigido e coberto por fato.
  2. **Harness**: a URL da ficha usava segmento de versão; a rota web da ficha é `contratos/{contractId}` (sem segmento — o detalhe por versão pertence ao Estúdio/API) → fetches vazios. Corrigido no script.
  3. **Harness**: asserção negativa sobre a página inteira capturava dívida pt-BR de seções **fora** da superfície de assinatura (pré-existente, fora de escopo da F-2) e segmentos de rota `/assinaturas/*` (identificadores, não texto i18n) → asserções negativas delimitadas a `<section id="assinatura">` sobre **texto visível** (tags removidas).
  4. **Residual de produto dentro do escopo F-2**: `aria-label` da seção de assinatura hardcoded em pt-BR → nova chave `sheet.sig_section_aria_label` × 4 culturas (já incluída nas contagens do §3) e positiva nas asserções S-*.

## 5. Capturas em navegador (CDP)

- `scripts/qa/capture-d37-screens.ps1` + driver `scripts/qa/cdp-capture-v2.mjs`: **1 tela** (ficha do contrato com assinatura confirmada) × **4 idiomas** × 360/768/1280/1440px + zoom 200% @1280 = **`CAPTURES-DONE ok=20 fail=0`** (transcript `evidence/d37-captures.txt`).
- PNGs em `%TEMP%\opencode\evidence\captures-d37\`, fora do repositório por tamanho (convenção das capturas anteriores); disponíveis para revisão humana. Sessão cliente (sem MFA).

## 6. Separação dos tipos de evidência

- **Teste local**: builds estritos, Domain 364/364, assets, suíte `-FreshDb` 211/211 (§2, reexecução final).
- **Prova ao vivo**: §4 (script reproduzível em um comando, fails=0).
- **Prova em navegador**: §5 (20 capturas para revisão).
- **CI oficial**: **não verificada** — branch sem push oficial; nenhum ID de execução inventado (vale para os SHAs da S1, da S2 e para o SHA deste incremento).
- **Aceite humano**: pendente — nenhum responsável ou aprovação inventada.
- **Portal**: `.odca/progress.json` **inalterado** (baseline 40%, IDs/pesos imutáveis; crédito de marco exige CI oficial do SHA funcional + aceite real pertinentes).

## 7. Riscos e próximo passo

- Bloqueio externo registrado: fornecedor de envio externo indefinido — contrato + transporte de referência entregues; nenhuma integração simulada como concluída.
- Itens pendentes registrados no BACKLOG: i18n do restante da ficha do contrato (seções fora da assinatura, pt-first desde antes de S3) e `aria-label` da navegação global (46 itens raw, padrão pré-S3); i18n do restante da área Documentos (carreado da S2).
- CI oficial deste SHA: pendente (push quando solicitado). Aceite humano: pendente (capturas disponíveis).
- Sprint 4 (atendimento, avisos e prazos) inicia-se sobre este commit.
