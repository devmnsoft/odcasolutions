[CmdletBinding()]
param([switch]$Force)

$ErrorActionPreference = 'Stop'
$repositoryRoot = [IO.Path]::GetFullPath((Split-Path -Parent $PSScriptRoot)).TrimEnd('\')
$escapedRoot = [Regex]::Escape($repositoryRoot)
$projectPattern = 'Odca\.(Api|Web|Worker)(\.csproj|\.dll|\.exe)?'

$processes = @(Get-CimInstance Win32_Process | Where-Object {
    $_.CommandLine -and $_.CommandLine -match $escapedRoot -and $_.CommandLine -match $projectPattern
})

if ($processes.Count -eq 0) {
    Write-Host "Nenhum processo ODCA comprovadamente pertencente a '$repositoryRoot' foi encontrado."
    exit 0
}

Write-Host 'Processos deste checkout:'
$processes | Select-Object ProcessId, Name, CommandLine | Format-Table -Wrap

foreach ($candidate in $processes) {
    $process = Get-Process -Id $candidate.ProcessId -ErrorAction SilentlyContinue
    if ($null -eq $process) { continue }

    if ($process.CloseMainWindow()) {
        Write-Host "Solicitado encerramento normal ao PID $($process.Id)."
        $null = $process.WaitForExit(5000)
    }

    if (-not $process.HasExited) {
        if ($Force) {
            Write-Warning "Encerrando à força o PID $($process.Id), conforme solicitado com -Force."
            Stop-Process -Id $process.Id -Force
        }
        else {
            Write-Warning "PID $($process.Id) não oferece encerramento normal. Pare a depuração/terminal ou repita com -Force."
        }
    }
}

if (-not $Force -and @($processes | Where-Object { Get-Process -Id $_.ProcessId -ErrorAction SilentlyContinue }).Count -gt 0) {
    exit 2
}
