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
  `signatureStatus=not_available`, nunca “assinado”.
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
