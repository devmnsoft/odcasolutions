[CmdletBinding()]
param(
    [switch]$NonInteractive
)

$ErrorActionPreference = 'Stop'
$script = Join-Path $PSScriptRoot 'scripts\run-local.ps1'
if (-not (Test-Path -LiteralPath $script)) {
    throw "Script canônico não encontrado em '$script'."
}

& $script @PSBoundParameters
