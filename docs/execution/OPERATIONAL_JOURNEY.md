# Jornada operacional: fontes, autorização e validação

## Projeções e datas

A caixa e a agenda são projeções de leitura; não existe uma tabela paralela de tarefas. As fontes são `contract_obligations` abertas/em andamento, `contract_reviews` em revisão/com ajustes solicitados e contratos dentro da janela de renovação. `OperationalInboxRow.Version` é sempre a versão otimista da fonte (`row_version` da obrigação, revisão ou contrato) e segue no DTO até os formulários de ação. Uma versão zero ou negativa é rejeitada na materialização da resposta.

A data civil de referência vem de `(now() AT TIME ZONE tenants.timezone)::date`. A agenda consulta um intervalo mensal inclusivo, do primeiro ao último dia, e converte `due_at` da revisão para a data civil da organização. O vencimento da renovação é o prazo de comunicação calculado quando configurado; o fim da vigência só é usado quando não há prazo de aviso configurado. Datas ausentes são `Unscheduled` na caixa e nunca são classificadas como atrasadas.

## Autorização

A API resolve o ator pelo token e consulta `tenant_actor_has_permission` para a organização da rota. A permissão de uma fonte não libera outra:

| Fonte | Leitura | Escopo da organização |
|---|---|---|
| Obrigações | `tenant.obligations.read` | `tenant.obligations.read_all` |
| Revisões | `tenant.reviews.read` | a própria permissão canônica de revisão |
| Renovações | `tenant.renewals.read` | a própria permissão canônica de renovação |

Sem leitura de nenhuma fonte, caixa e agenda respondem `403`. No escopo pessoal, a consulta mantém somente itens atribuídos ao ator. No escopo da organização, cada ramo do `UNION ALL` amplia seu conjunto apenas se a permissão daquela fonte autorizar. O tenant da URL não é prova de acesso: a função de permissão e o contexto RLS são configurados antes da consulta.

## Validação manual por perfil

1. **Operador completo:** abrir caixa e agenda em “Minhas pendências”; alternar para “Organização”; filtrar separadamente obrigações, revisões e renovações; confirmar que o atalho **Hoje** usa a data da organização.
2. **Leitor somente de obrigações:** confirmar que revisão e renovação não aparecem, mesmo quando atribuídas ao usuário; confirmar que “Organização” não amplia obrigações sem `tenant.obligations.read_all`.
3. **Leitor somente de revisões ou renovações:** confirmar que a central abre sem exigir permissão de obrigações e mostra apenas a fonte autorizada.
4. **Usuário sem permissões operacionais:** confirmar `403` tanto na caixa quanto na agenda.
5. **Isolamento:** repetir a URL com outra organização e IDs de fontes conhecidos; confirmar ausência de dados e impossibilidade de abrir a ficha.
6. **Concorrência:** abra a mesma pendência em duas sessões, conclua ou decida na primeira e envie a versão antiga na segunda; confirmar `409`, preservação do histórico e ausência de segundo efeito.

A validação de SQL, RLS, materialização e concorrência exige o PostgreSQL descartável descrito no `README.md`; testes unitários não substituem esse percurso.
