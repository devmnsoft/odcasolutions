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

## Sprint 15 - area de administracao de plataforma em 4 idiomas (D-OC49) (19/10/2026)

- [x] F-1 views: as 7 views da area em pt-BR/en-US/es-ES/fr-FR no padrao canonico (CultureMiddleware + L.T): `PlatformLibrary/Index` (catalogo oficial global: publicar/retirar com justificativa 5-500 auditada, versoes por modelo, linhas de revisao datadas, TypeLabels c/ fallback do valor cru, help card), `PlatformLibrary/Create` (residuo: eyebrow layout.platform, placeholders nome/descricao/proposito, hint de categorizacao, 7 opcoes de tipo + reuso studio.purpose_consent), `PlatformLibrary/NewRevision` (h1/meta/efeito da publicacao com strong em fragmentos, editor de campos ~30 rotulos, conteudo estruturado c/ badge Legal Design, cancelamento action.cancel, publicar c/ confirm() localizado e **bolsa i18n JS no padrao do Studio** - window.odcaI18n via Html.Raw + UnsafeRelaxedJsonEscaping e shim window.OdcaI18n.t com args {n}; script do editor inteiro sem pt hard-coded: sem campos, origens curtas, condicao "; obrigatorio quando {0} = {1}" c/ join " ou ", chips obrigatorio/ajuda, inserir/remover, catalogo de propriedades x9, opcoes-semente, 5 alertas, banner editor limitado titulo/a/b, bloqueio de publicacao), `PlatformAudit/Index` (busca/filtros/por-pagina, 7 cabecalhos, badges Sucesso/Negado/Falhou c/ classes CSS inalteras, Metadados, empty states, mostrando X-Y de Z / paginacao), `PlatformDiagnostics/Index` (8 dependencias + corrente honesta, 4 status + not_applicable -> common.na, filas c/ mais antigo pendente), `PlatformOperations/Index` (matriz SLA vigente: PlanLabel Basic/Enterprise literais + Intermediario reg.plan_intermediate, servicos x4, prioridade common.prio_*, h/min com InvariantCulture, calendario dias uteis -> admin.sla_business_days, Vigente/Desativada, help "como ler esta matriz") e `Modules/Index` (eyebrow por perfil, estados disponivel/em construcao, proximo passo, dica de menu, voltar ao painel).
- [x] F-1 datas: `"dd/MM/yyyy"` fixo sai das UIs -> `CulturedTime.Date(value.ToLocalTime(),_c)` (biblioteca) e `CulturedTime.DateTime(occurredAt.ToLocalTime(),_c)` (auditoria/diagnostico) preservando o relogio exibido no HEAD (conferido ponto a ponto); numericos/paginacao/versoes -> ToString(InvariantCulture); formato canonico dd/MM/yyyy do documento (PDF/papel) intacto (invariante D-OC22).
- [x] F-1 controllers BFF: injecao canonica de LocalizationService em 4 controllers - `PlatformLibraryController` (16 call sites: titulos, validacao nome/resumo/justificativa, sucessos criado-em-rascunho/**Revisao {0} publicada** c/ invariante de versoes imutaveis/retirado/publicado-global, falhas UserMessage c/ fallback localizado; JSON de conteudo inicial do template segue pt = conteudo de documento), `ModulesController` (catalogo estatico slug -> array de chaves resolvido por request com a cultura ativa; isolamento tenant x plataforma preservado - slug "contratos" existe nos dois perfis com titulos distintos; OpenHref so em auditoria; NotFound por perfil inalterado), `PlatformDiagnosticsController` + `PlatformOperationsController` (titulos localizados) = **310 call sites** nos 11 arquivos; teste de dominio que instanciava ModulesController ganhou o servico no construtor (cultura ausente -> fallback pt-BR; assercoes intactas).
- [x] i18n: delta versionado `scripts/i18n/d-s15-plataforma.json` com **216 chaves x 4** (platform.lib.* 34, platform.rev.* 45, platform.rev_js./rev_prop.* 31, module.* 43, platform.diag.* 25, platform.audit.* 16, platform.sla.* 18, platform.tpl.* 4) -> catalogos em **3620 chaves/cultura, paridade 4/4** (delta-catalogo 864 comparacoes, 0 divergencias; 241 chaves unicas referenciadas no codigo vs 4 catalogos = 0 ausentes; 33 module.* referenciadas indiretamente pelos arrays presentes nos 4). Politica de reuso: chaves flat + layout.platform/support.eyebrow+back_panel/common.*/dash.th_*/studio.ft_* x7/studio.purpose_consent/studio.status_published/studio.cat_search_label/studio.tpl_unmap/action.cancel/reg.plan_intermediate/admin.sla_business_days; literais registrados: Basic/Enterprise, h/min, Legal Design, identificador_unico, glifos neutros.
- [x] F-2 auditoria de design estendida: `audit-d41-design.ps1` 28 -> **39 rotas** - 4 rotas superadmin legadas (atendimento-odca, suporte-sessoes, plataforma-notificacoes, auditoria) passam de pt-BR para 4 idiomas (+12 pares) e entram 11 rotas novas x4 (superadmin: biblioteca, biblioteca-novo, biblioteca-revisao c/ fixture services-agreement published rev 1, sla-configuracoes, diagnostico; modulo-contratos/documentos/ajuda sessao cliente; modulo-usuarios/auditoria/biblioteca-modelos sessao superadmin) = 151 pares rota x idioma x 4 viewports = **604 medidas** (era 95 pares / 380) -> execucao ao vivo sobre o banco descartavel `AUDIT-DONE checks=604 ok=604 overflow=0 authfail=0 httpfail=0 (exit=0)`, 0 PNGs de falha; transcript commitado `evidence/d41-design-audit-s15-rodada-final.txt`.
- [x] Preservacao estrutural: dif de multiset de tags (HEAD x arvore) nos 7 views = 0 remocoes/0 adicoes em 6 arquivos; NewRevision +2 <span aria-hidden> (separadores encapsulados) + 2 <script> da bolsa i18n - proposital; varredura de nao-ascii residual nos 11 arquivos = apenas glifos neutros originais, comentarios C#/JS, aria-label global de breadcrumb (divida S17) e o JSON de conteudo de documento do template.
- [x] Classificacao QA (nao e do produto): (1) merge pos-git-checkout encontrou CRLF do smudge onde o canon e BOM+LF -> normalizacao \r\n->\n na carga + revalidacao BOM/LF/sem quebra final antes de escrever; (2) checagem verbatim de integridade de acentos (15 trechos) relida contra git show HEAD: pois o working tree ja estava editado; (3) cross-check de chaves apos escrita das views revelou 2 ausentes do delta (platform.rev_js_origin_contractor, platform.sla_th_priority) -> delta regenerado 214->216 e catalogos remergiados c/ paridade 3620 x4/4 revalidada; (4) transcricao removeField splice(index,index)->splice(index,1) corrigida no review pre-GATE; amostras byte-a-byte (c=c3a7, a=c3a3, o=c3b5, i=c3ad) e varredura de double-encoding (Â/Ã, U+FFFD) = 0 nos 4 catalogos.
- [x] GATE final sobre a arvore: builds Release+Debug estritos 0W/0E (sln -warnaserror), Domain 389/389, suite -FreshDb 236/236 TEST-EXIT=0, assets exit=0 (10 assets, 19 chaves d41, 3620 chaves/cultura paridade 4/4); SMTP/backup nao reexecutados de proposito (sem diff nesses dominios; rodadas S12 seguem validas).
- [ ] Limitacoes registradas: POSTs e estados internos dos fluxos (criacao de modelo, publicacao de revisao c/ confirmacao, retirar/publicar c/ justificativa, filtros da auditoria aplicados) sem rota fixa na d41 - cobertos por build estrito + varredura + dif estrutural + suite inalterada (risco avaliado baixo); divida i18n pt-first restante particionada em S16 gestao de clientes (Customers x4 + controller, Consumption + VMs, Plans, Inbox + controller, usos de StatusLabels) e S17 conta e residuo (Account x7 + controller + VMs MFA/ChangePassword, residuo Solicitacoes, BffErrorHandlingMiddleware, endonimo do _Layout, NavigationRegistry 30 rotulos + renderer, aria-label de breadcrumb); [Required(ErrorMessage)] segue pt-first (politica cross-area); d.Detail do diagnostico vem da API em pt; console.error cru (console de desenvolvedor); CI oficial nunca verde publicada; decisoes externas mantidas (assinatura, SMTP real, convidados+aprovacao, sessao 15 min, retencao de backups - D-OC45 sec.4).
- [ ] CI oficial deste SHA: pendente - branch sem push; nenhum ID de execucao inventado.
- [ ] Aceite humano dos 4 idiomas da area de administracao de plataforma: pendente.
- [x] Commit unico "sem push" fechando a Sprint 15.

## Sprint 16 - gestao de clientes em 4 idiomas: Customers + consumo + planos + caixa (D-OC50) (19/10/2026)

- [x] F-1 views: as 7 views em pt-BR/en-US/es-ES/fr-FR no padrao canonico (CultureMiddleware + L.T): `Customers/Index` (listagem: filtros plano/situacao/busca, tabela 7 colunas c/ dual Status+consumo OCR/assinatura c/ 4 args InvariantCulture, atividade CulturedTime.Date, help card), `Customers/Users` (busca de usuarios da plataforma, empty states geral+match {0}, situacao org.mem_active/blocked), `Customers/Create` (3 secoes org/plano/admin c/ notas pre/role/post em strong/em, select plano, help card), `Customers/Details` (ficha completa: plano c/ preview_change+limits+preserved, metricas c/ invitations_count arg, funcionalidades c/ estado switch 4 chaves+justificativas bloq/liber, situacao admin+comercial via StatusLabels overloads, solicitacoes approve/reject, concessao manual qty+unit, franquias credit_line 4 args, historico timeline, help), `Consumption/Index` (5 cards metricos c/ versao/status/seats_detail/max_file/periodo CulturedTime.Date, barra progresso, breakdown 5 itens, solicitar armazenamento 3 passos, creditos credit_line_unit 5 args, solicitacoes tabela, modulos help longo, historico, help 4 li), `Plans/Index` (cards c/ 7 limites, modulos via ModuleName->api.gate.feature_*, btn Contratar, help) e `Inbox/Index` (residuos: vistas pessoais aria/label/badge/botoes, salvar vista summary/ph/btn, DueOn CulturedTime.Date) = **329 call sites L.T**.
- [x] F-1 datas: `"dd/MM/yyyy"` fixo sai das UIs -> `CulturedTime.Date(DateTimeOffset,_c)` / `CulturedTime.Date(DateOnly,_c)` / `CulturedTime.DateTime(DateTimeOffset,_c)` preservando relogio 24h (LastActivity, PeriodStart/End, RequestedAt x2, OccurredAt x2, DueOn); numericos -> ToString(InvariantCulture); formato canonico dd/MM/yyyy do documento (PDF/papel) intacto (invariante D-OC22).
- [x] F-1 controllers BFF: injecao canonica de LocalizationService em 3 controllers - `CustomersController` (28 call sites: titulos, validacao plan/justify/grant, sucessos preview/apply/decide/grant/create {0}, falhas UserMessage c/ fallback localizado; PlanItems static->instancia c/ plan_item "{0} {1}"), `ConsumptionController` (2 call sites: err_request/ok_request), `InboxController` (10 call sites: titulos, vistas salvar/padrao/inativar, err_open) = **40 call sites** no total; + StatusLabels.cs sobrecargas Status(v,t)+Commercial(v,t) no padrao TenancyDisplay (8 t(), legado pt preservado). Total **377 call sites** em 11 arquivos.
- [x] i18n: delta versionado `scripts/i18n/d-s16-clientes.json` com **231 chaves x 4** (cust.* 131, cons.* 41, usage.* 17, plans.* 19, inbox.* 17, status.* 6) -> catalogos em **3851 chaves/cultura, paridade 4/4** (delta-catalogo 924 comparacoes, 0 divergencias; 308 chaves unicas referenciadas vs 4 catalogos = 1232 comparacoes / 0 ausentes; 8 chaves dinamicas dos overloads StatusLabels presentes nos 4). Politica de reuso: labels flat (visao-global/inicio/plano-consumo/localizar-usuarios), common.*/studio.cat_all/studio.ver_lower_version/documents.filter_search/action.search/action.cancel/reg.lbl_plan/reg.plan_intermediate/org.orgst_active+suspended/org.mem_active+blocked/org.ps_*/admin.decision/support.eyebrow/api.gate.feature_* x7 (nomes de modulo - unificacao registrada); literais: Enterprise, "de" (preposicao sem chave), glifos neutros —/·/✓, CNPJ/CPF placeholder, valores de dominio crus.
- [x] F-2 auditoria de design estendida: `audit-d41-design.ps1` 39 -> **46 rotas** - 7 rotas novas x4 idiomas (superadmin: clientes/clientes-novo/clientes-usuarios/cliente-detalhe; cliente: planos/plano-e-consumo/caixa) = 179 pares rota x idioma x 4 viewports = **716 medidas** (era 151 pares / 604) -> execucao ao vivo sobre o banco descartavel _[transcript d41-design-audit-s16-rodada-final.txt]_.
- [x] Preservacao estrutural: dif de multiset de tags (HEAD x arvore) nos 7 views = 0/0; varredura de nao-ascii residual nos 11 arquivos = apenas glifos neutros (—=U+2014, ·=U+00B7, ✓=U+2713, ⚠=U+26A0), comentarios C# e StatusLabels legado pt; double-encoding (Â/Ã, U+FFFD) = 0 nos 4 catalogos.
- [x] Classificacao QA (nao e do produto): (1) CS1503 int/long->string em L.T (params string[]) - 21 erros encontrados no build Release pre-GATE; correcao ToString(InvariantCulture) em todos os argumentos numericos (padrao S15); (2) indentacao irregular pre-existente no CustomersController (5 spaces) - edições via substring matching; (3) PowerShell inline node -e tokenizava mal regex c/ parenteses - padronizado para .mjs scripts.
- [x] GATE final sobre a arvore: builds Release+Debug estritos 0W/0E (sln -warnaserror), Domain 389/389, suite -FreshDb 236/236 TEST-EXIT=0, assets exit=0 (10 assets, 19 chaves d41, 3851 chaves/cultura paridade 4/4); SMTP/backup nao reexecutados de proposito (sem diff nesses dominios).
- [ ] Limitacoes registradas: POSTs e estados internos dos fluxos (criacao organizacao, preview/apply plano, bloquear/liberar feature, mudanca situacao, aprovar/rejeitar solicitacao, conceder armazenamento, solicitar armazenamento, salvar/padrao/inativar vista) sem rota fixa na d41 - cobertos por build estrito + varredura de chaves + dif estrutural + suite inalterada (risco avaliado baixo); divida i18n pt-first restante em S17 conta e residuo (Account x7 + controller + VMs MFA/ChangePassword, residuo Solicitacoes, BffErrorHandlingMiddleware, endonimo do _Layout, NavigationRegistry 30 rotulos + renderer + aria-label breadcrumb global + "Resumo do plano" Consumption); [Required(ErrorMessage)] segue pt-first; d.Detail/ErrorTitle da API em pt; CI oficial nunca verde publicada; decisoes externas mantidas (assinatura, SMTP real, convidados+aprovacao, sessao 15 min, retencao de backups - D-OC45 sec.4).
- [ ] CI oficial deste SHA: pendente - branch sem push; nenhum ID de execucao inventado.
- [ ] Aceite humano dos 4 idiomas da gestao de clientes + consumo + planos + caixa: pendente.
- [x] Commit unico "sem push" fechando a Sprint 16.

## Sprint 17 - conta e resíduo em 4 idiomas: Account + MFA + navegação + importações + fallback global (D-OC51) (19/10/2026)

- [x] F-1 views de conta (7) no padrao canonico (_culture + L.T): Login (toggle de senha via data-toggle-show/hide + data-processing-label), Conta (senha/MFA pendente-ativo/notificacoes/help card), ChangePassword, MfaEnrollment (QR/chave manual/URI TOTP/regenerar/ajuda), MfaChallenge, RecoveryCodes (aria-lista/continuar), AccessDenied - 62 call sites.
- [x] F-1 controller/middleware: AccountController 19 chaves (4 switches por ApiCallStatus: login/troca-senha/inscricao/desafio + ok_change_pass + regen_failed + aviso logout sem revogacao remota confirmada) + BffErrorHandlingMiddleware injeta LocalizationService no construtor primario (fallback JSON + HTML bruto bff.unavailable_*/bff.btn_*, html lang dinamico culture ?? pt-BR - middleware roda antes da CultureMiddleware, correto por construcao; WriteFallbackHtmlAsync static->instancia).
- [x] F-2 Central de importacoes: Imports/Index (34 L.T: header c/ reuso group_contratos + inbox.how_to_use, nota disponivel-em-breve c/ strong, filtros c/ reuso 6 chaves canonicas (documents.filter_from/to, action.filter, studio.cat_retry, inbox.type_all), erro de carregamento c/ passthrough do detalhe, empty state, lista c/ solicitada-por + data culturalizada CulturedTime.DateTime (substitui dd/MM/yyyy HH:mm fixo), etapa atual + 3 mensagens de retomada, botoes continuar-revisao/abrir, ImportDiagnostic 6 chaves como funcao local capturando a cultura), Imports/Review (45 L.T: eyebrow/sub/help, progresso 5 passos + tabs mobile aria, resumo summary_count+summary_states {0} InvariantCulture c/ strong preservado, painel do documento iframe-title/help viewer/security_status passthrough, formulario conferencia manual 6 rotulos + ajuda + botao, sugestoes metodo-estado/extraido/escolhido-c/ hint/trecho origem/pagina-nao-identificada/decisoes aceitar-editar-rejeitar, extracao-ausente, confirm() x2 c/ chaves dedicadas e apostrofo FR U+2019), ImportsController 11 chaves (titulos Index/Review, data-range, load fallback, manual save x2, confirm x3 c/ ja-confirmada+x-importada, cancel x2). Total 90 call sites.
- [x] F-3 navegacao: 4 chaves que faltavam no catalogo (group_inicio, group_operacoes, group_minha_organizacao_e_plano - formula exata do renderer com underline e acentos preservados - , central-notificacoes); verificacao programatica contra NavigationRegistry.cs real em disco (42 items + 7 grupos + 17 aria) = **66/66 nos 4 catalogos** por codepoint (NAV-ALL-PASS). Renderer/_Layout sem mudanca de C#.
- [x] F-3 residuo + forms.js: 13 aria-labels residuais em 10 views (Solicitacoes x3 c/ sol.filters_aria+sol.list_aria, Modules, PlatformAudit, PlatformLibrary x2, PlatformOperations via reuso common.breadcrumb_aria x8, Solicitacoes/Detalhes admin.summary_aria, Contracts/Sheet sheet.details_aria, Consumption cons.plan_summary_aria - linha revertida antes do commit S16 e re-aplicada como dif desta sprint); forms.js toggle de senha le data-toggle-show/hide com fallback PT (pt-first by design); varredura final global de toda a UI Razor = 0 literal pt-BR visivel (52 linhas acentuadas = 49 comentarios + 3 endonimos do seletor de idioma, mantidos por convencao).
- [x] Delta d-s17-conta-residuo.json: 163 chaves x 4 = 652 entradas (83 conta/residuo/nav + 80 importacoes) validadas por codepoint; merge idempotente 0 colisoes; catalogos 3851->4014 paridade 4/4 BOM+LF 0 CR 0 \uXXXX literal 0 double-encoding.
- [x] GATE sobre a arvore: builds sln -warnaserror 0W/0E Release+Debug, Domain 389/389 (middleware c/ LocalizationService injetada no teste), Integration -FreshDb 236/236 TEST-EXIT=0 (rodada limpa apos o d41 r3 liberar o Postgres), check-assets exit=0, chaves usadas (22 arquivos: 18 S17 + 4 dif S16) = 820 unicas vs catalogo real 4014 -> 0 ausentes (KEYS-ALL-OK), chaves de navegacao 66/66 NAV-ALL-PASS.
- [x] Classificacao QA (nao e do produto): (1) corrupcao de canal nos escapes \uXXXX do gerador v3 (3 chaves group_* c/ backslash literal + 2 valores vazios) -> regeneracao 100% ASCII + String.fromCharCode + validacao por codepoint; (2) espaco x underline na chave de grupo (delta/catalogo carregaram espaco literal, formula do renderer converte p/ underscore) -> corrigido delta + 4 catalogos, reverificado por codepoint (GROUPS-4x-PASS); (3) CS7036 no teste de dominio (construtor novo do middleware) -> injecao new LocalizationService(), nenhuma assercao mudou; (4) flake OutboxLeaseClaimTests sob carga do Postgres compartilhado c/ audit -> isolado passa 2/2, rodada limpa completa apos o fim do audit; (5) catalogos lidos da copia em bin nos testes -> rebuild obrigatorio apos editar JSON fonte; (6) Estado fr-FR sem circumflexa (69,116,97,234) e apostrofo tipografico U+2019 do imp_rev.confirm_msg FR (embutido em string JS inline; U+0027 quebraria o script) - ambos validados por codepoint; (7) falha ambiental na rodada r4 do d41: o processo Web (`localhost:5144`) morreu em plena execucao (stderr vazio, sem registro de crash) e as 72 medidas marcadas eram a pagina de erro `ERR_CONNECTION_REFUSED` da Chromium (titulo "localhost", largura fixa 908px -> OVERFLOW so no viewport 768) - zero overflow/authfail nas ~690 medidas contra servidor vivo; driver endurecido (`cdp-design-audit.mjs`: `responseStatus` vazio/0 vira HTTPFAIL + circuit breaker apos 4 falhas seguidas -> exit 2), transcript da r4 salvo em `evidence/d41-design-audit-s17-r4-falha-ambiental.txt`; rodada final da sprint e a r4b (D-OC51 §7 item 7).
- [x] d41 ao vivo estendido 49 rotas = 191 pares x 4 viewports = 764 medidas: `AUDIT-DONE checks=764 ok=764 overflow=0 authfail=0 httpfail=0` (exit 0), 0 PNGs de falha - transcript commitado `evidence/d41-design-audit-s17-rodada-final.txt` (rodada final r4b; a r4 anterior classificada como falha ambiental — item QA (7) acima).
- [ ] Limitacoes registradas: GETs internos sem rota fixa na d41 (importacao/{id}/revisao, mfa/inscricao, mfa/desafio, recuperacao-codigos, alterar-senha, acesso-negado) - cobertos por build estrito + varredura global + suíte inalterada (risco avaliado baixo); divida pt-first restante reduzida a cross-camada ([Required(ErrorMessage)] ~54, mensagens de dominio da API em pt, valores de dominio crus mapeados por chave onde possivel); 64 classes CSS sem definicao em site.css (inventariadas no roadmap -> S20); CI oficial nunca verde publicada; decisoes externas mantidas (assinatura, SMTP real, convidados+aprovacao, sessao 15 min, retencao de backups - D-OC45 sec.4).
- [ ] CI oficial deste SHA: pendente - branch sem push; nenhum ID de execucao inventado.
- [ ] Aceite humano da superficie de conta + MFA + central de importacoes em 4 idiomas: pendente.
- [x] Commit unico "sem push" fechando a Sprint 17.

## Sprint 18 - fundacoes de IA: analise c/ provedor configuravel + fallback honesto (D-OC52) (19/10/2026)

- [x] F-1 projeto Odca.AI (6 arquivos): IAiService + records (AiContractInput/AiClauseFinding/AiAnalysis.Unavailable), NullAiService, HttpAiService (POST relativo chat/completions c/ base normalizada em barra final, Bearer, temperature 0, max_tokens 700, response_format json_object, system prompt cultural, truncamento MaxInputChars, degradacao para provider_unavailable em rede/timeout/status/JSON), AiResponseParser (code-fence, clamps 1200/300/40, max 12 achados / 6 categorias dedupe case-insensitive, risco low|medium|high com desconhecido->medium, slug estavel <=48, resposta sem conteudo aproveitavel => null), AddOdcaAi (http so c/ Endpoint absoluto + ApiKey; demais casos none) + InternalsVisibleTo p/ testes.
- [x] F-2 API: ContractAiController GET .../contracts/{c}/documents/{d}/versions/{v}/ai-analysis (PasswordChanged + RLS SetContext + tenant.contracts.read igual ao endpoint de documentos; query version-scoped LEFT JOIN no resultado de extracao mais recente created_at DESC,id DESC; 404 versao ausente; 200 available=false reason=no_text; sha256 hex do texto trimado + AnalyzedChars=min(len,MaxInputChars)); ReleaseFeatureGate + feature AiAnalysis (segments contracts/../documents/../versions/../ai-analysis, prioridade de extractions preservada, TryResolveFeature private->internal para teste, titulo api.gate.release_ai localizado via X-Odca-Culture); PlatformDiagnosticsController dependencia ai.provider (configured c/ modelo ou not_applicable c/ detalhe honesto; numeração 8/9 preservada).
- [x] F-3 Web: OdcaApiClient.GetDocumentAiAnalysisAsync + StudioController.DocumentAiAnalysis (contractId do rascunho no servidor, 503/timeout preservam titulo/detalhe localizados, 403/404 mapeados); Edit.cshtml data-ai-base; studio.js botao "Analisar com IA" por versao safe + startAiAnalysis (estado carregando role=status, erro passthrough da API ja localizada, no_text => .warning, indisponivel => .muted, resultado: titulo + chip risco geral, resumo, achados como cards .suggestion c/ rotulos dos 8 codigos esperados resolvidos na bolsa e codigo desconhecido exibido cru, categorias chips, rodape modelo + chars formatados culturalmente + hash(12)) - zero literal de UI (bolsa _I18nBag prefixo studio.*, partial inalterado), nenhuma classe CSS nova (reuso status-chip/suggestion/muted/success/warning).
- [x] i18n delta d-s18-ia.json: 21 chaves x4 = 84 entradas (studio.ai_* 20 + api.gate.release_ai) validadas por codepoint (FR c/ apostrofo U+2019); merge canonico idempotente 0 colisoes; catalogos 4014->4035 paridade 4/4 BOM+LF 0 CR; keyuse real studio.js 85 chaves x4 = 0 ausentes (KEYUSE-S18-OK); delta<->catalogo 84/0 divergencias; scan de corrupcao 14 arquivos (glifos especiais pre-existentes no HEAD).
- [x] Mesclador canonico: regex de validacao de chave ampliado p/ segmentos Unicode \p{L} (chaves S17 central-notificacoes e group_inicio eram recusadas — script nunca havia passado sobre o delta S17); revalidado idempotente.
- [x] Testes novos 30 na suíte de integração: AiFoundationTests.cs (parser 9, provedores 8 c/ handler falso, selecao DI 4, caminho do gate 4) + ContractAiAnalysisTests.cs (no_text; provider_unavailable c/ hash e chars conferidos; vinculacao a extracao mais recente de duas; 404 versao; 403 membro sem permissao) — suite 236 -> 266.
- [x] Build Debug -warnaserror sln: 0W/0E (13 projetos, BUILD-EXIT=0).
- [x] Integration -FreshDb: 266/266 TEST-EXIT=0 (56 s; a 1ª rodada saiu 265/266 capturando o defeito de robustez do item 5 do QA do D-OC52 — corrigido e rodada limpa reexecutada).
- [x] d41 ao vivo rodada r5 sobre a arvore S18: `AUDIT-DONE checks=764 ok=764 overflow=0 authfail=0 httpfail=0` (exit 0), 0 PNGs de falha - transcript commitado `evidence/d41-design-audit-s18-rodada-final.txt` (mesmas 764 medidas 191 pares x 4 viewports; sem rota nova — painel e lazy-load dentro da rota do estudio ja auditada; botao novo nao altera overflow).
- [ ] Limitacoes registradas: provedor real de IA pendente de decisao externa (Provider=none ate la; fallback transparente); analise stateless em S18 (cache/versionamento de analises em S19); cenarios avancados (scoring de risco, comparacao c/ template, renovacao preditiva, busca semantica, disparo via Worker) em S19; divida pt-first herdada cross-camada ([Required] ~54, mensagens de dominio da API em pt, valores crus); 64 classes CSS sem definicao -> S20; CI oficial nunca verde publicada.
- [ ] CI oficial deste SHA: pendente - branch sem push; nenhum ID de execucao inventado.
- [ ] Aceite humano do painel de analise IA + estados + fallback em 4 idiomas: pendente.
- [x] Commit unico "sem push" fechando a Sprint 18.

## Sprint 19 - IA avancada: analise inteligente de contratos (D-OC53) (19/10/2026)

- [x] F-0 migracao v063: tabela odca.document_ai_analyses (append-only por versao imutavel, content_hash char(64) liga analise<>extracao, score_value 0..100, analysis jsonb c/ score+fatores+delta, RLS tenant, GRANT SELECT/INSERT ao odca_app) + entitlement module.ia_analysis nos 3 planos publicados v2 (catalogo passa a 7/7/8 modulos) + organizacao_feature_state recriada c/ whitelist de 8 + CHECK organization_feature_blocks_code_ck estendido c/ ia_analysis + DatabaseSchema.CurrentVersion 62->63. Canonico e snapshot byte-identicos; 63 blocos revalidados; checksum final do bloco 23559ecd3608160f66691e850c7e1ddca51574c815ca2777358e214c493ffd0f.
- [x] F-0 endpoints evoluem: GET devolve a linha persistida c/ hash igual ao da extracao atual (cache, zero provedor) senao analisa ao vivo sem gravar; POST gera+persiste com idempotencia por hash + evento contract_events ai_analysis_completed; modulo fora do plano => 200 available=false module_not_enabled ANTES do provedor; gate global Features:AiAnalysis segue 503 (kill-switch de operacao); wire aditivo score{value,factors}+templateComparison{expected,detected,missing,unexpected}.
- [x] F-A/F-B/F-C nucleos deterministicos em Odca.AI: RiskScorer (low=20/medium=45/high=70 base; achado +2/+6/+12; atipica +3; clamp 0..100; fatores code/severity/weight), TemplateComparisonBuilder vs. as 8 clausulas de AiResponseParser.ClauseCodes, RenewalSuggestionBuilder (high ou penalty/price_change medium-high => renegotiate; medium => review; low => renew; sem analise => null) consumido por RenewalCenterController.LoadRenewalSuggestionsAsync (DISTINCT ON contract_id, set_config sessao p/ RLS) apenas na List (aiSuggestion em RenewalListItem; Detail inalterado).
- [x] F-D busca semantica: ListDocuments busca agora o raw_text da extracao por EXISTS ILIKE termos OR (jamais restringe o que casava antes); dois estagi - keywords deterministico sempre ativo e IA (SearchQueryParser/AiInterpretation via IAiService.InterpretSearchQueryAsync, engine=ai so se ProviderUsed=true, queda interna degrada); filtro penaltyMinPercent numerico (regex multa/penalidade X %) nos dois modos quando declarado; anotacao semantic{engine,terms[]} na resposta e nota de modo na UI (documents.search_mode_ai/keywords).
- [x] F-E Worker: hook pos-commit em ExtractOne (try/catch de fronteira, OperationCanceledException relancada) + AnalyzeExtractedAsync (gate config + organizacao_feature_state ia_analysis + cultura coalesce(preferred_language,pt-BR), skip se hash ja persistido, created_by=uploaded_by, evento ai_analysis_completed, payload camelCase Web defaults) + AddOdcaAi no Program do Worker.
- [x] Web: OdcaApiClient.AnalyzeDocumentAiAsync (POST) + action StudioController [HttpPost .../analise-ia] (mesma resolucao de rascunho/contrato e estados 404/403/503/422 da extracao); studio.js botao faz POST + cache client-side Map(documentId/versionId) c/ nota reutilizada + blocos score 0-100 c/ fatores explicaveis + delta vs. padrao (check/circulo/exclamacao) + estado module_not_enabled; Renewals/Index.cshtml cartao de sugestao (ren.ai_*) entre dl e next-action; Documents/Index.cshtml nota de modo sob o search; correcao da chave pre-existente documents.stage_all ausente nos 4 catalogos desde antes da S19 (renderizava o nome da chave).
- [x] i18n delta d-s19-ia-avancada.json: 17 chaves x4 (studio.ai_* 9 + documents.* 2 + ren.ai_* 5 + documents.stage_all 1) mescladas no mesclador canonico idempotente -> catalogos 4035->4052 paridade 4/4 BOM+LF 0 CR; keyuse real (todas t() do studio.js + L.T dos 2 views editados x4, incluindo dinamicas studio.ai_risk_{low,medium,high}) = 0 ausentes (KEYUSE-S19-OK).
- [x] Testes novos 27 na suite: AiAdvancedTests.cs (AiAdvancedDeterministicTests 20 - scorer c/ clamp e bonus atipica, delta detected/missing/unexpected, renovacao c/ escalonamento e nunca-inventa, parser de busca c/ percentual decimal e teto de termos, JSON defensivo do provedor; AiAnalysisPersistenceTests 4 - POST persiste + evento + GET/POST repetidos nao chamam o provedor novamente (contador no fake), GET sem persistida nao grava nada, module_not_enabled antes do provedor, busca chega ao texto extraido e filtra multa >10% c/ controle negativo) + MigrationChecksumTests.ReleaseV063... (blocos 1..62 byte-identicos + conteudo v063) + 2 regressao da projecao de renovacoes (item 8 do QA: materializacao Dapper c/ linha real na janela + guarda de fonte NULL::text AS AiSuggestion). Suite 266 -> 293.
- [x] Builds sln 13 projetos -warnaserror Release E Debug: 0W/0E; Domain 389/389; check-assets.mjs exit=0 (10 assets, 19 chaves modulos JS, round trips); scan codepoint U+0100-U+024F/U+FFFD em 38 arquivos = 0 alem de oe frances legitimo pre-existente fora do diff.
- [x] Integration -FreshDb sobre a arvore final: 293/293 TEST-EXIT=0 (aplica v063 em banco limpo: script unico importa sem erro).
- [x] d41 ao vivo sobre a arvore S19: `AUDIT-DONE checks=764 ok=764 overflow=0 authfail=0 httpfail=0` (exit 0), 0 PNGs de falha novos - transcript commitado evidence/d41-design-audit-s19-rodada-final.txt (rodada r8, 764 medidas completas sobre a arvore corrigida). Historico: rodada r6 (arvore pre-correcao) interrompida pelo circuit breaker apos 503 real em /renovacoes nos 4 viewports (428/764 medidas OK, 0 overflow ate la; transcript preservado evidence/d41-design-audit-s19-r6-falha-renovacoes.txt) - correcao aplicada e arvore refeita; rodada r7 (arvore corrigida) percorreu 657 medidas com 0 falhas (incl. /renovacoes OK nos 4 viewports pt-BR/en-US/es-ES) e foi interrompida por esgotamento de RAM da maquina (item 9 do QA; orfaos encerrados; transcript preservado evidence/d41-design-audit-s19-r7-interrupcao-ambiental.txt).
- [ ] Classificacao QA registrada no D-OC53 sec.9: v063 inicial sem GRANT (42501 em 4 testes incl. 2 S18) e sem CHECK estendido (administratively_blocked impossivel no novo modulo) - ambos corrigidos dentro do bloco 63 c/ novo checksum; parentese ausente no filtro EXISTS da busca (42601 pego pela suite); documents.stage_all sem chave (pre-existente) corrigida no delta; OutboxLease flake de timing sem reproducao; Worker auto-analise SEM e2e na suite (sem fixture de extracao executada) - limitacao declarada, nao cobertura; banco descartavel de auditoria com historico do 057 anterior ao commit do D-OC34 - historico alinhado ao canonico p/ aplicar o 063 preservando a continuidade das medidas d41 (base descartavel, sem efeito no caminho de produto); **bug real de produto pego pela auditoria r6: construtor do RenewalListItem c/ AiSuggestion fora da projecao da lista (Dapper materializa sob demanda - listas vazias nao constroem o deserializer) => 503 em /renovacoes c/ dados; corrigido com NULL::text AS AiSuggestion na projecao + 2 testes de regressao (materializacao c/ linha real + guarda de fonte)**.
- [ ] Limitacoes registradas: sugestao nao deriva de contract_renewal_cycles (S22) nem altera prazos/multas; multa por regex na formulacao canonica; sem embeddings (interpretacao por frase); diff literal contra modelos oficiais futura; auto-analise so pos-extracao e sem e2e; sugestao so na List; provedor real pendente de decisao externa (Provider=none ate la; UI declara o modo); divida pt-first herdada cross-camada; 64 classes CSS -> S20; CI oficial nunca verde publicada.
- [ ] CI oficial deste SHA: pendente - branch sem push; nenhum ID de execucao inventado.
- [ ] Aceite humano (score, delta, sugestao, busca semantica, auto-analise em 4 idiomas): pendente.
- [x] Commit unico "sem push" fechando a Sprint 19.

## Sprint 20 - QA de design + polimento de UX: divida CSS, auditoria UX d54 e d41 pos-CSS (D-OC54) (20/10/2026)

- [x] F-1 divida CSS zerada: inventario exato e reprodutivel (scripts/qa/inventory-css-classes.mjs + snapshot versionado scripts/qa/css-class-inventory.json) - dos 47 nomes reais usados, 0 sem definicao; bloco de 58 linhas anexado ao site.css (chaves 752/752): 46 classes do inventario + 2 classes runtime nao capturadas pelo escaneador estatico (.comment gerada por studio.js como `comment${parentId?" reply":""}` e .sec-scan_failed do CHECK security_status via `sec-${status}`); field-search ja definida no fim do arquivo (redefinir sobrescreveria display:block) e form-actions resolvido por reuso da existente form-actions-row em SupportSessions/Index.cshtml - sem classe nova alem das necessarias; os 219 candidatos brutos remanescentes do inventario sao ruido classificado (Razor em class="@(...)", propriedades CSS de blocos <style>, hexadecimais, sufixos dinamicos).
- [x] F-2 auditoria UX nova d54: drivers scripts/qa/cdp-ux-audit.mjs + audit-d54-ux.ps1 (49 rotas x 4 idiomas x (360,780)/(1280,800) = 382 medidas): contraste WCAG AA por elemento (fundo composto alfa subindo a arvore; gradiente => branco opaco; limiar 4.5), rotulo acessivel em todo field, tabela vazia exige empty-state c/ CTA em ate 4 niveis de ancestor, presenca de role=status por pagina, overflow sw>iw conta como falha, img sem alt.
- [x] d54 r1 abortada (bug no proprio probe do driver, nao produto): effBg() devolvia objeto {r,g,b} e ratio() chamava .map() nele -> excecao por pagina tratada como OK com payload vazio (sw/iw undefined) - corrigido na fonte + 3 blindagens (ratio por array rgb, throw em exceptionDetails, timeout 30s no psend); transcript preservado evidence/d54-ux-s20-r1-falha-probe.txt.
- [x] d54 r2 ok=48 uxfail=334 (1186 falhas de contraste registradas + 56 fields sem rotulo) - 5 causas raiz corrigidas: .button-quiet branco-sobre-branco (812 falhas 1.0-1.07:1 - regra era variante p/ superficie escura usada em ~100 paginas claras; espelhada para a variante ghost existente .button-secondary surface/borda/navy-900 + hover + .button:disabled{cursor:not-allowed;opacity:.55} global que nao existia); .menu-caption em tom navy sobre topbar branca (334, 2.44:1); #ef4444 inline nos botoes X de remover campo (32, 3.76:1 - NewRevision+TemplateEdit JS => var(--danger)); var(--color-success,#16a34a) com fallback claro em CustomerHome (8, 3.07:1 => var(--success)); 56 fields sem rotulo acessivel (10 labels visiveis desassociados ganharam for= em NewRevision+TemplateEdit + input[name=name] do Inbox com aria-label reutilizando a chave existente obl.view_name - sem chave nova).
- [x] d54 r3 uxfail=342 (overflow/unlabeled/imagens/vazios todos 0; pior contraste 1.0->3.28) - 2 causas raiz novas corrigidas: colisao de superficie da .menu-caption/.context-label (sidebar navy-950 vs topbar branco - 1754 falhas 3.28:1; a correcao da r2 acertou o topbar e quebrou o sidebar; resolvida por escopo: regra base --on-navy-muted 6.77:1 sobre navy-950 + override .user-menu-panel .menu-caption{color:var(--text-muted)} 5.06:1 sobre branco) e **6 chaves layout.* ausentes dos 4 catalogos renderizadas como texto cru no sidebar por ContextLabel() do _Layout com L.T("...", culture) fora do escopo dos keyuse ad-hoc pre-S20 (bug de produto - usuario via literalmente LAYOUT.ORG_ACTIVE em qualquer idioma)** + botao "Publicar" branco sobre #059669 (8, 3.77:1 => var(--success) em NewRevision+TemplateEdit).
- [x] i18n delta d-s20-ux-audit.json: 6 chaves x4 = 24 entradas (layout.org_active/org_suspended/no_org/not_confirmed/session_not_accepted/access_not_confirmed) mescladas no mesclador canonico idempotente -> catalogos 4052->4058 chaves/cultura paridade 4/4 BOM+LF 0 CR; nenhum teste ancora contagem exata (>=600 + paridade).
- [x] Scanner permanente scripts/qa/scan-i18n-keys.mjs: extrai L.T("...") em todo Razor c/ qualquer 2o argumento, t("...") no JS e Key/AriaKey="..." no registro de navegacao C# contra os 4 catalogos - 2699 chaves em 92 arquivos (exclui vendor wwwroot/lib); antes do merge acusava exatamente as 6 chaves, depois KEYUSE-OK 0 ausentes - fecha o gap dos keyuse ad-hoc das sprints anteriores (escopo variavel).
- [x] d54 r4 FINAL VERDE: UX-DONE checks=382 ok=382 uxfail=0 authfail=0 httpfail=0 overflowTotal=0 unlabeledTotal=0 emptyNoCtaTotal=0 imgsNoAltTotal=0 statusElTotal=112 (exit 0), pior contraste medido 4.7:1 (tudo acima do limiar AA 4.5) - transcript evidence/d54-ux-s20-rodada-final.txt.
- [x] F-3 regressao visual d41 pos-CSS: 1a volta AUDIT-DONE checks=764 ok=760 overflow=4 - causa unica: suporte-sessoes @ 768x1024 em 4 idiomas (deltas 77/95/105/99px = comprimento do texto da coluna Motivo) porque td.nowrap-muted ganhou white-space:nowrap (classe antes indefinida) e a tabela nunca teve o wrapper canonico .table-wrap{overflow-x:auto}; corrigido envolvendo a tabela em SupportSessions/Index.cshtml + pre-check cirurgico da rota antes da volta completa (2 viewports c/ 768 x 4 idiomas = 8/8 OK, sw=iw); volta final AUDIT-DONE checks=764 ok=764 overflow=0 authfail=0 httpfail=0 (exit 0), 0 PNGs novos - transcript evidence/d41-design-audit-s20-postcss-rodada-final.txt.
- [x] GATE sobre a arvore final: builds sln 13 projetos -warnaserror Release E Debug 0W/0E; Domain 389/389; Integration -FreshDb 293/293 TEST-EXIT=0; check-assets.mjs exit=0 (10 assets, 19 chaves modulos JS em 4 catalogos, round trips); codepoints em 16.232 valores dos 4 catalogos: 0 mojibake/replace chars (conjunto nao-ASCII = acentuacao legitima + glifos tipograficos existentes).
- [ ] Limitacoes registradas: pseudo-elementos ::before/::after e SVG fora da mediacao de contraste (regra registrada no D-OC54 sec.1); estados vazios medidos so para <table> (listas de cards vazias usam .empty-state direto); placeholder-vs-instrucao e alinhamento de labels revisados por amostragem nas capturas + varredura estatica; chaves dinamicas por interpolacao (L.T($"..._{x}")) fora do scanner (caso a caso); provedor real de IA pendente de decisao externa (Provider=none ate la); divida pt-first herdada cross-camada ([Required(ErrorMessage)] ~54, mensagens de dominio da API em pt, valores crus); CI oficial nunca verde publicada.
- [ ] CI oficial deste SHA: pendente - branch sem push; nenhum ID de execucao inventado.
- [ ] Aceite humano das correcoes de contraste/rotulos/estados vazios + texto do sidebar em 4 idiomas: pendente.
- [x] Commit unico "sem push" fechando a Sprint 20.

## Sprint 21 - manuais contextuais em todas as paginas + linguagem para leigos (D-OC55) (20/10/2026)

- [x] F-1 manuais contextuais: inventario das 49 rotas da d41 x painel de ajuda (D-OC55 sec.2) identificou 17 paginas SEM "Como usar"; todas ganharam <details class="help-card"> reutilizando o padrao existente do design system (zero classe CSS nova; sumario reusa a chave canonica common.how_to_use) com passos em 4 idiomas - 46 chaves *.manual_* x4 = 184 entradas no delta versionado scripts/i18n/d-s21-manuais.json (catalogos 4058 -> 4104); paginas de funil/estado com paragrafo curto explicativo (privacidade, aceitar-convite, confirmar-email, login); isencoes registradas com justificativa no D-OC55 sec.2 (paginas de estado Error/ServiceUnavailable/AccessDenied, partials/_Layout, rotas internas de estudio/ficha sem rota fixa na d41 - limitacao herdada da S17).
- [x] F-2 linguagem para leigos: varredura sistematica dos 4 catalogos (jargao tecnico + conotacao teste/dev/homologacao) com revisao individual de cada acerto -> 59 valores reescritos (pt-BR 22, en-US 12, es-ES 12, fr-FR 13) + 1 chave nova (sheet.doc_simulated_engine x4, delta versionado scripts/i18n/d-s21-linguagem.json): conotacao dev/teste removida (reg.dev_transport "Transporte local de desenvolvimento...concluir o teste" -> entrega local declarada, sheet.doc_simulated "ambiente de teste" -> "provedor externo nao configurado", error.development_mode "Modo desenvolvimento" -> "Ambiente de preparacao - detalhes tecnicos exibidos", tooltip hardcoded "Engine: simulated-scanner-dev" da ficha de contrato virou chave de catalogo); worker/lease -> fila de processamento/estacao de envio (2 chaves); hash SHA-256 -> impressao digital criptografica (1 chave); snapshot -> instantaneo em pt-BR (8 chaves; en/es/fr mantem snapshot/retrato/instantane); "chave de idempotencia" -> "identificador da requisicao" (4 mensagens); acentuacao de 2 valores crus fora da familia canonica (api.internal.no_result, api.solicitations.operation_invalid); acertos restantes justificados um a um no D-OC55 sec.4 (hash exibido junto do valor hex, fila/queue/sonda/retry comuns, lease = tipo legitimo de contrato, token coerente com o label visivel "Token", cron = substring de sincrono, artificial = substring de inteligencia artificial, error.dev_p1/p2 so aparecem com ASPNETCORE_ENVIRONMENT=Development).
- [x] Revisao de mensagens de erro/status (causa + efeito + proxima acao, tom de producao): amostra deterministica 1-em-7 das 481 chaves de erro/status do catalogo + revisao integral das familias api.sql.* (37) e api.org.solicitations.*; padrao consistente das sprints anteriores confirmado, pontas encontradas corrigidas na F-2.
- [x] QA classificada: a 1a bateria do GATE acusou deriva real - reescrita de acentuacao da familia api.sql.* em pt-BR violou a invariante D-OC27 (camada SQL canonica em pt-BR, valores byte-exatos dos envelopes jsonb_build_object, imposta por EverySqlErrorEnvelopeMapsToACatalogKeyInAllFourCatalogs); os 27 valores afetados foram restaurados ao texto canonico exato (transcrito do mapa ApiSqlMessages.cs) e a suite refeita verde; canonizar esses textos com diacriticos exige mudanca de envelope SQL (nova migracao) - divida registrada, fora do escopo; en/es/fr (livres da restricao) receberam termos/acentos corretos. Efeito colateral: merge-i18n-delta.mjs ficou idempotente no caso 0 chaves novas (antes deixava virgula final invalida e quebrava).
- [x] GATE sobre a arvore final: builds sln 13 projetos -warnaserror Release E Debug 0W/0E; Domain 389/389; Integration -FreshDb 293/293 TEST-EXIT=0; check-assets.mjs exit=0 (10 assets, 19 chaves em 4 catalogos, round trips); scan-i18n-keys.mjs KEYUSE-OK 2746 chaves / 92 arquivos, 0 ausentes; paridade 4105 chaves/cultura 4/4 BOM+LF; codepoints 16.420 valores, 0 problemas (mojibake/replace chars).
- [x] d54 UX pos-textos (382 medidas): VERDE na primeira volta - UX-DONE checks=382 ok=382 uxfail=0 authfail=0 httpfail=0 overflowTotal=0 unlabeledTotal=0 emptyNoCtaTotal=0 imgsNoAltTotal=0 statusElTotal=112 (exit 0), pior contraste medido 4.7:1 - os 17 paineis de ajuda novos (details.help-card fechado por padrao) nao geraram overflow nem falha de contraste em nenhuma das 49 rotas x 4 idiomas x 2 viewports; transcript commitado evidence/d54-ux-s21-rodada-final.txt.
- [x] d41 design pos-textos (764 medidas): as passadas completas morreram no mesmo evento de ambiente a ~60 min da mediacao - r1 em 628/764 por suspensao da maquina; r2 em 635/764 e r3 em 617/764 por morte da arvore de processos (evento de sessao; as tres 0 falhas, sem marcador de saida); a passada unica (~73 min) excede a janela do ambiente, entao a volta final rodou em 2 partes dentro da janela cobrindo 100%: prefixo r3 (617 medidas; transcript evidence/d41-design-audit-s21-rodada-final-part1-r3-prefixo.txt) + rotas restantes clientes->importacao (AUDIT-DONE checks=160 ok=160 overflow=0 authfail=0 httpfail=0 exit=0, 13 de sobreposicao na primeira rota; transcript evidence/d41-design-audit-s21-rodada-final-part2-tail.txt) = 764 medidas unicas verdes / 0 falhas (2.040 medicoes reais nas 4 rodadas, nenhuma falha em nenhuma).
- [ ] Limitacoes registradas: rotas internas de estudio/ficha sem rota fixa na d41 (limitacao herdada da S17); familia api.sql.* em pt-BR sem diacriticos por construcao da camada canonica (divida de migracao registrada no D-OC55 sec.3); conteudo armazenado em pt-BR e divida de dados, nao de superficie (superficie revisada); provedor real de IA pendente de decisao externa (Provider=none ate la); CI oficial nunca verde publicada.
- [ ] CI oficial deste SHA: pendente - branch sem push; nenhum ID de execucao inventado.
- [ ] Aceite humano dos manuais em 4 idiomas + nova linguagem das mensagens: pendente.
- [ ] Commit unico "sem push" fechando a Sprint 21.

## Sprint 22 - dress rehearsal final da candidata sobre a arvore atual + veredito go/no-go atualizado + pacote do dia 1 (D-OC56) (21/10/2026)

- [x] F-1 dress rehearsal sobre a arvore S21: primeira reexecucao completa desde S11 (a candidata nunca tinha sido re-ensaiada apos S12-S21). Rodada r1 (foto de 8 estagios, ~27 min, sem evento de ambiente): 6 estagios verdes (C-SLA PASS=98 FAIL=0; ACEITE PASS=15/FAIL=0/BLOCKED=0; D37-ASSINATURA fails=0; D38-ATENDIMENTO fails=0; S10-BACKUP checks=26 ok=26; S10-SMTP checks=17 ok=17) + 2 falhas classificadas: (a) D34-JORNADA 8/61 checks vermelhos (X1-1/X1-2/X2-1/X2-2/X3/T1/N1/N2) = REGRESSAO REAL DE PRODUTO: a S19 (commit 1f94693/D-OC53) estendeu o record VersionItem de 4->6 parametros (DocumentId/UploadedBy p/ extracao+IA) sem atualizar a projecao de 4 colunas do claim de scan (ScanOne), e toda varredura documental lanca em runtime "A parameterless default constructor or one matching signature (...) is required for Odca.Worker.DocumentWorker+VersionItem materialization" (log preservado) - mesma classe do bug que a propria S19 corrigiu no RenewalListItem; ficou invisivel porque build estrito so prova compilacao, a suite nao tem e2e de scan executado (limitacao registrada na S19) e d41/d54 nao fazem varredura; (b) D39-SEGURANCA 1 check falho = DERIVA QA: o script assercia schema_migrations v061 (congelado na S11), canonico vigente e v063; comportamento ate W1b 100% verde e abort no checkpoint pos-W1b do proprio script. Hipotese "texto de UI reescrito por S12-S21": NAO confirmada (C-SLA/ACEITE/D37/D38 verdes 100% sobre as areas reescritas). Correcoes: construtor secundario de 4 parametros em VersionItem ancorando exatamente os aliases da projecao (registro internal via InternalsVisibleTo ja existente no csproj; projecao da extracao 6 colunas intacta) + teste COMPORTAMENTAL (materializa o claim real odca.claim_document_scan() c/ linha semeadas pela role da aplicacao, mesmo SQL e tipo de producao; retries no padrao FranchiseMetering pela fila global) + GUARD DE FONTE fixando o pareamento alias<->construtor; script QA atualizado para v063 c/ comentario datado. Rodada r2 (-Only 'D34-JORNADA;D39-SEGURANCA' com binarios estritos): D34 VERDE 61/0 "TODOS OS CHECKS PASSARAM" (773 s, escopo honesto preservado: OCR 20/25 truncado=true, docx sem fabricar paginas, imagem unica 1/1, codigos estaveis extraction-timeout/pdf-no-text-layer/extractor-not-configured) + D39 VERDE fails=0 (113 s, 22 checks: P0a em v063 + cadeia MFA A1-A3/W1a/W1b + W2/W2b pagina e nav de sessoes de suporte + W3-W5 ciclo abrir/estender/encerrar c/ eventos e ator real + F1a-F1c stale->redirect preservando returnUrl + F2/F2b 403 puro na API / leitura 200) = DRESS REHEARSAL CONSOLIDADO 8/8 ESTAGIOS VERDES NA ARVORE FINAL. Transcripts separados por rodada: evidence/d56-dress-rehearsal-r1.txt, d56-d34-jornada-r1.txt, d56-d34-r1-worker-erro.txt, d56-d39-seguranca-r1.txt, d56-dress-rehearsal-r2.txt, d56-d34-jornada-r2-verde.txt, d56-d39-seguranca-r2-verde.txt (+ d44-* re-versionados pelos proprios scripts nos estagios S10 da r1).
- [x] GATE sobre a arvore final: builds sln 13 projetos -warnaserror Release E Debug 0W/0E; Domain 389/389; Integration -FreshDb 295/295 TEST-EXIT=0 (baseline 293 + 2 novos guardas: DocumentScanClaimMaterializesVersionItemFromRealPostgreSql e DocumentWorkerScanClaimProjectionMatchesVersionItemSecondaryConstructor); check-assets.mjs exit=0 (10 assets/19 chaves/round trips); scan-i18n-keys.mjs KEYUSE-OK 2746 chaves/92 arquivos 0 ausentes; d41 (764) e d54 (382) da S21 validas nesta mesma arvore (nenhuma superficie auditada - Web/catalogos/assets - mudou nesta sprint).
- [x] F-2 veredito go/no-go atualizado (D-OC56 sec.4): NO-GO formal - capacidade tecnica de GO REFORCADA sobre a S11. Pendencias para GO = apenas decisoes externas nao simuladas: push + CI oficial do SHA (pre-condicao 1), transporte de assinatura (D-OC45 item 3), canal de aviso (item 4), usuarios convidados + cenarios, aprovacao explicita da versao; MFA dos superadministradores PRONTO PARA EXECUTAR (re-comprovado na r2). Delta vs S11 registrado: primeira re-ensaiagem completa da candidata, regressao real de S12-S21 capturada e corrigida c/ guardas, D39 pos-W1b validado pela primeira vez nesta arvore. Itens ordenados "para virar GO" no D-OC56 sec.4.
- [x] F-3 pacote do dia 1 (D-OC56 sec.5): checklist versionada - 6 pre-condicoes antes do convite (estado explicito de cada uma); ordem exata do dia 1 (stack ready -> backup pre-piloto c/ restore validado como pre-condicao de rollback -> re-execucao pontual do ensaio se a arvore mudar -> provisioners versionados p/ contas sinteticas -> MFA ativo antes das 09:00 -> runbook D-OC40 sec.7); cenarios por perfil (cliente/superadmin/titular); suporte por sessao (1-480 min extensivel), monitoramento, rollback (SHA anterior + backup pre-piloto; nenhuma migracao destrutiva durante o piloto) e criterio de saida (cenarios concluidos c/ falhas classificadas + aprovacao explicita -> creditacao dos marcos no progress.json sem alterar baseline/IDs/pesos). Adendo datado anexado ao D-OC40 (sec.7.5 v060->v063; contagens do sec.8 marcadas como snapshot da S6; divida i18n operacional ja resolvida S7-S17) sem reescrever o historico.
- [ ] Limitacoes mantidas: CI oficial nunca verde publicada (sem push); provedor real de IA pendente (Provider=none); provedores de assinatura/SMTP pendentes como decisao externa; sessao absoluta de 15 min sem sliding pendente como decisao de produto; impersonacao profunda ainda e divida (D-OC39) - sessao de suporte da acesso auditavel, nao impersonacao; rotas internas de estudio/ficha sem rota fixa na d41; api.sql.* pt-BR sem diacriticos (divida de migracao); deploy fisico multi-instancia e backup offsite fora do alcance local do ensaio.
- [ ] CI oficial deste SHA: pendente - branch sem push; nenhum ID de execucao inventado.
- [ ] Aceite humano do veredito go/no-go atualizado + decisoes externas (assinatura, aviso, convidados, versao): pendentes.
- [x] Commit unico "sem push" fechando a Sprint 22.

## Sprint 23 - CI oficial do GitHub no runner Linux: correcao da falha sistemica e primeiro push da candidata (D-OC57) (21/10/2026)

- [x] CI oficial desbloqueada: branch codex/s00-foundation pushed ao remote devmnsoft/odcasolutions (avanco 27b19b6..31b70db; decisao do responsavel em 21/10/2026). Run oficial 39635407117 disparada pelo push: secret-scan SUCCESS; build-test com Restore/Build/Assets SUCCESS e passo Tests FAILURE (dotnet test Odca.sln -c Release --no-build em ubuntu-24.04 + postgres:18.1-alpine). Historico pela API publica: os dois pushes anteriores tambem falharam no mesmo passo (runs 39024561909 @ 27b19b6 e 38911855089 @ a2b1360) = falha sistemica pre-existente, nao causada pela S22, consistente com o registro "CI oficial nunca verde publicada".
- [x] Causa raiz identificada (log do passo inacessivel sem token - API 403 anonimo; varredura sistemica da suite/projetos por APIs sensiveis a plataforma): DocumentWorker.ScanPageNumber usava Path.GetFileNameWithoutExtension sobre o literal "C:\tmp\odca-scan-x\page-1.png"; no Linux o '\' nao e separador de caminho -> devolve -1 no lugar de 1 -> fato ScannedPageFileNamesParseToOrderedPageNumbers (criado na D-OC33) falhava. unico item da suite/produto com comportamento deterministicamente divergente entre SOs (demais itens portaveis: Path.Combine com segmentos relativos, literais hardcoded nos dois lados dos asserts, culturas explicitas/invariante). Efeito de produto: worker em producao Linux passa a interpretar nomes de pagina renderizadas (page-N.png) com qualquer convenio de caminho.
- [x] Correcao (1 funcao em Odca.Worker, zero testes novos - o fato existente era o guarda ja escrito; mudou o codigo para passa-lo em qualquer plataforma): ultimo segmento resolvido manualmente (\ -> /, trecho apos o ultimo '/', extensao removida); resultados identicos em qualquer plataforma para todos os casos asseridos (page-12.png=12; page-.png=-1; page-a.png=-1; foo-page-3.png=-1; outro.png=-1; C:\tmp\...\page-1.png=1).
- [x] Causa raiz PROVA por reproducao negativa: replica Linux da CI (Docker postgres:18.1-alpine + mcr.microsoft.com/dotnet/sdk:10.0, mesmas variaveis ODCA_TEST_* e comandos exatos do ci.yml) em worktree @ 31b70db (SEM a correcao) executando somente o fato -> FALHOU 1/0 "Assert.Equal() Failure: Values differ Expected: 1 Actual: -1" (evidence/d57-ci-linux-prefix-sem-fix-falha.txt); mesma replica sobre a arvore corrigida: RESTORE-OK + BUILD-OK (Release 0W/0E) + SUITE COMPLETA Domain 389/389 e Integration 295/295 com 0 falhas em LINUX (evidence/d57-ci-linux-replica-com-fix-verde.txt).
- [x] GATE sobre a arvore final (Windows): builds sln 13 projetos -warnaserror Release E Debug 0W/0E; Domain 389/389; Integration -FreshDb 295/295 TEST-EXIT=0 (contagens identicas a S22; nenhum teste novo).
- [ ] Limitacoes mantidas: o runner oficial segue sendo a autoridade para creditacao (a replica reproduz ambiente+sequencia mas nao o substitui; o Steps Tests da run oficial do novo SHA e quem decide); demais pendencias de produto inalteradas (provedor real de IA none; provedores de assinatura/SMTP pendentes como decisao; sessao absoluta 15 min sem sliding; impersonacao profunda D-OC39; rotas internas s/ rota fixa na d41; api.sql.* pt-BR s/ diacriticos).
- [x] CI oficial do SHA S22 (31b70db): run 39635407117 FAILURE no passo Tests (documentada - motivou esta sprint; etapas secret-scan/Restore/Build/Assets success).
- [ ] CI oficial deste SHA (S23): run disparada pelo push em monitoramento - ID registrado no D-OC57 ao concluir.
- [ ] Aceite humano: pendente.
- [x] Commit unico fechando a Sprint 23 + push na branch (decisao do responsavel).

## Sprint 24 - CI oficial, segunda onda: hermeticidade dos testes de erro do framework em checkout limpo (D-OC58) (21/10/2026)

- [x] Segunda onda da CI oficial: run oficial 39642957579 (SHA cf0f74b da S23) -> secret-scan SUCCESS; build-test com Restore/Build/Assets SUCCESS e passo Tests FAILURE - com avancos concretos sobre a S23: o fato ScanPageNumber (S23) PASSOU e restaram EXATAMENTE 2 falhas (Integration 293/295; Domain 389/389 totalmente verde): FrameworkErrorLocalizationTests.UnhandledExceptionIsPresentedInTheRequestedCulture pt-BR e en-US. Log oficial do job baixado via credencial HTTPS ja guardada localmente (git credential fill - fim da limitacao 403 anonimo da S23) e versionado em evidence/d58-ci-run-39642957579-build-test-falha-framework-error.txt.
- [x] Causa raiz n.2 identificada e provada: DevelopmentFaultApiFactory sobe o host em UseEnvironment("Development"); em Development o Program exige development-runtime.json - arquivo pessoal GITIGNORED, presente na maquina de desenvolvimento e ausente em checkout limpo (runner); no .NET 10 o AddJsonFile(optional:false) carrega no proprio Add -> InvalidOperationException "Configuracao local nao encontrada. Ambiente=Development" envolvendo FileNotFoundException na construcao do host. Prova por reproducao negativa: replica Linux em ARVORE LIMPA (worktree @ cf0f74b sem o arquivo), classe isolada -> 2 falhas / 2 passes com a MESMA excecao do log oficial (evidence/d58-reproducao-negativa-linux-39642957579.txt); os 2 passes (fatos 429, host Testing via fixture) confirmam que o restante do host funciona sem o arquivo. Auditoria complementar: nenhum outro arquivo gitignored de src/tests e lido pela suite (data-protection-keys substituido por temporario nos hosts de teste; private-documents/.local resolvem relativos ao CWD fora dos hosts; bin/obj artefatos de build).
- [x] Correcao A (ZERO linhas de produto; 1 arquivo de teste): o factory provisiona o escape previsto pelo proprio produto - escreve JSON vazio {} em arquivo temporario e aponta ODCA_RUNTIME_CONFIG (LocalRuntimeConfiguration.ResolvePath) para ele antes da criacao do host; no Dispose restaura o valor anterior da variavel e remove o arquivo (best-effort); a classe entrou na colecao "Environment" (padrao da casa para quem altera essa variavel de processo - mesma colecao de LocalRuntimeConfigurationTests). Hermeticidade atingida: o host Development de teste independe de estado fora do checkout, nos dois SOs.
- [x] Correcao C (endurecimento de guard de fonte; 1 linha + comentario em RuntimeQueryRegressionTests.cs): ReadSource normaliza line endings (Replace \r\n -> \n) antes das comparacoes, pela mesma convencao ja usada em MigrationChecksumTests; assercoes inalteradas; o guard (substring multi-linha em arquivo-fonte) fica deterministico entre checkouts CRLF (Windows/autocrlf) e LF (Linux/CI) - um clone limpo em qualquer OS deixa de poder quebrar a suite nesse ponto. Nenhum fato novo ou removido; contagens intactas (Domain 389, Integration 295).
- [x] Replica positiva em arvore limpa real (copia de checkout limpo: worktree @ cf0f74b + somente os arquivos de teste corrigidos nesta entrega - conteudo src/tests identico ao commit S24; sequencia exata do ci.yml): r1 293/295 com 2 falhas CLASSIFICADAS COMO ARTIFATOS DE METODO (nao de codigo): (a) guard de fonte RuntimeQueryRegressionTests (substring multi-linha em LF) vs worktree de verificacao criada no Windows c/ autocrlf=true (CRLF - 348 CRs; o blob guardado no git tem 0 CRs) - exposto o perigo latente do mesmo tipo da S23 (clone limpo no Windows quebraria a suite nesse guard) -> motivou a correcao C; (b) 23505 duplicate key tenants_pkey por estado residual no Postgres reutilizado entre replicas (a CI ganha service container novo por run) (evidence/d58-replica-linux-arvore-limpa-r1-artefatos-metodo.txt). r2 com arvore em semantica LF (worktree recriada c/ -c core.autocrlf=false, bytes identicos ao armazenado) e container Postgres NOVO -> RESTORE-OK, BUILD-OK (Release 0W/0E), Domain 389/389 e Integration 295/295 com 0 falhas em LINUX em arvore limpa (evidence/d58-replica-linux-arvore-limpa-r2-verde.txt). Limitacao da replica da S23 registrada no D-OC57 sec.4 (montava o diretorio de trabalho c/ arquivos gitignored presentes - mascarou esta causa) e substituida por replicas em worktree/copia limpa (somente arquivos rastreados).
- [x] GATE sobre a arvore final (Windows): builds sln 13 projetos -warnaserror Release E Debug 0W/0E; Domain 389/389; Integration -FreshDb 295/295 TEST-EXIT=0 (contagens identicas a S23; nenhum fato novo).
- [ ] Limitacoes mantidas (idem D-OC57): provedor real de IA none; provedores de assinatura/SMTP pendentes como decisao; sessao absoluta 15min sem sliding pendente; impersonacao profunda D-OC39; api.sql.* pt-BR s/ diacriticos = divida de migracao. O runner oficial segue sendo a autoridade p/ creditacao ate a run verde deste SHA.
- [x] CI oficial deste SHA (S24): run 39651426314 SUCCESS apos o push - build-test (Restore/Build/Assets/Tests) e secret-scan todos verdes, passo Tests success = PRIMEIRA execucao oficial verde do repositorio para a branch codex/s00-foundation; encerrada a pendencia "CI oficial nunca verde publicada" para este SHA (registro posterior a conclusao da run, regra da casa).
- [ ] Aceite humano: pendente.
- [x] Commit unico fechando a Sprint 24 + push na branch (decisao do responsavel).

## Sprint 25 - D-OC59 - jornada operacional completa (23/10/2026)
- [x] Prova D-OC59: um unico caso sintetico atravessa paciente -> documento (NDA publicado) -> preenchimento manual confirmado (8 campos) -> versao navegavel + PDF -> revisao (solicitacao atribuida ao revisor + decisao aprovada) -> preparacao confirmada -> assinatura externa via provedor local-file (envio, estado accepted por arquivo, sync deduplicado, reenvio 409) -> externally_signed + hash PDF + aviso signature_completed in-app/outbox -> serie mensal de 3 obrigacoes -> vigencia aplicada (fim +30d, aviso 30d) -> worker renewal_reminders (lembrete entregue + sino + outbox ao responsavel) -> agenda (obrigacao no mes dos prazos/dono cliente; renovacao no mes atual/no due_on do aviso; revisao decidida sai da janela) -> proposta de renovacao (draft -> in_review -> formalized -> applied; reaplicar 409) -> grafo final consistente (mesmo contrato). 49/0 em r9 (~35 min, sem purgas entre fases).
- [x] Script novo scripts/qa/validate-d59-jornada-completa.ps1 autocontido, orquestrado como 9o estagio D59-JORNADA em scripts/qa/run-pilot-rehearsal.ps1. Stack unica com Signatures__Provider=local-file, worker ativo. Base descartavel odca_test_disposable + fixtures declaradas (plano enterprise v2, papel de obrigacoes, opt-in signature_completed, janelas de aviso {30,15,7}, preferencia de e-mail renewal_due para o responsavel).
- [x] Correcao de produto 1: ContractStudioController Submit review - dueAt=request.DueAt?.ToUniversalTime() (Npgsql recusava DateTimeOffset com offset != 0 em timestamptz, 500 em host fora de UTC). Teste de regressao ReviewSubmissionAcceptsNonUtcDueAtOffset em NotificationFeedTests (verificado em DB novo odca_test_d59t).
- [x] Correcao de produto 2: OperationalInboxRepository - corte da janela de renovacao seguia c.end_date <= @renewalWindowEnd; com aviso de 30 dias o vencimento cai no mes seguinte e a renovacao sumia da agenda justamente no mes em que o aviso vencia. Corte passou a seguir a data do aviso (due_on), materializada no SELECT. Guard RenewalAgendaWindowCutsByNoticeDateNotContractEnd.
- [x] Correcao de produto 3 (guard): ReviewSubmissionNormalizesDueAtToUtcBeforePersisting em RuntimeQueryRegressionTests.
- [x] Robustez do roteiro da prova: PS 5.1 (concatenacao em @(...) corrompendo datas; Exception.StatusCode nulo mascarando 401/403/409; chave MFA sem parenteses; caminho da agenda sem barra inicial; rate-limit 429; token de plataforma nao verificado recusado com 403 na agenda; expiracao de 15 min do access token); Ensure-Api reescrito para reiniciar com ASPNETCORE_URLS explicito; fixtures declaradas.
- [x] GATE Windows: builds Release+Debug -warnaserror 0W/0E; Domain 389/389; Integration -FreshDb 298/298 (295 baseline + 3 novos).
- [x] Prova local D-OC59: r9 49/0. Evidencias em evidence/d59-*.
- [ ] CI oficial deste SHA: pendente (a ser disparada apos push e monitorada via API).
- [ ] Aceite humano: pendente (impedimento para creditar operations).
- [ ] Portal .odca/progress.json: inalterado (baseline 40%, IDs e pesos intactos). Credito do marco operations exige evidencia no commit funcional + CI oficial do mesmo SHA verde + aceite real humano.

## Sprint 26 - D-OC60 - assinatura externa completa (23/10/2026)
- [x] Prova D-OC60: 64/0 em r13 (~40 min). J14 download do documento final da versao externally_signed (200, PDF, 1823 bytes) + integridade (hash baixado == hash confirmado). J15 segunda versao -> envio -> declined -> sync idempotente -> versao NAO externally_signed -> reenvio consome nova unidade (mvts 1 -> 3). J16 segundo envelope com 3 eventos (sent->declined->sent), versao aceita permanece externally_signed.
- [x] Script novo `scripts/qa/validate-d60-assinatura-completa.ps1` (reuso da jornada D-OC59 + J14/J15/J16). Orquestrador atualizado com 10o estagio `D60-ASSINATURA`.
- [x] GATE: sem mudanca de produto; GATE da S25 (Release/Debug 0W/0E, Domain 389/389, Integration 298/298) permanece valido.
- [x] Prova local D-OC60: r13 64/0. Evidencias em `evidence/d60-*`.
- [ ] CI oficial deste SHA: pendente (a ser disparada apos push e monitorada via API).
- [ ] Aceite humano: pendente (impedimento para creditar `signature`).
- [ ] Portal `.odca/progress.json`: inalterado (baseline 40%, IDs e pesos intactos). Credito do marco `signature` exige evidencia no commit funcional + CI oficial do mesmo SHA verde + aceite real humano.
## Sprint 27 - D-OC61 - auditoria do catalogo de estreia (23/10/2026)
- [x] Prova D-OC61: 6/0 em r8. 10 templates globais publicados, todos com versao/conteudo/campos rotulados, 0 placeholders, 9 oficiais com official_revision>=1, tipos: amendment, consent, lease, nda, service, services, supply.
- [x] Script `scripts/qa/validate-d61-catalogo-estreia.ps1` (somente leitura, DB-level).
- [x] GATE: sem mudanca de produto; GATE da S25 permanece valido.
- [x] Prova local D-OC61: r8 6/0. Evidencias em `evidence/d61-*`.
- [ ] CI oficial deste SHA: pendente (a ser disparada apos push e monitorada via API).
- [ ] Aceite humano (aprovacao dos textos/campos/PDFs): pendente (impedimento para creditar `templates`).
- [ ] Portal `.odca/progress.json`: inalterado. Credito do marco `templates` exige evidencia no commit funcional + CI oficial do mesmo SHA verde + aceite real humano.
## Sprint 28 - D-OC62 - endurecimento de producao (isolamento RLS) (23/10/2026)
- [x] Prova D-OC62: 7/0 em r1. Isolamento RLS AxB em nivel HTTP (403 para tenants estranhos, 200 para o proprio). Reafirma J2-1 (notificacoes do proprio tenant, 200).
- [x] Script `scripts/qa/validate-d62-producao.ps1` (HTTP-level, stack unica).
- [x] GATE: sem mudanca de produto; GATE da S25 permanece valido.
- [x] Prova local D-OC62: r1 7/0. Evidencias em `evidence/d62-*`.
- [ ] CI oficial deste SHA: pendente (a ser disparada apos push e monitorada via API).
- [ ] Aceite humano: pendente (impedimento para creditar `production`).
- [ ] Portal `.odca/progress.json`: inalterado. Credito do marco `production` exige SMTP real, storage real, isolamento e recupera??o comprovados + aceite real humano.