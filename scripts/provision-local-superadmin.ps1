[CmdletBinding()]
param(
    [string]$AdminEmail = 'admin@odca.local',
    [string]$AdminPassword = 'V7!qM2#rL9@xT4$p',
    [string]$OperatorEmail = 'operador@odca.local',
    [string]$OperatorPassword = 'OdcaOperador#2026Local',
    [string]$ClientEmail = 'cliente.teste@odca.local',
    [string]$ClientPassword = 'K8@wR3!nF6#zP2$m',
    [switch]$ApplyMigrations,
    [switch]$RequireInitialPasswordChange
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

# Development only. The application authenticates against odca.users.password_hash.
# Plaintext never enters src/Odca.Api or src/Odca.Web.

$repositoryRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$bootstrapProject = Join-Path $repositoryRoot 'src/Odca.Bootstrap/Odca.Bootstrap.csproj'
$runtimeFile = Join-Path $repositoryRoot 'src/Odca.Api/development-runtime.json'
$envFile = Join-Path $repositoryRoot 'database/development/local-access.env'

if (Test-Path $envFile) {
    Get-Content $envFile | ForEach-Object {
        if ($_ -match '^\s*#' -or $_ -notmatch '=') { return }
        $name, $value = $_.Split('=', 2)
        Set-Item -Path "Env:$($name.Trim())" -Value $value.Trim()
    }
}

if ($env:ODCA_DEV_ADMIN_EMAIL) { $AdminEmail = $env:ODCA_DEV_ADMIN_EMAIL }
if ($env:ODCA_DEV_ADMIN_PASSWORD) { $AdminPassword = $env:ODCA_DEV_ADMIN_PASSWORD }
if ($env:ODCA_DEV_OPERATOR_EMAIL) { $OperatorEmail = $env:ODCA_DEV_OPERATOR_EMAIL }
if ($env:ODCA_DEV_OPERATOR_PASSWORD) { $OperatorPassword = $env:ODCA_DEV_OPERATOR_PASSWORD }
if ($env:ODCA_DEV_CLIENT_EMAIL) { $ClientEmail = $env:ODCA_DEV_CLIENT_EMAIL }
if ($env:ODCA_DEV_CLIENT_PASSWORD) { $ClientPassword = $env:ODCA_DEV_CLIENT_PASSWORD }

if ($AdminEmail -ne 'admin@odca.local') {
    throw "A identidade reservada do superadministrador local e admin@odca.local. Encontrado '$AdminEmail'."
}
if ($OperatorEmail -ne 'operador@odca.local') {
    throw "A identidade reservada do operador local e operador@odca.local. Encontrado '$OperatorEmail'."
}
if ($ClientEmail -ne 'cliente.teste@odca.local') {
    throw "A identidade reservada do cliente local e cliente.teste@odca.local. Encontrado '$ClientEmail'."
}

if (-not (Test-Path $bootstrapProject)) {
    throw "Repositorio ODCA incompleto em '$repositoryRoot'."
}
if (-not (Test-Path $runtimeFile)) {
    throw "Configuracao ausente. Execute: dotnet run --project `"$bootstrapProject`" -- init --postgres-development"
}

Push-Location $repositoryRoot
try {
    if ($ApplyMigrations) {
        & dotnet run --project $bootstrapProject -- migrate
        if ($LASTEXITCODE -ne 0) { throw 'Falha ao aplicar migrations; nenhuma senha foi gravada.' }
    }

    $arguments = @(
        'run', '--project', $bootstrapProject, '--',
        'provision-test-access',
        '--environment', 'Development',
        '--allow-postgres-development',
        '--administrator-password', $AdminPassword,
        '--operator-password', $OperatorPassword,
        '--client-password', $ClientPassword
    )
    if (-not $RequireInitialPasswordChange) { $arguments += '--allow-immediate-login' }

    $env:ODCA_DEV_ADMIN_PASSWORD = $AdminPassword
    $env:ODCA_DEV_OPERATOR_PASSWORD = $OperatorPassword
    $env:ODCA_DEV_CLIENT_PASSWORD = $ClientPassword
    & dotnet @arguments
    if ($LASTEXITCODE -ne 0) { throw 'Falha no provisionamento; o hash nao foi confirmado no banco.' }

    & dotnet run --project $bootstrapProject -- show-login
    if ($LASTEXITCODE -ne 0) { throw 'Hash persistido, mas show-login nao confirmou a senha contra odca.users.' }

    Write-Host ''
    Write-Host 'Credenciais de Development confirmadas no banco (nao estao no codigo da API):'
    Write-Host "  Superadministrador  $AdminEmail"
    Write-Host "  Senha               $AdminPassword"
    Write-Host "  Operador            $OperatorEmail"
    Write-Host "  Senha               $OperatorPassword"
    Write-Host "  Cliente demo        $ClientEmail"
    Write-Host "  Senha               $ClientPassword"
    Write-Host 'Web: https://localhost:7144/entrar'
    Write-Host 'A autenticacao consulta odca.users. Se Security:MfaRequiredForSuperAdmin=true, conclua o TOTP apos o login.'
}
finally {
    Pop-Location
}
