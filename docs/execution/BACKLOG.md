# Backlog

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

- [x] Corrigir CA1859 sem alterar o contrato público ou materializar novamente o histórico.
- [x] Definir JSON estruturado como fonte canônica, validar nós/formatação e recusar superfícies de conteúdo ativo.
- [x] Modelar campos estáveis/repetidos, validação tipada, confirmação, conflito otimista, publicação imutável e restauração não destrutiva na camada Application.
- [ ] Persistir modelos, cláusulas, concessões, rascunhos, versões e emissões em migration nova, sem modificar releases aplicados.
- [ ] Integrar casos de uso tenant-aware, permissões/limites/auditoria, API e BFF ao núcleo existente.
- [ ] Entregar editor visual modular, autosave com retry/conflito, PDF rastreável e histórico autenticado.
- [ ] Executar PostgreSQL descartável, jornada no navegador, inspeção do PDF e QA 360/768/1280/1440 e zoom 200%.

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
- [ ] Cache distribuído e Data Protection persistido para produção/múltiplas instâncias.

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
- [ ] Configurar cache realmente compartilhado e validar Data Protection/tickets entre duas instâncias.
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
