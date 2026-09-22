# Central de pendências

A central é uma projeção de leitura; ela não mantém uma tabela ou um estado de
tarefa paralelo. Cada linha continua pertencendo ao seu módulo de origem e a
ação é realizada na ficha canônica desse módulo.

| Tipo | Origem e identificador | Responsável e prazo | Situação aberta | Versão de concorrência | Consulta | Ação e rota |
| --- | --- | --- | --- | --- | --- | --- |
| `Obligation` | `contract_obligations.id` | `owner_id`, `due_date` | `open`, `in_progress` | `contract_obligations.row_version` | responsável ou `tenant.obligations.read_all` | serviço canônico de obrigações, na ficha do contrato (`#obrigacoes`) |
| `Review` | `contract_reviews.id` | etapa atual e `due_at` no fuso do tenant | `in_review`, `changes_requested` | `contract_reviews.row_version` | revisor, solicitante ou `tenant.reviews.read` | serviço canônico de revisões, na ficha do contrato (`#revisao`) |
| `Renewal` | `contracts.id` | `owner_id`, data de aviso derivada da vigência | contrato dentro da janela de renovação | `contracts.row_version` | responsável ou `tenant.renewals.read` | fluxo canônico de renovação, na ficha do contrato (`#renovacao`) |

## Contrato de versão

`Version` é a versão de concorrência da entidade de origem, não uma versão de
documento nem da projeção. As três fontes atuais possuem `row_version bigint
NOT NULL DEFAULT 1`; por isso uma linha da central nunca admite versão ausente
ou menor que um. A consulta lê o valor da mesma entidade exibida e o entrega no
DTO sem substituí-lo pela versão mais recente.

Ao executar uma mutação, o módulo de origem continua responsável por validar
permissão e situação, comparar a versão esperada, verificar a quantidade de
linhas alteradas e devolver conflito. A central apenas leva o usuário à origem:
ela não repete automaticamente uma decisão depois de conflito.

## Limites atuais verificados no código

- **Funcional:** projeção tenant-aware de obrigações, revisões e renovações;
  filtro por tipo, responsável, contrato, urgência e escopo; ordenação estável;
  paginação e indicadores; abertura da ficha canônica.
- **Parcial:** a paginação é aplicada no serviço depois da consulta das linhas
  candidatas; uma futura evolução deve movê-la, junto das contagens, para SQL
  sem alterar a semântica de visibilidade.
- **Ausente:** atribuição e alteração de prazo diretamente na central. Essas
  operações permanecem somente nos módulos que possuem regras e auditoria.
- **Não verificado sem PostgreSQL e aplicação executável:** materialização
  Dapper real, login/autorização ponta a ponta e jornada visual nas três larguras.

