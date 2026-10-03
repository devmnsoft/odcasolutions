# Bloqueios e liberação

Estes estados são independentes. Restaurar um deles não remove os demais, não altera permissões e não registra pagamento.

| Estado | Onde vale | Efeito | Como desfazer |
| --- | --- | --- | --- |
| Organização suspensa | Menu, API e sessão operacional | Cadastros e documentos permanecem. Se essa é a única organização ativa do usuário, a sessão deixa de ser aceita. Se o usuário ainda tem outra organização ativa, a organização suspensa responde `organization_suspended` e a outra continua utilizável. | Superadministrador restaura com justificativa. A auditoria registra `organization.restored`. |
| Usuário bloqueado | Autenticação global | O usuário não inicia sessão. Outras pessoas da organização continuam. | Desbloqueio global do usuário, pela política já existente. |
| Vínculo bloqueado | Uma organização | O mesmo usuário permanece ativo nas outras organizações em que o vínculo não foi bloqueado. | Reativação somente daquele vínculo. |
| Funcionalidade bloqueada | Menu, API e processamento de importação | A chamada direta recebe `feature_blocked`. O motivo fica na auditoria `organization.feature_blocked`. | Superadministrador libera com justificativa. A auditoria registra `organization.feature_released`. |
| Restrição de plano | Assinatura e importação, quando o plano não tem a franquia habilitada | A API responde `plan_restricted`. Não é bloqueio administrativo nem falta de permissão. | Alteração do plano pelos fluxos já existentes. A liberação administrativa não cria cobrança. |
| Permissão insuficiente | API, conforme o perfil | A operação responde como falta de permissão. O vínculo e a funcionalidade podem estar liberados. | Ajuste do perfil pelo mecanismo canônico de permissões. |

Funcionalidades administráveis: pacientes, minutas, modelos, documentos e PDF, revisões, assinatura e importações. A consulta do catálogo fica em `GET /api/v1/organizations/{id}/features`. O superadministrador grava a decisão em `PUT /api/v1/organizations/{id}/features/{codigo}`.

A varredura e a extração de importações não retiram trabalho de uma organização enquanto a funcionalidade de importação estiver bloqueada ou fora do plano. O trabalho permanece na fila e volta a ser processado depois da liberação.

## Apresentação

O rótulo “ORGANIZAÇÃO ATIVA” só aparece depois que o catálogo autenticado de funcionalidades confirma `tenant_status=active`. Não há endpoint público de situação.

| O que o membro vê | Quando |
| --- | --- |
| ORGANIZAÇÃO SUSPENSA | O catálogo autenticado informa suspensão, ou a operação devolve `organization_suspended`. |
| SESSÃO NÃO ACEITA | A API recusa a sessão (401). Pode ser usuário bloqueado ou sessão encerrada. O login novo continua com o erro genérico de credenciais, sem dizer se a conta existe. |
| ACESSO NÃO CONFIRMADO | A consulta da organização volta 403. Pode ser vínculo bloqueado ou permissão insuficiente. Outra organização do mesmo usuário não entra nessa recusa. |
| SITUAÇÃO NÃO CONFIRMADA | Falha de comunicação, tempo esgotado ou resposta sem situação. A tela não trata a organização como ativa. |
| Módulo bloqueado ou fora do plano | O catálogo distingue `administratively_blocked` e `plan_restricted`. Liberar o módulo não remove plano nem suspensão. |

A única exceção de sessão é `GET /api/v1/organizations/{id}/features`: uma sessão ainda válida, de usuário não bloqueado, pode ler a situação da organização em que o vínculo está ativo mesmo quando essa é a única organização e ela está suspensa. As demais rotas continuam exigindo acesso ativo. Quem não tem vínculo ativo recebe 403, sem lista de organizações ou usuários.
