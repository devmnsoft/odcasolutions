# Próxima execução

## Continuação após Avisos, Confirmação e Formalização/Aplicação de Renovação (21/09/2026)

Parta da branch `feat/avisos-confirmacao-renovacao` com compilação 100% limpa (0 erros, 0 avisos) e 184 testes de domínio aprovados.
Execute validações ponta a ponta com PostgreSQL 18 descartável cobrindo:
1. Ciclo de formalização e aplicação na Central de Renovações (`/renovacoes`):
   - Proposta formalizada aciona o diálogo: *"Isto altera a vigência. A proposta deixa de ser só intenção."*.
   - Confirmação envia POST para `/organizacoes/{tenantId}/renovacoes/{requestId}/aplicar`.
   - Toast de sucesso exibe: *"Alteração aplicada com sucesso. Nova vigência até DD/MM/AAAA."* (baseado no `ends_on` resultante).
   - Teste de concorrência com versão alterada (409 Conflict): exibe o diálogo modal *"O registro mudou. Recarregue."* sem aplicação parcial da nova vigência.
2. Diálogos de confirmação com nome visível:
   - Cumprir obrigação (`fulfill`) exibe nome da obrigação.
   - Cancelar obrigação (`cancel`) exibe nome da obrigação.
   - Reabrir obrigação/apontamento (`reopen`) exibe identificador/nome do item.
   - Registrar proposta na ficha do contrato exibe título do contrato.
   - Tecla Escape fecha todos os modais e cancela a submissão.
3. Teste de perfil e restrições:
   - Login com `cliente.teste@odca.local` (role `tenant-client`): confirmação de que o menu operacional (Caixa, Agenda, Obrigações, Renovações, Minutas, Importações, Ficha) não é renderizado.
4. Verificação de toasts:
   - Sucesso com auto-dismiss após 8 segundos (`role="status"`).
   - Erro com persistência até clique em "Fechar" (`role="alert"`).
   - Responsividade em mobile (<768px) e desktop (>=768px), compatível com zoom 200%.

## Continuação após Vistas da Caixa, Datas Relativas e Refinamento de UI da Ficha (19/09/2026)

Parta da branch `feat/workspace-ui-vistas-datas` recompilável com 0 erros/avisos e 175 testes de domínio aprovados.
Execute validações ponta a ponta com PostgreSQL 18 descartável cobrindo:
1. Ciclo completo de vistas salvas na caixa operacional:
   - Salvar vista com token relativo (`relativeDate=dueThisWeek`).
   - Abrir vista e validar que cards de urgência e links de paginação conservam `viewId`.
   - Tornar padrão (`POST /vistas/{viewId}/padrao`) com verificação de concorrência (`RowVersion`) e checagem de isolamento cross-tenant / outro proprietário (retornando 404 para vistas alheias e 409 em versão defasada).
   - Inativar vista e confirmar que deixa de ser listada nas vistas ativas.
2. Comprovação da data civil do tenant:
   - Validação em banco com tenant em `America/Sao_Paulo` (UTC-3) às 01:30 UTC: data civil correspondente a D-1 (sem `DateTime.UtcNow`).
   - Proposta de alteração/renovação na ficha pré-populada com a data civil do tenant.
   - Navegação do link de proposta da ficha para a central `/renovacoes` filtrada por título e formalização exclusiva na central de renovações.
3. Acessibilidade e responsividade da interface:
   - Navegação por teclado: Skip-link -> Voltar -> Título -> Subnav sticky -> Seções da ficha (`#obrigacoes`, `#renovacao`, `#revisao`, `#minutas`) -> Ações.
   - Tracking automático de `aria-current="true"` no subnav conforme scroll / hash.
   - Inspeção visual em breakpoints 360px (mobile stack), 768px (tablet 2 colunas na caixa), 1280px e 1440px (desktop 4 colunas).
   - Validação de ausência total de atributos `style=` inline nas views migradas.

## Continuação após Ficha Acionável: Revisão, Comentários, Histórico e Resolução por Chave (18/09/2026)

Parta da branch `feat/ficha-revisao-historico-chave` recompilável com 0 erros/avisos e 136 testes de domínio aprovados.
Execute validações ponta a ponta com PostgreSQL descartável cobrindo:
1. Abertura do drawer de histórico para dono da obrigação e retorno 403 restrito no drawer para usuário sem permissão `tenant.obligations.read_all` mantendo a ficha intacta.
2. Resolução de minuta oficial após renomeação do título no catálogo, confirmando resolução por chave `metadata->>'key' = @key`.
3. Submissão de revisão pelo painel `#revisao` e adição/resolução de comentários contextuais.
4. Tentativa de iniciar minuta primária (NDA) com contrato em revisão (`in_review` / `changes_requested`) retornando 400 BadRequest.
5. Cumprimento de obrigação sem `EffectiveAt` comprovando uso da data civil no fuso do tenant (sem `UtcNow`).
6. Navegação preservando parâmetros de origem (`from=agenda&year=...` ou `from=caixa&scope=...`) na ida e volta da ficha.

## Continuação após Ficha Acionável, Minutas Oficiais e Contexto Operacional (18/09/2026)

Parta da branch `feat/ficha-acionavel-minuta-contexto` recompilável com 0 erros/avisos e 132 testes de domínio aprovados.
Execute testes ponta a ponta com PostgreSQL descartável cobrindo:
1. Mutação de obrigações concorrentes com colisão de `RowVersion` na ficha.
2. Criação de proposta de aditivo/renovação a partir da ficha com verificação da transação e concorrência na `odca.contract_change_requests`.
3. Instalação e abertura de minuta oficial a partir da ficha validando idempotência e geração do rascunho com parâmetros contextuais.
4. Validação de isolamento cross-tenant e verificação de 403 para usuários sem `tenant.obligations.read_all` no escopo organizacional.
5. Capturas de tela responsivas (360px, 768px, 1280px, 1440px) da nova Ficha do Contrato nas seções `#obrigacoes`, `#renovacao`, `#revisao` e `#minutas`.


Execute primeiro a suíte com SDK .NET 10.0.400 e PostgreSQL 18 descartável/role restrita. Prove isolamento por proprietário e tenant, revogação de acesso, referência indisponível, atualização concorrente, duas definições de padrão, inativação do padrão e repetição de criação. Em seguida modele datas relativas com `TimeProvider` e fuso persistido da organização, sem congelá-las como datas absolutas, e conecte os formulários de tratamento preservando `viewId`, filtros, ordenação e página de retorno. Só declare a jornada completa após HTTP autenticado e QA/capturas em 360, 768, 1280 e 1440 px e zoom 200%.

## Continuação após v014

Parta do gate recompilável com `ApiAssemblyMarker` e da migration v014 de vistas pessoais. Primeiro execute restore/build/testes com .NET 10.0.400 e PostgreSQL descartável usando a role restrita; prove isolamento por tenant e proprietário e concorrência de atualização de vista. Depois conecte renomear/padrão/inativar no BFF, complete a ficha operacional do contrato reutilizando documentos, revisões, obrigações, renovações e histórico existentes, e entregue a agenda mensal limitada à janela visível. Não declare a jornada de revisão concluída enquanto persistência/API/BFF e conflito otimista não estiverem cobertos ponta a ponta.

## Prioridade

Parta do fluxo de obrigações já existente e validado estaticamente. Primeiro execute, com o SDK .NET 10.0.400 e PostgreSQL 18 descartável, restore bloqueado, builds Debug/Release e todos os testes. Confirme em particular a variável concreta que removeu `CA1859`, as justificativas/no-op de reprogramação e reatribuição, evidência `safe` autorizada, histórico dentro da transação e recorrência próxima a `DateOnly.MaxValue`.

Depois conecte as mutações ao BFF sem UUID digitado e implemente a agenda mensal por janela usando os mesmos filtros e permissões da lista. Não declare a jornada concluída antes de provar HTTP/RLS, concorrência e navegador nas quatro larguras. Preserve a infraestrutura documental, a configuração local e o histórico SQL existentes.

Validar e fechar a integração S03 sem presumir aprovação: instalar/usar o SDK 10.0.400 e PostgreSQL 18 descartável; executar restore bloqueado, build Release, testes e v013 duas vezes; cobrir HTTP/RLS/concorrência/worker. Depois conectar o formulário BFF e a ficha do contrato aos endpoints já implementados, executar a jornada autenticada e produzir capturas reais em 360, 768, 1280 e 1440 px e zoom 200%. Não usar `development-runtime.json` como infraestrutura de teste nem tratar intenção de renovação como extensão de vigência.


## Continuação recomendada após 14/09/2026

Parta das correções já implementadas nos agregados e no cliente HTTP. Com SDK .NET 10.0.400 disponível, execute restore bloqueado, build Release e toda a solução de testes antes de ampliar a central. Em seguida, crie uma projeção de leitura autorizada (não uma tabela duplicada) que una revisões abertas, obrigações e contexto de renovação, com paginação/contagens idênticas, seletores por nome e links de retorno que preservem filtros. Conecte ficha e ações do contrato, teste concorrência/RLS com PostgreSQL descartável e só então faça capturas reais em 360, 768, 1280 e 1440 px.

Antes da ampliação, cubra em PostgreSQL o cumprimento seguido de reabertura: o evento deve continuar contendo data efetiva, observação e evidência, enquanto os campos correntes de cumprimento voltam a nulo sem violar o `CHECK`. Prove também a rejeição HTTP de `from > to` e os limites inclusivos nas duas pontas.

O histórico de obrigações já possui rota API tenant-aware, proxy BFF e apresentação no painel de pendências. A próxima execução deve validar essa rota com usuário proprietário, leitor global, usuário sem permissão e outro tenant; depois conectar as mutações com antiforgery e tratamento explícito de conflito, sem mover o token para o navegador.
