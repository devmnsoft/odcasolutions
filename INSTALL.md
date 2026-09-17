# Como aplicar

Copiar cada arquivo para o mesmo caminho relativo em odcasolutions
(base codex/s00-foundation após merge #62 e #63).

## Ajustes no código já existente

1. src/Odca.Infrastructure/DependencyInjection.cs
   services.AddOdcaOperationalInbox();
   using Odca.Infrastructure.Operations;

2. src/Odca.Web/Services/OdcaApiClient.cs
   Métodos de OdcaApiClient.Operations.fragment.cs
   using Odca.Contracts.Operations;

3. Schema já usado no SQL deste pacote:
   contracts.end_date, contracts.owner_id, contracts.renewal_notice_amount,
   contracts.renewal_notice_unit, contract_obligations.due_date,
   tenants.timezone, contract_reviews + contract_review_steps.
   Se v012/v017 usarem outro nome, alinhar só o SELECT.

4. POST da biblioteca oficial já existe no Estúdio:
   POST /api/v1/organizations/{tenantId}/studio/templates/official

Não criar migration de tarefas. Não gravar senha na API.
