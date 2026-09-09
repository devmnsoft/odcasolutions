[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$repositoryRoot = Split-Path -Parent $PSScriptRoot
$runtimeFile = Join-Path $env:LOCALAPPDATA 'ODCA Solutions\development-runtime.json'
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
