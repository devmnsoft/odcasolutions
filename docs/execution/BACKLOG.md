# Backlog

## Após vistas pessoais v014

- [x] Remover a ambiguidade entre entrypoints da API e Bootstrap sem retirar referências necessárias.
- [x] Persistir e reaplicar vistas pessoais de obrigações com allowlist, RLS por tenant/proprietário e revalidação das referências.
- [x] Conectar renomear, definir padrão e inativar aos controles do BFF com versão otimista.
- [ ] Reutilizar a infraestrutura de vistas nas listagens HTTP de revisões e contratos quando essas jornadas forem conectadas.
- [ ] Executar v014 em PostgreSQL descartável com role restrita e provar que proprietário/tenant diferentes recebem zero linhas.
- [ ] Concluir ficha operacional do contrato e agenda mensal sem duplicar projeções ou carregar catálogos integrais no navegador.

## Após integridade das ações de obrigações

- [ ] Conectar criação e ações de obrigação ao BFF com seletores por nome para contrato, responsável e documento, mantendo os dados após erro recuperável.
- [ ] Entregar agenda mensal consultando apenas a janela visível e reutilizando exatamente os filtros/autorização da lista.
- [ ] Implementar alteração de série com escopo “esta ocorrência” e “esta e futuras”, sem modificar ocorrências encerradas.
- [ ] Consolidar indicadores de obrigações, revisões e renovações no dashboard sem duplicar tarefas nem divergir os critérios.
- [ ] Executar a matriz PostgreSQL descartável, HTTP autenticado, RLS A × B, concorrência e QA visual/teclado em 360/768/1280/1440 e zoom 200% quando o runtime estiver disponível.

## Revisão interna v012

- [x] Modelar revisão sequencial presa à versão e snapshot material, com transições, comentários, reatribuição e conflito otimista; criar migration v012 tenant-aware e testes unitários do agregado.
- [ ] Persistir decisões/transições/auditoria na mesma transação Dapper e traduzir revisão obsoleta em HTTP 409; validar vínculo ativo e permissões sem confiar em IDs do cliente.
- [ ] Expor solicitação, decisão, cancelamento, reatribuição, comentários, comparação limitada e histórico pela API e pelo BFF.
- [ ] Processar a outbox de revisão com relógio/política de lembretes e deduplicação; integrar contagens autorizadas ao dashboard.
- [ ] Entregar ficha split-view e “Minhas pendências” com paginação/ordenação server-side, filtros pesquisáveis na URL e QA/capturas reais.

## Central de documentos e revisão

- [x] Corrigir CS0136/CS0119/CA1859 no controller sem descarregar o conteúdo em memória nem encerrar antecipadamente o stream da resposta.
- [x] Tornar o contexto RLS de listagem, download e revisão local à transação, na mesma conexão, e cobrir alternância/rollback com role restrita.
- [x] Compensar o arquivo promovido quando o commit dos metadados falhar e serializar novas versões do mesmo documento lógico.
- [ ] Criar protocolo persistente e idempotente de upload, reservas com expiração e reconciliador de arquivos/linhas órfãos após interrupção do processo.
- [ ] Implementar inativação autorizada com motivo, histórico legível e retenção separada da limpeza técnica.
- [ ] Integrar API existente ao BFF com ficha, lista/visualizador/detalhes responsivos e revisão assistida; executar QA e capturas em 360/768/1280/1440 e zoom 200%.
- [ ] Executar a suíte em PostgreSQL 18 descartável, testes HTTP autenticados e cenários de scanner/worker com dependências reais.

## Estúdio de contratos

> Seção remapeada em D-OC36 (17/10/2026): os itens que aqui constavam abertos foram entregues em
> incrementos posteriores — persistência em migrations novas sem tocar releases aplicados (fundação
> v011 de documento/minuta/versão + blocos v044–v057), casos de uso tenant-aware com API/BFF no núcleo
> existente (blocos A/B e incrementos do estúdio), editor visual com autosave/conflito, PDF rastreável e
> histórico autenticado (D-OC22–D-OC25 e suítes web 93/93–111/111) e PostgreSQL descartável + jornada no
> navegador + inspeção do PDF + QA nas quatro larguras (D-OC23/D-OC24/D-OC25).

- [x] Corrigir CA1859 sem alterar o contrato público ou materializar novamente o histórico.
- [x] Definir JSON estruturado como fonte canônica, validar nós/formatação e recusar superfícies de conteúdo ativo.
- [x] Modelar campos estáveis/repetidos, validação tipada, confirmação, conflito otimista, publicação imutável e restauração não destrutiva na camada Application.
- [x] Persistir modelos, cláusulas, concessões, rascunhos, versões e emissões em migration nova, sem modificar releases aplicados.
- [x] Integrar casos de uso tenant-aware, permissões/limites/auditoria, API e BFF ao núcleo existente.
- [x] Entregar editor visual modular, autosave com retry/conflito, PDF rastreável e histórico autenticado.
- [x] Executar PostgreSQL descartável, jornada no navegador, inspeção do PDF e QA 360/768/1280/1440 e zoom 200%.

## Recuperação da referência S02

- [x] Reparar a colisão PostgreSQL 42P13 no instalador novo sem `DROP CASCADE` e preservar o artefato histórico defeituoso.
- [x] Implementar compatibilidade estrita para o checksum v009 conhecido e publicar o snapshot v010 reparado.
- [x] Isolar Data Protection nos testes de startup sem carregar configuração pessoal.
- [ ] Recuperar/integrar `183aa2b5` ou reconciliar manualmente suas funcionalidades restantes; este checkout e seu pack Git não contêm o objeto e o remoto continua inacessível.
- [x] Criar fundação v011 de documento lógico, versão imutável, cota, segurança, jobs com lease, resultados, sugestões, revisão e eventos.
- [ ] Concluir API de decisão/aplicação transacional e BFF split-view; adicionar OCR de PDF digitalizado, reconciliação automática e suíte PostgreSQL/fixtures sintéticas.
- [ ] Só então concluir e verificar contratos, alertas, documentos, quota, scanner, UI e jornada autenticada descritos na solicitação; não duplicar esses módulos sobre S01.

## Prompt 10

- [x] Separar visualmente navegação autenticada de plataforma e cliente a partir de informação emitida pelo backend.
- [x] Impedir shell administrativo durante troca inicial de senha e MFA pendente; implementar sidebar/drawer acessível e preferência local apenas de apresentação.
- [x] Corrigir `.button-primary`, organizar tokens visuais e melhorar login, cadastro e leitura honesta dos limites do cliente.
- [x] Regenerar e validar lockfiles com SDK .NET 10; restore bloqueado e build locais aprovados (suíte PostgreSQL ainda pendente).
- [ ] Executar QA real e capturas de login, MFA, cadastro e dashboards em 360/768/1440.
- [x] Entregar seleção explícita de organização e contexto validado no servidor.
- [x] Entregar organização, equipe, convites/outbox, perfis delegáveis, quota e proteção do último administrador (prova concorrente em PostgreSQL ainda pendente).
- [ ] Entregar consulta/ficha de clientes ao superadministrador com filtros, autorização e auditoria.

## Prompt 09

- [x] Corrigir a duplicação de `StartupValidationService` sem desativar validações.
- [x] Implementar provisionamento local explícito e idempotente de superadministrador e cliente demonstrativo com conferência pós-commit.
- [x] Impedir `show-login` de exibir senha local divergente e tornar o reset de usuário preciso/transacional.
- [ ] Executar o provisionador contra PostgreSQL local autorizado e completar login HTTP/MFA com autenticador.
- [ ] Concluir seleção de organização, equipe, convites, perfis, quota e último administrador sob concorrência.

## Fechar S00 com evidência

0. Executar em CI `restore --locked-mode`, build e os novos testes de host após o alinhamento do `coverlet.collector`; não promover o Marco A até o resultado remoto do SHA entregue.
1. Criar banco descartável `odca_test*`, definir as três variáveis documentadas no README e executar migração vazia, upgrade, reaplicação, concorrência, recovery e suíte completa.
2. Tornar o pacote SQL direto realmente incremental no pgAdmin sem alterar os snapshots históricos 001/002; provar equivalência com o runner.
3. Confirmar roles de runtime/teste sem `SUPERUSER`, `CREATEDB`, `CREATEROLE`, propriedade ou `BYPASSRLS`, incluindo comportamento sob pool.
4. Corrigir qualquer falha do CI do novo SHA e confirmar os dois jobs; depois fazer QA em 360/768/1440 px.

## S01 — SaaS e acessos

- [x] Catálogo administrativo e catálogo público separados, sem preços inventados.
- [x] Entrada pública inicial de solicitação de titular com protocolo opaco e resposta neutra.
- [x] MFA real de superadmin, recovery codes e testes de senha apenas negada; reautenticação recente para mudanças privilegiadas ainda deve ser aplicada aos próximos endpoints sensíveis.
- [x] Cadastro transacional inicial CPF/CNPJ, responsável, termos/aviso/preferências separados e confirmação de e-mail por token com hash/uso único.
- [x] Assinatura presa à versão exata do plano e ativação comercial honesta em estado `commercial_pending`, sem pagamento fictício.
- [x] Seleção de organização, convites, equipe, perfis delegáveis e proteção do último administrador (API/SQL); prova concorrente A × B em PostgreSQL ainda pendente.
- [ ] Operação autenticada do atendimento de direitos, prazos configurados, preferências e exportação revisada.
- [ ] Sessão temporária de suporte com MFA recente, escopo, expiração, revogação e ator real.
- [x] Cache distribuído e Data Protection persistido para produção/múltiplas instâncias
  (D-OC31 A.4/D: `PostgresDistributedCache` + repositório compartilhado de chaves na v054,
  seleção condicional `Cache:Store`/`DataProtection:KeysStore`=postgres, dev simples em
  memória/filesystem; prova 2 instâncias 17/17 — login numa, sessão continua na outra,
  logout revoga nas duas; evidência `evidence/d31-gate-twostack.txt`).

## Sequência funcional posterior

- S02: contrapartes, contrato, armazenamento privado, quota concorrente e worker persistente.
- S03: modelo versionado, snapshots, editor e controle de conflito.
- Depois: OCR/Office, revisão/aprovação, cobrança/adicionais, assinatura, notificações e emissão rastreável.

Dependências externas: textos, prazos e contatos aprovados; provedor de e-mail; storage/scanner; cache compartilhado; integrações comerciais. Nenhuma delas deve ser simulada como produção.

## Pendências após o incremento do prompt 08

- [x] Corrigir contrato Dapper da home e datas do desafio MFA; derivar contagem esperada da versão canônica.
- [x] Sincronizar renovação MFA entre sessão persistida e token; condicionar inscrição e vincular o segredo confirmado.
- [x] Validar dígitos de CPF/CNPJ e reservar documento vivo sob concorrência com migração 006.
- [ ] Implementar transporte/outbox operacional (segredo protegido temporário, lease, retry, deduplicação e descarte), reenvio limitado/auditado e retomada de rascunho expirado.
- [ ] Substituir versões `pending-legal-approval` por identificadores de textos aprovados e capturar contexto mínimo definido por Privacidade.
- [x] Entregar seletor de organização, equipe, perfis delegáveis, convites e assentos com proteção do último administrador (API/SQL; BFF visual em andamento paralelo).
- [x] Configurar cache realmente compartilhado e validar Data Protection/tickets entre duas
  instâncias (D-OC31 A.4/D: stores v054 no Postgres + prova 17/17 entre duas instâncias
  vivas — ticket válido na outra após login e revogado nas duas após logout;
  evidência `evidence/d31-gate-twostack.txt`).
- [ ] Depois: suporte temporário e operação de direitos; S02; S03; OCR/Office e sequência comercial, sem antecipar editor.

## Após prompt 11

- [x] Contexto explícito de organização derivado da identidade, com tenant por URL/formulário e revalidação no servidor.
- [x] Administrador inicial idempotente, catálogo delegável inicial, listagem de equipe/perfis e criação de perfil permitido.
- [x] Criação de convite com reserva transacional de assento, token hashado/uso único, aceite vinculado a e-mail verificado e outbox com lease/retry local.
- [x] Lockfiles validados com SDK 10.0.400 (`restore --locked-mode` + build locais).
- [x] Completar paginação, bloqueio/inativação/restauração, reenvio/cancelamento, BFF com abas e proteção do último administrador; testes PostgreSQL A × B concorrentes ainda pendentes.
- [ ] Configurar adaptador de e-mail de produção, política de limpeza operacional e fila de falhas com operador humano.
- [ ] Executar QA por teclado e capturas 360/768/1440 com PostgreSQL/API/Web reais.

## Próxima fatia vertical — contratos (S02)

- [ ] Cadastro manual de tipo + contraparte → contrato com vigência → listagem/detalhe → permissões → histórico → alerta interno.
- Não antecipar OCR/editor rico nem declarar conformidade LGPD plena.

## Após S03 obrigações (14/09/2026)

- [ ] Executar v013 em PostgreSQL 18 descartável e provar RLS A × B, concorrência, leases e reexecução.
- [ ] Completar formulários BFF de criação, ações destrutivas e edição “esta/futuras”, com seleção prévia para eventual cópia na renovação.
- [ ] Integrar resumo de obrigações/renovação à ficha contratual quando a ficha HTTP for conectada.
- [ ] Realizar QA autenticado e registrar capturas nas quatro larguras e zoom 200%.


## Próximo incremento após correções de analisadores

- [x] Corrigir CA1859, CA1305, CS8604 e CA1512 sem supressões.
- [x] Cobrir motivo e auditoria de reatribuição, recorrência zero/negativa e datas invariáveis.
- [x] Preservar filtros e validar intervalo de datas em Minhas pendências.
- [ ] Unificar projeção autorizada de revisões, obrigações e decisões de renovação sem criar tabela de tarefas.
- [ ] Implementar ficha do contrato, ações BFF e histórico integrado.
- [ ] Executar SDK .NET, PostgreSQL descartável, HTTP autenticado e QA visual quando o ambiente estiver disponível.
- [x] Preservar no evento de cumprimento a data efetiva, observação e evidência, e permitir reabertura sem violar a consistência da projeção atual.
- [x] Rejeitar intervalo de datas invertido também na API, mantendo limites inclusivos (`due_date >= from` e `due_date <= to`).
- [ ] Implementar e validar a jornada HTTP/BFF completa de revisões; o agregado e as tabelas v012 não constituem, isoladamente, uma entrega ponta a ponta.
- [x] Expor e renderizar o histórico autorizado de obrigações sem enviar o token da API ao navegador.
- [ ] Conectar os formulários de iniciar, cumprir, reprogramar, reatribuir, cancelar e reabrir ao BFF, preservando valores após conflitos.

## Após a política canônica de vistas (14/09/2026)

- [x] Centralizar allowlist, tipos básicos, datas ISO, intervalos, ordenação e chaves de rota reservadas.
- [x] Aplicar precedência URL explícita → vista identificada → padrão → geral e reiniciar página ao abrir vista.
- [x] Permitir atualização explícita dos filtros com versão otimista e manter renomear/padrão/inativar.
- [ ] Persistir modos de data relativa e resolvê-los com relógio injetável e fuso cadastrado da organização em lista, agenda e contagens.
- [ ] Adicionar chave idempotente à criação e provar timeout/repetição e disputa de padrão em PostgreSQL descartável.
- [ ] Conectar formulários completos de tratamento e validar retorno ao contexto; executar HTTP/RLS, navegador, zoom e capturas nas quatro larguras.
# Próximo incremento — jornada ODCA Legal Med (23/09/2026)

> Não duplicar catálogo, estúdio, versão gerada, documento, revisão, storage ou auditoria.
> O diagnóstico e as correções de fundação estão em
> `docs/audits/LEGAL_MED_FOUNDATION_2026-09-23.md`.

1. **Gate A:** executar build/testes e PostgreSQL 18 descartável com role restrita;
   comprovar login pending restrito, suspensão, revogação e ficha por finalidade em dois
   tenants.
2. **Paciente:** após o gate, migration 023 com pessoa/paciente tenant-scoped,
   identificadores tipados normalizados, representante explícito, concorrência otimista,
   inativação e auditoria atômica; API e central Razor paginada.
3. **Jornada documental:** estender o catálogo existente com tipo/finalidade aprovados e
   ligar paciente/conjunto aos `contract_drafts` e `generated_contract_versions`, mantendo
   documentos separados e confirmação idempotente.
4. **Assinatura:** implementar contrato de provedor, envelopes, signatários, eventos
   deduplicados, reconciliação e original/evidências. Escolha de provedor, modalidade,
   credenciais e conteúdo aprovado continuam decisões externas; não simular conclusão.
   *(Avanço S3/D-OC37: contrato plugável do provedor + transporte de referência
   `local-file` + envelopes/eventos tenant-scoped com RLS (v058) + reconciliação no worker;
   assinatura por sessão e evidência documentais já existiam desde B.2. A escolha do
   provedor segue decisão externa.)*
5. **Acervo:** projetar referências existentes por paciente, com paginação, filtros,
   autorização e download revalidado, sem copiar bytes.

## Incremento 23 — pacientes e acervo vinculado

- [x] Cadastro tenant-scoped com identificador normalizado, representante explícito,
  concorrência otimista, inativação/restauração, RLS e auditoria.
- [x] Vincular a minuta ao paciente e preservar snapshot do paciente/representante na
  versão gerada; o acervo pagina referências existentes e separa revisão de assinatura.
- [x] Restringir a projeção de documentos da ficha por capacidade própria, mesmo quando
  uma atribuição de obrigação ou revisão permite abrir a ficha.
- [ ] Concluir as telas Razor da central/assistente e o QA visual em 360/768/1440 e zoom
  200%; os endpoints deste incremento não constituem sozinhos a jornada visual concluída.
- [ ] Selecionar e integrar provedor real de assinatura (envio idempotente, signatários,
  eventos autenticados, reconciliação e evidências). Até lá o acervo informa
  `signatureStatus=not_available`, nunca “assinado”. Contrato plugável do provedor e
  transporte de referência `local-file` entregues na Sprint 3 (D-OC37); seleção,
  credenciais e conteúdo aprovado seguem decisões externas.
- [ ] Carregar conteúdo clínico/jurídico aprovado. Testes devem continuar usando modelos
  sintéticos; nenhum texto demonstrativo deve ser publicado como conteúdo real.

## D-OC31 — Operação documental integrada e preparação comercial (15/10/2026)

- [x] GATE antes dos avanços dependentes: baseline compilável (build estrito 0W/0E), instalação
  limpa validada (`-FreshDb` 188/188), regressões críticas resolvidas (42P10 no
  `ON CONFLICT(source_id)` em três sites com predicado do índice parcial; 42601 no
  `retry_notification` corrigido na **v055** sem reescrever a v054 congelada), regra canônica
  de planos reconciliada e isolamento entre dois tenants provado (camada API 403/200/201 e
  RLS como `odca_app`: nA=1/nB=1, contratos 55/0; evidência `evidence/d31-gate-twotenants.txt`).
- [x] A.3: regra canônica preservada — base fresca 6/6/7 módulos (`module.reviews` apenas em
  `enterprise-v2`); `PlanServiceGateTests` prova os quatro serviços diferenciados por plano
  (documentais exigem Enterprise; `suporte_tecnico` aceita qualquer assinatura ativa;
  demais → 409 `solicitations.plan.required`; contagens finais 1/1/4).
- [x] B.1: notificação com outbox na mesma transação (evento + fila), retry/backoff/limite
  configurável, lease recuperável após interrupção, reprocessamento sem duplicar confirmações
  (source_id determinístico + índice parcial), estados enfileirado/aceito/entregue/comprovável/
  falha temporária/falha definitiva, SMTP configurável e desativado até configuração válida,
  central do superadmin com campos seguros, filtros e retry auditado
  (`platform.notifications.retried`); smoke ao vivo 0 falhas (remind queued=1, retry 200+auditoria,
  409 em pendente, pickup com 8 arquivos, zero erros no worker).
- [x] B.2: assinatura vinculada à versão exata com hash do PDF confirmado no servidor;
  evidência liga documento do acervo (id + sha256), exigindo varredura segura deste contrato;
  justificativa e permissão específica para terceiros; sem sobrescrita de evidências
  (correção por evento complementar auditado); idempotência/replay (`replayed=true`) e
  negativos 400 sem máscara de estado assinado.
- [x] A.4/D: `PostgresDistributedCache` + repositório compartilhado de chaves com seleção
  condicional (`Cache:Store=postgres` e `DataProtection:KeysStore=postgres`; desenvolvimento
  permanece simples em memória/filesystem); prova entre duas instâncias 17/17 PASS — login numa,
  chave DP e ticket no Postgres (`odca.data_protection_keys`/`odca.distributed_cache_entries`,
  v054), sessão válida na outra (200), logout numa → ticket removido do cache compartilhado →
  revogada na outra (302 `/entrar`); evidência `evidence/d31-gate-twostack.txt`. Detalhe net10:
  o repositório exige `AddKeyManagementOptions(o => o.XmlRepository = ...)` (singleton puro de
  `IXmlRepository` deixa o Data Protection efêmero).
- [x] B.3: jornada contínua na ficha (próxima ação priorizada overdue > revisão > alteração >
  renovação > assinatura > solicitação > obrigações; pendências contadas por seção com âncora;
  motivo de bloqueio em arquivado/encerrado; atalho para o histórico) calculada no BFF a partir
  dos dados já projetados — editor com busca de campos e grupos por origem preservando o
  preenchimento pós-falha; restaurar versão = nova revisão (`POST minutas/{id}/restaurar` →
  `POST drafts/{draftId}/restore-from-version`: normaliza, valida structural, protege origens
  automáticas, row_version otimista, evento `draft.restored` com fromVersionId/Number/newVersion,
  409 `draft.save.conflict` em estado desatualizado); prova integrada 47/47 PASS (evidência
  `evidence/d31-b345-final.txt`; rota dupla:
  arquivado "Nenhuma ação — contrato arquivado" / ativa "Acompanhar obrigações abertas" + cartão
  pendente ancorado) sem OCR/upload.
- [x] B.4: prova ao vivo em stack viva — assinatura do participante não altera vigência/valor/
  versão nem o conjunto de obrigações (snapshot idêntico antes/depois); obrigação aberta aparece
  na caixa operacional (scope=mine, sem janela) e some das projeções ao ser atendida; aditivo
  criado→enviado→formalizado→aplicado gera nova versão (1→2) com `obligations_preserved=true` e
  md5 do conjunto idêntico, vínculo `contract_change_applications` registrado
  (`NULL -> 100.00|BRL`) com trilha create/submit/formalização manual; histórico consultável
  via `GET contracts/{id}/events` (200 com eventos do ciclo, inclusive `draft.restored` da prova).
- [x] B.5: ficha administrativa diferencia "Situação administrativa" de "Situação comercial" e
  afirma que ativação administrativa não equivale a pagamento recebido; estado
  `commercial_pending` exibe "Aguardando confirmação comercial (sem pagamento recebido)";
  mudança de plano mostra impacto antes de confirmar (preview basic→intermediate) e grava auditoria
  `organization.plan_changed` com motivo, versão comercial anterior/nova e `payment_recorded=false`.
- [x] C: sete telas prioritárias (login, caixa, ficha de contrato, estúdio/minuta, renovações,
  cliente da administração, minha conta); quatro idiomas (pt-BR/en-US/es-ES/fr-FR) sem traduzir
  códigos/identificadores/conteúdo do usuário; validação em 360/768/1280/1440px e zoom 200%;
  **140 capturas full-page** headless das jornadas autenticadas (`CAPTURES-DONE ok=140 fail=0`,
  140 hashes únicos) em `evidence/captures/`. Correção registrada entre rodadas: cookie
  `__Host-odca-session` exige `secure:true` no CDP `Network.setCookies` (sem ele era
  descartado → redirecionamento para `/entrar`) + login fresco via HTTP no driver com TOTP
  controlado por passo (replay protection server-side).
- [x] E: matriz de aceite com dez itens diferindo aprovado/falhou/bloqueado/não executado, cada
  um com evidência apontada (tabela na doc D-OC31: 10 aprovados, 0 falhados/bloqueados/não
  executados).
- [x] F: resumo do implementado, arquivos + migrations, cenários e instruções de configuração
  sem segredos, dependências restantes em `docs/execution/D-OC31_OPERACAO_DOCUMENTAL_COMERCIAL.md`;
  STATUS.md atualizado; commit único "sem push".

## D-OC32 — Reupload idempotente por conteúdo + reconciliação de armazenamento + extração para o estúdio (15/10/2026)

- [x] GATE antes dos avanços dependentes: build estrito 0W/0E; baseline verde no `2af1c37`
  (D-OC31: suíte `-FreshDb` 191/191); sem regressões críticas abertas.
- [x] B.1: reupload por hash de conteúdo — mesmo sha256 enviado de novo ao MESMO documento
  (`documentId` presente) é replay honesto (200 `replayed=true`, sem nova versão, sem cota
  dupla, reserva liberada para `released`, evento `document_reuploaded` com sha256 e versão
  correspondida); 201 ganha propriedade aditiva `replayed=false`; dedup por conteúdo não
  aplica a documento novo (sem adivinhar).
- [x] B.2: reconciliador de armazenamento — reservas `reserved` expiradas viram `expired`
  (função SECURITY DEFINER v056, lote limitado, chamada pelo worker) e órfãos (tmp da
  quarentena e arquivos finais sem linha referenciando o storage_key, ambos mais velhos que
  retenção configurável) são movidos para pasta de retenção `reconciled/{yyyy-MM}/` (sem
  exclusão direta), contagens em log sem conteúdo.
- [x] B.3: aplicar extração/OCR ao rascunho do estúdio — `POST studio/drafts/{draftId}/apply-extraction`
  com decisões accepted/edited/rejected por sugestão: mapeamento FECHADO conceito→field id
  (match exato + alias documentados; candidatos monetários múltiplos só aplicam com candidato
  único no rascunho; nunca sobrescreve valor confirmado nem campo calculado); valores aterrizam
  `source="extraction"`, `confirmed=false`; bloqueio otimista por row_version (409
  `draft.save.conflict`), idempotência por `draft_extraction_reviews` (v056), evento
  `draft.extraction_applied` com applied/skipped detalhado; sugestões marcadas como revisadas.
- [x] C: painel de documentos + extração no editor (proxies BFF de listagem/upload multipart/
  extração/aplicação): versões com badge de status de segurança, upload, "extrair texto",
  painel de sugestões com evidência e aceitar/editar/rejeitar, aplicação explícita ao rascunho;
  chaves de catálogo ×4 idiomas com paridade.
- [x] D: release v056 (`odca.expire_storage_reservations`, tabela `odca.draft_extraction_reviews`
  + RLS + grants, índice não-único `document_versions(tenant_id,document_id,sha256)`),
  CurrentVersion 56, teste de prefixo imutável 1..55 e ausência de 057.
- [x] E: matriz de aceite com dez itens diferindo aprovado/falhou/bloqueado/não executado, cada
  um com evidência apontada.
- [x] F: doc de entrega, STATUS.md, backlog marcado, commit único "sem push".

## D-OC33 — OCR real para imagens rasterizadas e PDF escaneado no fluxo de extração (16/10/2026)

- [x] GATE antes dos avanços dependentes: build estrito 0W/0E sobre o `8d802bd` (D-OC32: suíte
  `-FreshDb` 197/197); sem regressões críticas abertas.
- [x] A: OCR de verdade para PNG/JPEG — o worker já invocava `Documents:OcrExecutable`
  (convenção Tesseract, `-l por` hardcoded) sem o binário em lugar nenhum: instalar/configurar
  Tesseract + traineddata pt (sem segredos; instruções de instalação na doc), linguagem via
  `Documents:OcrLanguage` (padrão `por`), e degradação honesta: saída vazia do OCR não é falha do
  job (página em branco → 0 sugestões), exit code "sem texto detectável" também não é erro.
- [x] B: PDF com camada de texto multi-página (`Documents:PdfTextExecutable`, convenção pdftotext)
  com atribuição por página: `Suggest` passa a reconhecer separadores de página (`\f`) no texto
  extraído e preenche `page_number` (1-based) nas sugestões — coluna e UI (`studio.x_page`) já
  existem; docx segue sem página (sem evidência de página).
- [x] C: PDF escaneado (sem camada de texto) — detecção honesta de texto ausente e fallback
  renderizar→OCR: páginas renderizadas por `Documents:PdfRenderExecutable` (convenção pdftoppm,
  até `Documents:OcrMaxPages` padrão 20, dpi configurável) passam pelo mesmo OCR e o texto final
  preserva os separadores de página (método vira `ocr` e a franquia `ocr_credit` consome as
  páginas efetivas); sem renderer configurado o job falha com código estável `pdf-no-text-layer`.
- [x] D: provas ao vivo — PNG de 1 página → OCR real com sugestões e evidência; PDF de 2 páginas
  com texto → `pdf-native` com `page_number` 1 e 2; PDF composto só de imagem (JPEG embutido) →
  fallback com páginas OCR; negativo sem executáveis → falha honesta; e a cadeia completa pelo
  painel web (upload de imagem → safe → extrair → sugestões com página → aplicar).
- [x] E: matriz de aceite com dez itens diferindo aprovado/falhou/bloqueado/não executado, cada
  um com evidência apontada.
- [x] F: doc de entrega, STATUS.md, backlog marcado, commit único "sem push".

## D-OC34 — Piloto da jornada documental para a estreia (16/10/2026)

- [x] GATE antes dos avanços dependentes: build estrito 0W/0E sobre o `8095bc8` (D-OC33),
  domain 364/364 e suíte de integração `-FreshDb` 201/201 (P1 F1/F2 corrigidos antes das
  funcionalidades dependentes).
- [x] F1: plano fantasma `odca-pro` eliminado — catálogo único com os planos canônicos v2
  (basic/intermediate/enterprise); zero resíduos em código, seed e base.
- [x] F2: escopo OCR honesto — `pages_total`/`pages_processed`/`truncated` persistidos (v057 +
  worker + API), banner de análise parcial com o que ficou fora e a próxima ação, páginas brancas
  contam, docx/imagem não fabricam paginação, `pdfinfo` ausente → total desconhecido declarado,
  códigos de falha distintos e estáveis (timeout ≠ falha de extração ≠ camada de texto ausente).
- [x] B.5: arquivar modelo exige motivo (5..500) persistido em `archive_reason`; versões
  publicadas imutáveis; gate opcional `required_activity_profile` (409
  `draft.profile.template_mismatch`; nulo = sem gate).
- [x] B.6/B.7: vincular/desvincular paciente na minuta (row_version otimista, snapshot imutável,
  autofill só de campos não confirmados, evento `patient.linked`, botões explícitos de conflito)
  e cadastro rápido de paciente na página de novo documento.
- [x] B.8/B.9: banner de modalidades de assinatura nas fichas (nenhum envelope externo enviado;
  franquia não consumida por isso) e próxima ação "Concluído — disponível para download" na
  central de documentos.
- [x] B.10: assuntos SMTP genéricos e fixos por kind (sem dados livres); novo kind
  `signature_completed` com deduplicação determinística (preparação+versão); envio segue sem
  homologação externa.
- [x] A.3: diagnóstico operacional para superadministrador — estados em quatro níveis
  (unavailable < configured < accessible < verified + not_applicable), valores configurados nunca
  exibidos, resumo de filas da plataforma (`odca.platform_queue_summary()`).
- [x] Decisão registrada no DECISIONS-LOG: envelope externo de assinatura "proposto, não
  escolhido" para a estreia (sem inventar fornecedor pago ou homologação de envio).
- [x] D: prova ao vivo A–N com evidência apontada por item e capturas 360/768/1280/1440 + zoom
  200% das telas afetadas.
- [x] E: matriz de estreia com evidência apontada por item (sem percentuais inventados).
- [x] F: doc de entrega, STATUS.md, backlog marcado, commit único "sem push".

## Sprint 1 — automação confiável e consolidação das provas ao vivo do D-OC34 (D-OC35) (17/10/2026)

- [x] GATE antes dos avanços: sobre o `27b19b6`, builds estritos 0W/0E (Release+Debug),
  Domain 364/364, suíte de integração `-FreshDb` 202/202 TEST-EXIT=0 e paridade de catálogos
  4/4 via `scripts/check-assets.mjs` (o "201" do GATE do D-OC34 é histórico de 16/10).
- [x] Correções (P0/P1) de confiabilidade antes dos avanços: desacoplamento do `window` em tempo
  de avaliação de módulo nos módulos puros `studio-decimal.js` / `template-editor-document.js`
  (importáveis em Node sem `window`, ordem de carga irrelevante) e gate i18n no
  `check-assets.mjs` (19 chaves dos módulos × 4 catálogos não vazias, com strip de BOM).
  Zero mudança de comportamento de produto.
- [x] Prova A–N reproduzível em um comando: `scripts/qa/validate-d34-an-web.ps1` (purga +
  fixtures determinísticas por rodada, stack própria, marker `PROVA-FIM`); 31 falhas da rodada
  10 classificadas por causa raiz em `evidence/d34-proof-an-rodada10-falhas-classificadas.md`
  — zero falhas de produto; convergência FINAL-3 (15) → FINAL-4 (12) → FINAL-5 (5) →
  FINAL-6 (0).
- [x] Rodada final **fails=0** (61 checks) com transcript no repositório
  (`evidence/d34-proof-an-rodada-final.txt`) e capturas CDP: 100 PNGs
  (`scripts/qa/capture-d34-screens.ps1` + `scripts/qa/cdp-capture-v2.mjs` — 5 telas ×
  360/768/1280/1440 + zoom 200% × 4 idiomas, sessões MFA reais).
- [x] Doc D-OC34 consolidada (§D provas ao vivo, §E matriz de estreia, Evidências, Lições novas)
  e STATUS.md atualizado; `.odca/progress.json` inalterado (sem CI oficial verde nem aceite
  humano para creditar marco).
- [ ] CI oficial deste SHA: pendente — branch sem push; nenhum ID de execução inventado.
- [ ] Aceite humano dos fluxos da jornada: pendente (capturas disponíveis para revisão).
- [x] Commit único "sem push" fechando a Sprint 1.

## Sprint 2 — área de paciente em 4 idiomas (D-OC36) (17/10/2026)

- [x] GATE sobre a árvore final (execução única com raciocínio registrado na doc): builds estritos 0W/0E
  (Release+Debug), Domain 364/364, assets 4/4 e suíte de integração `-FreshDb` 202/202 TEST-EXIT=0, incluindo
  paridade dos 4 catálogos com as 123 chaves `patient.*` novas (total 1533/cultura).
- [x] F-1 (P1): área de paciente em 4 idiomas (Index/Form/Details + `PatientsController` + 8 mensagens de
  paciente no `DocumentsController` + cadastro rápido em novo documento); validação server-side pelo catálogo
  no lugar de atributos de `FullName`/`Email` (sem mensagens padrão em inglês duplicadas); chaves existentes
  reutilizadas antes de criar novas; cookie canônico `odca_lang` preservado; datas localizadas por
  `CulturedTime` (novo overload `Date(DateOnly)`).
- [x] A-1: prova ao vivo `scripts/qa/validate-d35-patient-i18n-web.ps1` **fails=0** (27 checks; sessão cliente
  sem MFA; purga determinística; transcript `evidence/d35-proof-rodada-final.txt` + trace; falhas das rodadas
  1–3 100% classificadas como de harness) e capturas CDP `scripts/qa/capture-d35-patient-screens.ps1`:
  3 telas × 4 idiomas × 360/768/1280/1440 + zoom 200% = **ok=60 fail=0** (PNGs fora do repo por tamanho).
- [x] Higiene documental: seção obsoleta "Estúdio de contratos" remapeada acima, com as entregas reais apontadas.
- [ ] Pendente (fora de escopo da S2): i18n do restante da área Documentos (Index/Novo Documento com literais
  não relacionados a paciente — hoje pt-first).
- [ ] CI oficial deste SHA: pendente — branch sem push; nenhum ID de execução inventado.
- [ ] Aceite humano dos fluxos de paciente: pendente (capturas disponíveis para revisão).
- [x] Commit único "sem push" fechando a Sprint 2.

## Sprint 3 — assinatura: contrato de provedor de envelope externo + superfície em 4 idiomas (D-OC37) (17/10/2026)

- [x] GATE sobre a árvore final (execução inicial 210/210 invalidada por mudanças pós-execução —
  raciocínio na doc; reexecução final): builds estritos 0W/0E (Release+Debug), Domain 364/364, assets
  exit=0 e suíte de integração `-FreshDb` **211/211** TEST-EXIT=0 (baseline 202 + 8 fatos do envelope
  de assinatura + 1 fato de checksum da v058), catálogos 1622 chaves/cultura com paridade exata 4/4.
- [x] F-1: `signatureStatus` derivado da preparação real no detalhe da versão (`not_started|preparing|confirmed`)
  + readiness com advertência `integration.unavailable` fiel à configuração do transporte externo.
  Defeito adjacente corrigido: 500 Dapper na leitura de participante (registro público com parâmetro
  default) → `ParticipantReadRow` privado + projeção `.Select()`, coberto por fato de integração.
- [x] F-2: superfície de assinatura em 4 idiomas (seção da ficha + `aria-label`, mensagens de
  assinar/vincular/lembrete no `ContractsController` web, rótulos de estágio em `Documents/Index`);
  +89 chaves por cultura (total 1622), reuso de chaves existentes antes de criar novas.
- [x] F-3: triagem encerrada como **NÃO-defeito** — advertência de readiness com provider `none` é
  fiel à decisão "proposto, não escolhido"; envio falha explicitamente `409 provider_unconfigured`.
- [x] A-1: contrato plugável `ISignatureEnvelopeProvider` (`none` preserva a decisão; `local-file` é o
  transporte de referência), migração **v058** (envelopes + eventos tenant-scoped com RLS, funções de
  varredura/transição idempotente), endpoints send/sync/GET idempotentes (franquia consumida uma vez por
  envelope; replay sem consumo; reenvio após declined consome nova unidade), reconciliador periódico
  cross-tenant no worker, 8 fatos de integração. Nenhum fornecedor, credencial ou aprovação inventados.
- [x] A-2: prova ao vivo `scripts/qa/validate-d37-signature-web.ps1` **fails=0** (24 checks; convergência
  8→4→3→0 registrada; única falha de produto entre rodadas = defeito Dapper acima, corrigido; demais de
  harness classificadas na doc) e capturas CDP `scripts/qa/capture-d37-screens.ps1`: 1 tela × 4 idiomas ×
  360/768/1280/1440 + zoom 200% = **ok=20 fail=0** (PNGs fora do repo por tamanho).
- [x] Doc D-OC37 consolidada e STATUS.md atualizado; `.odca/progress.json` inalterado (sem CI oficial
  verde nem aceite humano para creditar marco).
- [ ] Seleção e integração de provedor real de assinatura: pendente — contrato entregue nesta sprint;
  decisão "proposto, não escolhido" preservada (credenciais/conteúdo aprovado seguem externos).
- [ ] i18n do restante da ficha do contrato (seções renovação/obrigações/revisão/histórico/formulários,
  pt-first desde antes de S3) e `aria-label` da navegação global (46 itens raw, padrão pré-S3).
- [ ] CI oficial deste SHA: pendente — branch sem push; nenhum ID de execução inventado.
- [ ] Aceite humano dos fluxos de assinatura: pendente (capturas disponíveis para revisão).
- [x] Commit único "sem push" fechando a Sprint 3.

## Sprint 4 — atendimento, avisos e prazos: sino in-app com deduplicação, i18n do atendimento e dos avisos, centro de obrigações (D-OC38) (18/10/2026)

- [x] GATE sobre a árvore final (após o defeito de view descoberto na prova): builds estritos 0W/0E (Release+Debug),
  Domain 367/367, assets exit=0 e suíte de integração `-FreshDb` **222/222** TEST-EXIT=0 (baseline S3 211 + 5 fatos do
  sino + 3 fatos dos endpoints de obrigação + 2 fatos do lease da outbox + 1 fato de checksum da v059), catálogos
  1765 chaves/cultura com paridade exata 4/4.
- [x] F-1: notificação in-app por evento de negócio (sino) que **nunca lança** (falha vira log 2403/2404), com dedup
  determinística por `source_id` (migração **v059**: coluna + índice único parcial `(tenant_id,source_id)`),
  `DeterministicSource` compartilhado com o e-mail e guard de membership ativo; 8 pontos de chamada (solicitações,
  revisões, assinatura); canais por tipo: in-app habilitado por padrão, e-mail desabilitado. 5 fatos de integração.
- [x] F-2: atendimento (central/nova/detalhe de solicitações) e avisos (notificações + preferências) em 4 idiomas;
  +143 chaves por cultura (total 1765); cinco views reescritas no padrão canônico. Dívida registrada: o *conteúdo*
  das notificações (título/corpo armazenados) segue pt-BR.
- [x] F-3: triagem encerrada como **NÃO-defeito** com evidência executável nova (2 fatos verdes): `claim_notification`
  é atômica (`FOR UPDATE SKIP LOCKED LIMIT 1`) e só re-pendencia pending/leased expirado; conclusão exige o token exato
  do lease (worker antigo = no-op). Nenhuma mudança de produto atribuída ao item.
- [x] A-1: centro de obrigações no Web BFF — cadastro (única ou série mensal 1–120, evidência, financeiro, pós-vigência),
  leitura individual via API (nova rota GET expondo `evidenceRequired`), ações por status com versão otimista; conflito
  409 rele a versão fresca **preservando os valores digitados**. Validação leve no BFF espelha a API; a API continua
  autoridade (permissão/validação/versão/auditoria). Defeito de produto descoberto e corrigido pela prova: `View(vm)`
  por convenção de action vs. arquivo `Acoes.cshtml` → `View("Acoes", vm)` explícito.
- [x] A-2: prova ao vivo `scripts/qa/validate-d38-s4-web.ps1` **fails=0** (rodada final 30/30 executados; 31 definidos —
  P0a só em base sem v059; convergência 10→2→0 registrada; única falha de produto entre rodadas = defeito de view acima,
  corrigido; demais de harness classificadas na doc) e
  capturas CDP `scripts/qa/capture-d38-screens.ps1`: 6 telas × 4 idiomas × 360/768/1280/1440 + zoom 200% =
  **ok=120 fail=0** (PNGs fora do repo por tamanho).
- [x] Doc D-OC38 consolidada e STATUS.md atualizado; `.odca/progress.json` inalterado (sem CI oficial verde nem aceite
  humano para creditar marco).
- [ ] Fila administrativa de atendimento da plataforma e i18n do centro de obrigações (pt-first nesta sprint) — dívidas.
- [ ] Localização do *conteúdo* das notificações (título/corpo armazenados seguem pt-BR).
- [ ] SMTP real: canal de e-mail segue desabilitado por padrão (fornecedor externo indefinido — decisão preservada).
- [ ] Botão "Agenda mensal" permanece disabled (fora do escopo desta sprint).
- [ ] CI oficial deste SHA: pendente — branch sem push; nenhum ID de execução inventado.
- [ ] Aceite humano dos fluxos de atendimento/sino/obrigações: pendente (capturas disponíveis para revisão).
- [x] Commit único "sem push" fechando a Sprint 4.

## Sprint 5 - segurança e operacional: reautenticação MFA recente, sessões de suporte de plataforma e purga de retenção documental (D-OC39) (19/10/2026)

- [x] F-1: reautenticação MFA recente para operações sensíveis — claims `mfa_enrolled`/`mfa_verified_at` no token, política `RecentMfa` (janela `Identity:RecentAuthMinutes`, padrão 15, explícita nos appsettings da API e da Web); handler isenta não-superadmin, troca forçada de senha e pré-inscrição; aplicada em `change-password`, `mfa/enrollment`, `mfa/enrollment/regenerate` e nas 9 escritas administrativas (membros, papéis, permissões, transferência) + escritas de suporte. API devolve 403 puro (`api.auth.recent_auth_required`); Web redireciona para `/mfa/desafio?returnUrl=` preservando o destino. 22 fatos novos de Domain.
- [x] F-2: sessões temporárias de suporte de plataforma ponta a ponta — migração **v060** (`extend_support_session` com evento/audit e expiração sem retrocesso + `support_session_tenant_name`, leitura governada que corrige o JOIN direto em `odca.tenants` com RLS FORCADA), API `api/v1/platform/support-sessions` (8 operações; 409 `active_exists`; expiração virtual; `end` dono/admin e `revoke` qualquer superadmin terminais; eventos + auditoria com ator real), Web BFF pt-first `/administracao/suporte-sessoes` com item de navegação e feedback. 8 fatos de integração + 1 de checksum.
- [x] A-1: purga física da área reconciliada no worker por idade de escrita (`Documents:ReconciledPurgeRetentionDays`, padrão 30 dias) — seleção testável e falha isolada por arquivo sem expor caminho.
- [x] A-2: prova ao vivo `scripts/qa/validate-d39-s5-web.ps1` **fails=0** (rodada final 22/22 executados; 23 definidos — P0a só em base sem v060; convergência 2→0 registrada; falhas intermediárias classificadas como harness) e capturas CDP `scripts/qa/capture-d39-screens.ps1`: 1 tela × pt-BR × 360/768/1280/1440 + zoom 200% = **ok=5 fail=0** (PNGs fora do repo por tamanho).
- [x] Doc D-OC39 consolidada e STATUS.md atualizado; `.odca/progress.json` inalterado (sem CI oficial verde nem aceite humano para creditar marco).
- [ ] Impersonação profunda dentro da janela de suporte (fronteira honesta do F-2) — dívida.
- [ ] i18n da área de sessões de suporte (pt-first nesta sprint) — dívida.
- [ ] Fila administrativa de atendimento, privacidade, conteúdo das notificações e `aria-label`s da navegação global (carreados das S3–S4) — dívidas.
- [ ] CI oficial deste SHA: pendente — branch sem push; nenhum ID de execução inventado.
- [ ] Aceite humano dos fluxos de reautenticação/suporte e da purga: pendente (capturas disponíveis para revisão).
- [x] Commit único "sem push" fechando a Sprint 5.

## Sprint 6 - candidata ao piloto: ensaio reproduzivel, design das superficies e matriz de prontidao (D-OC40) (18/10/2026)

- [x] F-1: ensaio de piloto reproduzível `scripts/qa/run-pilot-rehearsal.ps1` — reexecução sequencial das provas ao vivo versionadas (C-SLA, ACEITE, D34-JORNADA, D37-ASSINATURA, D38-ATENDIMENTO, D39-SEGURANCA) sobre a mesma base descartável, transcript consolidado, provisionamento/derruba da stack dev (run-local/stop-local) para os estágios legados, filtro `-Only` para reexecução pontual, espera do filho por polling `HasExited` (imune a netos que seguram handles) e probe de prontidão da stack legada por `curl -k`; rodada final 5/6 no transcript consolidado + reexecução pontual do D37 verde (fail único = ambiente transitório do banco, classificado em §4 do D-OC40).
- [x] F-2: auditoria de design das superfícies do piloto (`audit-d40-design.ps1` + `cdp-design-audit.mjs`) — overflow horizontal (360/768/1280/1440), autenticação e status HTTP por rota × idioma (10 rotas: 7 cliente/plataforma + privada anônima), PNG de triagem apenas das falhas; run-1 invalidada por bug do harness (contador `$t` esmagou o tenant `$T` → 404 mascarados em viewports ≥ 908px), arquivada como `*-run1-tenantbug*` e corrigida (loop `$att` + guarda do GUID + classe `HTTPFAIL` no driver); rodada final **72/72 ok (overflow=0 authfail=0 httpfail=0)**; correções de produto: **nenhuma** (`src/` inalterado).
- [x] F-3: matriz de prontidão do piloto (6 marcos do portal: requisito → implementação → evidência → responsável → pendência) + runbook do piloto (pré-condições, contas sintéticas, cenários por perfil, suporte via sessões de plataforma, rollback, critério de saída) em `docs/execution/D-OC40_PILOTO_DESIGN_PRONTIDAO.md`.
- [x] GATE final sobre a árvore; STATUS.md atualizado; `.odca/progress.json` inalterado (sem CI oficial verde publicada nem aceite real para creditar marco).
- [ ] CI oficial dos SHAs: pendente — branch sem push; nenhum ID de execução inventado.
- [ ] Provedor externo de assinatura (envelope): indefinido ("proposto, não escolhido" preservado); homologação com provedor real pendente.
- [ ] SMTP real: indefinido; canal de e-mail segue desabilitado por padrão.
- [ ] Backup/restauração real ensaiado: pendente (pré-condição explícita do runbook §7.1).
- [ ] Usuários convidados do piloto + aprovação da versão pelo responsável: pendente.
- [ ] Impersonação profunda na janela de suporte; i18n das áreas pt-first; fila admin/privacidade/conteúdo de notificações/aria-labels da navegação global (carreados das S3–S5) — dívidas.
- [x] Commit único "sem push" fechando a Sprint 6 — fim do plano de seis sprints.

## Sprint 7 - jornada do cliente do piloto em 4 idiomas e acessibilidade de navegacao global (D-OC41) (18/10/2026)

- [x] F-1: hub de documentos em 4 idiomas - Documents/Index e NewDocument + mensagens remanescentes do DocumentsController via catalogo documents.* (filtros, tabela, badges, proximas acoes, paginacao, empty state, jornada de criacao, busca/cadastro rapido de paciente); zero literal pt verificado por varredura.
- [x] F-2: central de obrigacoes em 4 idiomas - namespace novo obl.* nas views Index/Nova/Acoes (recorrencia mensal, lembretes, seis acoes, conflito 409); ObligationsController com LocalizationService injetada (padrao da casa) - todos os TempData/notificacoes/erros de campo viram chaves.
- [x] F-3: central de privacidade (canal publico anônimo) em 4 idiomas - priv.* no formulario, tipos de solicitacao, confirmacao com protocolo e falhas de rate-limit/timeout/indisponibilidade.
- [x] F-4: acessibilidade/design global - NavItem.AriaKey + resolver por localizer (nav.aria_* nos 4 catalogos), aria-labels identicos ao rotulo removidos (WCAG 2.5.3), _Layout localizado (sufixos de funcionalidade, Administracao ODCA, Escolha uma organizacao), paginas compartilhadas (Error antes todo em ingles, AccessDenied, ServiceUnavailable, _ConfirmDialog, _Toasts) em 4 idiomas, navigation.js via bag window.odcaNav, dialogs.js herdando o texto renderizado.
- [x] Catalogos: +391 chaves x 4 culturas (1770 -> 2161/cultura) via deltas versionados scripts/i18n/d-s7-*.json + mesclador deterministico scripts/i18n/merge-i18n-delta.mjs (valida unicidade e paridade 4/4 antes de gravar; BOM preservado).
- [x] GATE final sobre a arvore: builds 0W/0E, Domain 389/389, suite -FreshDb 231/231 TEST-EXIT=0, assets exit=0, paridade 4/4 em 2161; auditoria de design estendida em 4 idiomas (audit-d41-design.ps1) 152/152 OK overflow=0 authfail=0 httpfail=0 exit=0, transcript evidence/d41-design-audit-rodada-final.txt.
- [x] Doc D-OC41 consolidada e STATUS.md atualizado; .odca/progress.json inalterado (sem CI oficial verde nem aceite humano para creditar marco).
- [ ] Ficha do contrato detalhada, fila administrativa de atendimento (AdminSolicitacoes), sessoes de suporte, agenda mensal habilitada e conteudo das notificacoes: i18n carreado para a Sprint 8 (D-OC42).
- [ ] CI oficial deste SHA: pendente - branch sem push; nenhum ID de execucao inventado.
- [ ] Aceite humano dos fluxos de documentos/obrigacoes/privacidade: pendente (auditoria de design disponivel para revisao).
- [x] Commit unico "sem push" fechando a Sprint 7.

## Sprint 8 - superficies de plataforma/operacional em 4 idiomas (D-OC42) (18/10/2026)

- [x] F-1: ficha do contrato em 4 idiomas - Sheet.cshtml integral (jornada, proxima acao, propostas/aditivos, revisao, comentarios, obrigatoriedades, minutas, documentos/quarentena, solicitacoes, historico com drawer JS via bag drawerI18n), Print.cshtml (lang/watermarks/barra) e Contracts/Index.cshtml (lista "Meus contratos"); ContractsController: ~60 mensagens BFF para sheet.ct_* e OfficialTemplateNames substituido por HashSet de chaves (resolucao por KEY, titulos do catalogo do tenant).
- [x] F-2: fila administrativa de atendimento em 4 idiomas - AdminSolicitacoes Index/Aprovacoes/Detalhes + controller; namespace novo admin.*.
- [x] F-3: sessoes de suporte da plataforma em 4 idiomas - SupportSessions/Index + controller; namespace novo support.*.
- [x] F-4: central de notificacoes da plataforma + agenda mensal em 4 idiomas - PlatformNotifications/Index + controller (namespace novo platform.*) e Agenda/Index + controller com reuso de common.*; padrao canonico (CultureMiddleware + L()) injetado nos controllers faltantes.
- [x] Catalogos: +547 chaves x 4 culturas (2161 -> 2708/cultura, paridade 4/4) via deltas versionados scripts/i18n/d-s8-*.json (admin 131, support 38, platform 61, contracts 239, contracts-ctl 59, contracts-index 18) + correcao direta da chave ausente "obligacoes" (bug S7/S8 em Obligations/Acoes.cshtml); mesclador generalizado para d-sN-*.json e documentado como idempotente.
- [x] GATE final sobre a arvore: builds Release+Debug estritos 0W/0E, Domain 389/389, suite -FreshDb 231/231 TEST-EXIT=0, assets exit=0; auditoria de design estendida (audit-d41-design.ps1) ampliada 13 -> 17 rotas (+contratos, +ficha com contrato fixo da prova, +agenda em 4 idiomas; +notificacoes plataforma pt-BR) = AUDIT-DONE checks=204 ok=204 overflow=0 authfail=0 httpfail=0 exit=0 (transcript evidence/d41-design-audit-s8-rodada-final.txt).
- [x] Doc D-OC42 consolidada e STATUS.md atualizado; .odca/progress.json inalterado (sem CI oficial verde nem aceite humano para creditar marco).
- [ ] Limitacoes registradas: conteudo (titulo/corpo) das notificacoes permanece em pt-BR no armazenamento (devida); botao "Agenda mensal" na central de obrigacoes segue disabled; ModulesController fica para a proxima sprint; labels pt hardcoded em ContractSheetRepository (Infrastructure) mantidos por decisao (view mapeia pelos helpers localizados).
- [ ] CI oficial deste SHA: pendente - branch sem push; nenhum ID de execucao inventado.
- [ ] Aceite humano dos 4 idiomas nas superficies da ficha/plataforma: pendente.
- [x] Commit unico "sem push" fechando a Sprint 8.

## Sprint 9 - impersonacao profunda na janela de suporte (D-OC43) (18/10/2026)

- [x] F-1: nucleo de autorizacao - migracao v061 (`odca.support_impersonations` + indice parcial unico de ativa por janela + funcoes SECURITY DEFINER `start/end_support_impersonation`, `support_impersonation_is_valid`, `support_session_members` + eventos `impersonation_started/ended/revoked`; checksum `5e1e1072...`, blocos 1-60 imutaveis, release byte-identica); token da visao = JWT do usuario-ALVO em sessao dedicada (sub/sid/mfa) + claims `imp_op`/`imp_grant`; revalidacao por request no middleware da API (grant fim/expirado ou janela fora do ar → 401 `api.impersonation.expired`); duracao 5-60 min limitada por `LEAST` ao fim da janela; revogacao instantanea (a sessao dedicada do alvo e revogada); endpoints `GET {id}/members`, `POST {id}/impersonation` (RecentMfa) e `POST {id}/impersonation/{grantId}/end` (204; ator resolvido por `imp_op` ?? `sub`).
- [x] F-2: BFF/portal - botao "Ver como cliente" nas janelas ativas; seletor de membro (membership ativa, sem senha pendente, nao excluido) com duracao 5/10/15/30/60; re-signin que troca o cookie pela identidade do alvo preservando o token do operador em auth property; banner global role=status em todas as paginas (alvo + operador + expiracao) com botao "Sair"; Sair encerra a concessao (se ativa) e restaura a identidade/token do operador a partir do seu JWT original (fallback: login).
- [x] F-3: i18n delta `scripts/i18n/d-s9-support-imp.json`: 19 chaves x 4 idiomas (banner, acoes, erros BFF+API); catalogos 2708 → 2727 chaves/cultura, paridade 4/4 (mesclador idempotente).
- [x] Testes: e2e sobre HTTP real (`SupportImpersonationEndpointTests`: token escopo do alvo, revalidacao por request 403→401, saida vinda da propria visao auditada com o operador real, concorrencia 409, validacoes 400, colheita de grant expirado naturalmente, eventos+auditoria) + fato de migracao v061 (blocos 1-60 byte-invariantes, snapshot byte-idêntica, checksum revalidado).
- [x] GATE final sobre a arvore: builds Release+Debug estritos 0W/0E, Domain 389/389, suite -FreshDb 234/234 TEST-EXIT=0 (aplica v061 no bootstrap), assets exit=0 (19 chaves novas validadas em 4 catalogos); auditoria de regressao d41 em navegador real (audit-d41-design.ps1) = AUDIT-DONE checks=204 ok=204 overflow=0 authfail=0 httpfail=0 exit=0 (transcript `evidence/d41-design-audit-s9-rodada-final.txt`).
- [x] Doc D-OC43 consolidada e STATUS.md atualizado; .odca/progress.json inalterado (sem CI oficial verde deste SHA nem aceite humano para creditar marco).
- [ ] Limitacoes registradas: durante a visao do cliente o operador opera com as permissoes do alvo (areas de plataforma exigem sair antes); o encerrar da concessao nao exige MFA recente por decisao (saida robusta apos janelas longas); a superficie interativa da visao nao e rota fixa da auditoria de overflow (coberta pelos e2e de HTTP).
- [ ] CI oficial deste SHA: pendente - branch sem push; nenhum ID de execucao inventado.
- [ ] Aceite humano do fluxo de impersonacao profunda (entrar/banner/sair): pendente.
- [x] Commit unico "sem push" fechando a Sprint 9.

## Sprint 10 - backup/restore real ensaiado e transporte SMTP por protocolo real (D-OC44) (18/10/2026)

- [x] F-1: ensaio de backup/restore real versionado `scripts/qa/run-backup-restore.ps1` — pg_dump -Fc (PostgreSQL 18 local) de odca_test_disposable -> pg_restore --no-owner --exit-on-error em banco novo (odca_restore_check_s10) → 26 checks comparando fonte x restaurado (max_version 61=61; paridade de contagem em 11 tabelas-chave; md5 por id ordenado de users/contracts/audit_events; superficie de leitura RLS como odca_app sem contexto de tenant em paridade; funcao SECURITY DEFINER executavel apos o restore) → derruba o banco restaurado; dump binario so em TEMP; transcript versionado evidence/d44-backup-restore-rodada-final.txt (26/26, EXIT=0).
- [x] F-2: ensaio do transporte SMTP pelo protocolo real com o worker REAL - sink MTA local sem dependencias (scripts/qa/smtp-sink.mjs: 220/EHLO/MAIL/RCPT/DATA/QUIT, grava .eml + transcript de protocolo) + orquestrador scripts/qa/rehearse-smtp-transport.ps1 (banco dedicado migrado do canonico, cenario sintetico tenant/usuario/contrato/obrigacao/lembrete/canal_email=true, worker em Development com override SMTP): fase positiva (mensagem aceita pelo sink; .eml validado: To/From, charset=utf-8 no header, corpo base64 decodificado com o link /minha-conta/notificacoes e acentos intactos; outbox sent attempt=1; in-app criada) e fase negativa (MTA parado -> falha controlada pending + attempt + last_error_code, sem perda, in-app continua) - 17/17, EXIT=0, transcript evidence/d44-smtp-transport-rodada-final.txt.
- [x] Ajuste de produto unico exposto pelo ensaio: SmtpNotificationTransport define BodyEncoding=UTF8 (o pipeline de texto do System.Net.Mail usava US-ASCII por padrao e trocava acentos do corpo por '?').
- [x] GATE final sobre a arvore: builds Release+Debug estritos 0W/0E, Domain 389/389, suite -FreshDb 234/234 TEST-EXIT=0, assets exit=0 (10 assets, 19 chaves, 4 catalogos); d41 nao reexecutada de proposito (diff nao toca src/Odca.Web, catalogos nem assets - superficies auditadas byte-identicas a S9, onde o resultado foi 204/204).
- [x] Doc D-OC44 consolidada e STATUS.md atualizado; .odca/progress.json inalterado (sem CI oficial verde deste SHA nem aceite humano para creditar marco).
- [ ] Limitacoes registradas: o provedor SMTP real segue decisao externa (o mecanismo esta comprovado contra MTA local); a janela de retencao legal de backups permanece "a definir" na RETENTION_POLICY.md (cronograma diario/30 dias proposto para aprovacao, nao definido); deploy fisico multi-instancia e backup offsite de producao sao itens de infra externos.
- [ ] CI oficial deste SHA: pendente - branch sem push; nenhum ID de execucao inventado.
- [ ] Aceite humano da decisao de canal (SMTP vs sino in-app) e da infraestrutura de backup de producao: pendentes.
- [x] Commit unico "sem push" fechando a Sprint 10.

## Sprint 11 - dress rehearsal do piloto e veredito go/no-go (D-OC45) (18/10/2026)

- [x] F-1: dress rehearsal consolidado - run-pilot-rehearsal.ps1 ganhou os estagios autocontidos S10-BACKUP (run-backup-restore.ps1, 26/26) e S10-SMTP (rehearse-smtp-transport.ps1, 17/17): um unico comando reproduz toda a cadeia atual C-SLA -> ACEITE -> D34-JORNADA -> D37-ASSINATURA -> D38-ATENDIMENTO -> D39-SEGURANCA -> S10-BACKUP -> S10-SMTP (~18 min em maquina compartilhada; falha de estagio nao interrompe; criterio = exit code proprio).
- [x] Unica falha da rodada classificada (nao e do produto): D39-SEGURANCA saiu FAIL por assercao QA obsoleta no validate-d39-s5-web.ps1 (P0a "schema_migrations em v060", anterior a v061/S9; o checkpoint de seguranca apos W1b abortou a prova com exit 1 registrando a falha). Corrigida para v061 e reexecutada isolada (-Only D39-SEGURANCA) verde (exit=0, fails=0). Transcripts versionados: evidence/d45-dress-rehearsal-rodada-final.txt + evidence/d45-dress-rehearsal-d39-falha-classificada.txt + evidence/d45-dress-rehearsal-d39-rerun.txt.
- [x] F-2: go/no-go formal item a item contra as pre-condicoes do runbook D-OC40 sec.7.1 e a matriz sec.6 - atendidas localmente: backup/restauracao ensaiada (26/26) e ensaio+auditoria de design verdes (d41 204/204 desde S9); pendentes externas: CI oficial do SHA, transporte de assinatura, canal de aviso, usuarios convidados + aprovacao da versao, decisao de politica de sessao de 15 min. Veredito: NO-GO formal com capacidade tecnica de GO, com checklist de 6 passos para virar GO (D-OC45 sec.4).
- [x] GATE final sobre a arvore: builds Release+Debug estritos 0W/0E, Domain 389/389, suite -FreshDb 234/234 TEST-EXIT=0, assets exit=0 (10 assets, 19 chaves, 4 catalogos); d41 nao reexecutada de proposito (diff nao toca superficies auditadas).
- [x] Doc D-OC45 consolidado e STATUS.md atualizado; .odca/progress.json inalterado (40% - sem CI oficial deste SHA nem aceite humano para creditar marco).
- [ ] Limitacoes externas mantidas: CI oficial nunca verde publicada; provedor de assinatura "proposto, nao escolhido"; SMTP real ainda decisao (mecanismo comprovado em S10); deploy fisico multi-instancia e backup offsite de producao; janela legal de retencao "a definir"; divida i18n pt-first das areas operacionais.
- [ ] CI oficial deste SHA: pendente - branch sem push; nenhum ID de execucao inventado.
- [ ] Aceite humano (convite do piloto + aprovacao explicita da versao): pendente - decisao do responsavel.
- [x] Commit unico "sem push" fechando a Sprint 11.

## Sprint 12 - conteudo das notificacoes no idioma do destinatario + backup pre-piloto com retencao (D-OC46) (18/10/2026)

- [x] F-1: conteudo das notificacoes no idioma preferido do destinatario — v062 (coluna `language varchar(16) NOT NULL DEFAULT 'pt-BR'` + CHECK das 4 culturas em `odca.notification_outbox`; `claim_notification` retorna o idioma; `complete_obligation_reminder`/`complete_renewal_reminder` com CASE por idioma via LEFT JOIN em `user_notification_settings`, default pt-BR; checksum `11168e2afba1b6d9449696aa52e88f294c1556372631bceefa19277528465380`, 62 blocos, snapshot `database/releases/odca-v062.sql` byte-idêntica, `CurrentVersion` 61→62); resolucao na origem do evento (`NotificationEnqueue.RecipientLanguageAsync` com/sem transacao, nunca lanca → fallback pt-BR; `EnqueueEmailAsync` grava a coluna com paridade titulo×coluna por construcao); 19 chaves ×4 (`notif.title.*` ×10 + `notif.body.*` ×9, delta `scripts/i18n/d-s12-notifications.json`) + `notif.lang_help` atualizada → 2746 chaves/cultura, paridade 4/4; `INotificationTransport.SendUserNotificationAsync` com `language` nos 4 transports (SMTP localiza via `LocalizationService` por DI; FilePickup registra "Idioma:" no artefato; Stub ignora); 8 call sites migrados (ContractStudio ×4, ReviewRequests ×2, Solicitations ×2); overloads legados pt-BR preservados para testes.
- [x] F-2: backup pre-piloto com retencao — `run-backup-restore.ps1 -KeepDumpDir <dir>` copia o dump validado apenas DEPOIS de todos os checks de paridade, com carimbo `yyyy-MM-dd_HHmmss` e bytes+sha256 no log (26+1 = 27 checks); sem o parametro o comportamento e identico ao S10 (dump so em TEMP).
- [x] Testes: `NotificationFeedTests.NotificationContentFollowsRecipientPreferredLanguage` (seed en-US + canal e-mail ativo → titulo armazenado em ingles + `outbox.language='en-US'`) + fato de migracao v062 (snapshot byte-idêntica, checksum, blocos 1-61 invariantes; teste V061 ajustado) + smoke ao vivo das funcoes SQL por idioma (titulos e datas en-US/pt-BR).
- [x] Provas ao vivo: backup/restore 27/27 EXIT=0 com dump retido `odca_test_disposable-pre-pilot-2026-10-18_172328.dump` (989512 B, sha256 `f0ec4fbc…400e7`; transcript d44 regenerado); ensaio SMTP com worker real + sink MTA local estendido por `-RecipientLanguage` opcional: pt-BR 17/17 (baseline S10 preservado, mesmo fluxo sem o parametro), en-US 20/20 e fr-FR 20/20 (acento sobrevivendo ao transporte); transcript versionado commitado = rodada en-US 20/20.
- [x] GATE final sobre a arvore: builds Release+Debug estritos 0W/0E, Domain 389/389, suite -FreshDb 236/236 TEST-EXIT=0, assets exit=0 (2746 chaves/cultura, paridade 4/4, 80 valores S12 revalidados byte-a-byte); d41 nao reexecutada de proposito (nenhuma rota auditada renderiza chave nova/alterada; vistas/assets byte-idênticas as rondas onde 204/204 e valido).
- [x] Resolve a divida registrada na S8: "conteudo (titulo/corpo) das notificacoes permanece em pt-BR no armazenamento" — agora segue o idioma do destinatario (default pt-BR) do armazenamento ate o SMTP.
- [x] Doc D-OC46 consolidada e STATUS.md atualizado; .odca/progress.json inalterado (40% - sem CI oficial deste SHA nem aceite humano para creditar marco).
- [ ] Limitacoes registradas: strings das funcoes SQL mantem ortografia sem acento (estilo consistente dessas funcoes); convites por e-mail seguem pt-BR default (evento pre-conta, fora de escopo de F-1); divida i18n pt-first das areas operacionais permanece aberta (D-OC42/43); SMTP em nao-pt-BR provado pela extensao do ensaio + coluna testada (provedor real ainda decisao externa).
- [ ] CI oficial deste SHA: pendente - branch sem push; nenhum ID de execucao inventado.
- [ ] Aceite humano dos 4 idiomas do conteudo de notificacoes: pendente.
- [x] Commit unico "sem push" fechando a Sprint 12.

## Sprint 13 - central contratual do cliente em 4 idiomas: renovacoes + revisoes + acoes (D-OC47) (18/10/2026)

- [x] F-1 views: as 5 views da central contratual em pt-BR/en-US/es-ES/fr-FR no padrao canonico (CultureMiddleware + `L.T`): `Renewals/Index` e `Renewals/Detail` (cards de resumo, lembretes, filtros, linhas com status/kind/prioridade, confirmacoes de aplicacao, vigencia atual x proposta, formalizacao, acao condicional por estado/perfil, linha do tempo), `Reviews/Index` e `Reviews/Detail` (filtros/escopo/responsavel, fila com 6 colunas, resumo com nota de escopo de versao, proxima etapa apos aprovacao interna, mensagens/notas com visibilidade, historico, reatribuicao, decisao aprovar/solicitar ajustes, cancelamento), `Actions/Index` (resumo por categoria, filtros por fonte, sourceLabel, prazo, acao, responsavel inline, paginacao).
- [x] F-1 datas/moeda: `.ToString("dd/MM/yyyy")` fixo sai das UIs — `CulturedTime.Date/DateTime(value,_c)` (ordem da data por lingua ativa, relogio 24h, fallback pt-BR) e valores `N2` com `CultureInfo` da cultura ativa; formato canonico dd/MM/yyyy do documento (PDF/papel) preservado (invariante D-OC22).
- [x] F-1 controllers BFF: injecao canonica de `LocalizationService` (propriedade `Culture` + helper `L`) em `RenewalsController` (17 strings: ModelState de periodo, not-found, conflitos 409, submit/cancel/formalize/reminders/apply), `ReviewsController` (9: titulos, load error, decisao/cancelamento/atribuicao/mensagem) e `ActionsController` (2: titulo + falha de carga); `msg_applied_until` recebe data ja formatada culturalmente.
- [x] Melhoria registrada: badge de status em `Reviews/Detail` que exibia o codigo cru (`@Model.Status`) agora exibe o label localizado; classe CSS `status-@Model.Status` inalterada.
- [x] i18n: delta versionado `scripts/i18n/d-s13-contratos.json` com **235 chaves x 4** (`ren.*` ~104, `rev.*` ~77, `actions.*` 13, `common.*` +21) + atualizacao da chave existente `ren.how_note` (prefixo+corpo para preservar o `<strong>`; edicao direta dos 4 JSONs) -> catalogos em **2981 chaves/cultura, paridade 4/4** (1210 chaves de todos os deltas x 4 = 4840 comparacoes, 0 divergencias). Politica: status neutros compartilhados (`common.status_*`), status com genero por area, reuso de ~30 chaves `common.*` existentes.
- [x] F-2 auditoria de design estendida: `audit-d41-design.ps1` 17 -> 20 rotas (+renovacoes, +solicitacoes, +acoes do cliente x4 idiomas = 63 pares x 4 viewports = **252 medidas**) -> execucao ao vivo `AUDIT-DONE checks=252 ok=252 overflow=0 authfail=0 httpfail=0 (exit=0)`, 0 PNGs de falha; transcript commitado `evidence/d41-design-audit-s13-rodada-final.txt`.
- [x] Confirmacao estrutural: dif de esqueleto (tag+nomes de atributos, HEAD x nova) = 0 remocoes/0 adicoes nos 5 views; varredura de nao-ascii residual nos 8 arquivos = apenas separadores neutros originais (· — → ⚠); zero literal de UI pt restante.
- [x] Classificacao QA (nao e do produto): o diff byte-a-byte do delta capturou ANTES do GATE 5 typos (pt "Substitida/Substitida-circumflexa" -> Substituída; pt "Alterção" -> "Alteração" x3; ES "e inicio" -> "y inicio"; FR concordancia "enregistrée" -> "enregistré"; EN "; The" -> "; the") e o confronto com o codigo-fonte revelou 6 chaves ausentes adicionadas ao delta (`st_awaiting_formalization`, `cards_aria`, `how_note_prefix`, `msg_cancelled_ok`, `msg_reminders_saved`, `msg_record_changed`) — corrigidos com paridade revalidada e amostras de bytes no catalogo (ex.: "Substituída" termina c3 ad).
- [x] GATE final sobre a arvore: builds Release+Debug estritos 0W/0E, Domain 389/389, suite -FreshDb 236/236 TEST-EXIT=0, assets exit=0 (10 assets, 19 chaves d41, 2981 chaves/cultura paridade 4/4); SMTP/backup nao reexecutados de proposito (sem diff nesses dominios; rodadas S12 seguem validas).
- [ ] Limitacoes registradas: paginas de detalhe individuais (renovacao/revisao) sem rota fixa na d41 — cobertas por build estrito + varredura + dif de esqueleto (risco avaliado baixo); divida i18n pt-first restante (controllers Accounts/Customers/Organizations/Imports/Onboarding/Modules/Inbox/Consumption + areas operacionais, D-OC42/43); CI oficial nunca verde publicada; decisoes externas mantidas (assinatura, SMTP real, convidados+aprovacao, sessao 15 min, retencao de backups — D-OC45 sec.4).
- [ ] CI oficial deste SHA: pendente - branch sem push; nenhum ID de execucao inventado.
- [ ] Aceite humano dos 4 idiomas da central contratual: pendente.
- [x] Commit unico "sem push" fechando a Sprint 13.

## Sprint 14 - central de organizacao + visao geral do cliente + funil anonimo de onboarding em 4 idiomas (D-OC48) (18/10/2026)

- [x] F-1 views: as 10 views da area em pt-BR/en-US/es-ES/fr-FR no padrao canonico (CultureMiddleware + L.T): `Organizations/Team` (abas pessoas/convites/perfis com busca e filtros, badges localizados via delegadas, confirmacoes com motivo padronizado, transferencia de administracao, drawer de convite, drawer de perfil com matriz de permissoes completa e estados por plano/bloqueio/franquia, drawer de perfis do membro, help card), `Organizations/Edit` (cadastro com controle de versao + alerta 409, perfil de atuacao, estado atual), `Organizations/Index` (picker de organizacao com filtro client-side, cards de contexto, empty states filtro x acesso), `Organizations/Accept` (aceite anonio de convite: preview do destino em fragmentos strong, validade datada cultural, estados link incompleto/sem preview, chamada p/ login reutilizando support.imp_enter), `Onboarding/CustomerHome` (faixa suspensa/situacao comercial, CTAs, documentos recentes e revisoes atribuidas no fuso do tenant com badges localizados, metricas e pendencias acionaveis), `Onboarding/GettingStarted` (progresso real, 5 etapas com estado/responsavel/acao), `Onboarding/Register` (rotulos explicitos localizados substituindo DisplayNameFor - o label agora envolve o controle; checkboxes termos/privacidade/marketing; plano Intermediario localizado; Basic/Enterprise literais), `Onboarding/ConfirmEmail` + `RegistrationStarted` (confirmacao + recebimento c/ token de transporte de desenvolvimento) e `Home/Index` (visao global: saudacao, 14 indicadores, acoes rapidas, ultimos eventos de auditoria c/ CulturedTime, guia).
- [x] F-1 datas: `.ToString("dd/MM/yyyy HH:mm")`/`"g"` fixos saem das UIs -> `CulturedTime.DateTime(value,_c)` (ordem da data por lingua ativa, relogio 24h, fallback pt-BR); horario exibido preservado onde o original convertia zona (CustomerHome: `TimeZoneInfo.ConvertTime(..., tzTenant)`; Team/Accept/Home: `.ToLocalTime()` como no HEAD) - conferido ponto a ponto contra a versao original; formato canonico dd/MM/yyyy do documento (PDF/papel) intacto (invariante D-OC22).
- [x] F-1 controllers BFF + helper: injecao canonica de `LocalizationService` (propriedade Culture + helper privado L) em `OrganizationsController` (41 call sites: equipe/convites/perfis, transferencia, edicao c/ conflito de versao, aceite) e `OnboardingController` (17: erros de carga da visao geral, primeiros passos, registro, confirmar email); mensagens comuns de erro unificadas em `api.too_many_attempts`/`api.service_unavailable` (UserFacingError migrou de chaves proprias duplicadas); `TenancyDisplay` ganha overloads cientes de cultura (Func<string,string>) para MemberStatus/InvitationStatus/DeliveryStatus/OrganizationStatus/DeliveryErrorMessage/RolePresentation/PermissionLabel (`perm.<codigo com underscores>`) e PermissionGroup (13 slugs estaveis `permg.<slug>`), com fallback para o valor legado quando T devolve a propria chave; overloads legados de 1 arg preservados; "Administrador da organizacao" continua marcador deterministico do dominio -> exibicao org.role_superadmin.
- [x] i18n: delta versionado `scripts/i18n/d-s14-organizacao.json` com **423 chaves x 4** (org.* 213, perm.* 50, onb.* 50, dash.* 36, gs.* 35, reg.* 21, permg.* 13, conf.* 3, api.* 2) -> catalogos em **3404 chaves/cultura, paridade 4/4** (delta-catalogo 1692 comparacoes, 0 divergencias; checagem das 430 chaves unicas referenciadas no codigo vs catalogos = 0 ausentes). Politica de reuso: common.*/action.*/documents.*/patient.*/obl.status_all/actions.item_action/chaves flat visao-global+organizacoes/badges rev.st_* + common.status_*; "Entrar" = support.imp_enter.
- [x] F-2 auditoria de design estendida: `audit-d41-design.ps1` 20 -> 28 rotas (+org-picker/equipe/editar-org/visao-geral-cliente/primeiros-passos sessao cliente x4 idiomas; +aceitar-convite/contratar/confirmar-email sessao anonima x4 idiomas - caminhos gravados so apos conferir os [HttpGet] reais dos controllers) = 95 pares rota x idioma x 4 viewports = **380 medidas** (era 63 pares / 252) -> execucao ao vivo sobre o banco descartavel `AUDIT-DONE checks=380 ok=380 overflow=0 authfail=0 httpfail=0 (exit=0)`, 0 PNGs de falha; transcript commitado `evidence/d41-design-audit-s14-rodada-final.txt`.
- [x] Preservacao estrutural: dif de multiset de tags (HEAD x arvore) nos 10 views = 0 remocoes/0 adicoes em 9 arquivos; CustomerHome +2 <span aria-hidden> proposital (seta literal encapsulada); varredura de nao-ascii residual nos 13 arquivos do escopo = apenas glifos neutros originais (- x) e catalogo legado do TenancyDisplay (pt-BR canonico por design, usado como fallback).
- [x] Classificacao QA (nao e do produto): (1) o primeiro sync line-based dos catalogos corrompeu o pt-BR.json antes do merge (virgulas finais perdidas em 10 linhas) -> sync reescrito como parse/mutate/reserialize + reparo byte-a-byte + paridade 4/4 revalidada; (2) bug de zona em 3 datas onde o original exibia relogio local (datas de convite do Team x2 e validade do Accept x1) - helper recebendo valor sem .ToLocalTime() - detectado ao conferir o comportamento do CulturedTime contra o original e corrigido antes do GATE.
- [x] GATE final sobre a arvore: builds Release+Debug estritos 0W/0E, Domain 389/389, suite -FreshDb 236/236 TEST-EXIT=0, assets exit=0 (3404 chaves/cultura, paridade 4/4); SMTP/backup nao reexecutados de proposito (sem diff nesses dominios; rodadas S12 seguem validas).
- [ ] Limitacoes registradas: estados internos dos paineis da equipe (drawer de convite/perfil/perfis do membro = mesma rota equipe) e POSTs do funil anonimo sem rota fixa na d41 - cobertos por build estrito + varredura + dif estrutural + suite inalterada (risco avaliado baixo); divida i18n pt-first restante ~370 linhas por heuristica (controllers/views PlatformLibrary, Customers, Imports, Consumption, Plans/Modules, Account, Inbox, PlatformAudit/Diagnostics/Operations); atributos [Required(ErrorMessage)] seguem pt-first (política cross-area, sem acesso ao L); mensagens de dominio vindas da API permanecem em pt (pendencias da visao geral, mensagem do cadastro recebido); CI oficial nunca verde publicada; decisoes externas mantidas (assinatura, SMTP real, convidados+aprovacao, sessao 15 min, retencao de backups - D-OC45 sec.4).
- [ ] CI oficial deste SHA: pendente - branch sem push; nenhum ID de execucao inventado.
- [ ] Aceite humano dos 4 idiomas da central de organizacao + visao geral + funil anonimo: pendente.
- [x] Commit unico "sem push" fechando a Sprint 14.
