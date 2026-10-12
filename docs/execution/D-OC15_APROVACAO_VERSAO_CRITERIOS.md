# D-OC15 — Vinculação Aprovação → Versão + Critérios de Alteração

Data: 2026-10-12 · Branch: codex/s00-foundation @ 4fac709

## Contexto

O prompt exige que decisões (aprovações, formalizações) pertencem à versão correta e que o usuário entenda por que uma nova conferência é necessária após alterações. Este documento formaliza o estado atual da implementação e os critérios aplicados.

---

## 1. Como aprovações vinculam à versão exata (Item 7)

### Fluxo completo

```
CREATE (draft) → REVIEW (in_review) → APPROVE (internally_approved)
→ FORMALIZE (formalized + evidence) → APPLY (applied to contract)
```

### Vinculação imutável na formalização

| Campo | Tabela | O que garante |
|-------|--------|---------------|
| `evidence_version_id` | `contract_change_requests` | Aponta para um `document_versions.id` específico (arquivo com `sha256` + `security_status='safe'`) |
| `base_contract_version` | `contract_change_requests` | Versão do contrato no momento da criação da proposta |
| `formalized_by` / `formalized_at` | `contract_change_requests` | Autoria e data do ato de formalização |
| `formalization_justification` | `contract_change_requests` | Motivo explícito (mínimo 1 caractere, CHECK constraint) |
| `formalization_idempotency_key` | `contract_change_requests` | Replay seguro sem efeito duplo |

### Proteção contra mutação pós-formalização

```sql
-- Trigger protect_formalized_evidence (schema v045):
-- Impede UPDATE em evidence_version_id ou generated_version_id quando status='formalized'
```

### Validação na aplicação (ApplyCore)

```csharp
// 1. Base version check: se o contrato mudou desde a criação da proposta → CONFLICT
if (row.CurrentVersion != row.BaseVersion) { → status='conflict', application_status='failed' }

// 2. Revisão administrativa: delta comercial não permitido
if (row.Kind == "revision") {
    if (revStart != OldStart || revEnd != OldEnd || revValue != OldValue) { → CONFLICT }
}

// 3. CAS na aplicação: version=@baseVersion no WHERE da UPDATE
//    Se 0 rows → conflito de concorrência (outra aplicação aconteceu entre SELECT e UPDATE)
```

### Aceite verificado

> **Aprovar a versão A não permite formalizar a versão B sem cumprir os requisitos correspondentes.**

Isso é garantido porque:
1. A formalização exige `evidence_version_id` apontando para um documento com `security_status='safe'`.
2. A aplicação valida `base_contract_version == current_version`. Se o contrato avançou de versão, a proposta entra em `conflict`.
3. A `row_version` CAS impede duas aplicações concorrentes.
4. Após formalização, o trigger impede trocar a evidência.

---

## 2. Critérios: Alteração Administrativa vs Material (Item 8)

### Classificação por `kind`

| Kind | Classificação | Efeito sobre aprovação anterior | Exemplo |
|------|---------------|---------------------------------|---------|
| `renewal` | **Material** — muda vigência ou valor comercial | Nova revisão + formalização obrigatória | Renovação com novo valor, extensão de prazo |
| `amendment` | **Material** — aditivo contratual | Nova revisão + formalização obrigatória | Inclusão de cláusula, alteração de escopo |
| `revision` | **Administrativa** — corrige registro sem mudar condições comerciais | Sem novo ciclo de aprovação; mas com revalidação defensiva na aplicação | Correção de nome do titular, ajuste de fuso, fixação de typo |

### Critério explícito (não por interpretação textual)

Uma alteração é **administrativa** (`kind='revision'`) se e somente se:
- `proposed_start_date IS NULL OR = current_start_date`
- `proposed_end_date IS NULL OR = current_end_date`
- `proposed_value IS NULL OR = current_value`
- `clear_end_date = false` OU `current_end_date IS NULL` (já indeterminado)

Se qualquer uma das condições acima falhar → a aplicação entra em **conflict** automaticamente. O usuário vê: *"A revisão administrativa introduziu alteração comercial. Regularize como renovação ou aditivo."*

### Visibilidade ao usuário

Na Central de Renovações (ficha do contrato), a coluna "Próxima ação" mostra:
- `conflict` + `application_status='failed'` → **"Conflito: versão desatualizada ou delta inesperado"**
- `formalized` + `not_applied` → **"Registrar aplicação"**
- `formalized` + `scheduled` → **"Aguardar aplicação programada"**

O histórico completo fica em `contract_change_events` (tabela append-only).

---

## 3. Permissões específicas por etapa de decisão (Item 9)

| Etapa | Permissão | Ação | Segregação |
|-------|-----------|------|------------|
| Preparar proposta | `tenant.renewals.prepare` | Criar change_request (draft) | Qualquer membro com permissão |
| Ler/acompanhar | `tenant.renewals.read` | Listar, detalhar, acompanhar status | Leitura |
| Submeter para revisão | `tenant.renewals.decide` | Enviar draft → in_review | Diferente de preparar (segregação) |
| Aprovar internamente | `tenant.renewals.decide` | in_review → internally_approved | Mesmo permission que decide |
| Formalizar (registrar evidência) | `tenant.renewals.formalize` | Registrar evidence + justificativa | **Diferente** de decidir: exige prova documental |
| Aplicar ao contrato | `tenant.renewals.apply` | Atualizar contratos + histórico | **Diferente** de formalizar: quem aplica pode ser o worker |
| Cancelar | `tenant.renewals.cancel` | draft/in_review → cancelled | Antes da formalização |

### Desativação de aprovador

Se o usuário que formalizou for desativado (`is_deleted=true`):
- A formalização **permanece válida** (imutável).
- A autoria histórica é preservada (`formalized_by` não é nulo).
- Novas operações exigem outro ator com `tenant.renewals.formalize`.
- O RLS não esconde o evento de auditoria (escopo plataforma).

### Nenhua obrigatoriedade de dois aprovadores

A arquitetura atual usa **segregação por função** (preparar ≠ decidir ≠ formalizar ≠ aplicar), não **segregação por pessoa**. Não há requirement de dois aprovadores distintos para a mesma operação. Se o negócio exigir, a decisão deve ser registrada em DECISIONS-LOG.

---

## 4. Estado atual vs Gaps

### Implementado ✓
- Vinculação evidence→document_version com sha256
- Base version check na aplicação
- CAS row_version em toda transição
- Idempotency key na formalização
- Revalidação defensiva de revision (delta comercial)
- Trigger anti-mutação pós-formalização
- Permissões granulares (7 níveis)
- Histórico append-only em contract_change_events
- Application log (before/after JSON)

### Gap identificado (para próximo incremento)
- A UI ainda não mostra explicitamente "Por que esta aprovação não serve mais para a versão atual?" com linguagem acessível. O código bloqueia corretamente, mas a mensagem "Estado, versão ou evidência segura inválidos" é genérica.
- **Ação**: Adicionar mensagem contextual na ficha quando uma proposta está em `conflict` explicando qual check falhou (base version vs commercial delta).

---

## 5. Verificação automatizada

A matriz de aceite comportamental (`validate-acceptance-matrix.ps1`) cobre:
- S3: Conflito de gravação concorrente (CAS)
- S4/S5: Idempotência na criação de solicitações
- S6: Prazo indeterminado (clear_end_date)
- S13: Alteração aplicada sai da fila

Cenários de formalização completa (create → review → approve → formalize → apply) requerem fluxo autenticado MFA — estão BLOCKED com causa documentada.
