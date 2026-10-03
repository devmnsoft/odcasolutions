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

## Apresentação sob suspensão — limitação documentada

Homologação de 02/10/2026 (navegador real + banco real): suspensão e restauração da organização demonstração foram exercidas pela tela `administracao/clientes/{id}/situacao` e pela API, com verificação no banco (`tenants.status`) e na sessão do membro. O contrato de API está correto, mas a apresentação para o membro da organização suspensa tem uma limitação conhecida:

- **Membro com sessão aberta:** a validação de sessão rejeita qualquer chamada com escopo de organização quando a única organização do usuário está suspensa (`odca.user_has_active_access` exige `tenants.status='active'`, sem exceção para a consulta de funcionalidades). Como o banner dedicado do layout (`_Layout.cshtml`) depende de um `GET .../features` bem-sucedido para ler `tenant_status`, ele não é exibido; o membro vê o alerta genérico “Falha ao consultar funcionalidades … Esta mensagem indica falha de consulta, não ausência de bloqueios” e o rótolo estático “ORGANIZAÇÃO ATIVA” no topo. Ou seja: sob suspensão, a tela não distingue “suspensa” de “falha de consulta”.
- **Novo login:** o login de membro da organização suspensa responde com o erro genérico de credenciais (a busca por login retorna nula para usuário sem acesso ativo, sem incrementar tentativas falhas). Não há mensagem “organização suspensa” neste caminho.
- **Contrato preservado:** quem tem outra organização ativa continua recebendo `organization_suspended` na organização suspensa e usa a outra normalmente (regra da tabela acima). A restauração pelo superadministrador volta a ser efetiva imediatamente (sessão do membro aceita novamente no mesmo instante; homologado nos dois sentidos).

Decisão deste ciclo: **limitação documentada, não correção de código**. O estado canônico (`tenants.status` + validação de sessão + portão de funcionalidades) funciona; o aprimoramento seria permitir à camada de apresentação distinguir suspensão de falha de autenticação genérica (por exemplo, endpoint de status público para o layout ou erro estruturado específico no login), sem mudar as regras acima.
