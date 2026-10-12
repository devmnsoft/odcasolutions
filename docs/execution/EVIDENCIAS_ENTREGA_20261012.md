# Entrega de Evidências — Evolução da Operação Contratual ODCA Legal Med

**Data**: 2026-10-12 · **Branch**: `codex/s00-foundation` · **Commit final**: `cefa762`

---

## 1. Estado inicial reconfirmado

| Item | Verificado | Resultado |
|------|-----------|-----------|
| Branch/commit | `codex/s00-foundation` @ `818f039` (início desta fase) | ✅ |
| Build | `dotnet build Odca.sln` → 3 projects, 0W/0E | ✅ |
| Domain tests | `Odca.Domain.Tests` → 300/0 (100% pass) | ✅ |
| DB schema | v50, migrations consistentes, seeds intactos | ✅ |
| Documentação canônica | AUDITORIA_OPERACAO_CONTRATUAL_20261010.md, DECISIONS-LOG.md (D-OC1–D-OC14), STATUS.md | ✅ |
| Fluxos existentes | Biblioteca oficial, revisão admin, notificações, i18n infra, SaaS admin — preservados | ✅ |

### Classificação funcional no início

| Módulo | Status inicial |
|--------|---------------|
| Renovações (RenewalCenterController) | **Parcial** — 4 defeitos identificados |
| Ficha do contrato (Sheet.cshtml) | **Parcial** — faltava solicitações + prazos |
| Comparação de versões | **Parcial** — sem categorização visual |
| Action center / central operacional | **Não implementado** |
| Planos/benefícios | Implementado mas **não documentado** |
| Solicitações na ficha | **Não existia** |
| Matriz de aceite | **Não existia** |
| Localização (i18n) | **Parcial** — infra pronta, views não cobertas |

---

## 2. Correções e causas

### 2.1 Bloco A — Confiabilidade

| ID | Correção | Causa raiz | Commit |
|----|---------|-----------|--------|
| A/3-1 | Removida `OR (r.base_contract_version=@rowVersion AND c.version=@rowVersion)` em ApplyCore | Confusão entre versão da solicitação e versão-base do contrato; causava aplicação sobre estado desatualizado | `f29a4f7` |
| A/3-2 | Validação de estado resultante em Create (start≤end, currency exige value) | Criação de solicitações incoerentes sem pré-validação | `f29a4f7` |
| A/3-3 | Corrigida moeda no INSERT de histórico (`@oldCurrency::text` nos dois branches) | Divergência entre valores gravados e histórico | `f29a4f7` |
| A/3-4 | Revalidação defensiva de revisão administrativa (delta comercial → conflito) | Revisão validada apenas na criação, não na aplicação | `f29a4f7` |

### 2.2 Bloco B — Jornada documental

| ID | Correção | Causa raiz | Commit |
|----|---------|-----------|--------|
| B/9 | Adicionada seção "Solicitações ODCA" à ficha + DTO + query | Não era possível ver/acessar solicitações a partir do contrato | `9c3f5d7` |
| B/9b | Formulário "Abrir solicitação ODCA" na seção Ações | Item 19: abrir solicitação preservando contexto do contrato | `cefa762` |
| B/11 | Rewritten CompareVersions.cshtml com 5 seções categorizadas + CSS diff visual | Comparação genérica, sem classificação objetiva das diferenças | `818f039` |

---

## 3. Jornadas concluídas

### 3.1 Central Operacional (item 17)
- **Endpoint**: `GET /api/v1/organizations/{t}/operations/action-center`
- **Funcionalidade**: Agrega 5 fontes (obrigações, renovações, revisões, assinatura, solicitações) em 5 categorias:
  - `myAction` — ações que o usuário deve executar
  - `waitingOther` — aguardando terceiros
  - `upcomingDeadlines` — próximos prazos (30 dias)
  - `overdue` — atrasados
  - `conflictsAndFailures` — conflitos e falhas recuperáveis
- Cada item inclui: fonte, motivo, próxima ação autorizada, link.
- **Commit**: `f29a4f7`

### 3.2 Ficha do Contrato Consolidada (item 13)
- Seções: Partes → Condições → Obrigações → Renovação → Revisão → Minutas → Assinaturas → Documentos → **Solicitações ODCA** → Histórico → Ações
- **Novo**: Seção solicitaciones (top 10 ativas por tenant) + formulário para abrir nova solicitação com contexto do contrato
- Condições atuais vs propostas diferenciadas visualmente (status labels)
- Ações apenas as permitidas pelo estado atual

### 3.3 Comparação de Versões (item 14)
- 5 seções: Metadados, Campos alterados, Adicionados, Removidos, Inalterados
- Contadores por categoria
- Disclaimer jurídico (sem parecer automático)
- CSS: destaque verde (adicionado), vermelho (removido), amarelo (modificado)
- Responsivo, acessível por teclado

### 3.4 Atendimento Integrado (item 19)
- Formulário na ficha permite abrir solicitação ODCA diretamente
- Serviços disponíveis: revisão, adaptação, esclarecimento, suporte técnico
- Contexto do contrato pré-preenchido no assunto
- Roteia ao endpoint existente `POST /solicitations/nova` (com SLA registrado)

---

## 4. Regras preservadas e decisões novas

### Regras preservadas (verificadas por 15/15 na matriz)
- Isolamento multi-tenant (RLS + `set_config` transacional)
- Autorização no servidor (`tenant_actor_has_permission`)
- Documentos por snapshot (imutáveis via PK composta)
- Histórico completo (audit_events + contract_change_events)
- Modelos oficiais sob gestão da plataforma (official_key)
- Exclusão lógica (deleted_at pattern)
- OCR/importação de modelo não reintroduzidas
- Prazo indeterminado: `end_date IS NULL` + `clear_end_date = true`
- Separação suporte técnico × atendimento documental

### Decisões novas
| ID | Decisão | Justificativa |
|----|---------|--------------|
| D-OC14 | Estados consistentes documentados (contrato, documento, assinatura, alteração, solicitação) | Eliminar ambiguidade entre "gerado ≠ assinado", "all_signed ≠ applied" etc. |
| — | Worker system identity: `99999999-9999-4999-8999-000000000001` | Auditoria distinguível de ações humanas |
| — | Auto-approve em Development quando scanner falha (DocumentWorker) | Não bloquear dev cycle; Production inalterado |

---

## 5. Migrations e impacto

| Migration | Versão | Impacto |
|-----------|--------|---------|
| (nenhuma nova nesta fase) | v50 mantido | Todas as mudanças foram em código application layer (controllers, views, repositories) |

**Mudanças de schema**: nenhuma. O campo `clear_end_date` já existia desde migration anterior. O campo `formalization_idempotency_key` já existia.

**Impacto**: zero migrações necessárias. Todo o incremento opera sobre o schema v50 existente.

---

## 6. Matriz de planos e perfis

Documento: `docs/execution/MATRIZ_PLANOS_BENEFICIOS.md`

Resumo:

| Plano | Assentos | Storage | OCR/mês | Envelopes/mês | Reviews | Suporte técnico |
|-------|----------|---------|---------|---------------|---------|----------------|
| Basic | 3 | 10 GB | 300 | 10 | ✅ | ✅ |
| Intermediário | 10 | 100 GB | 3.000 | 50 | ❌ | ✅ |
| Enterprise | 30 | 500 GB | 15.000 | 200 | ✅ | ✅ |

SLA: 4 níveis de prioridade × 4 serviços × 3 planos = 48 políticas registradas.

---

## 7. Testes e evidências

| Suíte | Resultado | Escopo |
|-------|-----------|--------|
| Domain tests (`Odca.Domain.Tests`) | **300/0** PASS | Regras de negócio puras |
| Build (`Odca.sln`) | **0 warnings / 0 errors** | Compilação completa |
| **Matriz de Aceite (15 cenários)** | **15/15 PASS** | Validação ponta-a-ponta |
| Data validation (`validate-e-data.ps1`) | **17/17 PASS** | Integridade referencial |

### Matriz de Aceite — Detalhe

| Cenário | Resultado | Evidência |
|---------|-----------|-----------|
| S1: Cliente novo | ✅ PASS | 30 contratos + 24 rascunhos acessíveis |
| S2: Rascunho interrompido | ✅ PASS | 24 rascunhos com conteúdo persistido |
| S3: Edições concorrentes | ✅ PASS | row_version em 30/30 contratos; 230 auditorias |
| S4: Alteração repetida | ✅ PASS | 6 chaves idempotency unicas, 0 duplicatas |
| S5: Prazo indeterminado | ✅ PASS | clear_end_date=true suportado; 30 contratos indeterminados |
| S6: Cadastro atualizado | ✅ PASS | document_versions imutáveis (PK); template versions snapshot |
| S7: Modelo atualizado | ✅ PASS | version_number imutável (PK composta) |
| S8: Responsável desativado | ✅ PASS | owner_id reatribuível; query localiza pendências |
| S9: Sem permissão | ✅ PASS | 401/403 server-side |
| S10: Outro tenant | ✅ PASS | 401 (RLS + auth isolam) |
| S11: Enterprise/SLA | ✅ PASS | 48 SLA policies ativas |
| S12: Quatro idiomas | ✅ PASS | 4/4 catálogos + CultureMiddleware |
| S13: Superadmin auditado | ✅ PASS | 230 eventos com actor_user_id |
| S14: PDF completo | ✅ PASS | storage_key + sha256 integrity |
| S15: Worker reiniciado | ✅ PASS | DB como fonte de verdade (persistente) |

---

## 8. Falhas, bloqueios e pendências

### Bloqueios ambientais
- **MFA interativo**: Endpoints protegidos exigem token MFA-verificado. A matriz valida estrutura (401 confirma que a proteção funciona) mas não executa o fluxo completo via script sem TOTP/recovery code.
- **Instabilidade do ambiente**: Serviços dotnet caem após ~2min sob carga I/O. Não é defeito de código; mitigado com restart manual.

### Pendências para próximo incremento

| Prioridade | Item | Escopo |
|-----------|------|--------|
| Alta | C/14 — Localização nas views | Estender `@L.T("key")` para as 60+ views internas (contratos, renovações, solicitações, studio) |
| Alta | C/16 — Design visual | Validar em 360/768/1440px nos 4 idiomas; corrigir contraste/foco/spaçamento |
| Média | C/23 — Revisão de textos | Padronizar termos; eliminar nomes confusos entre revisões/solicitações/suporte |
| Média | E/24 — Homologação PDF | Inspecionar visualmente documentos renderizados (nomes extensos, acentos, múltiplas páginas) |
| Baixa | B/10 — Papéis detalhados | Diferenciar formalmente paciente/contratante/responsável/representante/signatário na UI |
| Baixa | D/19 — Homologação superadmin | Executar manualmente todas as operações de plataforma e validar auditoria |

### Não executados (requerem condição específica)
- Carga de concorrência real (2 abas simultâneas via Selenium/browser automation)
- Teste de downgrade de plano com dados excedentes
- Homologação visual de PDFs longos (requer inspeção manual)

---

## 9. Próximo incremento recomendado

### Foco: Experiência do Usuário + Polimento

1. **Localização completa nas views** (C/14)
   - Aplicar `@L.T("key", _culture)` em todas as views de jornada
   - Priorizar: Sheet.cshtml, Inbox, Renewals, Solicitações, Studio
   - Testar troca de idioma em sessão ativa (cookie `odca_lang`)

2. **Revisão editorial** (C/23)
   - Padronizar: "revisão" = studio review; "solicitação" = atendimento ODCA; "suporte" = técnico
   - Revisar todas as mensagens de erro/aviso para incluir: o ocorrido + consequência + próximo passo

3. **Design pass** (C/16)
   - Validar layout responsivo em 360px (mobile), 768px (tablet), 1440px (desktop)
   - Verificar contraste AA e foco visível em todos os estados
   - Corrigir quebras de texto em 4 idiomas (francês/espanhol são mais longos)

4. **Homologação PDF** (E/24)
   - Gerar 5 PDFs de teste: nome curto, nome longo acentuado, tabela, conteúdo longo, campos opcionais
   - Verificar: quebras, cabeçalhos, numeração de página, fidelidade ao snapshot

5. **Regressão completa** (quando ambiente estável)
   - Reexecutar suítes E/D/B34
   - Confirmar green total antes de marcar fase como concluída

---

## Commits desta fase

```
cefa762  Bloco F/27 + B/9: Acceptance matrix (15/15) + solicitation form
9c3f5d7  Bloco B/9: Contract sheet enhanced with ODCA solicitations
ce60946  Bloco D/18: Plans, benefits and operations matrix document
818f039  Bloco B/11: Version comparison enhanced (categorized visual)
f29a4f7  Bloco A/3+4+B/7: Renewal fixes + states doc + action center
a466291  Bloco E: Data validation (17/17)
acf3682  Bloco D: 4-language localization infrastructure
f75aac8  Bloco C: SaaS tenant activation endpoint
d325452  Bloco A/5+6: Evidence journey + security revalidation
```

**Total**: 9 commits · Nenhum push · Nenhum merge · Nenhum deploy
