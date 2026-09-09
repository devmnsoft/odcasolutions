# Backlog

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
- [ ] Seleção de organização, convites, equipe, perfis delegáveis e proteção concorrente do último administrador.
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
- [ ] Entregar seletor de organização, equipe, perfis delegáveis, convites e assentos com proteção do último administrador.
- [ ] Configurar cache realmente compartilhado e validar Data Protection/tickets entre duas instâncias.
- [ ] Depois: suporte temporário e operação de direitos; S02; S03; OCR/Office e sequência comercial, sem antecipar editor.
