# Auditoria inicial — Operação contratual completa (blocos A–D)

**Data:** 10/10/2026 · **Branch:** `codex/s00-foundation` · **Commit analisado:** `786694c`
**Método:** leitura de código/migrations/docs + comportamento medido pelas suítes QA do mesmo commit (runs de 09/10) + reconfirmação ao vivo (baseline deste dia, logs `baseline-*` em `%TEMP%\opencode\`). Nada foi assumido de prompts anteriores: cada item foi relocalizado no código ou em evidência executada.

Legenda de classificação:
- **COMPROVADA** — comportamento medido por suíte/assert (log citado).
- **PARCIAL** — existe e funciona parcialmente; lacunas listadas.
- **QUEBRADA** — defeito confirmado com repro.
- **AUSENTE** — buscado (código, views, endpoints, SQL) e inexistente.
- **NÃO VERIFICADA** — código existe, sem execução dedicada neste ciclo.

---

## 1. Base reconfirmada e alterações locais

| Item | Estado | Evidência |
|---|---|---|
| Branch | `codex/s00-foundation` | `git branch --show-current` |
| Commit atual | `786694c` ("Estabilizacao A") | `git log --oneline -1` |
| Base do prompt | `47a547d` — o commit atual é exatamente base + incremento Estabilização A (D-A1..A5) | histórico |
| Árvore | limpa (`git status --porcelain` vazio) | — |
| Stash | intocado: `stash@{0}: codex/corrigir-ca1848-e-bloqueio-worker temp-untracked` (pré-existente) | `git stash list` |
| Schema do banco | v043 aplicada com checksum (`odca.schema_migrations` v=43, applied 2026-10-09 17:59 −03) | psql nesta sessão |
| Plano do tenant demo (TC `2000…001`, "Cliente Teste ODCA") | subscription `active` → Basic **v2** (`3000…11`) | psql nesta sessão |
| Resíduos de suíte | 0 usuários `qa-%`; 0 tenants `Val %` | psql nesta sessão |
| Fingerprint dos seeds (email+hash dos usuários `@odca.local`) | `9b641fa0c67d2bc840054c24776c9feb` capturado **antes** do baseline de hoje | psql nesta sessão |
| Stack | reiniciada em 10/10 via `scripts/run-local.ps1` (HTTPS :7143/:7144), `health/ready` 200 | shell desta sessão |

Evidências pré-existentes do mesmo commit (runs de 09/10, ainda válidas): E `109/109 ×3` (e-run7/8/9), C `99/99` (c-run3), D `115/115` (d-run4), b34 `93/93` (b34-run1) — todos com `cleanup.seed_intacto` verde.

## 2. Componentes canônicos (localizados)

| Área | Canônico | Observação |
|---|---|---|
| Autenticação/MFA | `src/Odca.Web/Controllers/AccountController.cs` + `Views/Account/*` (Login, MfaChallenge, MfaEnrollment, RecoveryCodes, ChangePassword); API auth em `Odca.Api` | MFA exigido apenas para `SuperAdministrator` (policies `PasswordChanged`/`PlatformAdministrator`) |
| BFF/erros/sessão | `Middleware/BffErrorHandlingMiddleware.cs`, `Services/OdcaApiClient*.cs` | redirect p/ `/entrar?ReturnUrl=` |
| Pacientes | `Controllers/PatientsController.cs` + `Views/Patients/*` (Index/Form/Details) | responsável legal dentro do cadastro |
| Modelos/studio | `Controllers/StudioController.cs` (Web) ↔ `ContractStudioController`/`ContractStudioCatalogController` (API); visibilidade em `Infrastructure/Contracts/NpgsqlTemplateCatalogRepository.cs` (`VisibleFilter`: globais publicados, próprios, compartilhados) | modelos oficiais entram por `POST studio/templates/official` = **cópias privadas por tenant** |
| Planos/entitlements | `Infrastructure/Plans/NpgsqlPlanCatalogRepository.cs`; `plan_versions`×`plan_entitlements` em `database/odca.sql`; gates em controllers + funções SQL | 3 códigos: basic/intermediate/enterprise (v1 expiradas, v2 vigentes) |
| Features/bloqueios por org | `Controllers/OrganizationFeaturesController.cs` + `organization_feature_blocks`; estados `allowed/plan_restricted/administratively_blocked/quota_exhausted` | menu ganha sufixos "(fora do plano)/(bloqueada)/(limite atingido)" |
| Assinatura | `ContractStudioController` (sign-participants, prepare, replay, reminder, identity link) + estados de `contract_versions` | v043: modalidade (session/evidence), replay idempotente, aprovação cirúrgica vinculada à revisão (versão+sha256) |
| PDF | `Application/Contracts/ContractDocumentRenderer.cs` (+`ByteSequenceExtensions`) | snapshot da versão, WinAnsi, /Info, odca-pdf-2 |
| Renovações | `RenewalsController` + API renovações (proposta→formalização→aplicação) | fluxo preservado do seed |
| Revisões internas | `ReviewRequestsController` + `Views/Reviews/*` (permissão `tenant.reviews.read`; **enterprise** na v2) | separado de solicitações ODCA |
| Solicitações ODCA/Suporte | `SolicitacoesController` (tenant) + `PlatformSolicitationsController` (fila, SLA, aprovações de modelos) + worker de violações | v042: thread, SLA com snapshot/fuso/calendário, pausa/retomada/reatribuição |
| Caixa/agenda/obrigações | `InboxController`, `AgendaController`, `ObligationsController` (+ saved-views) | operacionais mensais |
| Notificações | tabelas `odca.user_notifications` e `odca.contract_review_notifications` (com `deduplication_key`) | **sem canal de e-mail/SMTP e sem tela de centro de notificações** |
| Auditoria | `audit_events` (tenant+platform); `PlatformAuditController` (filtros); tenant: `Consumption#historico` | — |
| Navegação | **inline em `Views/Shared/_Layout.cshtml:131-285`** (ramo plataforma L132-198, cliente L199-283) | sem registro central; sem seletor de idioma (`lang="pt-BR"` fixo; sem `AddLocalization`/`IStringLocalizer` no projeto) |

## 3. Menu atual (evidência `_Layout.cshtml`)

### Cliente (L199-283)
| Grupo | Itens (condição) | Status real |
|---|---|---|
| Início | Início (sempre); Primeiros passos (membership ativo) | COMPROVADAS (CustomerHome/GettingStarted) |
| Pacientes | Pacientes (`canReadPatients`) | COMPROVADA |
| Contratos | Modelos disponíveis; Meus contratos; Minhas minutas; Novo contrato; Acervo de documentos; Ficha do contrato (só com contrato ativo) | COMPROVADAS (jornadas C/D) |
| — | Assinaturas (stage `ready_for_signature`) | COMPROVADA |
| — | Renovações e prazos (`canReadRenewals`) | COMPROVADA |
| — | Solicitações ODCA (`canReadReviews && !reviewsPlanRestricted`); Central de solicitações (`canReadSolicitations`) | COMPROVADAS (v042/v043) |
| Operações | Caixa; Agenda; Obrigações; Filtros salvos | COMPROVADAS |
| Minha organização e plano | Equipe e permissões; Organização; Plano e consumo; Auditoria (#historico) | COMPROVADAS |
| Suporte | **Ajuda e como usar → `Modules?module=ajuda` = placeholder "em construção"** | PLACEHOLDER |
| — | Privacidade (sempre) | COMPROVADA |

Ausentes do menu cliente (presentes no pedido do incremento): **Minha conta** (idioma/seurança/notificações), **Arquivados** como item próprio (estado no acervo), **falhas e pendências de assinatura** como visão própria, **suporte técnico** separado das solicitações ODCA, **Minhas pendências** como painel agregado (ClienteHome cobre parcialmente), **seletor de idioma** (ausente em todo o app).

### Plataforma (L132-198)
| Item | Status real |
|---|---|
| Visão global (Home/Index) | COMPROVADA |
| Organizações | PARCIAL — lista/detalhe existem; filtros por nome/plano/situação e bloqueio-com-motivo pela UI a confirmar |
| Usuários e acessos (Customers) | PARCIAL — detalhes/criação existem; busca global de usuário ausente |
| Planos e módulos (Plans) | COMPROVADA (catálogo somente leitura) |
| Biblioteca de modelos (`Modules?module=biblioteca-modelos`) | **PLACEHOLDER "Em construção controlada"** |
| Publicação e versões (`Modules?module=publicacao-versoes`) | **PLACEHOLDER** |
| Solicitações e SLA (AdminSolicitacoes) | COMPROVADA (v042) |
| Aprovação de modelos (AdminSolicitacoes/Aprovacoes) | COMPROVADA (v042) |
| Auditoria (PlatformAudit) | COMPROVADA (filtros) |
| Configurações operacionais | **PLACEHOLDER** |
| Contexto de tenant (Caixa/Agenda/Obrigações/Renovações/Acervo/Assinaturas/Modelos/Minhas minutas/Pacientes/Solicitações/Ficha/Equipe/Plano) | espelha o ramo cliente quando o superadmin tem vínculo — COMPROVADO pelo uso nas suítes |

## 4. Matriz funcionalidade → plano → perfil → menu → endpoint → persistência → situação

Perfis em prova nas suítes: `tenant-administrator`/`tenant-client` (`cliente.teste@odca.local`), `tenant-operator` (`operador@odca.local`), `SuperAdministrator` (operador promovido, sessão nível-MFA). Detalhes medidos em `MATRIZ_ACOES_PERFIL_PLANO.md` (Seção D, mantida como referência).

### Acesso e conta
| Funcionalidade | Plano | Perfil | Menu | Endpoint principal | Persistência | Situação | Evidência |
|---|---|---|---|---|---|---|---|
| Login (e-mail/CPF+senha) | todos | qualquer | `/entrar` | `POST /api/v1/auth/login` | sessões+claims | COMPROVADA | b34/C/D/E |
| MFA desafio/inscrição/recuperação/reinscrição (UI) | — | SuperAdministrator (política) | redirect pós-login + páginas Account | API mfa/* | `mfa_*`, Data Protection | COMPROVADA | e-run9 109/109 |
| Troca de organização (multi-tenant) | todos | qualquer com vínculos múltiplos | seletor em sidebar + `/organizacoes` | claims/cookie BFF | preferências | COMPROVADA | d-run4 (qa-d-op em D1+D2) |
| Aceite de convite | todos | convidado | `/organizacoes/accept` | memberships API | `memberships` | COMPROVADA | d-run4 setup |
| Alterar senha | todos | dono da conta | página Account (sem item de menu dedicado) | password change API | `users.password_hash` | NÃO VERIFICADA (UI) | página existe |
| Minha conta (hub: idioma, segurança, notificações) | todos | dono da conta | **ausente** | — | — | AUSENTE | `_Layout.cshtml` (nenhum item) |
| Seletor/preferência de idioma | todos | dono da conta | **ausente** (login e conta) | — | — | AUSENTE | sem `AddLocalization`; `lang="pt-BR"` fixo |
| Inscrição/ativação de organização | todos | novo usuário | Onboarding (Register/ConfirmEmail) | registration API | `tenants(pending)`→ativação | PARCIAL (fluxo de e-mail de confirmação sem transporte real) | views + API |

### Organização, equipe, plano
| Funcionalidade | Plano | Perfil | Menu | Endpoint | Persistência | Situação | Evidência |
|---|---|---|---|---|---|---|---|
| Editar dados da organização | todos | `tenant.org.manage` | Organização | organizations PUT | `tenants` | COMPROVADA | c-run3 |
| Equipe: convite, papel, bloqueio | todos | `tenant.team.manage` | Equipe e permissões | memberships/roles API | `memberships`, `member_roles` | COMPROVADA | c-run3/d-run4 |
| Impedir remover último admin | todos | — | — | `last_admin_protected` | — | COMPROVADA | API inventory (assert de negativa) |
| Impedir elevação indevida | todos | — | — | `elevation_denied` | — | COMPROVADA | API inventory |
| Plano e consumo (recursos, limites, histórico) | todos | `tenant.org.read` | Plano e consumo / Auditoria | `GET organizations/{t}/features`, consumption | `resource_movements`, `audit_events` | COMPROVADA | c-run3 (sufixos de estado no menu) |
| Suspensão de organização (mensagem própria) | — | plataforma suspende | banner no layout | organization status | `tenants.status` | COMPROVADA | e-run (tenant suspended fixture) |
| Bloqueio administrativo de módulo com motivo | — | plataforma | estado no menu ("(bloqueada)") | `OrganizationFeaturesController` set/unset | `organization_feature_blocks` | COMPROVADA (API) · PARCIAL (UI de quem bloqueia) | c-run3 + controller |

### Pacientes
| Funcionalidade | Plano | Perfil | Menu | Endpoint | Persistência | Situação | Evidência |
|---|---|---|---|---|---|---|---|
| CRUD pacientes + responsável | todos | `tenant.patients.*` | Pacientes | patients API | `patients` (+responsible) | COMPROVADA | jornadas C/D |
| Isolamento entre tenants | — | — | — | GUC `odca.tenant_id` | — | COMPROVADA | d-run4 (D1×D2) |

### Modelos e biblioteca
| Funcionalidade | Plano | Perfil | Menu | Endpoint | Persistência | Situação | Evidência |
|---|---|---|---|---|---|---|---|
| Listar modelos visíveis (globais publicados/próprios/compartilhados) | todos | `tenant.templates.read` | Modelos disponíveis | `GET studio/templates` | `contract_templates(+access)` | COMPROVADA | b34 T4 |
| Instalar biblioteca oficial (9 modelos) | todos | `tenant.templates.manage` | ação na Studio | `POST studio/templates/official` | cópias `scope='private'` por tenant | COMPROVADA — **mas conflita com o limite "cliente não carrega modelos"** → decisão pendente (D-OC1) | c-run3 S0 |
| Criar/editar/publicar/duplicar/arquivar modelo próprio | todos | `tenant.templates.manage` | Studio (TemplateEdit/Version) | studio/templates* | `contract_templates(+versions)` | COMPROVADA | b34 |
| Modelo básico terapêutico disponível em todos os planos vigentes | basic/intermediate/enterprise v2 | qualquer com templates.read | via biblioteca oficial | — | `OfficialContractTemplates` (code) | COMPROVADA (conteúdo) · pendente o caminho sem instalação (D-OC1) | OfficialContractTemplates.cs |
| Aprovação ODCA de modelos cirúrgicos (fila plataforma) | enterprise (perfil `plastic_surgery`) | plataforma | Aprovação de modelos | `PlatformSolicitationsController` approvals | approvals + `document_purpose` | COMPROVADA | v042 + d-run4 (readiness destravável/reversível) |
| Importação de documentos pelo cliente | — | — | Imports (banner "disponível em breve"; API 503) | imports API (gate de release) | — | PARCIAL — **fora do limite do ciclo**; retirar da apresentação (decisão D-OC1) | Imports views + ReleaseFeatureGate |
| Biblioteca global administrada por superadmin (carregar/configurar/publicar/retirar) | — | plataforma | **placeholders** | **ausentes (nada publica `scope='global'`)** | `contract_templates.scope='global'` só 1 linha de teste no banco | AUSENTE | q-templates desta sessão (113 modelos, 1 global = teste) |

### Jornada contratual (rascunho → emissão → arquivo)
| Funcionalidade | Plano | Perfil | Menu | Endpoint | Persistência | Situação | Evidência |
|---|---|---|---|---|---|---|---|
| Novo contrato (wizard) | todos | `tenant.contract_drafts.manage` | Novo contrato | drafts API | `contract_documents/versions` | COMPROVADA | c-run3 |
| Rascunho persiste e é retomado | todos | dono | Minhas minutas | drafts GET/PUT | idem | COMPROVADA | c-run3 |
| Conferência/readiness antes de emitir (pendências: erro/alerta/info) | todos | `tenant.documents.read/manage` | ficha (conferência) | `GET drafts/{id}/conference` | snapshot de conferência | COMPROVADA (bloqueia erros; separação alerta/info a evoluir — Bloco C §7) | c-run3 |
| Emissão por snapshot da versão (imutável pós-emissão) | todos | — | ficha | versions PDF | `contract_template_versions` + hash | COMPROVADA | d-run4 + v043 (sha256 vinculado) |
| PDF íntegro (metadados /Info, WinAnsi, paginação, símbolos) | todos | — | download | `GET versions/{id}/pdf` | blob por hash | COMPROVADA nos cenários atuais; estender validação de conteúdo (Bloco A) | d-run4 (PNG 1440/768/360 + asserts de cabeçalho/metadados) |
| Arquivar preservando histórico | todos | — | Acervo (estado) | archive API | status+versões intactas | COMPROVADA | c-run3 (arquivados no acervo) |
| Versões/comparação | todos | `tenant.documents.read` | endpoints `versions/compare`, `signature-preparation/compare` | GET | snapshots | PARCIAL (endpoints COMPROVADOS; apresentação de comparação a evoluir — Bloco C §8) | API inventory + d-run4 |

### Assinatura
| Funcionalidade | Plano | Perfil | Menu | Endpoint | Persistência | Situação | Evidência |
|---|---|---|---|---|---|---|---|
| Preparação/envio individual (session link) | todos | `tenant.sign_self` / `sign_record` (v043) | Assinaturas | sign-participants, prepare, identity link | `sign_events` com modalidade+evidência | COMPROVADA | d-run4 (115/115) |
| Registro manual com modalidade explícita, autorização, evidência | todos | `sign_record` | ficha/assinaturas | record manual | `sign_events(evidence_jsonb)` | COMPROVADA | v043 + d-run4 |
| Replay retorna estado real (allSigned/remaining corretos) | todos | — | — | replay endpoint | eventos idempotentes | COMPROVADA (correção do falso allSigned do v042) | v043 + d-run4 |
| Repetição/concorrência não duplicam eventos | todos | — | — | replay/reminders | dedupe por chave | COMPROVADA | d-run4 (duplas invocações asseridas) |
| Conclusão externa por envelope (integração verificável) | todos | — | fila `signature_envelopes` | enqueue/consume | `signature_envelopes` | **QUEBRADA (falso positivo)** — fila existe sem provedor; concluir por ela hoje seria conclusão fictícia → decisão D-OC3 (canal explícito indisponível) | auditoria v043 (envelopes não consumidos) |

### Prazos, renovações, obrigações
| Funcionalidade | Plano | Perfil | Menu | Endpoint | Persistência | Situação | Evidência |
|---|---|---|---|---|---|---|---|
| Agenda mensal | todos | workspace | Agenda | agenda API | obrigações/events | COMPROVADA | b34 |
| Obrigações + filtros salvos | todos | `tenant.obligations.read` | Obrigações | obligations API | `contract_obligations` | COMPROVADA | b34 |
| Renovação: proposta → aprovação → aplicação (sem reescrever documento) | todos | responsável | Renovações e prazos | renewals API | `renewal_proposals`+aplicação idempotente | COMPROVADA | d-run4 (concorrência não aplica duas vezes) |
| Distinguir renovação/aditivo/revisão administrativa | — | — | — | tipos de proposta | — | PARCIAL (tipos existem; apresentação/semântica a evoluir — Bloco C §9) | renewals API |

### Solicitações, SLA, atendimento
| Funcionalidade | Plano | Perfil | Menu | Endpoint | Persistência | Situação | Evidência |
|---|---|---|---|---|---|---|---|
| Revisões internas (reviews.read) | **enterprise v2** | `tenant.reviews.read` | Solicitações ODCA (label; na verdade revisões internas) | reviews API + notificação com dedupe | `contract_reviews(_notifications)` | COMPROVADA (gate de plano v2) | v043 + c-run3 (fora do plano sufixo) |
| Solicitações ODCA documental (thread, prioridade, estados) | **enterprise** | solicitante | Central de solicitações | solicitation APIs | `odca_solicitations(+messages)` | COMPROVADA | v042 + d-run4 |
| SLA (1ª resposta × resolução, fuso/calendário, pausa/retomada, reatribuição sem reset, violação única) | enterprise | plataforma/cliente | fila AdminSolicitacoes + SLA | sla endpoints + worker | `sla_*` | COMPROVADA | v042 + worker |
| Suporte técnico para planos não-enterprise | basic/intermediate | qualquer | **indistinto hoje** (mesma central) | kinds de solicitação | — | PARCIAL — distinguir fluxo/SLA (Bloco B/C) | SolicitacoesController |
| Fila enterprise: responsável, prioridade, mensagens, resolução | — | plataforma | Solicitações e SLA | PlatformSolicitations | idem | COMPROVADA | v042 |

### Administração global
| Funcionalidade | Plano | Perfil | Menu | Endpoint | Persistência | Situação | Evidência |
|---|---|---|---|---|---|---|---|
| Visão global da plataforma | — | SuperAdministrator | Visão global | home APIs | — | COMPROVADA | suítes |
| Organizações: lista, detalhes, equipe/consumo/histórico | — | plataforma | Organizações/Clientes | admin orgs APIs | — | PARCIAL (filtros nome/plano/situação + bloqueio com motivo pela UI a completar — Bloco B §6) | Customers/Organizations views |
| Usuários: localizar global, vínculo/perfil, bloquear/reabilitar | — | plataforma | Usuários e acessos | admin users APIs | — | PARCIAL (busca global ausente) | API inventory |
| Planos: versões e benefícios; efeitos de mudança/bloqueio | — | plataforma | Planos e módulos | plan catalog APIs | `plan_versions/entitlements` | COMPROVADA (leitura) · mudança de plano pela UI a concluir | Plans view + repository |
| Mudança de plano (upgrade/downgrade) com efeito auditado | — | plataforma | **UI a concluir** | subscriptions (hoje via API/seed) | `subscriptions` | PARCIAL | subscriptions v1 expirada → v2 no histórico |
| Auditoria global com filtros (org, ator, ação, período; sem segredos) | — | plataforma | Auditoria | platform audit APIs | `audit_events` | COMPROVADA | e-run + PlatformAudit |

### Idiomas e textos
| Funcionalidade | Situação | Evidência |
|---|---|---|
| Mecanismo de localização (4 idiomas) | **AUSENTE** — sem `.resx`/`IStringLocalizer`/`RequestLocalization`; pt-BR hardcoded em views e middleware | grep em `Odca.Web` (zero hits de `AddLocalization`/`IStringLocalizer`) |
| Pref/linguagem simples/termos padronizados | PARCIAL — textos atuais já seguem tom claro; revisão terminológica no escopo do Bloco D | inspeção das views |
| Design/tokens | EXISTENTE — `site.css` com estados, drawer mobile, diálogos (`_ConfirmDialog`, `_Toasts`); homologação 360/768/1440 já praticada na Seção D | d-run4 (evidências PNG) |

## 5. Auditoria dos planos publicados (sem presumir BASIC/ENTERPRISE)

**Existem 3 códigos de plano** (v1 expirada em 2026-10-05, v2 vigente sem fim): `basic`, `intermediate`, `enterprise`. Entitlements v1≡v2 (cópia 1:1 na migração 034), exceto `reviews` que na **v2 ficou apenas enterprise**.

| Entitlement | unidade/período | basic v2 | intermediate v2 | enterprise v2 |
|---|---|---|---|---|
| Seats | usuários/vagas | 3 | 10 | 30 |
| Storage | GB | 10 | 100 | 500 |
| File size | MB | 25 | 100 | 250 |
| OCR pages | páginas/mês | 300 | 3000 | 15000 |
| Envelopes | unidades/mês | 10 | 50 | 200 |
| Reviews | módulo | ✗ | ✗ | ✓ |
| Módulos (documents/signatures/contracts/patients/agenda/obrigations/consumption) | toggles | conforme seed | conforme seed | conforme seed |

Gates de plano medidos (operacao → código canônico):
- Solicitações ODCA → `solicitations.plan.required` (enterprise)
- Perfil organizacional `plastic_surgery` → `organization.profile.plan_required` (enterprise)
- Rascunho com perfil cirúrgico → `draft.profile.surgical_required`
- Aprovação ODCA de modelos cirúrgicos → `approval.odca.required`
- Menus/features genéricos → `plan_restricted` / `feature_blocked` / `quota_exhausted` / `organization_suspended` / `membership_blocked`

Anomalias registradas (entradas dos blocos A/B):
1. Quota de PDF hardcoded (1GB) fora do catálogo.
2. `user_storage_bytes` declarada sem enforcement medido.
3. `signature_credit` sem consumo aplicado.
4. Sem caminho de ativação a partir de `commercial_pending`; `period_start/end` não usados.
5. Leitura de solicitações sem gate de plano (escrita tem).

## 6. Pendências críticas reconfirmadas (entrada do Bloco A — gate)

1. **Assinatura externa (envelope)** — fila `signature_envelopes` sem provedor = conclusão fictícia possível. Decisão D-OC3: canal "integração indisponível" explícito (mensagem dedicada, sem falsos positivos); modalidades session/evidence permanecem o caminho operacional comprovado.
2. **PDF** — validação de conteúdo (caracteres/tabelas/paginação) além de tamanho/status; garantir que emissão repetida não sobrescreva arquivo de versão já assinada; metadados persistidos já ok (v043). Verificar/estender asserts nas suítes.
3. **MFA** — fluxos UI homologados (E); reconfirmar regeneração de recovery codes e reinício; política atual mantém exigência só para SuperAdministrator (decisão registrada); contas demo preservadas por fingerprint (D-A5).
4. **Biblioteca** — cliente "carrega" modelos hoje (instalação por tenant); limite do ciclo exige superadministrador. Decisão D-OC1: catálogo oficial global (`scope='global'`) mantido na plataforma; cliente consulta e cria rascunhos; instalar/retirar viram operações de plataforma; `POST templates/official` mantém compatibilidade (idempotente) mas sai do menu cliente. Retirar importação da apresentação do cliente.
5. **Autorização** — reafirmar mensagens distintas (permissão/plano/limite/bloqueio/suspensão/estado/integração/sessão); falha de consulta de plano ≠ liberação (comportamento atual do layout já trata "situação não confirmada" — manter e cobrir na API).

## 7. Decisões a registrar (DECISIONS-LOG) junto da implementação

- **D-OC1** — Biblioteca oficial global sob gestão da plataforma; cliente lê publicado e cria rascunhos/copias; importação fora da apresentação do cliente.
- **D-OC2** — Política MFA: manter exigência obrigatória somente para SuperAdministrador (demo e demais perfis preservados); fluxos completos disponíveis na interface para quem adotar.
- **D-OC3** — Canal de assinatura por envelope = integração não contratada: estado explícito "integração indisponível"; conclusão exige evidência (session ou manual registrado).
- **D-OC4** — Registro central de navegação (chave/grupo/rótulo/rota/permisso/resource/plano) consumido pelo layout; fim de regras duplicadas no `_Layout`.
- **D-OC5** — Localização: recursos por domínio (`.resx`/catalog), allowlist `pt-BR,en,fr,es`, preferência persistida por usuário, fallback para pt-BR, retorno pós-troca apenas para URL local segura; trocas não perdem preenchimento nem alteram moeda/legislação/contrato emitido.
- **D-OC6** — Notificações: manter infra em app (tabelas existentes + dedupe); canal e-mail fica bloqueado/explícito (sem SMTP configurado) até integração contratada; centro de notificações no menu "Minha conta".

## 8. Baseline de hoje (concluído — GATE do Bloco A atendido)

Execução sequencial em 10/10 sobre o mesmo commit `786694c`, stack reiniciada, banco `odca_test_disposable` em v043:

| Suíte | Assertos | Falhas | seed_intacto | log |
|---|---|---|---|---|
| E (referência D-A5) | **109/109 PASS** | 0 | ✅ | `baseline-e-20261010-015413.log` |
| C | **99/99 PASS** | 0 | ✅ | `baseline-c-20261010-015440.log` |
| D | **115/115 PASS** | 0 | ✅ (+fixtures_removidas, plano_tc_restaurado) | `baseline-d-20261010-015601.log` |
| b34 (regressão) | **93/93 PASS** | 0 | ✅ | `baseline-b34-20261010-015646.log` |

Fingerprint dos seeds **após** as 4 execuções: `9b641fa0c67d2bc840054c24776c9feb` — **idêntico** ao pré-execução; 0 usuários `qa-%` residuais. Contas de demonstração preservadas (senha/permissões/MFA).

Itens do gate já demonstrados antes da implementação do Bloco A: baseline válido ✅ · MFA operacional (E) ✅ · PDF íntegro (D) ✅ · autorização coerente (C/D/matriz §4) ✅ · preservação das contas demo ✅ · assinatura sem conclusão fictícia → pendente da decisão D-OC3 (canal envelope), primeiro item a implementar.

## 9. Bloco A — implementação executada e revalidada (10/10/2026)

Escopo do commit de Bloco A: correções críticas (assinatura/PDF/MFA/biblioteca/autorização) já cobertas pelas suítes + as duas decisões de produto que mudam comportamento — **D-OC1** (biblioteca oficial global sob gestão da plataforma) e **D-OC3** (canal externo de envio declarado não configurado, sem entrega falsa). Migração **v044** aplicada (`CurrentVersion=44`, checksum `108c7c5e561f0a7047d06d52e80ad022c05864d93d6d48565170f5ee59610fc5`, snapshot `database/releases/odca-v044.sql`).

### Mudanças de código

| Área | Arquivo(s) | Mudança |
|---|---|---|
| Catálogo global | `src/Odca.Api/OfficialCatalogSeedingService.cs` (novo) + `Program.cs` | `IHostedService` publica os 9 modelos oficiais como linhas `scope='global'` (owner NULL, revisão 1) a cada start; idempotente por `official_key`; 5 tentativas (2 s); não-fatal; autor fixo do admin de plataforma; evento `template.official_catalog_seeded` |
| Dedupe de visibilidade | `src/Odca.Infrastructure/Contracts/NpgsqlTemplateCatalogRepository.cs` | `VisibleFilter`: linha global com `official_key` é ocultada quando o tenant possui cópia própria não arquivada da mesma chave |
| Instalação compatível | `src/Odca.Api/Controllers/ContractStudioCatalogController.cs` | exists-check do `POST studio/templates/official` encolheu para cópias próprias (`scope='private' AND owner_tenant_id=@tenant`) — arquivar→reinstalar voltou a funcionar (suíte E-S6) |
| Resolução/decisão/queue | `database/odca.sql` (v044) | `odca.resolve_official_template(tenant,key)` (ordem: último usado → cópia própria → linha global; sempre a versão publicada corrente); `template_approval_decide` passa a usá-lo (aprovação presa à versão aprovada mesmo via linha global); `template_approvals_queue` conta uso = cópias próprias ∪ versões geradas da linha global |
| Lembrete honesto | `src/Odca.Api/Controllers/ContractStudioController.cs` | `RemindPreparation`: evento, auditoria e resposta carregam `delivery={channel:"not_configured",sent:false,message}` — nada afirma entrega externa |
| Web | `Sheet.cshtml`, `Studio/Index.cshtml`, `Documents/NewDocument.cshtml`, `ContractsController.cs` | Botão "Instalar biblioteca oficial" e card de importação saíram da apresentação do cliente; textos de ajuda/empty-state retreinados para o catálogo publicado pela plataforma; mensagem de lembrete e diálogo de confirmação tornam explícito o canal externo não configurado |
| Esquema | `src/Odca.Infrastructure/Database/DatabaseSchema.cs` | `CurrentVersion` 43 → 44 (health `Schema de banco incompatível` dispara por design abaixo da versão canônica) |

### Evidência executada (stack reiniciada sobre o build do Bloco A)

1. **Seeder**: log da API `EventId 2204 "Catálogo oficial: 9 modelo(s) global(is) publicado(s)"`; banco com exatamente 9 linhas globais oficiais (`rev=1`); 9 eventos `template.official_catalog_seeded`.
2. **Dedupe/visibilidade**: tenant demo (possui as 9 cópias) vê **exatamente 9** oficiais publicados (não 18); tenant sem nenhuma cópia resolve o cirúrgico para a linha global (`resolve_official_template` → id global, v1) e o demo para a sua própria cópia.
3. **Decisão via linha global**: `POST /api/v1/platform/template-approvals/{tenant-sem-cópia}/surgical-consent/decision` → 200 `ok:true approvedRevision:1`; linha de aprovação carimbada com a `template_version_id` da **versão global** (probes diretos + asserts da suíte E-S6).
4. **Fila**: `template_approvals_queue` lista um tenant por linha (13 tenants distintos em prova), contagem de versões geradas correta.
5. **Regressão completa pós-mudança** (sequencial E→C→D→b34, banco `odca_test_disposable`):

| Suíte | Assertos | Falhas | novos asserts | seed_intacto | log |
|---|---|---|---|---|---|
| E (gate MFA/recovery) | **120/120 PASS** | 0 | S6: linha global publicada + decisão via global presa à versão global (3) · S7: recovery code em reinício de sessão, consumo único, reuso rejeitado, sessão reiniciada autorizada (8) | ✅ | `baseline-e-20261010-032233.log` |
| C | **99/99 PASS** | 0 | — (inalterada) | ✅ | `baseline-c-20261010-032414.log` |
| D | **116/116 PASS** | 0 | D02: catálogo publicado visível sem instalação (total ≥ 9 no tenant novo) (1) | ✅ (+fixtures) | `baseline-d-20261010-032429.log` |
| b34 (regressão) | **95/95 PASS** | 0 | T4.4: 9 linhas globais publicadas + dedupe sem duplicata no tenant dono (2) | ✅ | `baseline-b34-20261010-032619.log` |

Fingerprint dos seeds após a cadeia: `9b641fa0c67d2bc840054c24776c9feb` — **idêntico** ao baseline pré-execução (§8); 0 usuários `qa-%` residuais; schema em v44.

### Correções de suíte registradas neste ciclo

- **Ordem de cleanup (E)**: `mfa_recovery_codes` passou a ser removido antes de `sessions` — o novo S7 consome um código e grava `consumed_session_id`, criando FK para `sessions`; na ordem antiga a limpeza estolava no meio e o `finally` morria antes dos asserts de seed.
- **`SqlBest` (E)**: stderr do psql foi redirecionado para arquivo em vez de `2>&1` — com `$ErrorActionPreference='Stop'` o PS 5.1 converte registro de stderr em erro terminante, quebrando o contrato best-effort do batch de cleanup (documentado no comentário da função).
- **Metodologia preservada**: nenhuma expectativa anterior foi alterada para passar — só assertes novos foram adicionados (E 109→120, D 115→116, b34 93→95, C 99→99).
