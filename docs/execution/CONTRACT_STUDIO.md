# Biblioteca e estúdio de contratos — estado da entrega

## Implementado

- O login, MFA, seleção de organização, permissões e RLS continuam sendo a base autorizadora existente; esta evolução não altera provisionamento nem `development-runtime.json`.
- A migration 015 adiciona modelos particulares, de consultoria e globais, versões imutáveis, concessão revogável, minutas copiadas da versão de origem, valores estruturados, versões documentais e emissões rastreáveis. Todas as entidades operacionais carregam o tenant e minutas usam concorrência otimista.
- A API oferece catálogo paginado, criação/edição concorrente/publicação/duplicação/arquivamento de modelo, criação/reabertura/autosave de minuta, geração de snapshot imutável e submissão da versão exata à revisão interna.
- O estúdio BFF mantém o bearer token no servidor. A tela responsiva renderiza o JSON canônico em blocos, atualiza todas as ocorrências pela chave estável, confirma valores, destaca pendências e coordena autosave sem gravações sobrepostas. O conteúdo integral não é colocado em `localStorage`.
- O formato canônico é JSON versão 1 e aceita títulos, parágrafos, listas, tabelas, alinhamento, texto com negrito/itálico/sublinhado e campos. HTML, scripts, eventos e URLs não fazem parte do schema aceito. Não há promessa de fidelidade com Microsoft Word.

## Parcial

- A geração cria o snapshot canônico imutável, hash SHA-256 e arquivo privado; o catálogo visual ainda não oferece administração de modelos, embora os endpoints persistentes existam.
- Upload, antivírus, extração PDF/imagem/DOCX e revisão existentes foram preservados. Sugestões de OCR ainda não aparecem no painel contextual do novo estúdio.
- A revisão é criada para uma versão gerada específica; as telas de decisão da revisão permanecem separadas. Aprovação interna não é tratada como assinatura digital.

## Pendente e dependências externas

- Conversão PDF paginada, cópia emitida com serial/rodapé, consulta/download de emissão e processamento em job ainda dependem da escolha e homologação de um conversor comercialmente compatível. Nenhuma emissão é apresentada como prova de impressão física.
- Reserva/liberação de cota para artefatos gerados e idempotência física após falha precisam ser conectadas ao worker antes de habilitar emissão em produção.
- Faltam comparação visual de conflitos, sugestões cadastrais/OCR, prévia completa e administração visual de versões/acessos de modelos.
- QA autenticado em PostgreSQL e navegador (360/768/desktop), inspeção de PDF, acessibilidade assistiva e testes de concorrência/cota dependem do ambiente executável. Auditoria e exclusão lógica são controles, não uma declaração de adequação integral à LGPD.
