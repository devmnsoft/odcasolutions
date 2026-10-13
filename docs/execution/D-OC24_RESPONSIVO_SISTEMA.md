# D-OC24 — Bloco F: responsivo 360/768/1440px do sistema inteiro

Data: 2026-10-13 · Branch: codex/s00-foundation · Base: `598413e` (D-OC23)

## Contexto e escopo

Após fechar o Estúdio (D-OC23), estender a mesma auditoria de responsividade a **todo o sistema Web**: sem overflow horizontal nas larguras exatas **360px / 768px / 1440px** em todas as áreas principais, corrigindo os defeitos reais encontrados e documentando a matriz de evidência.

- **Páginas com sessão (53)**: Estúdio ×11 (as 6 do D-OC23 + revisores, checklist, comentários, versões da minuta, prévia de modelo); área organizacional ×22 (`/caixa`, `/contratos`, ficha `/contratos/{id}`, `/obrigacoes`, `/renovacoes` + detalhe, revisões lista + detalhe, central de solicitações + nova + detalhe, `/acoes`, `/agenda`, `/documentos` + novo, `/importacoes`, `/pacientes` + detalhe, `/plano-e-consumo`, `/equipe`, `/editar`, visão geral `/cliente`, `/primeiros-passos`, raiz `/`); conta ×5 (`/minha-conta`, notificações ×2, `/alterar-senha`, `/acesso-negado`); plataforma ×10 (clientes ×3, biblioteca ×3, configurações, auditoria, atendimento ODCA ×2); públicas ×3 (catálogo de planos, `/planos`, `/privacidade`).
- **Páginas anônimas, sem sessão (4)**: `/entrar`, `/conheca-os-planos`, `/planos`, `/privacidade`.
- Matriz: 57 páginas × 3 larguras = **171 combinações**.
- Alvos reais no banco QA (`odca_test_disposable`, tenant `20000000-…-0001`, exceto onde anotado): ficha `9a829f84-c615-4d59-ab3a-8d4b25249919`, solicitação `18de3694-2273-4226-90c4-78d7745c6c64`, revisão `adea675a-e90d-40f6-afce-94f3c403210f` (tenant `e5f78b3c-…`), ciclo de renovação `351adb2c-76d1-4c1b-86ad-bb0a80a71dd6` (idem), paciente `75c87013-4595-41dd-93d9-ac2e9c1f8fc4`, minuta/versão/modelo do D-OC23.

## Método (mesmo harness do D-OC23, versão estendida)

- Runner encadeado único `%TEMP%\sv-resp-chain-all.ps1` (o app dotnet morre quando o shell que o lançou sai): gate Postgres por `psql` → kill dotnet → API/Web com logs → readiness → reinserção do código MFA REV21A (hash SHA-256 `uid:CODE`) → login+MFA via curl → jar pt-BR → capturas.
- Driver `%TEMP%\sv-resp-shots-all.mjs`: `msedge` headless (Edg/155.0.4283.45) via CDP; viewports exatos por `Emulation.setDeviceMetricsOverride` (360×800 / 768×1024 / 1440×900); cookies via `Network.setCookie` (aceita jar vazio = sessão anônima); auditoria por combinação: `documentElement.scrollWidth − innerWidth` (tolerância ≤2px) + elementos com `right > vw+2px` excluindo descendentes de scrollers (`overflow` computed `auto`/`scroll`, independentemente do estado atual de scroll) + screenshot full-page.
- Sonda diagnóstica `%TEMP%\sv-resp-lprobe.mjs`: geometria (rects) + computed style + cadeia de ancestrais por seletor — foi o que encerrou as investigações abaixo.
- Zero mudança em C#/API: apenas CSS e 3 views (tags de classe/script).

## Defeitos encontrados e correções

### A) Layout — 4 causas raiz (7 combinações quebradas na pré-auditoria)

| # | Combinações | Sintoma | Causa raiz | Correção |
|---|-------------|---------|-----------|----------|
| 1 | ficha @360 | overflow +153px | `.renewal-row` é grid e a track `1fr` (≡ `minmax(auto,1fr)`) foi inchada pelo min-content de um token inquebrável (hash SHA-256 em `dd`, ~471px). Sonda in-browser: a track do artigo defeituoso media `471.625px` num container de 305px, os demais artigos do mesmo CSS `267px` | `.renewal-row>*{min-width:0}` + `overflow-wrap:anywhere` nos `h3/p/small/dd/code` das linhas |
| 2 | central-solicitacoes @360/@768 · atendimento ODCA @360/@768 · aprovações @360 | +461/+53 · +609/+201 · +213px | O padrão "cards" ≤768 converte `table/thead/tbody/tr/td` para `display:block` e esconde o `thead` com `position:absolute;clip:rect(0 0 0 0)` — mas os `th` permanecem `table-cell`, formando uma **tabela anônima shrink-to-fit** dentro do thead posicionado, com a largura intrínseca total da tabela (801px / 949px / 553px). O box invisível inflava a área rolável do documento: o overflow medido era **exatamente** a borda direita do thead − viewport (821−360=461 · 821−768=53 · 969−360=609 · 969−768=201 · 573−360=213). Só tabelas largas (8–9 colunas) disparavam — as mais estreitas tinham largura intrínseca < viewport (por isso Obrigação/Revisões/Studio-Version já passavam) | no bloco `@media(max-width:768px)`: `.table-scroll{position:relative}` + `thead{width:1px;height:1px;overflow:hidden}` (mantendo o clip) — o box oculto deixa de contribuir overflow |
| 3 | documentos @360/@768 · `/cliente` @360 | +528/+356 · +207px | A classe de wrapper `.table-responsive` usada em `Documents/Index.cshtml` e `Onboarding/CustomerHome.cshtml` (2 tabelas) **não existe no site.css** → tabela real de 868px/534px sem scroller. `/cliente` (Visão geral) nem estava coberto pelas matrizes anteriores | substituída por `.table-scroll`, a convenção já usada por todas as demais listas (e que garante o colapso em cards ≤768); `/cliente` e `/primeiros-passos` adicionados aos alvos |
| 4 | documentos @768 (barra de filtro) | itens do form até x=1124 | `.studio-filter` é flex-row **sem wrap**; com os 9–11 campos desta página, Σ min-content > largura disponível em 768px → os itens transbordam o próprio form (que cabia: 713px) | `.studio-filter{flex-wrap:wrap}` — sem efeito nas larguras onde já cabia (1440) e ≤600 já é coluna |

### B) JavaScript — jQuery ausente na validação de formulários (bug pré-existente)

- `/privacidade` (e qualquer consumidor do `_ValidationScriptsPartial`: inscrição/desafio MFA, registro, confirmação de e-mail) carregava `jquery.validate` + `unobtrusive` **sem que nada no app carregasse o jQuery core** — grep confirmou: a única menção a jquery em todo o Views era o próprio partial. Resultado: 2× `ReferenceError: jQuery is not defined` em toda carga, em todos os viewports (sessão e anônimo).
- Correção: `<script src="~/lib/jquery/dist/jquery.min.js"></script>` no início do `_ValidationScriptsPartial.cshtml` (o arquivo existe em `wwwroot/lib/jquery/dist`; nenhuma view carrega o jQuery separadamente → exatamente uma carga por página). Evidência: jsErrors 2→0 nas três larguras em ambas as rodadas.

### C) Lacuna de processo descoberta na verificação (não é defeito de código)

- Views Razor são **pré-compiladas no assembly**: na primeira re-roda pós-fix apenas as mudanças de CSS surtiram efeito — as edições `.cshtml` (wrappers + jquery) saíram como antes (documentos/ficha-cliente ainda com tabela crua, privacidade ainda `err=2`). Após rebuild de `Odca.Web` (0W/0E), tudo passou. Regra registrada: edição de `.cshtml` exige `dotnet build` antes da verificação em runtime (CSS/assets estáticos aplicam direto do disco).

## Mudanças

| Arquivo | Alteração |
|---------|-----------|
| `src/Odca.Web/wwwroot/css/site.css` | (i) bloco ≤768: `.table-scroll{position:relative}` + `thead{width:1px;height:1px;overflow:hidden;clip:rect(0 0 0 0)}`; (ii) `.studio-filter{…flex-wrap:wrap…}`; (iii) `.renewal-row>*{min-width:0}` + `overflow-wrap:anywhere` em `h3/p/small/dd/code` das linhas |
| `src/Odca.Web/Views/Documents/Index.cshtml` | wrapper `table-responsive` → `table-scroll` |
| `src/Odca.Web/Views/Onboarding/CustomerHome.cshtml` | mesma substituição ×2 tabelas |
| `src/Odca.Web/Views/Shared/_ValidationScriptsPartial.cshtml` | adiciona o script do jQuery core antes dos validadores |

## Evidência comportamental

- **Matriz pós-fix 171/171: `loaded=true · overflow=0px · elements=0 · jsErrors=0` → `RESPONSE-CLEAN`** nas duas rodadas (sessão 53 páginas + anônimas 4 páginas × 3 larguras).
- Pré-fix (registrado no log de execução): as 7 combinações de layout da tabela acima + `pub-privacidade` `err=2` nas 3 larguras de cada rodada; as demais ~160 combinações já limpas.
- Artefatos: `%TEMP%\sv-resp-evidence-all\` (PNGs full-page + `sv-resp-report-all.json`) e `%TEMP%\sv-resp-evidence-anon\`.
- Gates: build da solution **0W/0E** · Domain **353/353 PASS** · paridade de catálogos **4/4 PASS** · suíte de integração **54 falhas / 116 aprovados / 170 — idêntica à linha de base ambiental** (zero nova regressão).

## Limitações

- O driver não capturou o status HTTP por documento (evento `Network.responseReceived` de tipo Document não observado neste fluxo de CDP); o carregamento foi validado por `readyState=complete` + origem.
- `studio-previa` redireciona para o índice do Estúdio (modelo em rascunho sem versão previais — redirecionamento comportamental, sem quebra de layout).
- Detalhe de renovação cross-tenant (id de ciclo de outro tenant) redireciona para a lista de renovações do tenant (porte/autorização, não layout).
- Anônimo `/planos` redireciona para `/entrar` (comportamento projetado do controller).
- Homologação visual de PDFs permanece item próprio do Bloco F.
