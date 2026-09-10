[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$repositoryRoot = Split-Path -Parent $PSScriptRoot
$runtimeFile = $env:ODCA_RUNTIME_CONFIG
if ([string]::IsNullOrWhiteSpace($runtimeFile)) {
    $runtimeFile = Join-Path $repositoryRoot 'src\Odca.Api\development-runtime.json'
}
$env:ODCA_RUNTIME_CONFIG = $runtimeFile
$env:DOTNET_ENVIRONMENT = 'Development'
$env:ASPNETCORE_ENVIRONMENT = 'Development'
if (-not (Test-Path -LiteralPath $runtimeFile)) {
    throw 'Configuração local ausente. Execute o bootstrap init e configure-native (PostgreSQL nativo) ou init + Docker.'
}

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

    Start-Sleep -Seconds 5
    $failed = @($processes | Where-Object { $_.Refresh(); $_.HasExited })
    if ($failed.Count -gt 0) {
        $details = ($failed | ForEach-Object { "PID $($_.Id), código $($_.ExitCode)" }) -join '; '
        throw "Um ou mais processos encerraram prematuramente ($details). Execute 'dotnet run --project src/Odca.Bootstrap -- diagnose' e revise a saída do processo acima. Para preencher campos ausentes, execute repair."
    }

    Write-Host 'ODCA: https://localhost:7144 | API: https://localhost:7143'
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
