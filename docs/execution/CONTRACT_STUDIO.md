# Biblioteca e Estúdio de Contratos

## Fluxo entregue

1. Um usuário com `tenant.contract_drafts.manage` cria uma minuta a partir de uma versão publicada de modelo. Conteúdo, definições e valores usam o JSON estruturado canônico; HTML arbitrário, scripts, URLs e marcas desconhecidas são rejeitados.
2. O editor salva manualmente ou 900 ms após a última alteração. Cada tentativa leva a versão esperada e um UUID de revisão cliente. O servidor usa concorrência otimista e um recibo idempotente, portanto repetir a mesma tentativa não incrementa a versão duas vezes.
3. Em conflito, o conteúdo aberto não é substituído. A interface oferece abrir o estado do servidor em outra aba; a recuperação/mesclagem é uma decisão explícita do usuário.
4. O histórico lista retratos imutáveis. A comparação só aceita duas versões do mesmo contrato e separa texto, formatação suportada, valores de campos e metadados. O limite síncrono é 2 MB por componente e 10.000 diferenças; documentos maiores devem seguir o worker de processamento antes de esta operação ser habilitada.
5. Comentários contextuais são vinculados à minuta, à revisão numérica e, quando informado, à versão imutável. Respostas usam `parent_id`; resolução e reabertura geram eventos e não alteram aprovação. A referência estável é preservada com `reference_located=false` quando o trecho deixa de existir.
6. O checklist do servidor distingue bloqueios de avisos. Documento não salvo/conflitante e campos obrigatórios inválidos bloqueiam; comentários abertos e valores manuais avisam. Ele é uma validação operacional, não um parecer jurídico.
7. Ao encaminhar, o BFF confirma o checklist, exige um revisor autorizado, cria/reutiliza pelo UUID idempotente o retrato da versão exata da minuta e vincula esse retrato à revisão. A constraint e o estado da versão impedem duplo envio e aprovação herdada por conteúdo posterior.

## Persistência e segurança

A migration 016 adiciona recibos de salvamento, identidade idempotente e versão da minuta nos retratos, comentários contextuais, eventos, FKs compostas, índices e RLS. Todos os endpoints validam permissão e tenant no servidor e configuram `odca.tenant_id` e `odca.actor_id` na transação. Eventos guardam identificadores e resultados, nunca o texto completo do contrato. Versões geradas e submetidas não oferecem edição nem exclusão.

O bearer permanece somente no BFF e requisições mutáveis da interface mantêm o antiforgery token. O conteúdo completo não é colocado em `localStorage`.

## Estados

- Minuta: `Alterações não salvas` → `Salvando` → `Salvo`; falhas viram `Falha` e divergência vira `Conflito`.
- Versão: `generated` → `submitted` → `internally_approved` → `externally_signed` (transições posteriores continuam no fluxo de revisão/assinatura existente).
- Revisão: `in_review`, `changes_requested`, `internally_approved`, `cancelled` ou `superseded`.
- Comentário: aberto, resolvido, reaberto ou logicamente excluído; resolver nunca aprova uma etapa.

## Homologação de salvamento de minuta (12 linhas · 12/10/2026)

Executada contra a aplicação real (API `:7143` + Web `:5144`, banco `odca_test_disposable`), com sessões MFA de admin (`ODCA-QA-REV9`), operador (`ODCA-QA-REV10`) e web (`ODCA-QA-REV11`). Fixture única: minuta `46665c7b-c8c4-4ee9-9552-87b7a4687318` criada por `POST studio/drafts` sobre o modelo global `nda-mutual` (tenant 0001). A linha 11 usa SQL apenas como fixture reversível (remover/recolocar o papel do operador em `odca.member_roles`); a prova de cada comportamento é resposta HTTP, renderização da página ou estado do banco. Versões percorridas: 1 → 2 (linha 2) → 3 (linha 8) → 4 (vencedora da linha 10); estado final: `row_version=4`, 3 recibos, 3 eventos `draft.saved`.

| # | Cenário | Comportamento homologado | Evidência |
|---|---------|--------------------------|-----------|
| 1 | Carga inicial — "Salvo" verdadeiro | A barra inicial reflete o servidor, não memória: versão e carimbo vêm da linha persistida | Web GET da edição → 200 com `data-version="4"` (= `row_version`), `data-state="saved"`, `<small data-saved-at>` igual ao `updated_at` renderizado em horário local (`12/10/2026 16:20`); `Edit.cshtml:26,30` |
| 2 | Sujo → salvando → salvo | Edição marca sujo; PUT com `expectedVersion` + `clientRevision` (UUID novo por tentativa) grava os valores e avança a versão exatamente uma vez | `studio.js:156-157,462-465`; PUT → 200 `{version:2,savedAt}`; SQL: `row_version=2`, valores persistidos, `last_client_revision=r1`, `draft_save_receipts`=1, evento `draft.saved`=1 |
| 3 | Replay idempotente (retry de rede) | Repetir a mesma `clientRevision` devolve a resposta original persistida sem gravar de novo | Segundo PUT idêntico → 200 com o **mesmo** `savedAt` e `version=2`; SQL: continua `row_version=2`, recibos=1, eventos=1 (`ContractStudioController.Save`, replay por recibo) |
| 4 | Replay com corpo divergente | O recibo do servidor vence: mesma `clientRevision` com corpo diferente não substitui o que foi salvo | PUT com `clientRevision=r1` e outro valor → 200 com o `savedAt` original; SQL: o valor da linha 2 permanece intacto (o corpo divergente não persistiu) |
| 5 | Falha de validação de tipo | Valor inválido rejeitado com mensagem por campo; nada muda no banco | `effective_date="12/10/2026"` → 400 `O valor de 'Início' é inválido.`; SQL sem alteração de `row_version`/valores; UI: estado `failed` com a mensagem (`studio.js:469-471`) |
| 6 | Falha de validação estrutural | Campo definido sem ocorrência no documento é rejeitado antes de gravar | Campo fantasma adicionado às definições → 400 `Todo campo definido precisa ter ao menos uma ocorrência no documento.`; banco inalterado |
| 7 | Conflito concorrente | `expectedVersion` defasada → 409 com `currentVersion` do servidor; conteúdo local preservado, banner com consulta e repetição | PUT `expectedVersion=1` (atual 2) → 409 `{currentVersion:2}`; banco mantém a versão 2; UI: estado `conflict` + banner exibido (`studio.js:468`, `Edit.cshtml:70`) |
| 8 | Recuperar e repetir | Após o conflito, sincronizar com o servidor e salvar novamente fecha o loop | GET → `version=2`; PUT `expectedVersion=2` + nova revisão → 200 `version=3` |
| 9 | Respostas fora de ordem — sem falso "Salvo" | "Salvo" só quando a revisão respondida é a revisão enviada; resposta atrasada devolve ao estado sujo e re-salva; single-flight impede PUTs simultâneos | `studio.js:473-475` (`localRevision===sentRevision`; senão dirty + re-save em 150 ms), `430` (single-flight), `461` (estado limpo não dispara); camada de servidor: retry da mesma revisão = replay (linha 3), revisão diferente defasada = 409 (linha 7) — nenhuma duplicata aplica duas vezes |
| 10 | Duplo submit (corrida) | Dois PUT simultâneos com a mesma versão esperada: exatamente um aplica | Dois `curl` concorrentes (revisões distintas, `expectedVersion=3`) → **200/409**; SQL: `row_version` 3→4 (um único incremento), recibos 2→3, eventos 2→3; vencedor = revisão do corpo A (`last_client_revision` confere) |
| 11 | Permissão e isolamento | Sem papel habilitado, o servidor nega por requisição (checagem no banco, não em claim de token); isolamento por tenant via RLS | Removido o papel `tenant-operator` (fixture SQL reversível) → GET **403** e PUT **403**; linha exata recolocada → GET 200; draft inexistente → 404; URL de outro tenant para o mesmo draft → 404 |
| 12 | Verdade após reload | Depois de todas as gravações, a página reaberta mostra o estado persistido (sem cache local) | Web GET pós-salvamentos → `data-version="4"` = `row_version`, "Salvo" + carimbo do servidor = `updated_at`; `GET drafts/{id}` devolve a revisão vencedora como última |

## Limitações objetivas

A versão privada persistida nesta etapa é o snapshot canônico JSON com SHA-256. Conversão PDF paginada e processamento assíncrono para comparações acima do limite ainda dependem do worker/conversor homologado. A interface não implementa menções ou notificações externas. Referências removidas são preservadas pelo modelo de dados; a atualização automática de `reference_located` deverá ser feita pelo processamento de estrutura quando ele for conectado.

## Documento final (migration 026)

O estado atual da leitura, PDF, revisão e preparação de participantes está documentado em [FINAL_DOCUMENT_JOURNEY.md](FINAL_DOCUMENT_JOURNEY.md). O PDF e o HTML são derivados do JSON canônico; integração de assinatura continua fora do produto.
