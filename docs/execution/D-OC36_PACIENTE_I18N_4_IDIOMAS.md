# Sprint 2 — área de paciente em 4 idiomas + provas ao vivo (D-OC36) (17/10/2026)

Plano de seis sprints para candidata ao piloto (estreia 11/01/2027). Esta entrega fecha a Sprint 2.

## 1. Escopo e base

- **Base**: `codex/s00-foundation` @ `158a907` (Sprint 1 / D-OC35). Sem migração nova neste incremento (zero mudança de contrato de dados).
- **F-1 (correção P1)**: i18n em 4 idiomas (pt-BR/en-US/es-ES/fr-FR) da área de paciente — listagem, formulário de cadastro/edição e ficha — mais o bloco de **cadastro rápido** na página de novo documento e as mensagens de paciente do `DocumentsController`.
- **A-1 (avanço)**: prova ao vivo reproduzível sobre base descartável + capturas CDP das três telas de paciente nos 4 idiomas.
- **Higiene documental**: remapeio da seção obsoleta "Estúdio de contratos" do `BACKLOG.md` (itens entregues em incrementos posteriores estavam marcados abertos) e registro do item pendente da área Documentos.
- **Fora de escopo registrado**: o restante da área **Documentos** permanece **pt-first** — `Documents/Index` e o corpo de `NewDocument` têm dezenas de literais não relacionados a paciente; só o bloco de cadastro rápido e as mensagens de paciente entraram neste incremento (item pendente no BACKLOG).

## 2. GATE — execução única sobre a árvore final

Raciocínio registrado: a base da Sprint 2 (`158a907`) foi verificada na mesma sessão ao fechar a Sprint 1 (builds estritos 0W/0E, Domain 364/364, suíte `-FreshDb` 202/202). Todas as mudanças da S2 são **aditivas** sobre a superfície (novas chaves de catálogo, tradução de rótulos existentes, sem mudança de contrato de dados nem de migração); portanto o GATE rodou **uma única vez sobre a árvore final** com todas as mudanças aplicadas — um pré-GATE redundante re-verificaria exatamente o mesmo baseline já comprovado na sessão.

Resultados (log mestre `%TEMP%\opencode\d35-gate.log`; sublogs `%TEMP%\d35-gate-{release-strict,debug-strict,domain-tests,assets}.log` e `%TEMP%\odca-integration-tests.log`):

| Etapa | Resultado |
|---|---|
| Build Release estrito (`-warnaserror`) | exit=0 · **0 avisos / 0 erros** |
| Build Debug estrito (`-warnaserror`) | exit=0 · **0 avisos / 0 erros** |
| `Odca.Domain.Tests` | **364/364 PASS** (0 falhas) |
| `scripts/check-assets.mjs` | exit=0 (10 assets + 19 chaves i18n dos módulos JS × 4 catálogos) |
| Suíte de integração `-FreshDb` | **202/202 PASS** (FRESH-DB-OK → BUILD-OK → TEST-EXIT=0), incluindo paridade exata dos 4 catálogos com as 123 chaves `patient.*` novas |

## 3. Mudanças

### Catálogos

- **+123 chaves `patient.*` por cultura** (`src/Odca.Configuration/Localization/{pt-BR,en-US,fr-FR,es-ES}.json`) — total **1533 chaves/cultura**, paridade exata 4/4, BOM/indent-2/LF preservados pela injeção via script.
- **Reuso de chaves existentes antes de criar novas**: `pacientes` (H1/navegação — a nav global já resolve rótulos por `ctx.Localizer?.Invoke(item.Key) ?? item.Label`), `common.how_to_use`, `common.breadcrumb_aria`, `action.cancel`, `action.filter`, `inbox.prev`/`inbox.next`.

### Código

| Arquivo | Mudança |
|---|---|
| `src/Odca.Web/Views/Patients/Index.cshtml` | listagem integral em `L.T(key,_culture)` (colunas, badges, filtros, paginação, estado vazio); datas via `CulturedTime` |
| `src/Odca.Web/Views/Patients/Form.cshtml` | rótulos/dicas/resumo de validação pelo catálogo; bloco de representante |
| `src/Odca.Web/Views/Patients/Details.cshtml` | seções (resumo, dados de cadastro, acervo documental, situação) + banners; datas `CulturedTime` |
| `src/Odca.Web/Controllers/PatientsController.cs` | propriedade-shadow `Culture` (padrão canônico abaixo) + `L()`; todos os `ViewData["Title"]`, TempData e fallbacks via catálogo; validação manual `ValidatePatientForm` |
| `src/Odca.Web/Controllers/DocumentsController.cs` | 8 mensagens de paciente via `L()` (ficha/quick-registration/inativo) |
| `src/Odca.Web/Views/Documents/NewDocument.cshtml` | bloco "Cadastro rápido" (labels + dica + submit) no catálogo + `_culture` |
| `src/Odca.Web/Models/PatientViewModels.cs` | removidos `[Required]`/`[EmailAddress]`/`[StringLength(160,Min=2)]` de `FullName`/`Email` — evita mensagens padrão em inglês duplicando o resumo (validação agora server-side pelo catálogo) |
| `src/Odca.Web/Common/CulturedTime.cs` | novo overload `Date(DateOnly value, string? culture)` |
| `scripts/qa/cdp-capture-v2.mjs` | sessões eagerness (`cliente`/`operador`) viraram opcionais quando ausentes de `cfg.creds` (comportamento idêntico quando presentes — compatível com o uso do D-OC34); sessões não declaradas logam sob demanda no retry existente |

### Padrão de cultura (descoberta aplicada)

- `Controller.Culture` **não está populado** nesta aplicação — o padrão canônico é declarar uma propriedade privada sombreada lendo `HttpContext.Items[Odca.Web.Middleware.CultureMiddleware.ItemsKey]` (precedente `StudioController:18`), compilando limpo com `-warnaserror`.
- `LocalizationService.T` recebe `params string[]`: valores numéricos de view model exigem `.ToString()` explícito.
- Troca de idioma segue o caminho canônico do cookie **`odca_lang`** (`CultureMiddleware`); views leem a mesma `ItemsKey`.

## 4. Prova ao vivo (A-1)

- Script reproduzível: **`scripts/qa/validate-d35-patient-i18n-web.ps1`** — base descartável `odca_test_disposable`, api `:5143` + web `:5144` (worker não exigido por essas telas), purga determinística das linhas do tenant + reinsert do fixture de paciente (dados sintéticos, sem segredos), marcador `PROVA-FIM fails=N` em arquivo de evidência.
- Sessão: `cliente.teste@odca.local` (papel `tenant-client`, permissões `patients.read/manage` conferidas no banco descartável, **sem MFA** — login simples).
- **Rodada final fails=0 — 27 checks** (transcript `evidence/d35-proof-rodada-final.txt`, trace `evidence/d35-trace-rodada-final.log`; o transcript também preserva a convergência das rodadas anteriores: fails 7 → 5 → 5 → 0):
  - índice nos 4 idiomas (título/CTA/linha ativa; asserções negativas: "Central de pacientes" ausente em en/es/fr);
  - formulário em en-US/es-ES/fr-FR (rótulos de identificador/contatos/representante) + regressão pt-BR;
  - ficha em en-US com acervo vazio e **data localizada renderizada de fato** — `10/17/2026` (en `M/d/yyyy`) vs `17/10/2026` (pt `dd/MM/yyyy`), não ambígua pois dia ≠ mês;
  - **criação real via POST web em en-US** → ficha com alerta de sucesso + persistência conferida em banco (count=1);
  - validação server-side com nome vazio em en-US → mensagem do catálogo renderizada, mensagem padrão pt-BR ausente;
  - **cadastro rápido** no novo documento em en-US → 302 com `patientId` + alerta de sucesso;
  - ciclo inativar/restaurar em en-US, incluindo a regra "paciente inativo não pode ser selecionado para novas emissões";
  - estado final determinístico (3 pacientes ativos com os nomes esperados).
- **Classificação das falhas das rodadas 1–3 (todas de harness; zero defeito de produto)**:
  1. CPF sintético com dígito verificador inválido (`22255777710`) → 400 da API mapeado corretamente de volta ao formulário (o comportamento era o produto funcionando — `PatientRules` valida dígitos); fixture substituído pelo válido `22255777738`. O CPF do fixture histórico `11144777735` foi inserido via SQL (não revalida dígito) e permanece íntegro.
  2. Regex do harness capturava o **primeiro** GUID da `Location` do redirect (o do tenant) em vez do do paciente → 404 vazios numa URL errada; âncoras corrigidas (`/pacientes/{guid}$` e `patientId=`).
  3. `.ps1` UTF-8 sem BOM corrompia literais acentuadas sob PowerShell 5.1 (lidas como ANSI) — literal sem acento adotado + BOM no arquivo.
  4. `Location` relativo do redirect consumido sem prefixo `$base` no GET subsequente.
  - Observação de produto registrada (sem mudança neste escopo): `NotFound()` na ficha devolve corpo vazio (404 sem página) — comportamento pré-existente.

## 5. Capturas em navegador (CDP)

- `scripts/qa/capture-d35-patient-screens.ps1` + driver `scripts/qa/cdp-capture-v2.mjs`: **3 telas** (listagem / novo / ficha) × **4 idiomas** × 360/768/1280/1440px + zoom 200% @1280 = **`CAPTURES-DONE ok=60 fail=0`** (transcript `evidence/d35-captures-rodada-final.txt`).
- PNGs (≈6,7 MB) em `%TEMP%\opencode\evidence\captures-d35\`, fora do repositório por tamanho (convenção das capturas anteriores); disponíveis para revisão humana.
- Sessão cliente (sem MFA); a melhoria do driver tornou as sessões eagerness opcionais (§3).

## 6. Separação dos tipos de evidência

- **Teste local**: builds estritos, Domain 364/364, assets, suíte `-FreshDb` 202/202 (§2).
- **Prova ao vivo**: §4 (script reproduzível em um comando).
- **Prova em navegador**: §5 (60 capturas para revisão).
- **CI oficial**: **não verificada** — branch sem push oficial; nenhum ID de execução inventado (vale para o SHA da S1 e para o SHA deste incremento).
- **Aceite humano**: pendente — nenhum responsável ou aprovação inventada.
- **Portal**: `.odca/progress.json` **inalterado** (baseline 40%, IDs/pesos imutáveis; crédito de marco exige CI oficial do SHA funcional + aceite real pertinentes).

## 7. Riscos e próximo passo

- Item pendente registrado: i18n do restante da área Documentos (hoje pt-first).
- Sprint 3 (assinatura) inicia-se sobre este commit; bloqueio externo conhecido: **fornecedor de envio externo indefinido** — decisão registrada no DECISIONS-LOG (envelope externo "proposto, não escolhido" na estreia; modalidades comprovadas session/evidence preservadas).
- Push da branch quando solicitado (passa a verificar a CI oficial dos SHAs).
