# Plano Sprints 17+ — Roteiro até o Piloto (estreia 11/01/2027)

## Diretrizes do usuário
- IA integrada explicitamente (diferencial competitivo)
- Todos os diferenciais possíveis vs mercado (fazer igual ou melhor)
- Nada pode dar errado: cálculos, CRUD, mensagens
- Mensagens em tom de produção (sem conotação de homologação/teste)
- Design: verificar todos os pontos quebrados
- Seguir o fluxo de sprints sem parar

### Diretrizes atualizadas (19/10/2026 — trilha estendida: mais 50 sprints)
- Ser o **melhor SaaS do segmento**: cada sprint deve elevar o produto acima do concorrente próximo
- **Banco em script único**: todo o esquema em `database/odca.sql` (canônico cumulativo vNNN) importável num Postgres limpo de uma vez, sem erro — prova com import em banco descartável + runbook
- **Design sem confusão visual**: contraste texto×fundo auditado em todas as páginas (WCAG AA), cores de fundo nunca confundidas com cor de letra; forms e templates responsivos sem quebrar (360px)
- **Texto compreensível para leigos**: todo texto revisado para linguagem simples e direta (sem jargão técnico onde houver alternativa clara)
- **Manual em todas as páginas**: cada tela com painel de ajuda/manual ("Como usar") contextual
- **Regras de negócio pela legislação brasileira** (e viáveis para fora do país): LGPD (fluxos de titular, retenção, base legal), prazos/renovação/contratos (CDC/Código Civil), CNPJ/CPF validação real, multas/juros; pontos de extensão documentados para mercados internacionais
- **Zero conotação de homologação**: varredura contínua de mensagens que sugiram teste/homologação/dev

## Sprint 17 — Conta e resíduo i18n (D-OC51) [EM ANDAMENTO]
- Account ×7 views + AccountController (19 chaves) + BffErrorHandlingMiddleware (fallback HTML/JSON + lang dinâmico)
- Navegação: 4 chaves que faltavam (3 grupos acentuados + central-notificacoes); 66/66 chaves nav verificadas por codepoint
- Resíduo: 13 aria-labels em 10 views (reuso de chaves existentes) + forms.js toggle via data-attributes
- Central de importações: Imports/{Index,Review} + ImportsController completos (90 call sites; data culturalizada)
- Delta: 163 chaves × 4 (83 conta/resíduo/navegação + 80 importações); 652 entradas
- Catálogos: 3851→4014 chaves/cultura, paridade 4/4
- Scan global final: 0 literal pt-BR visível em toda a UI Razor (52 linhas acentuadas = 49 comentários + 3 endônimos)
- Auditoria d41 estendida: 46→49 rotas (+login, +minha-conta, +importacao) = 191 pares × 4 viewports = 764 medidas

## Sprint 18 — Fundações IA: camada de integração + primeiro uso (D-OC52)
**Diferenciais de mercado**:
- `Odca.AI` project (novos): interface `IAiService` com provedor configurável
- Primeiros casos de uso:
  1. **Resumo executivo de documento** — no detalhe do documento, botão "Resumir" que gera resumo de 3-5 frases
  2. **Classificação automática** — ao fazer upload, IA classifica o tipo (contrato, minuta, nota fiscal, etc.)
  3. **Detecção de cláusulas-chave** — identifica obrigações, multas, prazos, rescisão
- UI: badge "IA" discreto, loading state honesto ("Analisando documento..."), resultado em card expandível
- Mensagens: produção, sem "teste" ou "homologação"
- Erro: fallback elegante ("Análise não disponível no momento")
- Configuração: provider em appsettings (Azure OpenAI / Anthropic / local), chave via KeyVault/env

## Sprint 19 — IA avançada: análise inteligente de contratos (D-OC53)
- **Scoring de risco por contrato**: score 0-100 com fatores (cláusulas atípicas, desproporção de prazos, ausência de SLA)
- **Comparação com template padrão**: delta visual entre contrato e template base
- **Sugestão de renovação preditiva**: alertar quando contrato entra em janela de renovação com análise de histórico
- **Busca semântica**: campo de busca natural na central de documentos ("contratos com multa superior a 10%")
- Integração com o catálogo de módulos: feature "ia_analysis" habilitável por plano

## Sprint 20 — QA de design + polimento de UX (D-OC54)
- Auditoria visual de TODOS os pages em 4 viewports × 4 idiomas
- Verificar: overflow, quebras, alinhamento, **contraste texto×fundo (WCAG AA — cor de letra nunca confundida com a do fundo)**, spacing consistente
- Fixar o inventário das 64 classes CSS sem definição mapeado (site.css)
- Forms: labels alinhados, erros abaixo do field, placeholder como exemplo não instrução
- Estados vazios: sempre com CTA claro
- Loading states: skeleton ou spinner, nunca tela em branco
- Responsivo: mobile-first check em 360px; templates não podem quebrar nem confundir hierarquia

## Sprint 21 — Manual em todas as páginas + linguagem para leigos (D-OC55)
- Inventário: rotas SEM painel de ajuda/manual ("Como usar") → cada uma ganha manual contextual em 4 idiomas (chaves `*.manual_*`)
- Revisão de todo texto da UI para linguagem simples e direta (compreensível por leigos): substituir jargão técnico por termos claros com mesmo significado operacional
- Revisar mensagens de erro/status: causa + efeito + próxima ação, sempre em tom de produção
- Zero conotação de homologação/teste/dev nas mensagens varridas (scan contínuo no GATE)

## Sprint 22 — Regras de negócio × legislação brasileira (+ extensão internacional) (D-OC56)
- LGPD: fluxo de titular (acesso/retificação/portabilidade/esquecimento), retenção configurável, base legal registrada por categoria de dado
- Prazos e renovação: regras alinhadas a CDC/Código Civil (prazo de aviso prévio, multa, juros moratórios com índice configurável — SELIC/juros monetários)
- CNPJ/CPF: validação real (dígitos verificadores) nos forms públicos
- Contratos: cláusulas padrão BR (foro, vencimento dia do mês, fuso America/Sao_Paulo em todas as datas de negócio)
- Extensão internacional documentada: pontos de configuração (moeda ISO já existe, moeda/fuso/validação de documento fiscal por jurisdição) com limites declarados

## Sprint 23 — Banco em script único: import one-shot verificado (D-OC57)
- `database/odca.sql` canônico vNNN = esquema completo; prova de import num Postgres limpo/banco descartável: `psql -v ON_ERROR_STOP=1` exit=0 + contagem de objetos (schemas/tabelas/índices/roles)
- Runbook de operação: criar banco → importar → provisionar (passos exatos, sem ambiguidade)
- Byte-paridade `database/odca.sql` × `database/releases/odca-vNNN.sql` preservada no GATE

## Sprint 24 — Validação end-to-end + hardening (D-OC58)
- Testes E2E críticos: login→MFA→criar documento→OCR→revisão→assinatura→arquivo
- Cálculos: storage, OCR credits, assinatura envelopes, períodos, prazos
- CRUD completo: cada entidade create/read/update/delete com validação
- Concorrência: idempotência, versionamento otimista, re-tentativa
- Performance: lazy loading, pagination, time-to-interactive < 3s
- Segurança: headers, CSP, CORS, session timeout, rate limiting

## Sprint 25 — Piloto: dress rehearsal final + veredito (D-OC59)
- Cenário completo com dados sintéticos realistas
- Onboarding assistido para o cliente piloto
- Monitoramento: métricas de uso, erros, latência
- Go/No-Go decision gate com critérios objetivos
- Backup pré-piloto + restore test

## Sprints 26+ — trilha contínua até e além do piloto (runway de 50 sprints)
- Melhorias incrementais sequenciais: cada sprint fecha um corte verificável (funcional, qualidade ou operação)
- Priorização contínua pelo critério "melhor SaaS do segmento": IA (busca semântica, copiloto de contrato), automações (workflow de renovação, alertas preditivos), experiência (dark mode se não degradar contraste), integração (assinatura multi-região, ERP/contabilidade BR)
- Regra invariante: sprint só fecha com GATE verde + evidência no repositório + registro em STATUS/BACKLOG; sem push automático

## Diferenciais vs Mercado (checklist)
| Feature | Concorrente A | Concorrente B | ODCA |
|---------|--------------|--------------|------|
| OCR inteligente (IA) | Básico | Médio | Semântico + cláusulas |
| Assinatura digital | Sim | Sim | Sim + envelope tracking |
| Gestão de contratos | Sim | Parcial | Sim + scoring risco IA |
| Busca | Keyword | Keyword+filter | Semântica natural |
| SLA tracking | Não | Básico | Completo com breach alerts |
| Multi-idioma (4) | 2 | 1 | 4 nativos |
| MFA + Recovery | Sim | Sim | Sim + gestão completa |
| Versionamento imutável | Sim | Sim | Sim + auditoria |
| IA integrada | Não | Beta | Core (resumo, classe, risco) |
| Privacy requests | Sim | Não | Sim + fluxo completo |
| Offline-first | Não | Não | Cache inteligente |
| API-first | Sim | Sim | Sim + BFF otimizado |
