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

## Limitações objetivas

A versão privada persistida nesta etapa é o snapshot canônico JSON com SHA-256. Conversão PDF paginada e processamento assíncrono para comparações acima do limite ainda dependem do worker/conversor homologado. A interface não implementa menções ou notificações externas. Referências removidas são preservadas pelo modelo de dados; a atualização automática de `reference_located` deverá ser feita pelo processamento de estrutura quando ele for conectado.

## Documento final (migration 026)

O estado atual da leitura, PDF, revisão e preparação de participantes está documentado em [FINAL_DOCUMENT_JOURNEY.md](FINAL_DOCUMENT_JOURNEY.md). O PDF e o HTML são derivados do JSON canônico; integração de assinatura continua fora do produto.
