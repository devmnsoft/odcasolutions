# Incremento D — Identidade visual, documento com capa/sumário e homologação ponta a ponta

**Plano registrado em 09/10/2026 · Fecha a Seção D do MVP (`ENTREGA_JORNADA_MVP_20261006.md`, "D. Design e homologação").**

## Objetivo

Homologar o produto ponta a ponta na stack real contra os requisitos da Seção D: identidade visual ODCA aplicada de forma consistente (tela **e** documento), documento gerado com capa/sumário quando adequado e sem variáveis não resolvidas, lista de contratos com CTA "Novo contrato", responsivo obrigatório em 360/768/1440 px + teclado/foco/contraste, matriz ações×perfil×plano documentada e provada, e os **19 cenários mínimos** executados sobre **2 organizações + 4 perfis** com evidências UI.

## Estado verificado antes do incremento (base do plano)

- `site.css` já declara os tokens da paleta da SPEC §8 (`--navy-900:#102a43`, `--action-600:#245bdb`, `--text:#243746`…), mas o arquivo acumulou ~20 hexos avulsos que desviam dos tokens (ex.: `#0056b3` no subnav ativo, foco global `#78a9ff`, `#f5f7fa` ≠ branco-gelo `#F4F7FB` da SPEC); Bootstrap 5.3.3 está vendorizado mas nunca é carregado — classes `table-responsive`/`table` em `Contracts/Index.cshtml:53-54` são inertes.
- O renderer documental (`ContractDocumentRenderer`) não tem capa nem sumário (PDF = título + "Versão N" + rodapé de página); numeração de cláusulas é texto literal nos modelos oficiais; campo sem valor renderiza o texto literal "Não informado" (rascunho) e a geração completa falha 422 (`ContractValidationMode.Complete`) quando falta variável.
- Não existe componente "caixa de atenção" nem no documento nem nas telas (só `.notice/.warning/.error` genéricos).
- O CTA existe: `+ Novo contrato` em `Contracts/Index.cshtml:17-19` (+ duplicado no estado vazio l.194) — será homologado, não reescrito.
- Breakpoints existentes cobrem 360–1279 px por módulo; falta a trilha formal de verificação nessas três larguras canônicas.
- Semente demo atual tem 1 organização estável (TC `20000000-…-001`, perfil `general`, plano BASIC) e perfis `tenant-administrator` / `tenant-operator` / `tenant-client` + SuperAdministrator da plataforma. As duas organizações da homologação entram como fixtures determinísticos da suíte (o instalador de modelos oficiais é install-if-absent: capa/sumário/callout só existem em instalações novas, que só as fixtures fazem).

## Blocos de trabalho

### D.1 — Consolidação da identidade visual (tela)

1. `site.css`: fundo global passa ao branco-gelo da SPEC (`#F4F7FB`); todo hexo avulso remanescente é substituído pelo token correspondente (varredura completa do arquivo); foco visível unificado com o token de ação sobre fundo de alto contraste.
2. Novo token `--field:#FFF3BF` (amarelo de campos da SPEC) aplicado ao fundo dos campos pendentes do Studio (inputs com valor ausente/pendente), mantendo contraste AA do texto.
3. Componente **.caixa de atenção**: `.callout` (variante `attention` com faixa esquerda verde/navy conforme contexto) para telas; usado no banner de aprovação ODCA pendente e nos bloqueios de módulo/plano (mesma classe nos pontos onde hoje há `.notice` de advertência estrutural).
4. Lista de contratos: as classes inertes `table-responsive`/`table` dão lugar ao padrão real do produto (`.responsive-table` com `data-label` nos células) — tabela legível em 360 px sem scroll horizontal perdido.

### D.2 — Documento: capa, sumário e "sem variáveis não resolvidas"

1. `ContractDocumentRenderer` ganha **capa** (nome/CNPJ da organização, título, nº da versão, data de emissão, partes quando houver campos de identificação) e **sumário** (lista dos títulos h1/h2 com ponteiro visual) quando o documento é "adequado": ≥ 2 seções `heading[level=1]` ou presença de `pageBreak`. Documentos simples (termos curtos) permanecem sem capa/sumário.
2. Metadados da organização chegam ao renderer pelo `ContractStudioController` (já resolve a ficha da organização para outros fins); o mesmo HTML alimenta `Print.cshtml` (capa/sumário somem da impressão apenas via CSS quando o usuário imprime seleção?) — decisão: capa e sumário **são parte do documento impresso** (D2).
3. Suporte ao nó **`callout`** (nível bloco, variantes `attention|info`, texto livre) em `StructuredContractDocument.Parse` + render HTML (`<aside class="document-callout">` com rótulo "ATENÇÃO:") e no `SimplePdf`; o modelo oficial cirúrgico (`surgical-consent`) converte o parágrafo de riscos em caixa de atenção (demais modelos preservados byte-a-byte exceto esta conversão).
4. Garantia de variáveis: asserções de que a versão final gerada (pdf_status='generated') não contém "Não informado"/`document-field-empty` no HTML e que rascunho com campo vazio exibe a marca cinza honesta (comportamento atual mantido).

### D.3 — Homologação responsiva e de acessibilidade

Trilha formal com navegador (evidências PNG nomeadas em pasta externa de trabalho, convenção dos incrementes A–C):

| Tela | 1440 | 768 | 360 |
| --- | --- | --- | --- |
| Login · Visão geral · Lista de contratos (CTA + empty state) · Ficha do contrato · Studio (editor + banner) · Renovações (central + detalhe) · Solicitações (portal cliente) · Central ODCA (fila + detalhes + aprovações) · Impressão da versão | ● | ● | ● |

+ **Teclado/foco**: trilha Tab completa em login, form de proposta de renovação e editor do Studio com foco sempre visível (anel `--action-*`, nunca removido sem substituto); **contraste**: verificação calculada (WCAG AA) dos pares reais em uso (texto sobre fundo, branco sobre navy, verde destaque sobre branco e sobre navy, amarelo-campo com texto) registrada na suíte.

### D.4 — Matriz ações×perfil×plano (documento + prova)

Matriz consolidada em `docs/execution/MATRIZ_ACOES_PERFIL_PLANO.md` (ação × {tenant-administrator, tenant-operator, tenant-client, SuperAdministrator} × {BASIC, ENTERPRISE}), cobrindo ao mínimo: criar minuta cirúrgica, encaminhar assinatura, renovar (propor/submeter/formalizar/aplicar/cancelar), aprovar modelo ODCA, solicitações (abrir/responder/encerrar × triar/pausar/resolver), configuração de lembretes e gestão organizacional. Cada linha cita o assert da suíte ou evidência UI que a prova.

### D.5 — Ambiente da homologação (2 organizações + 4 perfis)

Fixture determinístico criado/destruído pela própria suíte (`Val D:` / UUIDs `20000000-0000-4000-8000-0000000000D1/D2`): **org1 "Val D: Centro Terapeutico Aurora"** (`activity_profile=therapy_clinic`, plano BASIC — cenários 2–6, 10–13, 15–16) e **org2 "Val D: Clinica Cirurgica Harmonia"** (`activity_profile=plastic_surgery`, assinatura Enterprise ativa — cenários 7–9, 14, 17–18). Em ambas, `cliente.teste@odca.local` é tenant-administrator e `operador@odca.local` é tenant-operator; os papéis copiam as permissões medidas do tenant demo. O TC (general/BASIC) permanece para o cenário 1 (menu/perfil) e para a prova de isolamento web; o overlay de terapia vem da org1 fixture. Perfis em prova: admin de tenant, operator, client e o ator de plataforma (operador promovido + sessão nível-MFA, mesmo padrão da Seção C). Cleanup devolve seeds (plano TC, promoções, MFA, fixtures e modelos instalados removidos).

## TC — os 19 cenários mínimos (assert em UI + API + banco)

| # | Org · Perfil | Cenário | Assert |
| --- | --- | --- | --- |
| 1 | org1 · client | Login + menu por permissão | sessão OK; menu só com itens permitidos; `/administracao` → acesso negado |
| 2 | org1 · admin | Criar minuta do modelo oficial `multiple-therapies` | wizard → Studio abre com overlay terapia (cláusula 2ª-A presente) |
| 3 | org1 · admin | Preencher e salvar minuta válida (CPF válido, moeda pt-BR) | 200 + valores canônicos no banco; `monthly_fee_total` recalculado |
| 4 | org1 · admin | Campo obrigatório vazio + CPF inválido | 400 apontando os campos; UI exibe mensagem junto ao campo |
| 5 | org1 · admin | Gerar versão da minuta completa | 201/gerado · PDF `generated` · HTML da versão **sem** "Não informado" e **com** capa/sumário quando adequado |
| 6 | org1 · admin | Minuta cirúrgica fora do perfil | 409 `draft.profile.surgical_required` (controle negativo do gate) |
| 7 | org2 · admin | Minuta cirúrgica no perfil Enterprise | 201 · banner `data-pending-odca-approval` · readiness com blocker + `canConfirm=false` |
| 8 | org2 · plataforma | Aprovar `surgical-consent` | decisão gravada (decisor/data) · readiness sem blocker · preparação confirma |
| 9 | org2 · plataforma | Reverter p/ reprovado com nota | blocker volta; nota ≥ 5 caracteres obrigatória (400 `approvals.validation_note`) |
| 10 | org1 · admin | Concorrência de abas no Studio | PUT obsoleto → 409 + conteúdo local preservado (banner de conflito) |
| 11 | org1 · admin | Preparação de assinatura completa | readiness `canConfirm=true` → confirmação cria preparação com signatários |
| 12 | org1 · admin | Proposta de renovação crítica na ficha | 201 draft · badge "crítica" na central · datas atuais×propostas no banco |
| 13 | org1 · admin | Submeter → formalizar (evidência+jus+) → aplicar | estados/eventos corretos · contrato com nova vigência/valor · `version+1` · re-aplicar → 409 |
| 14 | org2 · admin | Contrato indeterminado → proposta sem fim | "Prazo indeterminado" na central · aplicar mantém `end_date NULL` |
| 15 | org1 · admin | Lembretes 45/20/5 salvos na central | persistido em `tenants` + janela refletida; valor inválido → 400 |
| 16 | org1 · operator | Operador propõe mas não decide | lista 200 · propor 201 · submeter/aplicar 403 (API) e `/acesso-negado` (Web) · zero mutações |
| 17 | org2 · platform+client | Solicitação Enterprise completa | abrir (gate Enterprise OK) → triagem → iniciar → resolver (plataforma) → encerrar (cliente) · auditoria completa |
| 18 | org2 · platform | Pausa deslocando prazos + retorno do cliente | pausa sem justificativa 400 · pausa justa desloca dues · msg do cliente auto-retoma · resposta ODCA na thread |
| 19 | ambas · admin | Isolamento entre organizações | listas/detalhes/solicitações/propostas da org2 invisíveis no contexto da org1 (rota cruzada → indisponível/404) e vice-versa |

**Regressão obrigatória após D.1/D.2**: `validate-b34-web.ps1` (T4 contagem/instalação de modelos e T5 estrutura cirúrgica podem sentir a conversão do parágrafo de riscos → ajustar asserts de conteúdo apenas onde o nó mudou) e `validate-c-web.ps1` (S16/S17 leem o modelo oficial) — ambas ALL PASS antes do fechamento.

## Decisões (registro)

- **D1 (registrada)** — Tokens da SPEC §8 são a fonte única da paleta em tela; hexos avulsos viram referência a token. Fundo global `#F4F7FB`; campo pendente usa `#FFF3BF`; foco usa o anel de ação com AA. Verde de destaque da marca (`--success` atual) é o verde das métricas positivas e do selo de aprovado.
- **D2 (registrada)** — Capa e sumário fazem parte do documento (tela, impressão e PDF), gerados pelo renderer quando o documento tem ≥ 2 seções de nível 1 ou quebras de página; documentos curtos ficam sem eles ("quando adequado"). Numeração de cláusulas continua literal nos modelos (não duplicar).
- **D3 (registrada)** — Caixa de atenção é nó de documento próprio (`callout`) e componente de tela (`.callout`); a conversão do parágrafo de riscos do `surgical-consent` é a única mudança de conteúdo em modelos oficiais neste incremento.
- **D4 (registrada)** — O documento final jamais pode conter variável não resolvida: a geração em modo `Complete` continua sendo a barreira (422), e a homologação prova que a saída gerada não contém a marca "Não informado".
- **D5 (registrada)** — A segunda organização da homologação é fixture determinístico da suíte (não seed permanente), removida no cleanup com as assinaturas/promovimentos restaurados — o banco disposable permanece reutilizável sem estado residual.
