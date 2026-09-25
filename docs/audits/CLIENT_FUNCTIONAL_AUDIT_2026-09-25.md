# Auditoria funcional do perfil cliente — 25/09/2026

## Base, execução e limites da evidência

- Checkout auditado: branch `work`, commit inicial
  `2232175a64a529de88f9615ca6d1b793d530be3d` (`AJUSTE`), com árvore limpa.
- O repositório fixa o SDK .NET `10.0.400` (`rollForward=latestPatch`) e Node >= 24.
  O executor disponível possui Node `20.20.2`, mas não possui `dotnet`, `docker`, `psql`
  nem servidor PostgreSQL. Por isso nenhum host foi iniciado e nenhuma evidência de
  navegador ou banco foi produzida nesta execução.
- O schema canônico é `odca`, com versão corrente 028. A aplicação exige PostgreSQL,
  API, Web e Worker; o Bootstrap é o migrador/provisionador. Nenhum banco foi limpo,
  recriado ou alterado.
- O perfil previsto para homologação é `cliente.teste@odca.local`, role
  `tenant-client`, organização **Cliente Teste ODCA**. O provisionamento e a senha não
  foram testados nesta máquina. Uma decisão de revisão requer ainda um membro sintético
  ativo com `tenant.reviews.decide`.

Assim, “validada funcional” abaixo só é usado quando já existe evidência executável no
checkout que não depende deste incremento. “Corrigida e validada” exige banco e browser
e não é atribuído sem essa evidência. A inspeção de código não é apresentada como teste
de persistência.

## Causas e correções imediatas

1. **SQLSTATE 42601:** o literal raw do `SELECT` de importações terminava imediatamente
   antes do fragmento `WHERE`, formando `u.id=i.requested_byWHERE`. A composição agora
   insere quebras de linha explícitas, tipa `requester` nulo como `uuid` e usa ordenação
   determinística por `created_at, id`. Contagem e listagem continuam usando exatamente
   o mesmo filtro parametrizado.
2. **SQLSTATE 42883:** o helper `Allowed` da fila chamava a função inexistente
   `odca.has_tenant_permission`. A função canônica definida e concedida nas migrations é
   `odca.tenant_actor_has_permission(actor, tenant, permission)`. O helper passou a usar
   essa função, sem alias de compatibilidade, bypass ou mudança de grants.

Não é necessária migration para as duas correções: ambas são divergências no código da
API em relação ao schema já canônico. A busca no checkout depois da correção não encontra
consumidor executável de `has_tenant_permission`; permanece apenas uma menção histórica
em documentação, que não é chamada pelo runtime.

## Matriz completa das superfícies do cliente

| Tela / superfície | Rota Web | Permissão principal | Consulta e ações expostas | Persistência | Resultado / classificação | Correção necessária / evidência final |
|---|---|---|---|---|---|---|
| Login | `/entrar` | pública | autenticar | sessão, lockout e auditoria | **Ainda incompleta nesta auditoria** | Executar login real, troca de senha e reload; ambiente sem hosts. |
| Troca de senha / MFA / recuperação | `/alterar-senha`, `/mfa/inscricao`, `/mfa/desafio` | usuário autenticando | trocar senha, inscrever e desafiar MFA | usuário, segredo protegido, recovery codes e sessão | **Ainda incompleta nesta auditoria** | Validar pelo browser e banco com a conta Development. |
| Seleção de organização | `/organizacoes` | membership ativo | listar e selecionar por nome | contexto explícito na URL/sessão BFF | **Ainda incompleta nesta auditoria** | Validar manutenção do tenant e negativa cruzada. |
| Início do cliente / primeiros passos | `/cliente`, `/organizacoes/{tenantId}/primeiros-passos` | membership/capacidades | resumo e atalhos reais | leitura | **Ainda incompleta nesta auditoria** | Requer API e banco. |
| Organização | `/organizacoes/{tenantId}/editar` | `tenant.organization.manage` | consultar e editar cadastro | tenant + versão | **Ainda incompleta nesta auditoria** | Testar conflito otimista e releitura. |
| Equipe — pessoas | `/organizacoes/{tenantId}/equipe?tab=pessoas` | `tenant.team.read/manage` | listar, alterar vínculo e papéis | memberships/member_roles/auditoria | **Ainda incompleta nesta auditoria** | Testar último administrador, assentos e tenant cruzado. |
| Equipe — convites | `/organizacoes/{tenantId}/equipe?tab=convites`, `/convites/aceitar` | `tenant.team.manage` | criar, reenviar, revogar e aceitar | convite/outbox/membership | **Bloqueada por dependência identificada** | Entrega depende do transporte; registro não prova mensagem entregue. |
| Equipe — perfis | `/organizacoes/{tenantId}/equipe?tab=perfis` | `tenant.team.read/manage` | listar/criar/editar/arquivar perfil | roles/role_permissions | **Ainda incompleta nesta auditoria** | Testar catálogo delegável e proteção administrativa. |
| Pacientes | `/organizacoes/{tenantId}/pacientes` | `tenant.patients.read` | listar, buscar, paginação e inativos | leitura tenant-scoped | **Ainda incompleta nesta auditoria** | Requer execução com dois tenants. |
| Novo/editar paciente e representante | `/pacientes/novo`, `/pacientes/{id}/editar` | `tenant.patients.manage` | cadastro mínimo, edição e representante | patients/patient_representatives + versão | **Ainda incompleta nesta auditoria** | UI/API existem; validar 400/409, reload e auditoria. |
| Ficha e situação do paciente | `/pacientes/{id}` | `tenant.patients.read/manage` | ficha, acervo, inativar/restaurar | patient + histórico preservado | **Ainda incompleta nesta auditoria** | Testar conflito de identificador e versão concorrente. |
| Estúdio / catálogo | `/organizacoes/{tenantId}/estudio` | `tenant.templates.read`, `tenant.contract_drafts.*` | catálogo publicado, criar minuta (com ou sem paciente) | template/draft/snapshot | **Ainda incompleta nesta auditoria** | Confirmar jornada paciente e preservar B2B sem paciente. |
| Edição da minuta | `/estudio/minutas/{id}` | `tenant.contract_drafts.manage` | campos, blocos, pendências, comentários, autosave | draft + row version | **Ainda incompleta nesta auditoria** | Testar reload, idempotência e concorrência. |
| Versão imutável / PDF | `/estudio/versoes/{id}` | leitura da minuta/documento | visualizar, gerar/baixar PDF, revisão | generated version + hash/PDF | **Bloqueada por dependência identificada** | Renderização e download precisam dos hosts; verificar bytes/hash e autorização. |
| Participantes da versão | `/estudio/versoes/{id}#participantes` | gestão da minuta | incluir, editar, ordenar, retirar, confirmar e reabrir | preparation revisions/events | **Ainda incompleta nesta auditoria** | UI e API existem; confirmar contra PDF, histórico, comparação e replay no banco. |
| Solicitações de revisão — fila | `/organizacoes/{tenantId}/solicitacoes` | `tenant.reviews.read/decide` | filtros, escopos, responsáveis | leitura paginada | **Ainda incompleta** | Helper usa função canônica; testar perfis permitido/negado, vínculo bloqueado e tenant inativo. |
| Solicitação de revisão — detalhe | `/solicitacoes/{reviewId}` | `tenant.reviews.read/decide` | comentário, reatribuição, aprovar/ajustes | comments/steps/events/notifications | **Ainda incompleta** | O bloqueio 42883 foi removido; ainda requer requester e reviewer reais. |
| Importações — lista | `/organizacoes/{tenantId}/importacoes` | `tenant.imports.read` | status, solicitante, minhas, revisão, datas | leitura paginada | **Ainda incompleta** | SQL 42601 corrigido; executar todas as combinações pedidas no PostgreSQL. |
| Importação — revisão/preview | `/importacoes/{id}`, `/importacoes/preview/...` | `tenant.imports.manage/confirm`, documentos | diagnóstico, sugestões/manual, confirmar/cancelar | import/events/audit/contract | **Bloqueada por dependência identificada** | Scanner/OCR não podem ser simulados; testar original, estados e contrato resultante. |
| Ficha de contrato | `/organizacoes/{tenantId}/contratos/{contractId}` | permissão da finalidade + atribuição | documentos, revisão, obrigações, renovação | múltiplos agregados | **Ainda incompleta nesta auditoria** | Validar cada ação e download sob role restrita. |
| Obrigações / histórico / vistas | `/organizacoes/{tenantId}/obrigacoes` e `/obrigacoes/{id}/historico` | `tenant.obligations.*`, `tenant.saved_views.manage` | filtros, vistas, criar, atribuir, concluir, reabrir, cancelar | obligation/events/saved views | **Ainda incompleta nesta auditoria** | Testar ações pela ficha, reload, agenda e caixa. |
| Agenda | `/organizacoes/{tenantId}/agenda` | permissões das fontes | mês, filtros e origem | leitura derivada | **Ainda incompleta nesta auditoria** | Testar timezone e atualização sem duplicatas. |
| Caixa operacional / vistas | `/organizacoes/{tenantId}/caixa` | permissões por fonte | filtros, origem, salvar/padrão/inativar vista | saved views; leitura derivada | **Ainda incompleta nesta auditoria** | Testar escopo de cada item e navegação de retorno. |
| Renovações e aditivos | `/organizacoes/{tenantId}/renovacoes` | `tenant.renewals.*` | preparar, submeter, formalizar, aplicar/cancelar | change request/review/events/term | **Ainda incompleta nesta auditoria** | Validar condições, versões e obrigações anteriores. |
| Plano e consumo | `/organizacoes/{tenantId}/plano-e-consumo` | contexto tenant/consumo | uso, limites, pacotes, solicitar adicional | request/ledger/audit | **Ainda incompleta nesta auditoria** | Confirmar dados reais; solicitação não equivale a pagamento/aprovação. |
| Planos públicos | `/conheca-os-planos`, `/planos` | pública/autenticada | catálogo | leitura | **Ainda incompleta nesta auditoria** | Requer hosts; não há pagamento simulado. |
| Privacidade | `/privacidade` | pública | registrar solicitação | privacy request/protocolo | **Ainda incompleta nesta auditoria** | Fora da jornada prioritária; validar persistência sem enumeração. |
| Assinaturas | `/modulos/assinaturas` | autenticada | apenas estado do módulo | nenhuma assinatura externa | **Bloqueada por dependência identificada** | Não existe provedor de assinatura real; preparação interna não declara envio/assinatura. |

## Roteiro de homologação reproduzível

1. Em máquina com .NET 10.0.400, Node 24+ e PostgreSQL 18, aplicar o migrador incremental
   e executar `provision-test-access` conforme `README.md`; nunca resetar a base existente.
2. Iniciar API, Web e Worker. Entrar como `cliente.teste@odca.local`, concluir a troca de
   senha/MFA exigida, selecionar **Cliente Teste ODCA** pelo nome e guardar o tenant da URL.
3. Criar um paciente mínimo com representante; reler, editar, provocar conflito de versão,
   inativar e restaurar. Conferir as linhas e eventos com conexão administrativa somente
   para observação, enquanto as operações usam a role da aplicação.
4. Escolher modelo publicado, criar minuta vinculada, salvar/reler, gerar versão e PDF,
   baixar e comparar hash. Alterar cadastro e confirmar que a versão anterior não mudou.
5. Com revisor sintético elegível, solicitar revisão; comentar, pedir ajustes, gerar nova
   versão e aprovar somente a nova. Conferir steps/events e negativa entre tenants.
6. Preparar participantes, salvar/retomar, reordenar, confirmar o hash do PDF, comparar
   revisões e reabrir com justificativa. Repetir a mesma chave para conferir idempotência.
7. Executar a lista de importações sem filtro e com cada filtro isolado/combinado, nenhum
   resultado e tenant B. Só avançar upload/extrator com ClamAV/OCR configurados.
8. Percorrer obrigação → agenda → caixa e renovação → revisão → formalização → aplicação,
   sempre verificando resposta, reload, persistência e evento de auditoria.

## Dependências e conclusão honesta

- ClamAV, Poppler e Tesseract são dependências reais do Worker. Ausência/falha deve manter
  `scan_failed`/erro configuracional; nenhum arquivo foi marcado seguro nesta auditoria.
- O transporte de convite em Development é file pickup; fora dele é necessário provedor.
- Não foi localizado provedor real de assinatura eletrônica. O escopo operacional existente
  termina na preparação/confirmação interna dos participantes.
- O incremento recupera os dois bloqueios reproduzíveis por inspeção, mas **não demonstra
  que todas as telas são funcionais**. Sem SDK, PostgreSQL e browser, as classificações
  pendentes acima não podem ser promovidas para “validada funcional”.
