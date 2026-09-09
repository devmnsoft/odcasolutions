# Registro de operações de tratamento

Modelo técnico versionado; não é parecer jurídico nem contém dados pessoais reais.

| Versão | Atividade | Finalidade | Titulares/dados | Origem e necessidade | Hipótese proposta | Responsável | Sistemas/destinatários/países | Retenção | Controles | Revisão |
|---|---|---|---|---|---|---|---|---|---|---|
| 1 | Identidade e acesso | Autenticar, proteger contas e auditar eventos | Usuários; identificação, contato, hash e eventos | Cadastro/uso; necessário ao acesso individual | Contrato e legítimo interesse de segurança — **validação pendente** | Privacidade e Segurança | PostgreSQL/API; equipe autorizada; Brasil | ver `RETENTION_POLICY.md` | hash, lockout, sessão revogável, minimização de logs | pendente |

Cada funcionalidade futura deve acrescentar versão antes da liberação. Integração de produção fica bloqueada enquanto finalidade, escopo e fornecedor não tiverem responsável e aprovação configurados.
