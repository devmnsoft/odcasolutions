# Reconciliação S00/S01 — 09/09/2026

Referência examinada: branch de trabalho no SHA base `5ab8df3b8585101bae5d2068255c58e74f01ae81`.
Esta matriz distingue implementação, teste escrito e evidência efetivamente executada; nenhum serviço externo foi validado.

| Requisito | Implementação encontrada | Evidência executada | Lacuna | Ação desta evolução |
|---|---|---|---|---|
| Inicialização da API em teste | A fixture injeta conexão e JWT com `ConfigureAppConfiguration` | CI 34350222079 chegou à suíte PostgreSQL, mas 1 fluxo falhou antes das asserções HTTP | `Program` e Infrastructure liam configuração antes da composição do test host | Leituras adiadas para options/factories e validação no início do host; testes de validação adicionados |
| PostgreSQL isolado | Marcador, banco `odca_test*` e role exclusiva são obrigatórios | 8 testes de integração aprovados na CI citada, incluindo cenários PostgreSQL | SQL direto, concorrência e recuperação não comprovados | Preservadas as proteções; cenários avançados permanecem no backlog |
| MFA | Flag de configuração é exigida fora de Development | Nenhum fluxo de segundo fator aprovado | Não existe inscrição, desafio, recuperação nem nível MFA na sessão | Pendente; acesso após somente senha não representa o aceite final de S01 |
| Onboarding, assinatura e equipe | Somente catálogo/versionamento de planos | Nenhum fluxo comercial aprovado | Organização, membership, confirmação de e-mail, assinatura e convites ausentes | Pendente; não foi simulado pagamento ou ativação |
| Privacidade | Entrada pública, protocolo opaco e evento inicial | Testes unitários aprovados; fluxo HTTP ficou bloqueado pela inicialização na CI citada | Identidade, triagem, exportação e conclusão controlada ausentes | Correção de host desbloqueia a execução; operação permanece pendente |
| BFF distribuído | Cookie protegido com ticket em `IDistributedCache` | Build da CI aprovado | Implementação usa memória e Data Protection não compartilhado | Pendente para produção multi-instância |

O escopo amplo de S01 não é declarado concluído. O incremento atual fecha o bloqueio reproduzido de composição do host e mantém as demais lacunas explícitas para não confundir código existente com controle aprovado.
