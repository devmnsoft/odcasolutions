[CmdletBinding()]
param([switch]$NonInteractive)

$ErrorActionPreference = 'Stop'
$repositoryRoot = Split-Path -Parent $PSScriptRoot
$runtimeFile = $env:ODCA_RUNTIME_CONFIG
if ($null -ne $runtimeFile -and [string]::IsNullOrWhiteSpace($runtimeFile)) {
    throw 'ODCA_RUNTIME_CONFIG foi definida com um caminho vazio.'
}
if ($null -eq $runtimeFile) {
    $runtimeFile = Join-Path $repositoryRoot 'src\Odca.Api\development-runtime.json'
}
$runtimeFile = [IO.Path]::GetFullPath($runtimeFile)
$env:ODCA_RUNTIME_CONFIG = $runtimeFile
$env:DOTNET_ENVIRONMENT = 'Development'
$env:ASPNETCORE_ENVIRONMENT = 'Development'
if (-not (Test-Path -LiteralPath $runtimeFile)) {
    if ($NonInteractive -or -not [Environment]::UserInteractive) {
        throw "Configuração local ausente em '$runtimeFile'. Execute .\scripts\setup-local.ps1 em um terminal interativo antes de iniciar os serviços."
    }
    Write-Host 'Configuração ausente; iniciando a preparação local antes dos serviços.'
    & (Join-Path $PSScriptRoot 'setup-local.ps1')
    if ($LASTEXITCODE -ne 0 -or -not (Test-Path -LiteralPath $runtimeFile)) {
        throw 'A preparação local falhou ou não criou a configuração; nenhum serviço foi iniciado.'
    }
}

& dotnet run --project (Join-Path $repositoryRoot 'src\Odca.Bootstrap') -- diagnose
if ($LASTEXITCODE -ne 0) { throw 'A configuração local é inválida; nenhum serviço foi iniciado.' }

$projects = @(
    @{ Name = 'API'; Path = 'src\Odca.Api'; Profile = 'https' },
    @{ Name = 'Web'; Path = 'src\Odca.Web'; Profile = 'https' },
    @{ Name = 'Worker'; Path = 'src\Odca.Worker'; Profile = 'Odca.Worker' }
)
$processes = @()
try {
    foreach ($project in $projects) {
        $arguments = @('run', '--project', (Join-Path $repositoryRoot $project.Path), '--launch-profile', $project.Profile)
        $processes += Start-Process dotnet -ArgumentList $arguments -WorkingDirectory $repositoryRoot -NoNewWindow -PassThru
        Write-Host ("{0} iniciado (PID {1})." -f $project.Name, $processes[-1].Id)
    }

    $ready = $false
    for ($attempt = 0; $attempt -lt 30 -and -not $ready; $attempt++) {
        Start-Sleep -Seconds 1
        $failed = @($processes | Where-Object { $_.Refresh(); $_.HasExited })
        if ($failed.Count -gt 0) {
            $details = ($failed | ForEach-Object { "PID $($_.Id), código $($_.ExitCode)" }) -join '; '
            throw "Um ou mais processos encerraram prematuramente ($details)."
        }
        try {
            $response = Invoke-WebRequest -Uri 'https://localhost:7143/health/ready' -UseBasicParsing -TimeoutSec 2
            $ready = $response.StatusCode -eq 200
        }
        catch { Write-Verbose "Readiness ainda indisponível: $($_.Exception.Message)" }
    }
    if (-not $ready) { throw 'A API não respondeu pronta em /health/ready após 30 segundos; os serviços serão encerrados.' }

    Write-Host 'ODCA pronta: https://localhost:7144 | API pronta: https://localhost:7143'
    Write-Host 'Pressione Ctrl+C para encerrar os processos locais.'
    Wait-Process -Id $processes.Id
}
finally {
    foreach ($process in $processes) {
        if (-not $process.HasExited) {
            Stop-Process -Id $process.Id
        }
    }
}
