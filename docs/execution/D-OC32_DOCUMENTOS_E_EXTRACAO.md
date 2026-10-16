# D-OC32 - Reupload idempotente por conteúdo + reconciliação de armazenamento + extração para o estúdio (16/10/2026)

**Base**: `codex/s00-foundation` @ `2af1c37` (= D-OC31). Commit único ao final, **sem push**.

## Objetivo

Fechar a fatia documental pendente do D-OC31 (OCR/upload): subir documento ao contrato de
modo idempotente por hash de conteúdo, conciliar o armazenamento físico com o catálogo (reservas
vencidas e órfãos, sem exclusão direta), extrair texto de versão aprovada e aplicar as sugestões
ao rascunho do estúdio com regras conservadoras e idempotência — tudo exposto no editor via
painel de documentos (proxies BFF), mantendo os caminhos existentes sem módulos paralelos.

## GATE (pré-requisito dos avanços dependentes)

- Build estrito (`-warnaserror`, Release+Debug) **0W/0E** sobre o `2af1c37`.
- Baseline verde: suíte `-FreshDb` do D-OC31 **191/191 PASS**; sem regressões críticas abertas.
- Após o incremento: suíte `-FreshDb` **197/197 PASS** (+6: 5 testes novos em
  `DocumentReuploadAndDraftExtractionTests.cs` + 1 caso em `MigrationChecksumTests.cs`), TEST-EXIT=0 —
  `%TEMP%\opencode\evidence\d32-freshdb-final2.log` (primeira execução verde: `d32-freshdb-final.log`).

## B.1 — Reupload idempotente por hash de conteúdo

- Mesmo sha256 enviado de novo **ao MESMO documento** (`documentId` presente no upload) é replay
  honesto: **200 `replayed=true`**, mesma versão (sem nova versão), sem cota dupla, reserva liberada
  para `released`, evento `document_reuploaded` com sha256 e a versão correspondida.
- **201** ganha a propriedade aditiva `replayed=false`; dedup por conteúdo **não** aplica a documento
  novo (sem adivinhar — cada documento novo recebe a própria primeira versão).
- Prova ao vivo: 201 com `usedDelta=961` (= tamanho do arquivo) e `reserved=0` após commit; replay
  com mesmo conteúdo → 200 `replayed=true` mesma versão — `%TEMP%\opencode\evidence\d32-b1b2b3.txt`
  (bloco P1; 46 [PASS], zero [FAIL]).

## B.2 — Reconciliador de armazenamento

- `odca.expire_storage_reservations(p_limit int DEFAULT 500)` **SECURITY DEFINER** (v056): reservas
  `reserved` vencidas viram `expired` em lote limitado e devolvem a cota reservada.
- O worker (`DocumentWorker.cs`) chama `SELECT odca.expire_storage_reservations(500)` em cada ciclo e
  reconcilia órfãos: tmp da quarentena e arquivos finais **sem linha referenciando o `storage_key`**,
  ambos mais velhos que `Documents:ReconcileOrphanRetentionHours` (padrão 24h), são **movidos** para
  `reconciled/{yyyy-MM}/` — retenção auditável, **nunca exclusão direta**; logs só com contagens e, em
  falha por arquivo, apenas `ErrorType` (sem conteúdo nem caminho).
- Prova ao vivo: reserva vencida seedada (+9876543) → `expired`/finalizada e cota devolvida; órfão
  final e tmp de quarentena movidos para `reconciled/2026-10/` (bloco P2 na mesma evidência).

## B.3 — Aplicar extração/OCR ao rascunho do estúdio

- Fluxo: upload → varredura de segurança (`pending` → `safe`) → `POST .../versions/{versionId}/extractions`
  (**409** `api.documents.version_not_approved` se não estiver `safe`; senão **202** `{extractionId,status:"queued"}`)
  → worker processa → `GET .../extractions/{extractionId}` retorna `{job,suggestions[]}` com
  `evidence`, `page_number`, `method` e `review_status`.
- `POST studio/drafts/{draftId}/apply-extraction` (`ContractStudioController`) com decisões
  accepted/edited/rejected por sugestão (1–30). **Mapeamento FECHADO** conceito→field id em
  `ExtractionDraftMapping`: match exato de identificador tem prioridade; tabela de alias completa e
  auditável (`title`→base_title/object, `reference`→base_reference, `start_date`→effective_date,
  `end_date`, `party_document`→12 ids de parte); `value` só aplica com candidato monetário **único**
  (`amount/rent_amount/procedure_fee/unit_price`); `party_document` exige 11/14 dígitos e candidato
  único. Fora da tabela → razões estáveis `unmapped`/`ambiguous_candidates`.
- Nunca sobrescreve valor confirmado nem campo calculado (`already_confirmed`); valor inválido para o
  campo → `value_invalid_for_field`. Valores aplicados aterrisam com `source="extraction"` e
  `confirmed=false` (confirmação humana continua obrigatória).
- Bloqueio otimista por `row_version`: estado desatualizado → **409 `draft.save.conflict`** com
  `currentVersion`, review marcada `conflicting` e decisões preservadas (retriar sem editar).
- Idempotência por `odca.draft_extraction_reviews` (v056, UNIQUE(tenant,idempotency_key)): replay do
  mesmo payload + chave → `{status:"applied", repeated:true}` sem reprocessar nem incrementar versão;
  mesma chave com payload diferente → **409 `idempotency.payload.conflict`**.
- Evento `draft.extraction_applied` com applied/skipped detalhado; sugestões marcadas como revisadas;
  permissões `tenant.extractions.request/review/apply` exigidas (teste dedicado para as duas de apply).
- Prova ao vivo (bloco P3): job `ready_for_review` com 7 sugestões; apply 200 `applied`/`version 2→3`
  com 7 skips estáveis (6 unmapped + 1 already_confirmed); campo confirmado intocado; replay
  `repeated=true` sem reprocessar; evento +1; obsoleto → 409 `draft.save.conflict` + review
  `conflicting` — `%TEMP%\opencode\evidence\d32-b1b2b3.txt`.

## C — Painel de documentos + extração no editor (BFF + UI)

- Proxies BFF ancorados no `draftId` (o JS **nunca** envia contractId; o contrato vem do retrato do
  rascunho no servidor):
  - `GET minutas/{draftId}/documentos` → lista da API (shape snake_case: `version_id`,
    `version_number`, `display_name`, `detected_type`, `byte_size`, `security_status`, `uploaded_at`,
    `uploaded_by`);
  - `POST minutas/{draftId}/documentos` (multipart) → BFF retorna **200** nos dois casos (novo/replay);
    o cliente confia no campo `replayed`, não no status code;
  - `POST minutas/{draftId}/extrair {documentId,versionId}` → **202** (Accepted) espelhando a API /
    409 versão não aprovada;
  - `GET minutas/{draftId}/extracao/{extractionId}` → `{job,suggestions[]}`;
  - `POST minutas/{draftId}/aplicar-extracao` → 200 `{status,repeated,version,applied[],skipped[]}` / 409.
- Endurecimento descoberto em prova: o controller é MVC puro (sem `[ApiController]`) — quando o parse
  do `[FromBody]` falha, a action é chamada com modelo **null**. As duas actions com corpo agora
  guardam null com **422 `studio.request_invalid`** (nova chave ×4 idiomas). O sintoma original era um
  NRE mascarado em 405 pela reentrada do `BffErrorHandlingMiddleware` em `/erro-indisponivel`
  (Aceite não-JSON) — registrado na seção "Lições".
- Antiforgery global já instalado no web: os POSTs JSON do painel enviam o header
  `RequestVerificationToken` (token exposto na página); upload multipart validado pelo mesmo filtro.
- UI (aba "Documentos" no editor: `Edit.cshtml` + `studio.js` + `site.css`): lista com badge de status
  de segurança por versão, upload/substituição de conteúdo, botão **"Extrair texto" somente para
  versão `safe`**, painel de sugestões com evidência (fragmento/página/método) e aceitar/editar/rejeitar,
  aplicação explícita ao rascunho; conflito 409 recuperável recarregando o estado mais recente.
- Catálogos: **+38 chaves × 4 idiomas** (1353 → 1391 por idioma; paridade 4/4 validada pela suíte).

## D — Release v056

- `database/releases/odca-v056.sql`: índice não-único `document_versions(tenant_id,document_id,sha256)`
  (apoia a busca de replay), função `odca.expire_storage_reservations(p_limit int DEFAULT 500)`
  SECURITY DEFINER, tabela `odca.draft_extraction_reviews` (PK uuid, UNIQUE(tenant_id,idempotency_key))
  com RLS e GRANTs; `database/odca.sql` atualizado com v056 anexado.
- `DatabaseSchema.cs`: CurrentVersion **56**; checksum canônico v056 =
  `4fde553fc9d60cc62913d22de318396867b55314e831f83e509cc109f6e688f6`; prefixo imutável 1..55 e ausência
  de v057 fixados em teste (+1 caso em `MigrationChecksumTests.cs`).

## E — Matriz de aceite (dez itens)

| # | item | status | evidência |
|---|------|--------|-----------|
| 1 | B.1 replay honesto: mesmo sha256 → 200 `replayed=true` mesma versão, sem cota dupla, reserva devolvida | aprovado | `evidence/d32-b1b2b3.txt` P1: 201 `replayed=false` c/ `usedDelta=961` e `reserved=0`; replay 200 `status=replayed` mesma `versionId` |
| 2 | B.1 evento `document_reuploaded` c/ sha256+versão e propriedade aditiva no 201 | aprovado | P1 do `d32-b1b2b3.txt` (evento auditado no acervo) + `d32-web-c.txt` W8/W9 (mesmo comportamento pelo BFF: 200 `replayed=false`/`true`) |
| 3 | B.2 reserva vencida finalizada em lote limitado devolvendo cota | aprovado | P2 do `d32-b1b2b3.txt` (seed +9876543 → `expired`/finalizada, `reserved=0`) + teste `ExpiredReservationsAreFinalizedAndReleaseReservedQuota` (suíte 197/197) |
| 4 | B.2 órfãos (tmp de quarentena + finais sem referência) movidos para retenção, sem exclusão direta | aprovado | P2 do `d32-b1b2b3.txt`: os dois arquivos presentes em `reconciled/2026-10/`; `Documents:ReconcileOrphanRetentionHours=24h` padrão; log só contagens |
| 5 | B.3 mapeamento fechado c/ regras conservadoras (alias, monetário único, 11/14 dígitos, `unmapped`/`ambiguous_candidates`) | aprovado | `d32-b1b2b3.txt` P3 (7 skips estáveis) + testes `ApplyExtractionToDraftMapsOnlySafeConceptsAndIsIdempotent` e `ApplyExtractionSkipsEveryUnsafeConceptWithAStableReason` |
| 6 | B.3 bloqueio otimista: 409 `draft.save.conflict` c/ `currentVersion`, review `conflicting`, decisões preservadas | aprovado | P3 do `d32-b1b2b3.txt`: "chave nova com versao obsoleta 409 draft.save.conflict currentVersion=3" + "review ... marcada como conflicting" |
| 7 | B.3 idempotência: replay mesmo payload → `repeated=true` sem reprocessar; chave diferente payload → 409 `idempotency.payload.conflict`; evento `draft.extraction_applied` + sugestões revisadas | aprovado | P3 do `d32-b1b2b3.txt`: "replay ... repeated=true", "nao reprocessa nem incrementa versao", "evento draft.extraction_applied +1", "nao aceitas=0"; código em `ContractStudioController` (L733–750) |
| 8 | C cadeia completa BFF a partir de sessão de browser: lista/upload/replay/extrair/poll/sugestões/aplicar | aprovado | `evidence/d32-web-c.txt` W1–W17 todos PASS (login, página c/ aba+antiforgery, list 200 array, upload 200 novo, replay 200, extrair 202, `ready_for_review`, 7 sugestões c/ evidência, apply 200 v2→v3, reversão) |
| 9 | C extração exige versão `safe` (senão 409) e o botão da UI só aparece para `safe` | aprovado | W9b+W10 do `d32-web-c.txt` (poll até `safe`, então 202); guard no servidor `ContractDocumentsController` (EXISTS security_status='safe' senão 409 `api.documents.version_not_approved`); JS só expõe o botão quando `security_status==='safe'` |
| 10 | Regressão: build estrito 0W/0E + suíte `-FreshDb` + paridade de catálogos | aprovado | Debug+Release `-warnaserror` 0W/0E; **197/197 PASS** TEST-EXIT=0 — `evidence/d32-freshdb-final2.log`; paridade 4/4 @ 1391 chaves/idioma |

Nenhum item falhou, bloqueado ou não executado nesta fatia.

## Configuração (sem novos segredos)

- `Documents:StoragePath` (padrão `./private-documents`) — raiz dos arquivos (já existente do fluxo de
  upload/varredura).
- `Documents:ReconcileOrphanRetentionHours` (padrão `24`) — idade mínima antes de mover órfãos para a
  pasta de retenção.

## Arquivos e migrations

- Novos (4): `database/releases/odca-v056.sql`, `src/Odca.Application/Documents/ExtractionDraftMapping.cs`,
  `tests/Odca.IntegrationTests/DocumentReuploadAndDraftExtractionTests.cs` (5 testes), este documento.
- Modificados (17): `database/odca.sql` (+v056), `DatabaseSchema.cs` (CurrentVersion 56 + checksum),
  `StructuredContractDocument.cs`, `ContractDocumentsController.cs` (replay por sha256, rotas de
  extração/apply no contrato), `ContractStudioController.cs` (`apply-extraction` do rascunho +
  idempotência/conflictos), `StudioContracts.cs` (contratos da aplicação), `StudioController.cs`
  (5 proxies BFF + endurecimento 422 c/ corpo nulo + 202 em extrair), `OdcaApiClient.cs` (5 métodos),
  `Edit.cshtml` (aba Documentos), `studio.js` (lógica da aba, shape snake_case da lista), `site.css`
  (estilos do painel), `DocumentWorker.cs` (expiração de reservas + órfãos), `MigrationChecksumTests.cs`
  (+1 caso), 4 catálogos (+38 chaves por idioma), `STATUS.md`, `BACKLOG.md`.

## Lições registradas (bugs encontrados em prova)

- MVC puro (sem `[ApiController]`) executa a action mesmo quando o parse do `[FromBody]` falha
  (modelo chega `null`) — o NRE resultante era mascarado em **405** porque o
  `BffErrorHandlingMiddleware` reentra no pipeline em `/erro-indisponivel` para Aceite não-JSON; com
  `Accept: application/json` o 503 JSON real aparece. Remédio: guarda explícita `request is null` →
  422 `studio.request_invalid` nas actions com corpo.
- PowerShell 5.1 + `curl.exe` **raspa as aspas duplas** de argumentos nativos: `-d '{"k":"v"}'` (literal
  OU variável) chega no servidor como `{k:v}` (prova: corpo de 104 chars enviado com 96 bytes; STJ
  relata "'d' is an invalid start of a property name"). Os scripts de prova enviam corpos JSON por
  `-d @file` (UTF-8 sem BOM).
- Upload/replay pelo BFF retornam 200 nos dois casos (relê do envelope da API) — clientes devem
  confiar no campo `replayed`; criar extração pelo BFF agora retorna 202 espelhando a API.

## Dependências restantes / próximo backlog

- OCR real para imagens rasterizadas (PNG/JPEG) e PDF escaneado: a extração atual opera sobre o texto
  do documento (docx/PDF com camada de texto); sugestões já persistem `page_number`/`method` para o
  provedor futuro.
- Purga física após a retenção: os órfãos ficam preservados em `reconciled/{yyyy-MM}/` (decisão de
  descarte automático ainda aberta).
- Push da branch quando o usuário pedir.

## Evidências (fora do repositório, em `%TEMP%\opencode\evidence\`)

- `d32-b1b2b3.txt` — prova ao vivo B.1/B.2/B.3 contra a API Development (46 [PASS], zero [FAIL]).
- `d32-web-c.txt` — prova da cadeia BFF/Web W1–W17 (todos PASS; VERIFY-EXIT=0).
- `d32-freshdb-final.log` / `d32-freshdb-final2.log` — suítes `-FreshDb` 197/197 PASS (a segunda após a
  última mudança de código: endurecimento 422 + 202 em extrair + chave `studio.request_invalid`).
