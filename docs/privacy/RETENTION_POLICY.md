# Política técnica de retenção

Valores reais precisam de aprovação antes de produção. O motor futuro será versionado por categoria e não usará um prazo único.

| Categoria | Evento inicial | Duração | Fundamento | Decisão final | Estado |
|---|---|---|---|---|---|
| Cadastro pendente | criação | a definir | necessidade operacional proposta | eliminar/anonimizar conforme aprovação | bloqueado para produção |
| Identidade ativa | encerramento do vínculo | a definir | contrato/segurança propostos | revisar impedimentos e eliminar/anonimizar | bloqueado para produção |
| Sessão | criação/revogação | a definir | segurança | eliminar payload técnico | bloqueado para produção |
| Auditoria de segurança | ocorrência | a definir por evento | prestação de contas/segurança | minimizar, reter ou eliminar | bloqueado para produção |
| Backups | criação do backup | janela a definir | continuidade | expirar e reaplicar decisões após restore | bloqueado para produção |

Inativação não equivale a eliminação. Legal hold requer motivo, escopo, autor, revisão e encerramento. Eliminação será em lotes idempotentes, com comprovante minimizado por destino e sem registrar novamente o conteúdo eliminado.
