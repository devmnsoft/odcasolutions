# Central de solicitações de revisão

## Inventário confirmado neste ciclo

| Funcionalidade | Situação encontrada | Evidência | Lacuna tratada |
|---|---|---|---|
| Modelos e minutas | Persistência, publicação, arquivo, cópia e vínculo à versão já existiam | migration 015, `ContractStudioController` e tela Studio | Mantidos sem criar uma segunda biblioteca |
| Revisão contratual | Solicitação idempotente, passos sequenciais, eventos e notificações já existiam | migration 012 e envio do Studio/ficha | Faltavam fila própria, detalhe e conversa segura |
| Comentários de revisão | A tabela aceitava somente versão de documento importado | `contract_review_comments.document_version_id NOT NULL` | Comentários passam a referenciar exatamente a versão importada **ou** gerada |
| Visibilidade | Comentários não distinguiam mensagem e nota interna | schema anterior à migration 022 | `visibility` explícita; API omite notas internas sem `tenant.reviews.decide` |
| Planos e consumo | Assinatura, recursos, armazenamento e reservas já estavam implementados | migrations 018/019 e `ConsumptionController` | Sem regra comercial nova ou preço inventado |
| Obrigações e renovações | Centrais, filtros, comandos auditados e navegação já estavam implementados | controllers e migrations 013/017 | Aprovação agora oferece a conferência contextual; a ficha envia a versão real da obrigação em vez de assumir a versão inicial |
| Regressões Dapper/seed | Correções e testes PostgreSQL já existiam | `RuntimeQueryRegressionTests` e `DevelopmentAccessProvisioningTests` | Reconfirmadas por inspeção; execução PostgreSQL depende do ambiente descrito no README |

## Rotas e autorização

O BFF atende em `/organizacoes/{tenantId}/solicitacoes`. A API canônica atende em
`/api/v1/organizations/{tenantId}/reviews`. O identificador da organização na URL
não concede acesso: todas as operações chamam `odca.has_tenant_permission` e, antes
de acessar tabelas protegidas, configuram os contextos `odca.tenant_id` e
`odca.user_id` usados por RLS.

`tenant.reviews.read` permite consultar a fila e enviar mensagem ao cliente.
`tenant.reviews.decide` é adicionalmente obrigatório para criar ou receber notas
internas. A filtragem ocorre na consulta da API; portanto uma nota interna não é
serializada para um cliente sem essa permissão. O UUID enviado como chave de
idempotência também é o identificador do comentário, impedindo duplicação em
reenvios. Solicitações canceladas ou substituídas não recebem mensagens novas.

Os estados continuam sendo os estados canônicos da revisão sequencial:
`in_review`, `changes_requested`, `internally_approved`, `cancelled` e
`superseded`. Este ciclo não criou estados concorrentes nem fez a conclusão do
atendimento aprovar um contrato automaticamente.

## Matriz de transições e concorrência

| Origem | Ação | Destino | Permissão | Validações e efeitos |
|---|---|---|---|---|
| versão gerada | enviar para revisão | `in_review` | `tenant.reviews.request` | versão imutável, revisor elegível e chave idempotente; cria solicitação, passo atual, evento e notificação pelo fluxo do Studio |
| `in_review` | mensagem pública ou nota interna | sem alteração | `tenant.reviews.read`; nota exige `tenant.reviews.decide` | mensagem não altera estado; UUID impede duplicação; conteúdo interno é filtrado no servidor |
| `in_review` | reatribuir responsável | `in_review` | `tenant.reviews.decide` | exige responsável ativo com permissão de decisão, justificativa e versão esperada; encerra o passo anterior, cria outro e audita na mesma transação |
| `in_review` | solicitar ajustes | `changes_requested` | `tenant.reviews.decide`, pelo revisor atual | justificativa, versão esperada e bloqueio da solicitação; passo, solicitação e evento são gravados na mesma transação |
| `in_review` | aprovar versão | `internally_approved` | `tenant.reviews.decide`, pelo revisor atual | justificativa e versão esperada; a versão aprovada é a indicada no detalhe e aprovação não equivale a assinatura |
| `changes_requested` | responder/reenviar | nova revisão `in_review` | fluxo contratual do solicitante | uma nova versão não herda aprovação; eventos e decisões anteriores permanecem no histórico |
| aberto | cancelar | `cancelled` | `tenant.reviews.cancel` | somente pelo endpoint canônico que revalida escopo e estado; não há exclusão física |

O endpoint de decisão bloqueia a linha da solicitação antes de conferir repetição.
A chave idempotente fica vinculada à ação e à versão esperada: repetir o mesmo
conteúdo devolve o recibo persistido, enquanto reutilizá-la com outro conteúdo
gera conflito. Atualizações condicionais conferem a versão e só inserem o evento
quando passo e solicitação foram alterados; o commit ocorre antes da resposta de
sucesso.

A reatribuição também usa versão esperada e chave de idempotência. Uma disputa não
sobrescreve silenciosamente o responsável: a segunda operação recebe conflito. A
fila oferece responsáveis elegíveis por nome e perfil, pesquisa pelo título do
contrato e paginação que conserva os filtros de situação, escopo, responsável,
período e pesquisa.

Depois de `internally_approved`, o detalhe oferece dois próximos passos explícitos:
conferir/cadastrar obrigações na ficha do mesmo contrato e acompanhar as pendências
filtradas na central. Nenhuma obrigação é criada pela aprovação. Na ficha, cada
comando envia o `row_version` materializado da fonte canônica; assim uma ação depois
de iniciar, reagendar ou reatribuir não reutiliza a versão `1`, e uma aba desatualizada
continua recebendo o conflito já tratado pelo endpoint.

## Aplicação e validação

Execute `dotnet run --project src/Odca.Bootstrap -- migrate`; a migration 022 é
incremental e preserva comentários anteriores como mensagens visíveis ao cliente.
Depois execute os comandos de build e teste do README. Para validar SQL, RLS e
materialização, configure a base descartável `odca_test_*` e as três variáveis
`ODCA_TEST_*` documentadas no README antes de executar o projeto de integração.

Roteiro manual por perfil:

1. Como cliente com `tenant.reviews.read`, abra **Solicitações**, filtre as suas,
   entre no detalhe e envie uma mensagem marcada como visível ao cliente.
2. Como consultor com `tenant.reviews.decide`, confira a mensagem e crie uma nota
   interna; confirme o destaque visual amarelo.
3. Volte ao cliente e confirme que a nota interna não aparece na resposta da API
   nem na página, enquanto a mensagem pública permanece no histórico.
4. Troque o `tenantId` ou `reviewId` na URL e confirme `403`/`404`, sem conteúdo de
   outra organização.
5. Reenvie a mesma requisição da API com a mesma `idempotencyKey` e confirme que
   existe apenas um comentário.

## Limites desta validação

O contêiner utilizado neste ciclo não disponibilizou o SDK `dotnet` nem um
PostgreSQL descartável. Assim, build, login HTTP, migration real, RLS, concorrência
e jornada visual autenticada precisam ser executados no ambiente oficial antes de
promover a entrega. O verificador de assets e a verificação de whitespace foram
executados localmente; isso não equivale a uma integração aprovada.
