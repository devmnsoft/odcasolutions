# D-OC14 — Estados: definição consistente por domínio

**Data**: 2026-10-11 · **Status**: Registrado

## Princípio

Cada entidade tem um estado próprio que representa UM fato específico. Um único
rótulo "concluído" não pode cobrir situações semanticamente diferentes.

## Definições

### Contrato (`odca.contracts.status`)

| Valor | Significado | Transições |
|-------|-------------|-----------|
| `active` | Vigente, em execução | → `archived`, → `closed` |
| `archived` | Retirado de circulação ativa, dados preservados (≠ rescisão) | → `active` (restaurar) |
| `closed` | Encerrado definitivamente pelo ciclo de vida | (terminal) |

### Documento / Versão (`odca.generated_contract_versions.security_status`)

| Valor | Significado |
|-------|-------------|
| `safe` | Gerado e íntegro; pode ser evidência ou download |
| `quarantine` | Suspeito de corrupção; bloqueado até revisão |
| `generated` | (estado inicial antes do scanner) |

Nota: `pdf_status='generated'` = PDF renderizado com sucesso. **Não significa
que o contrato foi assinado.**

### Assinatura (`odca.signature_preparations.status`)

| Valor | Significado |
|-------|-------------|
| `draft` | Preparada, aguardando confirmação |
| `confirmed` | Confirmada; signatários notificados |
| `partially_signed` | Pelo menos um signatário assinou |
| `all_signed` | Todos os signatários concluíram |
| `cancelled` | Anulada |

Nota: `all_signed` ≠ contrato ativo. A assinatura é um evento no documento, não
no contrato. O contrato muda de estado apenas quando uma alteração é aplicada.

### Solicitação de alteração (`odca.contract_change_requests.status`)

| Valor | Significado |
|-------|-------------|
| `draft` | Proposta em elaboração |
| `in_review` | Submetida, aguardando formalização |
| `internally_approved` | (intermediário; reserva para fluxos futuros) |
| `awaiting_formalization` | (intermediário; reserva para fluxos futuros) |
| `formalized` | Formalizada (evidência registrada); aguarda aplicação |
| `cancelled` | Cancelada com motivo |
| `conflict` | Conflito detectado na aplicação |

### Aplicação (`odca.contract_change_requests.application_status`)

| Valor | Significado |
|-------|-------------|
| `not_applied` | Formalizada, efeito ainda não executado |
| `scheduled` | Efeito futuro programado (effective_on > hoje) |
| `applied` | Efeito executado sobre o contrato |
| `failed` | Falha na aplicação (conflito, etc.) |

Nota: `application_status='applied'` + `status='formalized'` = alteração
completada com sucesso sobre o contrato.

### Atendimento / Solicitação ODCA (`odca.solicitations.status`)

| Valor | Significado |
|-------|-------------|
| `open` | Aberta, aguardando triagem |
| `in_progress` | Triagem/iniciada pela plataforma |
| `awaiting_customer` | Aguardando resposta do cliente |
| `resolved` | Resolvida pela plataforma |
| `closed` | Encerrada (cliente confirmou ou SLA expirou) |
| `paused` | Pausa justificada (prazos congelados) |
| `cancelled` | Cancelada (cliente ou plataforma) |

Nota: solicitação encerrada **não significa** contrato encerrado. São domínios
independentes.

## Regras de ouro

1. **Documento gerado ≠ contrato assinado.** A geração do PDF é um evento
   documental. A assinatura é um ato jurídico independente.
2. **Assinatura concluída ≠ alteração aplicada.** Assinar o aditivo não o
   aplica automaticamente; a aplicação é ato explícito na central.
3. **Solicitação encerrada ≠ contrato encerrado.** Cada solicitação ODCA é um
   atendimento; seu encerramento não afeta o ciclo de vida do contrato.
4. **Contrato arquivado ≠ rescisão.** Arquivar retira do fluxo ativo mas
   preserva histórico e permite restauração. Rescisão seria `closed`.
5. **Prazo indeterminado = `end_date IS NULL` + `clear_end_date=true`.**
   Não é "vencido" nem "sem vigência"; é vigente sem termo final.
