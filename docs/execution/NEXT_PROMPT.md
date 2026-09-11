# Próxima execução

## Fechar evidência do S02 (contratos)

```powershell
.\scripts\setup-local.ps1
dotnet run --project src/Odca.Bootstrap -- migrate
dotnet run --project src/Odca.Bootstrap -- provision-test-access --environment Development --allow-postgres-development
dotnet run --project src/Odca.Bootstrap -- seed-contracts-demo --reference-date=2026-09-11
dotnet run --project src/Odca.Bootstrap -- show-login
.\scripts\run-local.ps1
```

Suíte descartável:

```powershell
$env:ODCA_TEST_ENVIRONMENT = 'ODCA_INTEGRATION_TESTS'
$env:ODCA_TEST_ADMIN_CONNECTION = 'Host=localhost;Port=5432;Database=odca_test_local;Username=postgres;Password=<senha-admin>;Include Error Detail=false'
$env:ODCA_TEST_APP_CONNECTION = 'Host=localhost;Port=5432;Database=odca_test_local;Username=odca_test_app_login;Password=<senha-teste>;Include Error Detail=false'
dotnet test tests/Odca.IntegrationTests --configuration Release
```

Jornada E2E: login → organização → contraparte → tipo → contrato rascunho → ativar acompanhamento → alerta → renovação → histórico. QA 360/768/1280/1440, teclado e zoom 200%.

## Próxima fatia vertical

Upload seguro → revisão de extração → editor e versões → assinatura e créditos → integrações de comunicação e cobrança.

Não implementar vários módulos pela metade.
