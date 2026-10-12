# D-OC18 — Recuperação de Divergência e Expiração de Sessão nas Operações da Caixa/Ficha (Bloco E, item 19)

Data: 2026-10-12 · Branch: codex/s00-foundation · Base: `27e2574`

## Escopo

Garantir que, durante o trabalho operacional (Ficha do contrato, Caixa, Estúdio e revisão de importação), duas classes de falha deixem de produzir estados enganosos e permitam recuperação sem perda de contexto:

1. **Divergência (409)**: mutações com versão defasada devem devolver à UI uma mensagem dedicada com instrução de recarga, não um erro genérico nem página vazia.
2. **Expiração de sessão**: sessões que expiram no meio da operação devem levar o usuário de volta ao login **e de lá para a página exata de origem**, e chamadas `fetch` devem reconhecer a redireção (hoje viravam erro de parse ou falha silenciosa).

## Regras adotadas

| Ponto | Comportamento |
|-------|---------------|
| `ReturnUrl` na cadeia login+MFA | O destino original é preservado em três saltos: desafio do cookie (`/entrar?ReturnUrl=`), POST de login → `/mfa/desafio?returnUrl=`, e hidden input na página de desafio. Após MFA válido, o redirect volta para a página de origem (ou Home quando ausente). Somente URLs locais passam pelo validador existente (`TenancyDisplay.IsSafeLocalUrl`). |
| Middleware de estágio de segurança | Super-admin autenticado sem MFA confirmado é redirecionado a `/mfa/desafio?returnUrl=<caminho+query>` (antes: destino fixo sem origem). |
| Ação de obrigação (Ficha) | 409 com código `obligation.version.conflict` → mensagem dedicada "A obrigação foi alterada em outra sessão. Recarregue a página e reaplique a ação."; outros 409 seguem a mensagem do servidor. API passou a incluir o código no corpo do conflito. |
| Proposta de renovação/aditivo (Ficha) | 409 com código `contract.version.conflict` → "O contrato foi alterado em outra sessão. Recarregue a página e reaplique a alteração."; outros 409 (ex.: alteração ativa existente) mantêm a mensagem do servidor. API passou a incluir o código no guarda de versão. |
| Definir vista padrão (Caixa) | 409 de versão divergente deixa de devolver `StatusCode(409)` vazio: redireciona ao índice da Caixa com a mensagem "A vista foi alterada em outra sessão (versão divergente). Recarregue a caixa e defina a vista novamente." |
| `fetch` nas quatro frentes (studio.js, imports.js, obligations.js, drawer da Ficha) | Resposta `redirected` (ou 401) é tratada como sessão expirada: mensagem clara no local da ação + redirecionamento programático para `/entrar?ReturnUrl=<página atual>`. Antes do parse JSON — HTML de login nunca mais vira `SyntaxError` ou falha genérica. |

## Evidência comportamental (aplicação real, API `:7143` + Web `:5144`, banco `odca_test_disposable`)

Sessões MFA consumidas nesta homologação: `ODCA-QA-REV12–REV17` (admin). Fixture reversível: 1 linha em `odca.saved_work_views` (`VistaHomologacaoDivergencia`, tipo `obligations`) para o teste D.

| Teste | Cenário | Resultado observado |
|-------|---------|---------------------|
| A | Expiração real: `UPDATE odca.sessions SET revoked_at=now()` (30 sessões admin; o caminho de validação `IsSessionValidAsync` é o mesmo de expiração) → GET da ficha | **302** `http://localhost:5144/entrar?ReturnUrl=%2Forganizacoes%2F20000000…%2Fcontratos%2Fcfd0bf87…`; login preserva o hidden `ReturnUrl`; POST login → **302** `/mfa/desafio?returnUrl=%2Forganizacoes%2F…` (comportamento novo); página de desafio preserva o hidden; POST MFA → **302 exatamente** para a URL original da ficha; ficha reaberta → **200** |
| M | Super-admin autenticado sem MFA confirmando → middleware | **302** `/mfa/desafio?returnUrl=%2Forganizacoes%2F…%2Fcfd0bf87…` (antes: `/mfa/desafio` sem origem) |
| B | Obrigação `d2ce37e4…` (tenant `e5f78b3c…`, `row_version=1`) com form POST `version=0` defasado | **302** para `…#obrigacoes`; página renderiza a mensagem dedicada ("alterada em outra …" + "Recarregue a p…" + "e reaplique"); banco intacto (`row_version=1`) |
| C | Proposta de renovação no contrato `cfd0bf87…` (v2) com `contractVersion=1` defasado | **302** para `…#renovacao`; página renderiza a mensagem dedicada ("alterado em outra …" + "Recarregue a p…" + "e reaplique"); sem nova linha em `contract_change_requests` (contagem inalterada = 1) |
| D | `POST vistas/{id}/padrao` com `version=1` defasada (`row_version=4`) | **302** ao índice da Caixa (antes: `409` com corpo vazio); índice renderiza "A vista foi alterada em outra sessão (versão divergente). Recarregue a caixa e defina a vista novamente."; estado inalterado |
| E | Handlers de sessão expirada servidos pela Web | `GET /js/studio.js`, `/js/imports.js`, `/js/obligations.js` (todos 200) contêm `response.redirected` + a mensagem de expiração específica; página da ficha contém o mesmo guard no drawer de histórico |

Classificação: comportamento provado por comandos reais da aplicação (HTTP + renderização + estado do banco). SQL usado apenas para forçar a invalidação de sessões no teste A (mesmo caminho de validação da expiração), para criar a linha-fixture do teste D e para asserções de observação.

## Descoberta colateral (pré-existente, fora do escopo deste incremento)

**Salvar vista da Caixa devolve 500**: `InboxController.SaveView` envia `listingType="inbox"`, mas a constraint canônica (`database/odca.sql`:1925) restringe `saved_work_views.listing_type IN ('obligations','reviews','contracts')` — `'inbox'` nunca esteve no esquema, então toda criação de vista da Caixa viola `saved_work_views_listing_type_check` (Npgsql 23514 → 500; a UI mostra a mensagem genérica "Não foi possível salvar a vista da caixa"). A tabela estava vazia em todos os tenants por causa disso. Requer migração (extensão da constraint ou novo tipo) ou alinhamento do contrato BFF/API — agendar incremento dedicado.

## Limitações

- A cadeia de **inscrição** MFA (`/mfa/inscricao`) não preserva `ReturnUrl` (fluxo de primeiro acesso, onde o destino de origem raramente importa); a do desafio sim.
- No Estúdio, a navegação para o login ao detectar expiração descarta edições não salvas da aba atual (o guard de `beforeunload` é acionado); o conteúdo já salvo permanece íntegro no servidor.
- Os handlers `fetch` foram comprovados pela entrega real dos assets e pela leitura da lógica; a verificação em navegador headless entra na homologação visual do Bloco F.
- Sem push/merge/deploy automático.
