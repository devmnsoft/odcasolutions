# D-OC26 - Integration suite runner (dedicated disposable database).
# Run: powershell -NoProfile -ExecutionPolicy Bypass -File scripts/qa/run-integration-tests.ps1 [-FreshDb]
#
# DatabaseFixture creates/migrates odca_test_integration and provisions the role
# odca_test_app_login (see tests/Odca.IntegrationTests/DatabaseFixture.cs).
# -FreshDb drops and recreates the database first: the fully-green proof in this
# increment was done on a fresh database, where defects that a stale database
# masks (migration bootstrap, official-catalog seeding) actually surface.
# Connection strings may be overridden via the same environment variables the
# fixture reads; defaults below match the local QA PostgreSQL instance.
#
# ErrorActionPreference is 'Continue', not 'Stop': dotnet streams progress to
# stderr and PowerShell would otherwise turn each line into a terminating error.
param([switch]$FreshDb)

$ErrorActionPreference = 'Continue'
[Console]::OutputEncoding = [System.Text.Encoding]::UTF8

$repoRoot = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)

if (-not $env:ODCA_TEST_ENVIRONMENT) { $env:ODCA_TEST_ENVIRONMENT = 'ODCA_INTEGRATION_TESTS' }
if (-not $env:ODCA_TEST_ADMIN_CONNECTION) { $env:ODCA_TEST_ADMIN_CONNECTION = 'Host=127.0.0.1;Port=5432;Database=odca_test_integration;Username=postgres;Password=123456' }
if (-not $env:ODCA_TEST_APP_CONNECTION) { $env:ODCA_TEST_APP_CONNECTION = 'Host=127.0.0.1;Port=5432;Database=odca_test_integration;Username=odca_test_app_login;Password=OdcaTestApp#2026' }

function CsValue([string]$connectionString, [string]$key) {
    $m = [regex]::Match($connectionString, '(?:^|;)' + $key + '=([^;]+)')
    if (-not $m.Success) { throw ('chave ausente na connection string: ' + $key) }
    return $m.Groups[1].Value
}

if ($FreshDb) {
    $psql = 'C:\Program Files\PostgreSQL\18\bin\psql.exe'
    $env:PGPASSWORD = CsValue $env:ODCA_TEST_ADMIN_CONNECTION 'Password'
    $dbName = CsValue $env:ODCA_TEST_ADMIN_CONNECTION 'Database'
    $srv = @('-h', (CsValue $env:ODCA_TEST_ADMIN_CONNECTION 'Host'), '-p', (CsValue $env:ODCA_TEST_ADMIN_CONNECTION 'Port'), '-U', (CsValue $env:ODCA_TEST_ADMIN_CONNECTION 'Username'), '-d', 'postgres')
    & $psql @srv -v ON_ERROR_STOP=1 -c "DROP DATABASE IF EXISTS $dbName;" -c "CREATE DATABASE $dbName;"
    if ($LASTEXITCODE -ne 0) { Write-Host 'FRESH-DB-FAIL'; exit $LASTEXITCODE }
    Write-Host ('FRESH-DB-OK ' + $dbName)
}

# Release binary locks: dev apps still running against this repository prevent
# the Windows build from replacing assemblies.
Get-CimInstance Win32_Process -Filter "Name='dotnet.exe'" | Where-Object { $_.CommandLine -like ('*' + $repoRoot + '*') } | ForEach-Object { try { Stop-Process -Id $_.ProcessId -Force -ErrorAction SilentlyContinue } catch {} ; Start-Sleep -Seconds 1 }

Set-Location $repoRoot
dotnet build .\Odca.sln -c Debug -v m
if ($LASTEXITCODE -ne 0) { Write-Host 'BUILD-FAIL'; exit $LASTEXITCODE }
Write-Host 'BUILD-OK'

$logPath = Join-Path $env:TEMP 'odca-integration-tests.log'
dotnet test .\tests\Odca.IntegrationTests -c Debug --no-build --nologo -v m 2>&1 | Tee-Object -FilePath $logPath
$code = $LASTEXITCODE
Write-Host ('TEST-EXIT=' + $code)
exit $code
