[CmdletBinding()]
param([switch]$Force)

$ErrorActionPreference = 'Stop'
$repositoryRoot = [IO.Path]::GetFullPath((Split-Path -Parent $PSScriptRoot)).TrimEnd('\')
$escapedRoot = [Regex]::Escape($repositoryRoot)
$projectPattern = 'Odca\.(Api|Web|Worker)(\.csproj|\.dll|\.exe)?'

function Test-OdcaCheckoutProcess {
    param($Process)

    if ($Process.CommandLine -and $Process.CommandLine -match $escapedRoot -and $Process.CommandLine -match $projectPattern) {
        return $true
    }

    # Fallback when CommandLine is truncated: the executable itself lives under this checkout.
    if ($Process.ExecutablePath -and $Process.ExecutablePath -match $escapedRoot -and $Process.ExecutablePath -match 'Odca\.(Api|Web|Worker)\.exe$') {
        return $true
    }

    return $false
}

$processes = @(Get-CimInstance Win32_Process | Where-Object { Test-OdcaCheckoutProcess $_ })

if ($processes.Count -eq 0) {
    Write-Host "Nenhum processo ODCA comprovadamente pertencente a '$repositoryRoot' foi encontrado."
    exit 0
}

Write-Host 'Processos deste checkout:'
$processes | Select-Object ProcessId, Name, CommandLine, ExecutablePath | Format-Table -Wrap

foreach ($candidate in $processes) {
    $process = Get-Process -Id $candidate.ProcessId -ErrorAction SilentlyContinue
    if ($null -eq $process) { continue }

    if ($process.CloseMainWindow()) {
        Write-Host "Solicitado encerramento normal ao PID $($process.Id)."
        $null = $process.WaitForExit(5000)
    }

    if (-not $process.HasExited) {
        if ($Force) {
            Write-Warning "Encerrando à força a árvore do PID $($process.Id), conforme solicitado com -Force."
            # /T covers `dotnet run` hosts and their Odca.*.exe children from this checkout.
            & taskkill.exe /PID $process.Id /T /F 2>$null | Out-Null
            $null = $process.WaitForExit(5000)
        }
        else {
            Write-Warning "PID $($process.Id) não oferece encerramento normal. Pare a depuração/terminal ou repita com -Force."
        }
    }
}

$remaining = @(Get-CimInstance Win32_Process | Where-Object { Test-OdcaCheckoutProcess $_ })
if ($remaining.Count -gt 0) {
    Write-Host 'Processos ainda ativos neste checkout:'
    $remaining | Select-Object ProcessId, Name, CommandLine, ExecutablePath | Format-Table -Wrap
    if (-not $Force) { exit 2 }
    exit 1
}

Write-Host "Nenhum processo ODCA deste checkout permanece ativo."
