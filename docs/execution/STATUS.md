# Status de execução

## Gate do MVP operacional — contexto documental e auditoria local (21/09/2026)

- Branch de trabalho: `feat/mvp-operacional-validacao-superadmin`, criada sobre o estado integrado de `codex/s00-foundation` (`01295a1`). O checkout fornecido não possui remoto configurado; portanto, pull/rebase remoto não pôde ser executado.
- Auditoria do ambiente: os snapshots imutáveis 001–019, a solution e os lockfiles estão presentes. O contêiner não possui .NET 10.0.400, servidor PostgreSQL 18 nem Docker (somente `pg_config` 16), impedindo restore/build/testes .NET, banco descartável, login HTTP e QA autenticado. `npm run build` validou os sete assets Web.
- O fluxo documental agora configura `odca.user_id` e `odca.tenant_id` com `set_config(..., true)` dentro da mesma transação usada pela autorização, leitura, upload, extração e aplicação. A consulta de limites do upload também passou a usar contexto e transação próprios, evitando depender de estado residual do pool.
- O download pelo BFF mantém a dupla verificação de documento `safe` e autorização da API, mas passa a responder com `Content-Disposition: inline`; o nome visível é codificado e nenhum bearer token é enviado ao navegador.
- Este incremento **não declara o MVP concluído**: Central completa do Superadministrador, suporte temporário, operação autenticada LGPD, prova PostgreSQL/RLS/login e capturas responsivas continuam bloqueados pelos gates descritos acima e permanecem na próxima execução.

## Documentos, importação, confirmações e agenda (21/09/2026)

- Estado da entrega: branch `feat/documentos-importacao-confirmacoes` a partir de `codex/s00-foundation` @ `a4ca12c`.
- Gates: `dotnet restore --locked-mode` ✔ · `dotnet build -c Release` ✔ · `dotnet test` 184/184 ✔.
- Template oficial por Key canônica (type + scope), nunca por name nem audit_events:
  - `ContractSheetRepository.cs`: `publishedCount` conta por `CASE WHEN contract_type IN (...) ... THEN 'key'` sem `AND t.name = ANY(...)`.
  - `ContractStudioCatalogController.cs` (PR anterior): lookup usa `contract_type` e campo NDA discriminante; sem referência a `audit_events`.
- ImportId resolvido no servidor:
  - `ContractSheetRepository.cs`: query agora retorna `i.id` (`Guid?`) em vez de `EXISTS`; `HasImportAwaitingReview` derivado de `importId.HasValue`.
  - `ContractSheetDto`: campo `ImportId: importId` passado ao DTO.
  - `Sheet.cshtml`: chip navega para `/importacoes/{ImportId.Value}` (Review) quando `ImportId` existe, fallback para lista `?awaitingReview=true`.
- _ConfirmDialog ligado em todas as ações de mutação na ficha:
  - `start`, `reschedule`, `reassign`: botões trocados de `type=submit` para `type=button` com `data-confirm-open`; forms com `id` explícito.
  - `CreateObligation`: form recebeu `id="create-obligation-form"`, botão com confirmação explícita.
  - `StartOfficialDraft`: form com `id="draft-form-{key}"`, botão com confirmação e nome do template.
  - `InstallOfficialLibrary`: form com `id="install-library-form"`, botão com confirmação.
- #documentos — download seguro:
  - `ContractsController.DownloadDocument` (GET): verifica `SafetyState == "safe"` via ficha; se quarentena, `TempData["ContractSheetError"]` e redirect `#documentos`. Se safe, retorna `File(bytes, contentType, fileName)`.
  - `Sheet.cshtml`: seção #documentos renderiza botão "Baixar" apenas se `SafetyState == "safe"`; caso contrário, label "Quarentena".
- Agenda — `?day=` e empty-state:
  - `AgendaController`: aceita `int? day`; filtra `Days` por `d.Day.Day == day`; se `Total == 0`, seta `TempData["AgendaEmptyNotice"]`.
  - `AgendaWorkspaceViewModel`: novo campo `int? Day`.
  - `Agenda/Index.cshtml`: exibe banner `banner-notice-warning` quando mês vazio; chip de filtro de dia ativo com botão remover; empty-state com link para caixa.
- Caixa — faixa role=status para atrasadas:
  - `Inbox/Index.cshtml`: renderiza `<div class="banner-notice-warning" role="status">` com contagem e link `urgency=Overdue&viewId=...` quando `Model.Page.Overdue > 0`.
  - `site.css`: `.banner-notice-warning` adicionado com cor âmbar (#fff3cd / #856404), flex.

## Avisos, confirmação e formalização/aplicação de renovação (21/09/2026)

- Estado da entrega: branch `feat/avisos-confirmacao-renovacao` a partir de `codex/s00-foundation` @ `3bc6f58`.
- Separação estrita de erros e ausência de captura 4xx no middleware:
  - `BffErrorHandlingMiddleware` monitora estritamente exceções e status 500/503; códigos 400 (Bad Request) e 409 (Conflict) nunca são interceptados como indisponibilidade.
  - 400 Bad Request exibe mensagens e validações diretamente no formulário (`asp-validation-summary`).
  - 409 Conflict exibe diálogo modal com mensagem amigável: "O registro mudou. Recarregue." com botão de recarregar (`location.reload()`) e fechar.
- Sistema de notificações e toasts (`_Toasts.cshtml`):
  - Integrado ao `_Layout.cshtml` consumindo `TempData` de sucesso, erro, aviso e conflito.
  - Sucesso (`role="status"`): auto-dismiss após 8s (8000ms) ou via botão de fechar.
  - Erro (`role="alert"`): persiste na tela até ação explícita no botão "Fechar".
  - Layout responsivo em `site.css`: fixo no canto inferior direito para desktop (>=768px), largura total na base da tela para mobile (<768px). Não obstrui a subnav sticky na ficha do contrato. Compatível com zoom 200%.
  - Tecla Escape fecha notificações e diálogos.
- Catálogo de modelos oficiais por chave:
  - Consulta do catálogo oficial (`ContractStudioCatalogController.GetOfficial`) busca diretamente em `OfficialContractTemplates.All` por `key` e resolve o modelo em `odca.contract_templates` por `name`, eliminando dependência de `odca.audit_events`.
- Diálogo nativo de confirmação (`_ConfirmDialog.cshtml`):
  - `<dialog id="confirm-modal" data-confirm-dialog aria-modal="true">` reutilizado em todo o sistema via delegação de eventos em `dialogs.js` com suporte a `data-confirm-open`, `data-confirm-form`, `data-confirm-title`, `data-confirm-body` e `data-confirm-action`.
  - Confirmação com nome visível do item implementada para:
    - Cumprimento de obrigação (`fulfill`) com nome da obrigação.
    - Cancelamento de obrigação (`cancel`) com nome da obrigação.
    - Reabertura de obrigação/apontamento (`reopen`) com nome do apontamento/obrigação.
    - Registro de proposta de renovação/aditivo na ficha com título do contrato e aviso de que a proposta deixa de ser apenas intenção e tem efeito em vigência somente após formalização e aplicação.
  - Tecla Escape fecha o diálogo e restaura o foco ao botão de origem.
- Formalização e aplicação exclusiva na Central de Renovações (`/renovacoes`):
  - Proibido `/apply` na ficha do contrato; aplicação restrita a `/organizacoes/{tenantId}/renovacoes/{requestId}/aplicar`.
  - Diálogo de confirmação com texto estrito: *"Isto altera a vigência. A proposta deixa de ser só intenção."*.
  - Toast de sucesso exibe o `ends_on` resultante da nova vigência.
  - Aplicação transacional com bloqueio pessimista `FOR UPDATE OF r,c` em `ApplyCore`: em caso de conflito de versão (409), aborta a operação sem aplicação parcial.
- Menu do cliente de demonstração restrito:
  - Usuário demo (`cliente.teste@odca.local`, role `tenant-client`) não visualiza menu operacional (Caixa, Agenda, Obrigações, Renovações e aditivos, Modelos e minutas, Importações, Ficha do contrato).
- Validação e testes:
  - `dotnet build Odca.sln -c Release`: 0 avisos, 0 erros.
  - `dotnet test tests/Odca.Domain.Tests/Odca.Domain.Tests.csproj -c Release`: 184/184 testes aprovados (100% passando).
  - Novos testes unitários em `AvisosEConfirmacaoRenovacaoTests.cs` cobrindo separação de erro 400/409, `ApplyRenewalResponse` com `EndsOn`, busca por chave do catálogo e restrições de menu do cliente demo.

## Acesso local de desenvolvimento, perfil de operador, landing e página indisponível (21/09/2026)

- Estado da entrega: branch `feat/acesso-local-operador-indisponivel` a partir de `codex/s00-foundation` @ `89fbbb8`.
- Provisionamento e usuários de desenvolvimento:
  - Suporte completo às 3 identidades reservadas: `admin@odca.local`, `operador@odca.local` e `cliente.teste@odca.local`.
  - Seed SQL (`database/development/seed-test-access.sql`) e `TestAccessProvisioner`:
    - Role `tenant-operator` criada com o conjunto completo de permissões operacionais (`tenant.obligations.read|read_all|manage|fulfill|reopen|cancel|assign`, `tenant.documents.download`, `tenant.renewals.read|prepare`, `tenant.templates.read`, `tenant.contract_drafts.read`, `tenant.reviews.read`, `tenant.imports.read`, `tenant.saved_views.manage`).
    - Role `tenant-client` com permissões não-operacionais (`tenant.organization.read`, `tenant.billing.read`).
    - Membership ativo do superadministrador vinculado ao tenant de demonstração (sem criar segundo tenant), viabilizando `activeTenantId` para navegação.
  - Scripts `scripts/provision-local-superadmin.ps1` e `.sh`:
    - Validação de identidades reservadas: bloqueio contra e-mails divergentes.
    - Suporte a `--operator-password`, `$env:ODCA_DEV_OPERATOR_PASSWORD` e `--allow-immediate-login`.
    - Exibição de senhas locais confirmadas no banco via `show-login` (nenhuma senha mantida em código ou controllers).
- Landing no BFF (`HomeController`):
  - Operador autenticado com `tenant.obligations.read` é redirecionado automaticamente para `/organizacoes/{tenantId}/caixa`.
  - Cliente autenticado sem permissão operacional é redirecionado para `/cliente` (`CustomerHome`).
- Navegação e menu de permissões (`_Layout.cshtml` + `IUserTenantContext`):
  - `UserTenantContext` injetado como serviço com cache por requisição em `HttpContext.Items`.
  - Menu operacional condicionado a `membership active` no tenant.
  - Cada link operacional é filtrado estritamente por permissão específica (`obligations.read`, `renewals.read`, `templates.read`, `contract_drafts.read`, `imports.read`, `organization.read/manage`).
  - Item "Ficha" é exibido no menu exclusivamente quando a rota possui `contractId`.
  - Cliente não visualiza Caixa, Agenda, Obrigações, Renovações, Minutas, Importações ou Ficha.
- Tratamento de indisponibilidade e auditoria técnica (`BffErrorHandlingMiddleware` + `ServiceUnavailable.cshtml`):
  - Middleware intercepta exceções e respostas 500/503 no BFF.
  - Registro estruturado no `ILogger.LogError` via `[LoggerMessage]` (CA1848 compliant) com tipo da exceção, mensagem, inner exception, path, tenantId, sub do JWT e request id (zero senhas em log).
  - Diálogo `<dialog class="unavailable-dialog" role="alertdialog" open aria-labelledby="dialog-title" aria-describedby="dialog-desc">`:
    - Título: `Página indisponível`.
    - Texto: `Não foi possível abrir este recurso agora. O detalhe técnico foi registrado.`
    - Botões: `Fechar` (nativo via `<form method="dialog">` e script) e `Voltar à caixa` (quando `tenantId` está disponível na rota, query ou sessão).
    - UI limpa e estéril: sem stack trace, SQL, connection string, token ou hash.
  - 401 preserva Challenge, 403 preserva Forbid, e 404 preserva página de não encontrado (sem disparar o diálogo 5xx).
- Build e testes:
  - `dotnet build Odca.sln -c Release`: 0 erros, 0 avisos.
  - `dotnet test tests/Odca.Domain.Tests/Odca.Domain.Tests.csproj -c Release`: 179/179 testes aprovados.
  - Novos testes unitários em `BffAccessAndIndisponibilidadeTests.cs` e integração em `DevelopmentAccessProvisioningTests.cs`.

## Vistas da caixa, datas relativas e refinamento de UI da ficha (19/09/2026)


- Estado da entrega: branch `feat/workspace-ui-vistas-datas` a partir de `codex/s00-foundation` @ `0c1ef9c6`.
- Vistas da caixa no módulo v014:
  - Salvar vista da caixa operacional (`POST /vistas/salvar` com `listingType=inbox`).
  - Abrir vista (`GET /vistas/{viewId}`), alternar com chip ativo.
  - Tornar padrão (`POST /vistas/{viewId}/padrao`) com checagem de concorrência (`RowVersion`) e isolamento tenant + dono (retorna 404 para vista de outro usuário/tenant e 409 em conflito de versão).
  - Inativar vista (`POST /vistas/{viewId}/inativar`).
  - Indicador textual explícito `(Padrão)` além de estrelas nos chips de vistas.
  - Paginação e cards de urgência levam `viewId` preservado na URL; `page` nunca é gravado no JSON da vista.
  - Allowlist server-side em `SavedViewFilterPolicy` para `listingType=inbox`: `scope`, `kind`, `urgency`, `contractId`, `ownerId`, `relativeDate`.
- Datas relativas dinâmicas:
  - Tokens suportados: `today`, `overdue`, `dueToday`, `dueThisWeek`, `nextDays:N` (0 <= N <= 365).
  - Resolução no servidor em `RelativeDateResolver` utilizando `TimeProvider` e fuso horário civil do tenant (`tenants.timezone`).
  - Proibido persistir `DateOnly` absoluto no JSON quando data relativa for utilizada; proibido uso concomitante de data relativa com datas absolutas (`from`/`to`).
  - `dueThisWeek` mapeia para a urgência `DueThisWeek` e avança na virada de meia-noite civil do tenant (`TimeProvider`).
- Data default de proposta e "hoje" na ficha e renovações:
  - Ficha do contrato expõe `sheet.Today` derivado de `(now() AT TIME ZONE tenants.timezone)::date` do banco.
  - Proposta de alteração/renovação utiliza `sheet.Today` como valor inicial padrão (sem `DateTime.UtcNow`).
  - Central de renovações (`/renovacoes`) utiliza data civil do tenant retornada pela API (`Today`).
  - Link de proposta na ficha direciona para `/renovacoes` filtrado por título (formalização/aplicação exclusivamente lá; sem `/apply` na ficha).
  - Regra de negócio: 403 em `scope=organization` sem `tenant.obligations.read_all` mantido intacto.
- Acessibilidade e Estilo Semântico (sem inline `style=`):
  - Removidos todos os atributos inline `style=` de `Views/Inbox/Index.cshtml`, `Views/Agenda/Index.cshtml` e `Views/Contracts/Sheet.cshtml`.
  - Subnav da ficha (`#obrigacoes #renovacao #revisao #minutas`) sticky com tracking de `aria-current="true"` baseado na hash via JavaScript e suporte a teclado.
  - Chip de importação com texto + status-label sem depender apenas de emojis.
  - Grid responsivo: stack < 768px; Inbox com 1 coluna < 768px, 2 colunas 768–1279px, 4 colunas >= 1280px.
  - Focus visível e sequência de tabulação consistente.
- Build e testes:
  - `dotnet build Odca.sln -c Release`: 0 avisos, 0 erros.
  - `dotnet test tests/Odca.Domain.Tests/Odca.Domain.Tests.csproj -c Release`: 175/175 testes aprovados.

## Ficha acionável: revisão, comentários, histórico seguro e resolução por chave (18/09/2026)

- Estado da entrega: branch `feat/ficha-revisao-historico-chave` a partir de `codex/s00-foundation` @ `600915e7`.
- Drawer de histórico de obrigação:
  - Consumo via `GET /organizacoes/{tenantId}/obrigacoes/{id}/historico`.
  - Renderização estritamente segura usando DOM APIs (`createElement`, `textContent`), sem `innerHTML`.
  - Controle de acesso com dono da obrigação ou permissão `tenant.obligations.read_all` (403 restrito no drawer sem quebrar a ficha).
- Painel `#revisao`:
  - Link direto para o rascunho no Estúdio preservando `returnUrl` integral.
  - Submissão de versão para revisão reutilizando `api.SubmitStudioReviewAsync` (POST `revisao/submeter`).
  - Adição e alteração de estado (resolve/reopen) de comentários contextuais no rascunho com aviso explícito "comentário ≠ aprovação".
  - Tratamento de colisão/conflito 409 recarregando a ficha com mensagem de erro amigável sem perder o contexto.
  - Atalhos diretos para Checklist e Comparação de versões.
- Resolução de minutas oficiais por Key:
  - Lookup resiliente a renomeações de título via `odca.audit_events.metadata->>'key' = @key` através do endpoint `GET /api/v1/organizations/{tenantId}/studio/templates/official/{key}`.
  - Proibido `FirstOrDefault(x => x.Name == título)`.
  - Retry de instalação sob demanda caso `CanInstallOfficialLibrary` seja verdadeiro.
  - Bloqueio 400 BadRequest para minutas primárias (NDA, serviços, fornecimento, locação) se houver revisão aberta (`in_review` ou `changes_requested`).
  - Termo aditivo (`contract-amendment`) permitido apenas se `RequiresAmendment` (janela de renovação ou alteração de prazo).
- Data civil do fuso horário no cumprimento de obrigação:
  - Quando `EffectiveAt` for omitido em `fulfill`, calcula a data civil do tenant via `(now() AT TIME ZONE timezone)::date` com fallback seguro `TimeZonePolicy.Resolve(tenantInfo.Timezone, ...)`. Proibido `DateTimeOffset.UtcNow`.
- Preservação da query de retorno:
  - `from`, `year`, `month`, `scope`, `kind`, `urgency`, `viewId`, `obrigacao` preservados em formulários POST e links da ficha ("Voltar à caixa" / "Voltar à agenda").
- Build e testes:
  - `dotnet build Odca.sln -c Release`: 0 erros, 0 avisos.
  - `dotnet test tests/Odca.Domain.Tests/Odca.Domain.Tests.csproj -c Release`: 136/136 testes aprovados.

## Ficha acionável do contrato, biblioteca de minutas oficiais e contexto operacional (18/09/2026)

- Estado da entrega: branch `feat/ficha-acionavel-minuta-contexto` criada a partir de `codex/s00-foundation`.
- Catálogo canônico de 6 minutas oficiais consolidado em `OfficialContractTemplates`:
  1. `nda-unilateral` ("Acordo de Confidencialidade (NDA) — Unilateral")
  2. `nda-mutual` ("Acordo de Confidencialidade (NDA) — Mútuo")
  3. `services-agreement` ("Contrato de Prestação de Serviços Técnicos")
  4. `contract-amendment` ("Termo Aditivo Contratual")
  5. `supply-agreement` ("Contrato de Fornecimento de Bens")
  6. `lease-agreement` ("Contrato de Locação de Imóvel Comercial")
- Ficha acionável do contrato (`/sheet`):
  - Ações diretas em obrigações: iniciar (`start`), cumprir (`fulfill`), cancelar (`cancel`), reabrir (`reopen`), reprogramar (`reschedule`), reatribuir (`reassign`) e criar obrigação manual com validação de versão otimista e redirecionamento para âncora `#obrigacoes`.
  - Proposta de alteração/renovação (`POST /proposta-renovacao`): registro sem efeito direto na vigência até formalização, obrigatoriedade de aditivo (`amendment`) na janela de 3 meses ou alteração de data final, validação de responsável e idempotência.
  - Rascunhos de minutas oficiais (`POST /rascunho-oficial`): criação direta de rascunho de minuta oficial via API de estúdio com base no catálogo oficial do tenant (com instalação automática se permitido quando catálogo incompleto).
  - Instalação sob demanda da biblioteca oficial (`POST /biblioteca-oficial`) autorizada por permissão.
  - Contexto de importação aguardando revisão destacado na ficha via chip de alerta direto para importações.
- Visibilidade e RLS:
  - `scope=organization` na caixa operacional exige permissão estrita `tenant.obligations.read_all` retornando 403 `Forbid()` (sem degradação silenciosa para `mine`). Contratos de outros tenants retornam 404 `NotFound`.
  - Links profundos canônicos com âncora (`?obrigacao={id}&from=caixa#obrigacoes`, `#revisao`, `#renovacao`).
- Separação de camadas:
  - `Odca.Web` estritamente desacoplado de `Odca.Application` (usings removidos, regras de roteamento e catálogo mapeados localmente).
- Testes e compilação:
  - `dotnet build Odca.sln -c Release`: 0 erros, 0 avisos.
  - `dotnet test tests/Odca.Domain.Tests/Odca.Domain.Tests.csproj -c Release`: 132/132 testes aprovados.
- Fora de escopo preservado: sem editor visual, sem PDF rastreável, sem OCR, sem séries recorrentes ("esta e futuras"), sem dashboard consolidado de tarefas, sem billing/PIX, sem impersonation, sem sessão de suporte, sem migração de banco alterando snapshots 001–019.


- Estado inicial desta entrega: branch `work`, SHA `b3b3d8c2b452254797384a8781cc4e54bbfcb35c`, árvore limpa e nenhum `AGENTS.md` presente no checkout ou diretório pai.
- As actions de solicitação agora possuem nomes de negócio sem ocultar `ControllerBase.Request`/`Controller.Request`; as URLs públicas permanecem explícitas e o POST do BFF exige antiforgery.
- Pacotes sem preço e moeda válidos não são oferecidos nem aceitos. A idempotência compara pacote e quantidade no mesmo tenant, impedindo que a mesma chave seja reutilizada silenciosamente com outro payload; o snapshot comercial continua calculado no servidor.
- O repositório alinha parâmetros públicos à interface, preserva nomes SQL explícitos, propaga cancelamento até delegates e commit/rollback e usa o retorno concreto `Task<int>` do helper Dapper.
- O worker usa lock transacional entre instâncias, verifica versão, tenant, request, linhas afetadas e registro único da aplicação antes do commit. Conflitos são persistidos como tal e nenhuma aplicação parcial é confirmada.
- O SDK .NET 10.0.400 e PostgreSQL descartável não estão disponíveis neste contêiner; a tentativa de obter o instalador oficial retornou HTTP 403. Builds/testes .NET/PostgreSQL e captura autenticada permanecem gates não executados localmente.

## Política canônica e jornada de vistas pessoais (14/09/2026)

- Estado inicial registrado: branch `work`, SHA `1e0a470ed78f6151da7e284474f8e99d395ba58f`; árvore inicialmente limpa.

### Implementado

- Os alvos `CA1859` em `SavedViewsController` agora expõem os tipos concretos realmente usados nos detalhes privados; o helper de contexto continua retornando `Task<int>`. O parâmetro privado `Add` do BFF recebe `Dictionary<string,string>`.
- A construção de rota deixou de usar o construtor incompatível de `RouteValueDictionary`: filtros passam pela política canônica, são inseridos individualmente e somente depois recebem `tenantId`, `viewId` e página inicial confiáveis. Chaves reservadas são recusadas.
- Salvar, atualizar e aplicar reutilizam a mesma normalização de chaves, enumerações, UUIDs, datas ISO, intervalo, ordenação e limites. Atualização de filtros é explícita e preserva a versão otimista.
- Acesso sem filtro/vista abre a vista padrão; filtros explícitos (inclusive dashboard) prevalecem. A central identifica vista atual e alterações não salvas, oferece chips, limpeza e gerenciamento responsivo.

### Validado nesta execução

- `npm run build` e `git diff --check` aprovados. Foram adicionados testes comportamentais para rota confiável, caracteres especiais, chaves reservadas, datas e exclusão de página da persistência.

### Pendente por limitação do ambiente

- O container não contém `dotnet`; restore bloqueado, builds Debug/Release e testes .NET não puderam ser executados. Não há PostgreSQL descartável/role restrita nem aplicação executável, portanto HTTP/RLS, concorrência real, jornada autenticada, capturas e QA 360/768/1280/1440 permanecem não validados.
- Datas relativas dependentes do fuso da organização, ficha/formulários de tratamento completos e criação idempotente com chave de requisição permanecem pendentes; não são declarados concluídos.

## Evolução — factory inequívoca e vistas pessoais (14/09/2026)

- Estado inicial registrado: branch `work`, SHA `4a38ba0f368e0fc9dfc3c89b6238da707b920315`; árvore de trabalho inicialmente limpa.

### Implementado

- A ambiguidade `CS0433` vinha das referências simultâneas a `Odca.Api` e `Odca.Bootstrap`, cujos entrypoints declaram `Program` no namespace global. As factories agora usam o marcador público exclusivo `Odca.Api.ApiAssemblyMarker`; a referência ao Bootstrap e seu comportamento foram preservados.
- A migration v014 adiciona vistas pessoais de trabalho, isoladas por tenant **e proprietário** via RLS, com definição JSON de filtros, ordenação allowlisted pela API, inativação lógica, versão otimista e unicidade de nome/padrão.
- A central de obrigações permite salvar os filtros correntes e reabrir uma vista. A API revalida permissão, tenant, contrato e responsável a cada abertura; somente a definição é persistida e a consulta operacional continua paginada no servidor.
- O schema canônico passa a declarar v014. A divergência preexistente entre a migration v013 presente e `DatabaseSchema.CurrentVersion` foi corrigida.

### Validado nesta execução

- `npm run build`, validação independente dos checksums 001–014 e `git diff --check` foram aprovados.
- A ausência de ocorrências restantes de `WebApplicationFactory<Program>`/`typeof(Program)` nos testes foi verificada por busca estática.

### Pendente por limitação do ambiente

- O container não possui `dotnet`; restore, builds Debug/Release e testes .NET não puderam ser executados localmente.
- Não há PostgreSQL descartável nem as credenciais exclusivas `ODCA_TEST_*`; migration real, RLS A × B e jornada autenticada permanecem gates de CI/QA.
- A ficha completa de contrato, agenda mensal e jornada HTTP integral de revisões permanecem no backlog; esta entrega não os declara concluídos.

## Obrigações: compilação e integridade das ações (14/09/2026)

### Implementado

- O alvo de `CA1859` em `ObligationsController.Create` era a variável local `IReadOnlyList<DateOnly> dates`; ela agora declara o `List<DateOnly>` efetivamente produzido pelos dois ramos, sem materialização adicional nem mudança no contrato HTTP.
- Reprogramações e reatribuições sem mudança real são rejeitadas antes da gravação, e a reatribuição exige justificativa tanto na regra de aplicação quanto na API.
- Evidências de cumprimento exigem permissão de download, versão do mesmo tenant e contrato, documento lógico ativo e estado de segurança `safe`; anexar não altera quarentena.
- A recorrência mensal limita a janela no extremo de `DateOnly`, evitando overflow, e continua preservando o dia-base nos meses curtos.
- A leitura de histórico agora executa a segunda consulta de permissão na transação ativa da conexão.

### Validado neste ambiente

- `npm run build` e verificações de diff/whitespace foram executados localmente.
- Foram adicionados testes comportamentais de reprogramação, reatribuição e limite superior da recorrência.

### Pendente / limitação verificada

- O SDK .NET 10.0.400 e o PostgreSQL descartável não estão disponíveis neste contêiner; restore, builds Debug/Release, testes .NET/HTTP/RLS e jornada autenticada no navegador não são declarados aprovados.
- Agenda mensal, formulários BFF das mutações, edição prospectiva de séries e dashboard consolidado permanecem incrementos posteriores; esta entrega não os apresenta como concluídos.

## Histórico autorizado de obrigações (14/09/2026)

### Implementado

- A API expõe o histórico cronológico da obrigação somente após validar vínculo, permissão de leitura, tenant RLS e o escopo do responsável; quem possui apenas leitura própria não consulta o histórico de outro responsável.
- O BFF mantém o token fora do JavaScript, distingue ausência, proibição e indisponibilidade e entrega à interface apenas a projeção autorizada.
- O painel de detalhes de “Minhas pendências” carrega autor, data, tipo, motivo, observação, mudança de prazo/responsável e presença de evidência, com texto seguro criado via DOM e estados de carregamento, vazio e erro.

### Validado neste ambiente

- `npm run build` validou os seis assets web e `git diff --check` não encontrou erros.

### Pendente / limitação verificada

- O SDK .NET 10.0.400 e o PostgreSQL descartável continuam indisponíveis; restore, builds .NET, testes, HTTP autenticado, RLS real e jornada no navegador não são declarados aprovados.
- As ações de mutação e a jornada completa de revisões ainda precisam ser conectadas no BFF. A consulta de histórico entregue não é apresentada como conclusão integral desses fluxos.

## Correções de analisadores e refinamento de Minhas pendências (14/09/2026)

### Implementado

- `CA1859` corrigido usando `Dictionary` no mapa privado de transições, sem alterar a exposição somente leitura das etapas e do histórico.
- Reatribuição rejeita motivo nulo, vazio ou em branco antes de qualquer mutação; normaliza com `Trim`, preserva a política de truncamento em 1.000 caracteres e mantém separados ator, revisor anterior e novo revisor na etapa e no evento.
- `CA1512` corrigido com `ArgumentOutOfRangeException.ThrowIfNegative(offset)`, preservando zero como ocorrência-base válida.
- Datas opcionais da consulta de obrigações usam `InvariantCulture` no formato ISO e continuam omitidas quando ausentes. Intervalo invertido retorna validação antes de chamar a API.
- A superfície existente foi renomeada para “Minhas pendências”, ganhou linguagem de central operacional, labels explícitos, estado de validação e paginação que preserva todos os filtros da URL.

### Validado neste ambiente

- `npm run build` validou os assets e `git diff --check` não encontrou erros de whitespace.
- Testes foram ampliados para justificativas, ausência de mutação parcial, auditoria de reatribuição, limite, offset negativo/zero e serialização ISO sob `pt-BR` e `ar-SA`, com restauração das culturas no `finally`.

### Pendente / limitação verificada

- O contêiner não possui o SDK .NET 10.0.400; por isso restore, build Release, testes .NET, PostgreSQL descartável, HTTP e jornada autenticada não são declarados aprovados.
- Sem aplicação .NET executável, não houve captura honesta nem QA em navegador nas quatro larguras. Integração completa de revisões e renovações na central, ficha do contrato e ações BFF continuam pendentes.
- A consulta da CI remota não pôde ser autenticada: `gh` não possui sessão e a API pública respondeu HTTP 403. O workflow versionado executa restore bloqueado, build, assets, testes com PostgreSQL 18 e secret scan.

## Incremento de revisão contratual interna (14/09/2026)

### Implementado

- O `CS9007` do teste de reparo foi removido na origem: o cenário de JSON válido agora serializa um objeto com `System.Text.Json`, preservando conexão, chave e propriedade desconhecida verificadas pelo teste. Os valores globais de ambiente usados pelos testes são restaurados ao valor anterior em `finally`, e a coleção continua serializada.
- A camada de aplicação recebeu o agregado `ContractReview`, separado do processamento do arquivo e do estado operacional do contrato. Ele prende a solicitação a uma versão imutável e a um snapshot de hash, partes, valor, moeda e vigência; aplica fluxo sequencial, confirmação explícita, justificativas, tabela de transições, revisão otimista, reatribuição histórica e comentários resolvíveis sem convertê-los em aprovação.
- A migration aditiva v012 cria solicitações, etapas, comentários, eventos e outbox interna com FKs compostas por tenant, RLS, índices de pendências, unicidade da etapa corrente/idempotência e catálogo de seis permissões de revisão. `database/odca.sql` e o snapshot de release foram consolidados com o mesmo checksum.
- O procedimento documentado de `ENC0097` agora explicita parar somente a sessão, corrigir erros reais, recompilar e reiniciar; não orienta alterar configuração nem finalizar todos os processos `dotnet`.

### Validado neste ambiente

- Checksum de todos os 12 blocos do SQL e igualdade do snapshot v012 foram verificados por script local; `npm run build` e verificações textuais são registradas na entrega.
- A tentativa real de `dotnet restore Odca.sln --locked-mode` foi feita, mas o executável `dotnet` não está instalado neste contêiner. Por isso build/testes .NET, PostgreSQL descartável e jornada autenticada não são declarados aprovados nesta execução.

### Pendente

- A v012 é a base transacional e de domínio, não a jornada HTTP/BFF completa. Repositório Dapper, casos de uso autorizados, endpoints, Worker da outbox, ficha split-view, central “Minhas pendências”, comparação e dashboard ainda precisam ser conectados antes de declarar o critério de conclusão do fluxo autenticado.
- Também permanecem pendentes a prova PostgreSQL de decisão/cancelamento concorrentes e isolamento A × B, e QA real por navegador/teclado em 360/768/1280/1440 e zoom 200%. Nenhuma captura foi fabricada sem uma aplicação executável.

## Correção do controller de documentos e contexto RLS (14/09/2026)

- **Implementado:** os streams e caminhos do upload têm nomes sem conflito; operações físicas usam o alias explícito de `System.IO.File`, enquanto respostas continuam usando os helpers do MVC. O arquivo temporário é descartado após a gravação e removido no `finally`; falha do commit remove o objeto que ainda não se tornou válido no banco.
- **Implementado:** `SetTenant` expõe o `Task<int>` realmente devolvido pelo Dapper. Listagem, conteúdo e revisão de extração agora definem `odca.tenant_id` localmente em uma transação e consultam na mesma conexão, eliminando o contexto de sessão que sobrevivia no pool. Nova versão bloqueia e valida o documento lógico no tenant/contrato antes de calcular seu número.
- **Validado neste ambiente:** `npm run build` e `git diff --check`. O teste PostgreSQL foi ampliado para alternância A → B e limpeza do contexto após commit e rollback usando a role restrita.
- **Não validado neste ambiente:** restore, build e testes .NET, PostgreSQL descartável, HTTP autenticado, navegador e capturas; o executável `dotnet` não está instalado e a obtenção do instalador oficial retornou HTTP 403. Essas verificações não são declaradas aprovadas.
- **Pendente:** BFF da área de contratos/documentos/revisão, inativação, reconciliação automática de órfãos/reservas, idempotência de upload, indicadores do dashboard e QA visual. A jornada integral solicitada continua explicitamente não concluída.

## Evolução do estúdio estruturado — fundação validável (14/09/2026)

- **Implementado:** CA1859 no migrador corrigido no parâmetro privado com o tipo concreto efetivamente produzido por `LoadHistoryAsync`, sem materialização, mudança de contrato público, ordem, checksum, transação ou cancelamento.
- **Implementado:** formato canônico JSON único para documentos, com nós/atributos/marcas permitidos, limites, campos por identificador estável e recusa de HTML, handlers e recursos externos. Validação tipada de campos, confirmação obrigatória na publicação, snapshot imutável, conflito otimista e restauração como novo rascunho foram adicionados à camada Application.
- **Validado neste ambiente:** assets e integridade do diff. O SDK .NET 10.0.400 não está instalado, portanto restore, build e testes .NET não foram declarados aprovados. A CLI do GitHub não está autenticada, logo o resultado remoto atual da CI não pôde ser consultado.
- **Documentado:** ENC0097 como limitação de Hot Reload e sequência segura de reinicialização do perfil `ODCA local`, sem modificar ou excluir configuração pessoal.
- **Pendente:** persistência/API/BFF do estúdio, modelos e cláusulas, PDF rastreável, migration v012, navegador autenticado, PostgreSQL descartável e QA visual. A fundação não é apresentada como jornada completa.

## Incremento S02 recuperado sobre o checkout disponível (14/09/2026)

| Funcionalidade | Implementada | Validada | Pendência | Ação desta entrega |
|---|---|---|---|---|
| Equipe e convites | sim | testes existentes; banco não executado aqui | prova PostgreSQL | preservada; schema avançado para v011 |
| Contratos | núcleo SQL | checksum estático | telas/casos completos da referência ausente | modelo mínimo tenant-aware criado |
| Upload e versões | API e armazenamento privado | testes de detecção/limites | reconciliação automática | streaming, hash, cota transacional e imutabilidade |
| Segurança | Worker local | fail-closed por código | fixture ClamAV | estados separados; sem liberação implícita |
| Extração | PDF nativo, imagem OCR e DOCX | DOCX/sugestões unitários | OCR de PDF digitalizado e QA executável | job com lease, retry e resultado rastreável |
| Revisão/aplicação | schema de decisões | checksum | API/BFF transacional e tela split-view | persistência preparada, sem alegar jornada completa |

O SHA histórico `183aa2b5` continua ausente do pack local e o fetch do GitHub foi
recusado pelo proxy (HTTP 403). O estado real foi reavaliado em `f6954f3`: este
checkout continha S01 e apenas o reparo v010, não os módulos S02 mencionados.
Esta entrega implementa uma primeira fatia executável em vez de declarar como
existente o código inacessível. SDK .NET, PostgreSQL descartável e navegador não
estão disponíveis neste ambiente; esses gates permanecem explicitamente não
aprovados.

## Reparo do pacote S01/S02 (14/09/2026)

- O checkout disponível está em `596ffe6` (S01), e não contém o SHA S02 informado (`183aa2b5`); o acesso ao remoto foi recusado pelo proxy HTTP 403. Assim, a central de contratos/documentos não foi recriada sobre uma base antiga nem declarada concluída.
- Foi reproduzido por inspeção o bloqueio PostgreSQL 42P13 da migração 009: parâmetro de entrada e coluna `RETURNS TABLE` usavam `invitation_id`. O instalador canônico agora usa `p_invitation_id`, sem alterar a assinatura por tipos, permissões ou o snapshot distribuído v009.
- O release v009 defeituoso permanece imutável para auditoria. O migrador aceita **somente** a transição dos checksums conhecidos v009 defeituoso → reparado, recria a função sem `DROP CASCADE` e atualiza o registro; qualquer outro checksum continua sendo recusado. O v010 registra o reparo e é o pacote canônico integral para instalação nova, atualização completa ou histórico parcialmente aplicado.
- Os testes de host agora isolam também Data Protection e cada caso mantém válidos os campos alheios ao alvo. As mensagens JWT identificam a própria validação, sem reduzir os requisitos de produção.
- Não verificado neste ambiente: restore/build/testes .NET (SDK ausente), PostgreSQL descartável, HTTP autenticado, navegador e larguras visuais. O build de assets e as verificações estáticas executadas estão registrados no PR.

## Configuração local consolidada (10/09/2026)

- O setup oficial passou a ser `.\scripts\setup-local.ps1`; ele mantém `Database=postgres`, schema `odca` e separa configuração/conectividade de migration e provisionamento.
- A exceção para provisionar acesso de demonstração na base `postgres` requer simultaneamente `--environment Development` e `--allow-postgres-development`; as recusas históricas abaixo descrevem versões anteriores.
- Neste ambiente de revisão o executável `dotnet` não está instalado e não há conexão autorizada ao PostgreSQL do usuário; portanto build/testes .NET, schema, credenciais, login HTTP, MFA e dashboard não foram declarados aprovados.

## Evolução prompt 13 — central operacional de equipe (10/09/2026)

- SHA inicial desta sessão: `297b2cde045c11f27665f4bf5d82ebd3d39b151c` (já à frente de `6ac493b` com lockfiles/CI).
- Restore bloqueado, build Release e `npm run build` aprovados com SDK 10.0.400 neste ambiente. NU1004/NU1510 não reproduzidos.
- Migração 008: checksum persistido corrigido. Migração 009 (`CurrentVersion=9`): status `expired`, aceite sem reativar vínculo bloqueado/inativo, entrega separada do ciclo do convite, expiração/cancelamento/reenvio, proteção do último administrador.
- API/BFF: central com abas Pessoas/Convites/Perfis, paginação real, 403 ≠ lista vazia, overview de assentos, ações de membro, perfis, seletor/edição de organização, preview de convite e copy honesta de reautenticação.
- Worker: lease por token; cancelamento do host separado; stub de produção falha com `notification_provider_missing`.
- Testes locais sem banco: Domain (23) + MigrationChecksum/startup/host (27) aprovados. Suíte PostgreSQL descartável e QA visual autenticado **não** executados: falta credencial admin (`development-runtime.json` ausente) e Docker Desktop parado; PostgreSQL 18 nativo está em execução na porta 5432.
- Esta entrega **não** declara conformidade LGPD plena nem e-mail de produção.

## Evolução prompt 10 — primeira fatia visual e navegação por perfil (10/09/2026)

- O contrato de autenticação agora transporta explicitamente o tipo de conta; o BFF converte esse dado em role do ticket protegido e monta navegação de plataforma ou cliente sem inferência visual.
- O layout autenticado ganhou sidebar recolhível, contexto inequívoco, topbar e drawer mobile com retorno/contenção de foco e fechamento por `Escape`. Percursos de senha inicial e MFA continuam fora do shell administrativo.
- Tokens visuais, controles, métricas, planos e autenticação foram consolidados em um sistema corporativo responsivo. A largura total saiu de `.button-primary` e passou ao modificador contextual `.button-block`.
- Login e cadastro têm exibição acessível de senha e proteção visual de envio repetido. A home do cliente diferencia limites contratados de consumo medido.
- `npm run build` e `git diff --check` foram aprovados. Restore/build/testes .NET não puderam ser executados porque o SDK 10 não existe no ambiente e a instalação oficial foi recusada por HTTP 403. Por isso, o NU1004 não foi declarado corrigido e lockfiles não foram alterados manualmente.
- QA renderizado e capturas em 360/768/1440 também ficaram bloqueados pela ausência de runtime .NET e navegador instalado. Esta fatia não declara organização múltipla, equipe, convites, quotas ou ficha de clientes concluídos.

## Etapa atual

Referência revalidada em 09/09/2026: SHA base `5ab8df3`. S00 continua **em validação de banco/navegador**. S01 está **parcial**: catálogo público e entrada pública de solicitações de privacidade existem; onboarding, MFA, equipe e operação do atendimento ainda não.

## Evolução prompt 09 — acesso local verificável

- A duplicação de `StartupValidationService` introduzida pelo merge foi removida de `StartupValidation.cs`; a implementação permanece no arquivo próprio e há um único registro hospedado.
- O Bootstrap ganhou `provision-test-access --environment Development`: destino sanitizado, recusa da base `postgres`, transação para superadministrador e cliente sintético, organização de demonstração, administrador de tenant, Basic vigente e auditoria da concessão local.
- Reexecução preserva identidades e senhas. `--rotate-passwords` é necessário para rotação; conta comum preexistente não é elevada. Após o commit, o comando relê perfil, bloqueio/exclusão, vínculo, papel e plano, e verifica senhas disponíveis pelo `IPasswordService`.
- `show-login` agora consulta o banco e não apresenta arquivo desatualizado como senha atual. `reset-password` valida exatamente uma linha de usuário, revoga sessões na mesma transação e preserva MFA.
- Este ambiente não contém SDK .NET nem conexão PostgreSQL local autorizada. Portanto persistência, senha, login HTTP e MFA ainda não foram alegados como executados; os comandos exatos estão no README.

## Correção posterior à CI 34350222079

- A configuração da API deixou de ser lida antecipadamente em `Program` e no registro da Infrastructure. JWT, política de autenticação e data source agora são materializados depois que o host terminou de compor suas fontes de configuração.
- Uma validação hospedada mantém o fail-fast para conexão, issuer, audience, chave, expiração e controles de produção, sem inserir configuração sintética na aplicação.
- Testes determinísticos cobrem aceitação da configuração isolada e rejeição de JWT/MFA inválidos. A execução local não foi possível neste ambiente porque o SDK `dotnet` não está instalado; a correção precisa do gate de CI/PostgreSQL.
- A matriz factual deste incremento está em `docs/execution/S01_RECONCILIATION.md`.

## Implementado nesta evolução

- NU1004 corrigido: JwtBearer e Mvc.Testing alinhados em `10.0.12`, locks regenerados e restore bloqueado aprovado.
- Fixture isolada: sem fallback para `development-runtime.json`, sem mutação global de ambiente e com marcador, banco `odca_test*` e role `odca_test_app_login` obrigatórios.
- CI com secret scan independente e publicação de TRX mesmo após falha de testes.
- Runner valida todo o histórico conhecido antes de mutar; rejeita checksum divergente, lacuna e banco mais novo que o código. Readiness exige PostgreSQL 18 e schema exatamente na versão 3.
- Sessão de autenticação usa a duração JWT configurada; login durante bloqueio não prorroga a janela.
- BFF diferencia 401, 403, 429, timeout e 5xx em login, troca de senha, dashboard e planos; falha do backend não é apresentada como logout.
- Catálogo público separado da administração em `/api/v1/catalog/plans` e `/conheca-os-planos`, com rejeição explícita de plano vigente ambíguo ou incompleto.
- Migração 003 e fatia ODCA-PRIV-003: formulário `/privacidade`, API pública com rate limit, protocolo opaco, resposta neutra, histórico inicial e gravação por função de privilégio restrito.
- Teste comportamental preparado para role runtime: ausência de contexto, alternância de tenant, `role_permissions`, auditoria e FK cruzada A × B.

## Histórico de verificações (resultados não cumulativos)

| Verificação | Resultado |
|---|---|
| `dotnet restore Odca.sln --locked-mode` no SHA anterior | sucesso |
| `dotnet build Odca.sln --configuration Release --no-restore` no SHA anterior | sucesso; 0 avisos, 0 erros |
| `npm run build` no SHA anterior | sucesso |
| testes de domínio no SHA anterior | 10 aprovados |
| parser/checksum/snapshots de migração no SHA anterior | 6 aprovados |
| `dotnet test Odca.sln --configuration Release --no-build` no SHA anterior | 16 aprovados; 3 cenários PostgreSQL recusados antes de conexão por ausência do marcador exclusivo |
| CI remoto do SHA `c5e5064` | secret scan aprovado; restore falhou por NU1004 antes de build/assets/testes |
| `npm run build` nesta correção | sucesso com Node 20.20.2; o projeto declara Node >=24, portanto a CI continua sendo o gate na versão suportada |
| `dotnet restore Odca.sln --locked-mode` nesta correção | não executado: SDK .NET 10 ausente e download bloqueado por HTTP 403 no ambiente |
| suíte PostgreSQL atual | não executada: faltam as três variáveis exclusivas de teste e uma credencial administrativa autorizada |

## Parcial, pendente ou bloqueado

- `database/odca.sql` permanece um histórico rastreável e o runner pula versões aplicadas, mas a execução direta integral no pgAdmin ainda reexecuta corpos históricos. A equivalência/recovery/concurrency do pacote SQL não foi comprovada.
- O teste A × B e o fluxo API foram escritos, mas aguardam banco descartável autorizado; nenhum banco de desenvolvimento foi tocado.
- MFA real, cache/Data Protection compartilhados, onboarding de organização, confirmação de e-mail, assinatura, equipe/perfis, suporte temporário, tratamento autenticado de direitos, retenção executável e S02/S03 permanecem pendentes.
- A página pública de privacidade é um canal de entrada técnico; não publica texto jurídico, prazo ou contato não aprovados e não constitui comprovação de conformidade LGPD.
- QA visual responsivo e navegador autenticado ainda não foram executados.
- Marcos B–E não foram implementados por esta correção do gate A; permanecem explicitamente pendentes e não devem ser inferidos dos testes de startup.

## Evolução prompt 08 — incremento local de integridade (09/09/2026)

- Corrigidos os três defeitos reproduzidos da CI 34367965217: contagem canônica de migrações, projeção explícita da home e conversão UTC do estado MFA.
- A elevação MFA agora renova, na mesma transação, a expiração persistida da sessão para exatamente a validade emitida no JWT. O início exige sessão/versão válidas e estado ainda não confirmado; a confirmação compara o segredo protegido observado.
- CPF/CNPJ passam por dígitos verificadores. A migração 006 preserva 001–005, impede mais de um cadastro vivo por documento e acrescenta campos de evidência de termos/aviso. Os marcadores de versão permanecem `pending-legal-approval`, sem representar texto jurídico aprovado.
- Repetição idempotente volta a criar a mensagem de outbox ligada ao token rotacionado, e a confirmação elimina o token de desenvolvimento.
- Chaves Data Protection têm caminho persistente configurável para API e BFF. O ticket store segue em cache de memória: múltiplas instâncias **não** estão declaradas prontas.

Ainda pendentes nesta entrega: transporte de e-mail de produção/worker com lease e retentativas, seleção explícita de organização, equipe/perfis/convites/quota, reset privilegiado de MFA, QA visual e suíte PostgreSQL descartável. Não há editor/OCR iniciado.

## Evolução prompt 11 — contexto explícito e primeiro percurso de convite (10/09/2026)

- A migração 007 introduz versão otimista da organização, permissões delegáveis do tenant, convites que reservam assento e outbox com lease/retry. Funções `SECURITY DEFINER` têm superfície explícita e o acesso comum continua sob RLS.
- A API lista organizações pelo `sub` autenticado, exige tenant explícito nas operações e revalida vínculo/permissão. Há leitura de equipe/perfis, criação restrita de perfil, edição organizacional com conflito e criação/aceite de convite de uso único.
- A confirmação do onboarding passa a atribuir o administrador inicial idempotentemente. Aceitar convite exige a identidade autenticada com e-mail verificado e revoga suas sessões para renovar autorização.
- O BFF ganhou escolha por nome e equipe/perfis sem IDs digitados. O shell usa o nome selecionado, fallback seguro de avatar e melhorias de drawer/localStorage/formulários.
- `npm run build`, validação local dos sete checksums e `git diff --check` passaram. O SDK 10.0.400 continua ausente e o instalador oficial respondeu HTTP 403; portanto restore forçado/bloqueado, build/testes .NET, PostgreSQL e QA renderizado não foram executados neste ambiente. Os lockfiles não foram editados manualmente.
- Bloqueio/restauração de membros, reenvio/cancelamento, proteção concorrente do último administrador e transporte de produção permanecem pendentes; esta entrega não declara o módulo integral concluído.

## Evolução S03 — obrigações e renovações (14/09/2026)

### Matriz de reconciliação

| Funcionalidade | Existente | Validada agora | Problema encontrado | Alteração prevista/realizada |
|---|---|---|---|---|
| Configuração local | Sim | Inspeção + teste isolado existente | SDK ausente impede execução .NET | CS9007 permanece corrigido por `JsonSerializer`; orientação ENC0097 preservada |
| Contratos/documentos/versões | Sim (v011) | Inspeção de código e FKs | Não havia obrigação ligada a versão | Evidência referencia a versão imutável sem novo upload nem ampliação de download |
| Revisão/aprovação | Domínio e SQL (v012) | Testes existentes inspecionados | Jornada HTTP ainda parcial | Mantida separada de cumprimento e renovação |
| Obrigações | Não | Regras puras e assets | Ausência de modelo, API, central e auditoria | Implementado agregado, API tenant-aware, concorrência, histórico, filtros e central responsiva |
| Recorrência | Não | Testes para fevereiro, bissexto e dia 31 | Risco de deriva pelo último dia ajustado | Série mensal usa sempre o dia-base e materializa no máximo 24 ocorrências |
| Alertas internos | Outbox de convites/revisão | Checksum e inspeção | Não havia lembrete de obrigação | Lease tokenizado, deduplicação, atraso limitado a um dia e notificação interna individual |
| Renovações | Campos básicos no contrato | Inspeção | Intenção poderia ser confundida com vigência | Ciclos históricos; apenas `renewed` com novas datas altera a vigência |
| Central visual | Não | `npm run build` | Sem lista operacional | Lista paginada, URL filtrável, escopo, painel lateral e adaptação móvel |

### Implementado

- Migration aditiva v013, snapshot, permissões, RLS, FKs compostas, índices, concorrência otimista, séries/ocorrências, evidências, eventos, lembretes, notificações individuais e ciclos de renovação.
- Obrigações pontuais ou mensais; atraso derivado; cumprimento, cancelamento, reabertura, reatribuição e mudança de prazo auditáveis. Obrigações financeiras mantêm valor decimal e moeda explícita e não acionam pagamento.
- API preserva tenant explícito e valida permissão por operação, vínculo ativo, transições e versão. Evidência apenas aponta para `document_versions`; download continua sob a permissão própria.
- Worker interno reivindica um lembrete com `SKIP LOCKED`, proprietário/token, tentativas limitadas e cancelamento do host. Itens muito antigos não causam avalanche.
- Renovação guarda ciclos anteriores e obrigações pendentes. Intenção e negociação não mudam vigência; somente renovação registrada altera datas.

### Validado neste ambiente

- `npm run build`, verificação independente dos 13 checksums e `git diff --check` foram executados.
- O SDK .NET 10 não está instalado (`dotnet: command not found`), portanto restore/build/testes .NET, PostgreSQL descartável, HTTP, navegador autenticado e capturas não são declarados aprovados.

### Pendente

- Conectar formulário completo de criação/ações e ficha visual do contrato; a central entregue é consultiva e o percurso de mutação está disponível na API.
- Executar CI com SDK 10/PostgreSQL 18 e QA renderizado em 360/768/1280/1440 e zoom 200%. Sem runtime não foi fabricada captura.
## Correção dos diagnósticos e integridade do histórico (14/09/2026)

- O HEAD inicial desta execução foi `e923e81f8636464cd0fe0575d359a3f8d15f8861`, posterior à referência `09b078947bdf1a39bc0966ece0f60655a1daa615`; as alterações existentes foram preservadas e ajustadas incrementalmente.
- CA1859 foi corrigido somente no campo privado de transições. CS8604 foi corrigido pelo contrato de nulabilidade de `Limit`; o motivo de reatribuição é normalizado/truncado uma vez e reutilizado na decisão e auditoria, sem alterar a política preexistente.
- CA1305 permanece corrigido com datas `DateOnly` invariáveis na query e montagem legível. CA1512 permanece corrigido com `ThrowIfNegative`, mantendo zero e o cálculo a partir da data-base.
- A API agora rejeita intervalos invertidos. O evento de cumprimento preserva data efetiva, observação e identificador da evidência; a reabertura limpa apenas a projeção atual de cumprimento, mantendo o evento histórico e respeitando o `CHECK` do banco.
- `npm run build` e `git diff --check` passaram. Restore, build e testes .NET não foram executados porque o SDK 10.0.400 não existe no contêiner e o instalador oficial respondeu HTTP 403. PostgreSQL descartável, HTTP autenticado, navegador e capturas continuam não aprovados.

## Central do cliente, consumo e concessões auditadas (15/09/2026)

### Implementado

- A central **Plano e consumo** consulta a versão imutável do plano vinculada à assinatura e separa vagas ativas, convites reservados, limite contratado, capacidade adicional vigente, bytes persistidos, reservas e disponibilidade. Falha da API apresenta indisponibilidade e nunca é convertida em zero.
- Pacotes adicionais são versionados e pedidos preservam snapshot de nome, quantidade, unidade, preço/moeda opcionais e condições. As propostas iniciais permanecem em `draft`: não há preço, checkout, PIX ou pagamento fictício. O fluxo operacional começa quando um administrador publica uma versão configurada.
- Aprovação e concessão manual são operações exclusivas do SuperAdministrador com ator real, justificativa, idempotência, bloqueio concorrente, movimento imutável e auditoria. Aprovar duas vezes não duplica capacidade.
- O upload passa a recalcular a cota efetiva (plano da assinatura + concessões vigentes), serializa a reserva na linha de consumo, confirma a reserva e grava o movimento somente com a versão persistida. A leitura continua disponível quando a capacidade está excedida; somente novas gravações deixam de reservar.
- A administração global lista clientes com documento mascarado, plano, estados independentes, usuários, consumo, pendências e atividade; detalhes deixam explícito o tenant afetado e não implementam impersonação.

### Regras e limitações comerciais

- Concessão de capacidade não equivale a pagamento. Faturas/pagamentos e prorrata não existem operacionalmente no checkout atual e não foram simulados.
- Capacidade em bytes é um limite vigente; OCR e envelopes permanecem créditos consumíveis separados. Exclusão lógica de documento não reduz consumo físico.
- Troca de plano ainda não possui fluxo comercial existente completo; nenhum cálculo de prorrata ou aplicação automática foi inventado. Bloqueio administrativo continua separado do estado comercial da assinatura e do vínculo do usuário.
- Expiração automática de reservas abandonadas e contabilização dos demais produtores de arquivo (PDF gerado/cópia/evidência) exigem integração nos respectivos workers; a tabela e os movimentos foram preparados, mas somente o upload de documento está conectado nesta entrega.

### Validação desta execução

- `npm run build`, verificação independente dos checksums das 18 migrations e `git diff --check` foram executados.
- O contêiner não contém `dotnet` nem PostgreSQL de teste; portanto restore/build/testes .NET, concorrência PostgreSQL real, HTTP autenticado e captura de navegador não são declarados aprovados.

### Execução local

1. Instale o SDK indicado em `global.json` e PostgreSQL 18.
2. Execute `pwsh ./scripts/setup-local.ps1` e configure `development-runtime.json` conforme o exemplo da API.
3. Execute `dotnet restore Odca.sln --locked-mode`, `dotnet run --project src/Odca.Bootstrap` e depois os perfis API/Web/Worker.
4. Publique uma versão de pacote somente após configurar condições comerciais; as propostas distribuídas são rascunhos intencionais.
