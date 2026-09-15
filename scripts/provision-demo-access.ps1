[CmdletBinding()]
param(
    [switch]$ApplyMigrations,
    [ValidateSet('admin', 'client')]
    [string]$ResetPassword
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$repositoryRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$bootstrapProject = Join-Path $repositoryRoot 'src/Odca.Bootstrap/Odca.Bootstrap.csproj'
$runtimeFile = Join-Path $repositoryRoot 'src/Odca.Api/development-runtime.json'
$seedFile = Join-Path $repositoryRoot 'database/development/seed-test-access.sql'

if (-not (Test-Path $bootstrapProject) -or -not (Test-Path $seedFile)) {
    throw "Repositorio ODCA incompleto em '$repositoryRoot'."
}
if (-not (Test-Path $runtimeFile)) {
    throw "Configuracao ausente. Execute: dotnet run --project `"$bootstrapProject`" -- init --postgres-development"
}

$runtime = Get-Content -Raw $runtimeFile | ConvertFrom-Json
if (-not $runtime.ConnectionStrings.DatabaseAdmin) {
    throw 'ConnectionStrings:DatabaseAdmin esta ausente em development-runtime.json.'
}
if (-not $runtime.Security.AllowDevelopmentBootstrap) {
    throw 'Security:AllowDevelopmentBootstrap precisa estar habilitado explicitamente em Development.'
}

$connection = [System.Data.Common.DbConnectionStringBuilder]::new()
$connection.ConnectionString = [string]$runtime.ConnectionStrings.DatabaseAdmin
$applicationConnection = [System.Data.Common.DbConnectionStringBuilder]::new()
$applicationConnection.ConnectionString = [string]$runtime.ConnectionStrings.Database
$database = [string]$connection['Database']
$hostName = [string]$connection['Host']
$port = [int]$connection['Port']
$username = [string]$connection['Username']
$searchPath = [string]$connection['Search Path']
if ($database -ne 'postgres') {
    throw "Este script local exige Database=postgres; encontrado '$database'."
}
if ($hostName -notin @('localhost', '127.0.0.1', '::1') -or $port -ne 5432 -or
    $username -ne 'postgres' -or $searchPath -ne 'odca') {
    throw 'DatabaseAdmin deve apontar para localhost:5432, Database=postgres, Username=postgres e Search Path=odca.'
}
if ([string]$applicationConnection['Host'] -ne $hostName -or
    [int]$applicationConnection['Port'] -ne $port -or
    [string]$applicationConnection['Database'] -ne $database -or
    [string]$applicationConnection['Search Path'] -ne 'odca') {
    throw 'ConnectionStrings:Database da API deve consultar o mesmo host, porta, banco e schema odca.'
}

Push-Location $repositoryRoot
try {
    & dotnet run --project $bootstrapProject -- diagnose --connection
    if ($LASTEXITCODE -ne 0 -and -not $ApplyMigrations) {
        throw "Schema ausente ou desatualizado. Execute novamente com -ApplyMigrations (nao recria o banco), ou: dotnet run --project `"$bootstrapProject`" -- migrate"
    }
    if ($ApplyMigrations) {
        & dotnet run --project $bootstrapProject -- migrate
        if ($LASTEXITCODE -ne 0) { throw 'Falha ao aplicar migrations; provisionamento cancelado.' }
    }

    $arguments = @('run', '--project', $bootstrapProject, '--', 'provision-test-access',
        '--environment', 'Development', '--allow-postgres-development', '--prompt-passwords')
    if ($ResetPassword) { $arguments += @('--rotate-password', $ResetPassword) }
    & dotnet @arguments
    if ($LASTEXITCODE -ne 0) { throw 'Falha no provisionamento; nenhuma credencial sera exibida.' }

    & dotnet run --project $bootstrapProject -- show-login
    if ($LASTEXITCODE -ne 0) { throw 'Hashes persistidos, mas a exibicao local das credenciais nao foi confirmada.' }
}
finally {
    Pop-Location
}
