# Central documental, colaboração e consumo

Baseline conferido: `426ff891e849af0fc03b3315d36774e4b2584a30` (`ajuste`), árvore limpa antes deste incremento. A administração hierárquica da migration 034, os perfis padrão, o catálogo de planos com sete módulos e os limites quantitativos publicados foram preservados. Nenhuma migration 001–034 foi reescrita.

## Preservado

- UUIDs, histórico, RLS, checksums aplicados e versionamento documental.
- Bypass de autorização só do papel `tenant-administrator` dentro de `tenant_actor_has_permission`. Administrador delegado, coordenador e usuário continuam limitados às permissões gravadas.
- Jornada do Studio já corrigida: minuta, conferência, snapshot do paciente, PDF e preparação interna de assinatura.
- Listagem canônica em `GET .../studio/documents` e ficha do paciente. Não foi criada outra central.
- Estados de revisão `in_review`, `changes_requested`, `internally_approved`, `cancelled` e `superseded`.
- Armazenamento continua medido por `storage_usage`. Franquia mensal não gera pagamento nem fatura.

## Evoluído

- Migration 035: período mensal em `America/Sao_Paulo`; `consume_monthly_franchise` com trava de concorrência e chave de idempotência; saldo usado no estado do módulo; varredura e extração não reivindicam organização suspensa; saldos agregados para a MNSOFT.
- OCR: o worker consome páginas só depois de extração com método `ocr` e na mesma transação do resultado. Texto sem separador conta 1 página; separador de formulário conta as páginas produzidas. PDF nativo não conta. Repetição da mesma chave não conta de novo. Saldo insuficiente não grava sugestão.
- Assinatura: a função aceita envelope, mas nenhum envio a chama. Não há provedor nem credencial no repositório.
- Central: filtros de referência, responsável por nome, período, paciente, tipo e estado, inclusive ajustes solicitados. A próxima ação some quando a permissão ou o módulo de assinatura não permitem a operação.
- Revisão: cancelamento canônico com versão esperada, justificativa e idempotência. Não reabre a versão nem a aprova. Botões de decidir, reatribuir e cancelar seguem a permissão gravada.
- Paciente inativo: o acervo permanece; a ficha não oferece novo documento. Representante não é contratante, responsável financeiro nem signatário.
- Importação suspensa ou fora do plano volta para a fila sem queimar a tentativa. Franquia esgotada encerra o trabalho com diagnóstico e sem retry automático.

## Regras de consumo

| Franquia | Unidade | Evento que consome | O que não consome |
|---|---|---|---|
| `ocr_pages_monthly` | página | Extração concluída com método `ocr` | Upload, falha, PDF nativo, tentativa repetida da mesma chave |
| `signature_envelopes_monthly` | envelope | Aceite de envio por provedor, quando existir | Preparação, confirmação de composição, tentativa sem resposta |

O módulo de importações usa a franquia de OCR como limite quantitativo já definido em `organization_feature_state`. Com o saldo mensal esgotado, o estado passa a `quota_exhausted` e novos trabalhos de importação deixam de ser reivindicados. Não há reserva em aberto: o consumo é confirmado na mesma transação do resultado. Não existe regra documentada para cobrar tentativa.

## Assinatura

Não implementado o envio real. Dependência exata: não há `ISignatureProvider`, configuração de provedor nem sandbox no código. A tela da versão informa “Não integrada”. Confirmar a composição não altera o documento emitido e não grava `signature_credit`.

## Evidência

Executado neste ciclo, com banco descartável novo `odca_test_central_035`:

- `dotnet restore Odca.sln --locked-mode` atualizado.
- `dotnet build Odca.sln --configuration Release -m:1 -nodeReuse:false` sem avisos e sem erros.
- `npm run build` validou 10 assets e os percursos decimal e de modelo.
- `dotnet test Odca.sln --configuration Release --no-build`: Domain.Tests 298 aprovados; IntegrationTests 162 aprovados, incluindo `FranchiseMeteringTests` e o checksum da migration 035.

`FranchiseMeteringTests` comprovou: 300 páginas consumidas uma vez; a mesma chave devolve `duplicate`; a página seguinte devolve `exhausted` e o módulo de importação fica `quota_exhausted`; envelope conta uma vez; organização suspensa com franquia disponível não é escolhida pela varredura, e a versão dela permanece pendente. A transação do teste foi desfeita.

Não houve navegação em navegador. As ferramentas de browser não estão conectadas. Larguras 360, 768 e 1440 e a jornada visual paciente → minuta → revisão com ajustes → PDF não foram reabertas nesta entrega. O endpoint novo de cancelamento de revisão não tem teste HTTP dedicado; a suíte de revisão já existente passou. Nada foi publicado nem integrado.

## Pendências de implantação

- Aplicar a migration 035 no ambiente de destino com o migrador. Não editar o histórico anterior.
- Provedor de assinatura, credencial e endpoint autenticado de eventos continuam ausentes. Sem isso, envelope não é enviado nem consumido.
- Decisão comercial ainda em aberto: não há pacote adicional de páginas ou envelopes além do limite da versão de plano contratada. Concessão manual existente continua só para armazenamento.
- Acoplar o consumo de envelope ao aceite idempotente do provedor, distinguindo timeout sem envio de resultado externo desconhecido, antes de qualquer reenvio.
