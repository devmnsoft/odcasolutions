# Auditoria de estabilização — Bloco A (out/2026)

Base auditada: `codex/s00-foundation` @ `47a547d` (limpa). Auditoria estática completa de
assinatura/Studio (1300 linhas), renderer PDF (291), isolation QA/MFA e biblioteca/planos,
mais verificação viva do banco `odca_test_disposable` (migração 42 aplicada).

Classificações usadas em todos os itens antigos marcados `[x]`:
**CONFIRMADO** (funciona como descrito) · **PARCIAL** (núcleo ok, lacuna real) ·
**DESGASTADO** (mudou e não foi revalidado) · **FALSE-POSITIVO** (cobertura não prova o critério) ·
**FALHO** (evidência atual falha).

## 1. Assinatura (Studio Sheet + API)

| Item | Classificação | Evidência | Correção |
|---|---|---|---|
| Estados de preparação (draft→confirmed→reabrir) | CONFIRMADO | `ContractStudioController` 838–926, testes 22/22 B34 | — |
| Reparo A-2/A-3/A-4/A-5/B-4 da seção B | PARCIAL | reparo existe mas só parcial | ver linhas abaixo |
| `SignParticipant` exige composição confirmada | CONFIRMADO | lin. 972 | — |
| `signed_by` só membro ativo do tenant | CONFIRMED | FK composta lin. 2178 + teste B-4 | — |
| Permissão para assinar por terceiros | **FALHO** | lin. 966 aceita só `tenant.contract_drafts.manage`: quem prepara pode se autodeclarar administrador-assinante de qualquer participante sem evidência | v043: permissões distintas por modalidade (v043) |
| Modalidade de assinatura registrada | **FALHO** | `signature_participants` não guarda como a assinatura foi tomada | v043: `signed_through` + `signed_session_id` + `signature_evidence` com CHECKs |
| Autoassinatura com vínculo sessão↔participante↔versão | **FALHO** | não há vínculo identidade↔participante nem uso do claim `sid` | v043: `identity_membership_id` + endpoint de vinculação + sign modo `session` valida sessão ativa/Nível MFA/versão |
| Replay de assinatura retorna estado real | **FALHO** | paths de replay (lin. 977, 986) devolvem `remaining=0, allSigned=true` hardcoded | recalcular contagem real no replay |
| Repetição por outro usuário | CONFIRMADO | lin. 978/985 Conflict `participant.already_signed` | — |
| Concorrência última assinatura | CONFIRMADO | `FOR UPDATE` serializa versão+preparação+participante; `remaining` lido no mesmo tx | teste novo de concorrência na suíte |
| Evento `externally_signed` | PARCIAL | transição correta (lin. 990–996); nome vocabulário herdado de rascunho externo — legado documentado em D-B4 | manter nome, exibir rótulo honesto "concluída na plataforma" |
| Fila real de envelopes (`signature_envelopes`) | FALSE-POSITIVO | tabela/COTA existem, fluxo atual não usa; sem `ISignatureProvider`/callbacks — homologação com provedor real permanece pendente por credenciais | matiz na matriz final |
| `CalculateReadiness`: `review.changes` bloqueante | **FALHO (ramo morto)** | lin. 1279 compara `review_status='changes_requested'`, valor impossível pelo CHECK da tabela (2004) | derivar de `contract_review_requests.status` mais recente da versão |
| Filtro `in_review` em documentos | PARCIAL | lin. 434 inclui `'in_review'` inatingível em `review_status` | usar EXISTS em requests, igual ao caso `changes_requested` (437) |
| Rótulos `changes_requested`/`in_review` em `Studio/Version.cshtml` etc. | PARCIAL | alguns consumidos via derivação correta (lin. 487–498), um direto sobre `review_status` (lin. 757→30) | alinhar derivando do request |
| UI `/sign`/`/remind` exercitada em QA | FALSE-POSITIVO | nenhum suíte chama os endpoints nem clica nos botões | suíte E nova cobre jornada completa |

## 2. PDF (SimplePdf + templates)

| Item | Classificação | Evidência | Correção |
|---|---|---|---|
| Data de emissão no artefato | **FALHO** | `GeneratePdf` passa `DateTimeOffset.UtcNow` (lin. 799) dentro de `FOR UPDATE` — reimpressão muda a data do documento imutável | usar `created_at` da versão (snapshot); congelar também `tax_id` em `emission_metadata` |
| Imutabilidade do arquivo | CONFIRMADO | escrita atômica + colisão por hash + fixed-time compare (lin. 800–804) | — |
| Renderização de acentos | CONFIRMADO | U+00C1–00FA dentro de Latin-1 byte pass-through | manter |
| Travessão (U+2014) e bullet (U+2022) | **FALHO** | `ch<256?(byte)ch:'?'` — todos os U+>0xFF viram `?`; placeholders agora são "—" (commit base) | mapeamento Unicode→WinAnsi (CP1252 puros bytes), declarar encoding coerente |
| Escapes de string PDF | CONFIRMADO | `Escape()` trata `\` `(` `)` CR LF (lin. 284) | manter (afirmação contrária de revisão refutada por leitura direta) |
| Metadados do arquivo (Info) | **FALHO** | sem /Info /Title etc. | adicionar Info dictionary (título, produtor, datas da versão) |
| Capa/sumário | PARCIAL | existem no fluxo com front-matter gated (D2), mas hierarquia plana — tudo 10pt, sem negrito/centralização | styles por tipo de linha (capa/título/corpo/bullet/callout), fontes Helvetica+Bold |
| Callout legal | PARCIAL | só prefixo "ATENÇÃO:" no PDF | caixa com barra de fundo |
| Numeração de página dentro de BT..ET | PARCIAL (estética) | página pode carimbar texto longo na baseline 28 | mover para fora do bloco de texto, posicionar via largura aproximada |
| Moeda | CONFIRMADO | `CanonicalDecimal.TryFormatPtBr` gera `1.000,00`, teste asserts | — |
| `ToUpper(CultureInfo.CurrentCulture)` | PARCIAL | culturalmente instável p/ texto pt-BR | InvariantCulture (decisão D-C4) |
| Testes do renderer | FALHO (baseline) | 2 falhas no commit base (placeholders e nº de modelos) | corrigidos neste incremento (9 modelos; traço em branco) |

## 3. QA/MFA — isolamento de contas

| Item | Classificação | Evidência | Correção |
|---|---|---|---|
| Suites promovem `operador@odca.local` a admin de plataforma | **FALHO (método)** | D:371–372 promove+limpa MFA dele; C faz igual; cleanup não restaura | contas QA exclusivas (`qa-d-*`), operador intocado, asserções de seed restaurado |
| Recovery codes demo apagados | **FALHO (método)** | C:398/D:842 deletam hashes dos seeds | recovery codes das contas QA |
| Grants residuais | **FALHO (método)** | C deixa `tenant.solicitations.manage` no operador | cleanup cobre grants/planos criados; try/finally |
| Risco de apontar para banco populado | **FALHO (método)** | scripts assumem `odca_test_disposable` sem checar `current_database()` | guarda explícita + recusa fora dela; nomes exclusivos p/ fixtures |
| MFA de superadmin validado por UI real | FALSE-POSITIVO | suítes logam via recovery code injetado via SQL; `Login-MfaAdmin` limpa estado demo | suíte MFA-real nova: credencial→enrollment→confirm→logout→challenge→acesso via páginas Web, código inválido, replay e one-shot |
| Data Protection persistente entre restart | CONFIRMADO | chaves em `%LOCALAPPDATA%\ODCA Solutions\data-protection-keys`, cookie sobrevive | cobrir na suíte real com restart |
| Bloqueio de superadmin sem MFA | PARCIAL | regra existe (`Program.cs` 127–135, D-C7) mas nenhum teste cobre | cobertura na suíte real |

## 4. Biblioteca, planos e autorização

| Item | Classificação | Evidência | Correção |
|---|---|---|---|
| Superadmin exclusivo em publicar/aprovar/importar/OCR no catálogo | CONFIRMADO | rotas `/api/v1/admin/**` exigem `platform.*`; `catalog/manage` é namespace isolado; seeds dev espelham trio padrão exatamente (`ensure_tenant_standard_roles`) | — |
| Cliente mantém catálogo read/use e rascunho próprio | CONFIRMADO | `tenant.templates.read/use`, `contract_drafts.*`, import de arquivo próprio permitido | — |
| Instalação oficial = atalho de publicação auditado | CONFIRMADO | `InstallOfficial` grava evento com origem e versão | — |
| Edição de rascunho ≠ edição de biblioteca | CONFIRMADO | cópias privadas por tenant; versão publicada imutável | — |
| Herança de aprovação cirúrgica | **FALHO** | `template_approvals` único por (tenant, official_key) sem vínculo a hash/versão: aprovar hoje cobre instalação futura | v043: vincular aprovação à revisão aprovada (versão instalada + sha256) e validar no readiness |
| `tenant.reviews.read` concedida a papéis-padrão | PARCIAL | existe no catálogo (v033) mas nunca concedida: aviso `review.separate` permanente mesmo com revisão aprovada | v043: conceder a admin/coordenador/clínico no trio e em tenants existentes |
| Modelo terapêutico básico em todos os planos | CONFIRMADO | `multiple-therapies` disponível em básico; humano ODCA e OCR Enterprise confirmados em catálogo | — |

## Decisões deste incremento

- **D-A1**: Assinatura passa a registrar modalidade: `session` (autoassinatura de participante
  vinculado à própria conta, exige sessão ativa com nível MFA) ou `evidence` (registro manual por
  evidência, exige descrição). Linhas anteriores à v043 permanecem com `signed_through IS NULL`
  (histórico preservado, exibido como "registro anterior à rastreabilidade de modalidade").
- **D-A2**: Permissões novas: `tenant.signature_participants.sign_self` (o próprio participante) e
  `tenant.signature_participants.record` (registrar terceiro com evidência).
  `tenant.contract_drafts.manage` continua governando preparar/confirm/reabrir/lembrar, mas
  deixa de, sozinha, permitir declarar assinatura de terceiro. Trio-padrão e seeds dev recebem
  as duas novas permissões nos mesmos papéis que já tinham `contract_drafts.manage`.
- **D-A3**: Vinculação de identidade é ato próprio: o usuário logado vincula a *sua* assinatura
  digital a um participante da composição confirmada (endpoint dedicado, evento auditável);
  ninguém vincula terceiros.
- **D-A4**: PDF de versão imutável usa exclusivamente o snapshot persistido
  (`created_at`, `emission_metadata` incluindo CNPJ da emissão); `renderer_version` sobe para
  `odca-pdf-2`; artefatos antigos permanecem baixáveis e inalterados.
- **D-A5**: Suites QA passam a criar identidade própria (`qa-<incremento>@odca.local`),
  recusam qualquer banco cujo `current_database()` não seja o descartável declarado e limpam
  tudo em `try/finally`, asserindo ao final que os três usuários-semente seguem intactos.

## Status do incremento (09–10/10/2026)

Implementado e validado na stack real (`odca_test_disposable`, API :7143 / Web :7144):

- **D-A1…D-A4** — migração **v043** aplicada (`CurrentVersion=43`, checksum `b879b4e9c174…fe79245`,
  snapshot `database/releases/odca-v043.sql`): modalidade de assinatura (`signed_through`,
  `signed_session_id`, `identity_membership_id`, `signature_evidence` + CHECKs), replay com
  contagem real, permissões `sign_self`/`record`, endpoint de vinculação própria, aprovação
  vinculada à revisão aprovada (versão instalada + sha256) validada no readiness,
  `tenant.reviews.read` concedida no trio e tenants existentes; PDF por snapshot da versão
  (`renderer_version odca-pdf-2`), WinAnsi correto, `/Info`, estilos de capa/sumário/callout.
- **Reparo v042** — pacote consolidado rehashado para o canônico `901a50fc9b4cf02bc18189b602df973102ae754f27926511700281eadb75ee9e`;
  `database/releases/odca-v042.sql` preservado como registro imutável do estado original.
- **D-A5** — suíte **E** é a implementação de referência (identidades `qa-e-*`, guarda de banco,
  try/finally, fingerprint do seed): **109/109 em 3 execuções consecutivas**. Suítes **C**
  (`validate-c-web.ps1`, reescrita) e **D** (`validate-d-web.ps1`, reescrita) adotaram o mesmo
  método: **C 99/99** e **D 115/115** em execução limpa, com `cleanup.seed_intacto` verde.
  Regressão `validate-b34-web.ps1` (metodologia inalterada): **93/93**.
- **Achados relevantes** (detalhados em `DECISIONS-LOG.md`): peculiaridade do PowerShell 5.1
  com `ConvertFrom-Json` (array emitido como item único — wrapper falso em `@(...)`);
  endpoints de escopo da organização exigem membership (403 para platform-admin sem ela);
  enrollment MFA restrito a platform-administrators; cleanup da E remove os modelos cirúrgicos
  do TC (dependência entre suítes coberta pelo install idempotente da biblioteca oficial no setup).
