# Matriz de Planos, Benefícios e Operações

**Data**: 2026-10-11 · **Status**: Documentado e comprovado em `odca_test_disposable` · **2026-10-14 (D-OC31)**: reconciliada com o canônico (`v038`/`v045`/`v051` e banco zerado `odca_test_integration`): `module.reviews` é **exclusiva do Enterprise** (catálogo canônico 6/6/7); os três serviços documentais exigem plano Enterprise na abertura; `suporte_tecnico` exige apenas assinatura ativa.

## 1. Visão geral dos planos

| Plano | Cód. banco | Assentos máx. | Arquivo máx. | Armazenamento | Por usuário | OCR pág/mês | Envelopes/mês |
|-------|-----------|:---:|:---:|:---:|:---:|:---:|:---:|
| Basic | `basic` | 3 | 25 MB | 10 GB | 5 GB | 300 | 10 |
| Intermediário | `intermediate` | 10 | 100 MB | 100 GB | 25 GB | 3.000 | 50 |
| Enterprise | `enterprise` | 30 | 250 MB | 500 GB | 100 GB | 15.000 | 200 |

## 2. Módulos por plano

| Módulo | Basic | Intermediário | Enterprise |
|--------|:---:|:---:|:---:|
| contract_drafts (minutas) | ✅ | ✅ | ✅ |
| documents (acervo) | ✅ | ✅ | ✅ |
| imports (importação) | ✅ | ✅ | ✅ |
| patients (pacientes) | ✅ | ✅ | ✅ |
| reviews (solicitações ODCA) | ❌ | ❌ | ✅ |
| signatures (assinaturas) | ✅ | ✅ | ✅ |
| templates (modelos) | ✅ | ✅ | ✅ |

**Única diferença de módulo**: `module.reviews` (fila de revisões documentais / Solicitações ODCA) está **desativada nos planos não Enterprise** (Basic e Intermediário). Canônico desde a `v038` ("ODCA requests become an Enterprise capability from this version forward"), confirmado em banco zerado: **Basic 6 / Intermediário 6 / Enterprise 7** módulos. O atendimento documental humano é benefício **Enterprise**.

### O que é contado e quando ocorre consumo:

| Recurso | Medida | Evento de consumo | Falha/repetição |
|---------|--------|-------------------|-----------------|
| Assentos | Usuários ativos com membership ativo | Criação de membro ativo | Remover membro libera; suspender mantém |
| Arquivo (file_bytes) | Tamanho do upload | Upload concluído com sucesso | Upload falho não consome; retry do mesmo arquivo substitui |
| Armazenamento (storage_bytes) | Soma de todos os blobs do tenant | Qualquer persistência de blob (PDF, uploads) | Exceder → bloqueia novos uploads (503 problem+json) |
| OCR páginas | Páginas processadas por extração | Aplicação de extração OCR | Extração falha não consome |
| Envelopes de assinatura | Preparações criadas por mês | Confirmação da preparação | Cancelar não devolve; mês novo reseta |

## 3. Serviços de solicitação e SLA por plano

O acesso é validado **serviço a serviço** na abertura (`odca.solicitations_open`, v045/v051), independentemente do item de menu:

- `revisao` — Revisão documental — **apenas Enterprise**
- `adaptacao` — Adaptação de modelo — **apenas Enterprise**
- `esclarecimento` — Esclarecimento contratual — **apenas Enterprise**
- `suporte_tecnico` — Suporte técnico e recuperação de acesso — **qualquer plano com assinatura ativa**

Em planos não Enterprise, abrir um serviço documental devolve 409 com código estável `solicitations.plan.required` ("Central de solicitações disponível apenas no plano Enterprise."). Os tempos de serviço documental abaixo existem semeados em `sla_policies` para os três planos, mas são **inativos** para Basic/Intermediário enquanto o gate de plano vigora.

### SLA — Tempo de primeira resposta (minutos)

| Serviço | Prioridade | Basic | Intermediário | Enterprise |
|---------|-----------|:---:|:---:|:---:|
| esclarecimento | crítica | 240 (4h) | 120 (2h) | 60 (1h) |
| esclarecimento | alta | 480 (8h) | 240 (4h) | 120 (2h) |
| esclarecimento | normal | 960 (16h) | 480 (8h) | 240 (4h) |
| esclarecimento | baixa | 5760 (4d) | 2880 (2d) | 1440 (1d) |
| revisao | crítica | 240 (4h) | 120 (2h) | 60 (1h) |
| revisao | alta | 960 (16h) | 480 (8h) | 240 (4h) |
| revisao | normal | 1920 (32h) | 960 (16h) | 480 (8h) |
| revisao | baixa | 5760 (4d) | 2880 (2d) | 1440 (1d) |
| adaptacao | crítica | 480 (8h) | 240 (4h) | 120 (2h) |
| adaptacao | alta | 1920 (32h) | 960 (16h) | 480 (8h) |
| adaptacao | normal | 3840 (64h) | 1920 (32h) | 960 (16h) |
| adaptacao | baixa | 11520 (7.5d) | 5760 (4d) | 2880 (2d) |
| suporte_tecnico | crítica | 240 (4h) | 120 (2h) | 60 (1h) |
| suporte_tecnico | alta | 480 (8h) | 240 (4h) | 120 (2h) |
| suporte_tecnico | normal | 960 (16h) | 480 (8h) | 240 (4h) |
| suporte_tecnico | baixa | 5760 (4d) | 2880 (2d) | 1440 (1d) |

### SLA — Tempo de resolução (minutos)

| Serviço | Prioridade | Basic | Intermediário | Enterprise |
|---------|-----------|:---:|:---:|:---:|
| esclarecimento | crítica | 720 (12h) | 360 (6h) | 180 (3h) |
| suporte_tecnico | crítica | 720 (12h) | 360 (6h) | 180 (3h) |
| revisao | crítica | 960 (16h) | 480 (8h) | 240 (4h) |

*(Resolução completa segue multiplicador ×3–×4 sobre primeira resposta conforme prioridade.)*

## 4. Operações administrativas (SuperAdministrador)

| Operação | Permissão | Menu | Endpoint | Auditoria |
|----------|-----------|------|----------|-----------|
| Localizar cliente | `PlatformAdministrator` | Organizações | `GET /api/v1/platform/customers?search=` | — |
| Consultar vínculos | `PlatformAdministrator` | Busca usuários | `GET /api/v1/platform/users/search?q=` | — |
| Suspender organização | `PlatformAdministrator` | Clientes | `POST /customers/{t}/suspend` | `organization.suspended` |
| Restaurar organização | `PlatformAdministrator` | Clientes | `POST /customers/{t}/restore` | `organization.restored` |
| Ativar organização (pending) | `PlatformAdministrator` | Clientes | `POST /customers/{t}/activate` | `organization.activated` |
| Publicar modelo oficial | `PlatformAdministrator` | Biblioteca | `POST /platform/catalog/{key}/publish` | `platform_catalog.status_changed` |
| Retirar modelo oficial | `PlatformAdministrator` | Biblioteca | `POST /platform/catalog/{key}/retire` | `platform_catalog.status_changed` |
| Gerenciar recursos/plano | `PlatformAdministrator` | Planos | `PUT /platform/plans/{id}/entitlements` | `plan.entitlements.updated` |
| Acompanhar solicitações | `PlatformAdministrator` | Solicitações e SLA | `GET /api/v1/platform/solicitations` | — |
| Consultar auditoria | `PlatformAdministrator` | Auditoria | `GET /api/v1/platform/audit` | — |
| Consultar consumo | `PlatformAdministrator` | Clientes | `GET /api/v1/platform/customers/{t}/consumption` | — |

## 5. Separação: suporte técnico vs atendimento documental

| Aspecto | Suporte técnico | Atendimento documental |
|---------|----------------|----------------------|
| Serviço no banco | `suporte_tecnico` | `revisao`, `adaptacao`, `esclarecimento` |
| Disponibilidade | Qualquer plano com assinatura ativa | **Enterprise apenas** (gate de plano em `solicitations_open`; o módulo `module.reviews` também só está habilitado no Enterprise) |
| Exemplo de uso | Recuperação de acesso, erro técnico | Revisão de minuta, adaptação de cláusula |
| SLA | Idêntico ao plano (mesma tabela) | Idêntico ao plano (Enterprise) |
| Canal de notificação | In-app + outbox | In-app + outbox |

## 6. Upgrade/Downgrade

| Situação | Comportamento | Dados |
|----------|--------------|-------|
| Upgrade Basic→Enterprise | Módulos e limites liberados imediatamente | Preservados; nada removido |
| Downgrade Enterprise→Basic | Excesso de assentos: membros inativados até limite excedente | Preservados; acesso reabilita no próximo upgrade |
| Downgrade com storage > limite | Bloqueio de novos uploads; downloads mantidos | Preservados; consumo só diminui com remoção |
| Downgrade de OCR | Limite mensal reduzido; extrações em fila aguardam | Processadas quando o novo período começar |

## 7. Evidência de prova

| Item | Suíte | Assert | Status |
|------|-------|--------|--------|
| Gate reviews no intermediate | E-suite S8 | `module.reviews` desativado → 403 na API + item oculto no menu | ✅ |
| Gate services por plano | `PlanServiceGateTests` (D-OC31) | abertura revisao/adaptacao/esclarecimento em basic e intermediate → 409 `solicitations.plan.required`; suporte_tecnico abre nos três planos; catálogos 6/6/7 | ✅ |
| Suporte técnico em basic | E-suite S8 | `suporte_tecnico` abre em basic (sem gate Enterprise) | ✅ |
| Limite de assentos | E-suite S5 | 4º membro em basic → 409 `plan.seats.exceeded` | ✅ |
| Storage excedido | D-suite D12 | Upload acima do limite → 503 problem+json | ✅ |
| SLA snapshot no abrir | C-suite S2 | `sla_due_on` calculada com fuso+calendário do tenant | ✅ |
| Pausa desloca prazos | C-suite S8 | `sla_due_on` recalculado após pausa justificada | ✅ |
| Biblioteca global | E-suite S8 | 9 modelos publicados visíveis sem instalação local | ✅ |
