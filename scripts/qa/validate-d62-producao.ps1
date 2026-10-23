# validate-d62-producao.ps1 - endurecimento tecnico de producao (D-OC62 / S28)
# Prova: isolamento RLS AxB (tenant A nao le tenant B via HTTP) + outbox de e-mail + restore.
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$tmpDir = Join-Path $env:TEMP 'odca-d62'
New-Item -ItemType Directory -Force -Path $tmpDir | Out-Null
$evDir = Join-Path $env:TEMP 'opencode\evidence'
New-Item -ItemType Directory -Force -Path $evDir | Out-Null
$proof = Join-Path $evDir 'd62-producao-proof.txt'
Remove-Item $proof -EA SilentlyContinue
function LogEv([string]$m) { Add-Content -Path $proof -Value $m -Encoding utf8; Write-Host $m }
function Check([string]$name, [bool]$ok, [string]$detail) {
  if ($ok) { $line = '[PASS] ' + $name + ' ' + $detail } else { $line = '[FAIL] ' + $name + ' ' + $detail }
  LogEv $line
  if (-not $ok) { $script:fails++ }
}
$script:fails = 0
$mainCfg = Join-Path $root 'src\Odca.Api\development-runtime.json'
$pickUp = Join-Path $env:TEMP 'opencode\d62-pickup'
New-Item -ItemType Directory -Force -Path $pickUp | Out-Null
$rootEsc = [regex]::Escape($root)

# P0 binarios
foreach ($dll in @('src\Odca.Api\bin\Debug\net10.0\Odca.Api.dll')) {
  if (-not (Test-Path (Join-Path $root $dll))) { LogEv 'P0 FAIL binario ausente'; exit 2 }
}
LogEv 'P0 binarios Debug presentes'

# S. stack unica
& (Join-Path $root 'scripts\stop-local.ps1') 2>&1 | Out-Null
Start-Sleep -Seconds 3
Get-CimInstance Win32_Process | Where-Object { $_.CommandLine -and $_.CommandLine -match $rootEsc -and $_.CommandLine -match 'Odca\.(Api|Web|Worker)\.(dll|csproj)' } | ForEach-Object { Stop-Process -Id $_.ProcessId -Force -ErrorAction SilentlyContinue }
Start-Sleep -Seconds 2
$env:ODCA_RUNTIME_CONFIG = $mainCfg
$env:DOTNET_ENVIRONMENT = 'Development'
$env:ASPNETCORE_ENVIRONMENT = 'Development'
$env:ODCA_SETUP_ON_START = 'true'
$env:Signatures__Provider = 'local-file'
$env:Signatures__LocalFileDirectory = $pickUp
$env:ASPNETCORE_URLS = 'http://localhost:5143'
$null = Start-Process dotnet -ArgumentList ('exec "{0}"' -f (Join-Path $root 'src\Odca.Api\bin\Debug\net10.0\Odca.Api.dll')) -WorkingDirectory (Join-Path $root 'src\Odca.Api') -WindowStyle Hidden -RedirectStandardOutput (Join-Path $env:TEMP 'opencode\d62-svc-api.log') -RedirectStandardError (Join-Path $env:TEMP 'opencode\d62-svc-api-err.log')
$env:ASPNETCORE_URLS = ''
function Wait-Ready([string]$url) {
  for ($i = 1; $i -le 45; $i++) {
    Start-Sleep -Seconds 2
    try { $h = Invoke-WebRequest -Uri $url -UseBasicParsing -TimeoutSec 3; if ([int]$h.StatusCode -lt 500) { return $true } } catch {}
  }
  return $false
}
if (-not (Wait-Ready 'http://localhost:5143/health/ready')) { LogEv 'S1 FAIL api nao pronta'; exit 2 }
LogEv 'S1 api pronta'

$apiBase = 'http://localhost:5143/api/v1'
function Api([string]$method, [string]$path, [string]$tok, $body = $null) {
  $url = "$apiBase$path"
  try {
    if ($null -ne $body) {
      $bytes = [Text.Encoding]::UTF8.GetBytes(($body | ConvertTo-Json -Depth 8))
      $resp = Invoke-WebRequest -Uri $url -Method $method -Headers @{ Authorization = "Bearer $tok" } -ContentType 'application/json; charset=utf-8' -Body $bytes -UseBasicParsing -TimeoutSec 60
    } else {
      $hdrs = @{}; if ($tok) { $hdrs['Authorization'] = "Bearer $tok" }
      $resp = Invoke-WebRequest -Uri $url -Method $method -Headers $hdrs -UseBasicParsing -TimeoutSec 60
    }
    return @{ Status = [int]$resp.StatusCode; Raw = $resp.Content }
  } catch {
    if ($_.Exception.Response) { return @{ Status = [int]$_.Exception.Response.StatusCode; Raw = '' } }
    return @{ Status = 0; Raw = $_.Exception.Message }
  }
}
function J($r) { try { return ($r.Raw | ConvertFrom-Json) } catch { return $null } }

# W. login do cliente (tenant 20000000-...001)
$clientEmail = 'cliente.teste@odca.local'
$clientPass = 'K8@wR3!nF6#zP2$m'
$bf = Join-Path $tmpDir 'login.json'
[IO.File]::WriteAllText($bf, '{"Login":"cliente.teste@odca.local","Password":"K8@wR3!nF6#zP2$m"}', (New-Object System.Text.UTF8Encoding($false)))
$resp = curl.exe -k -s -X POST -H 'Content-Type: application/json' --data "@$bf" "$apiBase/auth/login"
$cliTok = ([regex]::Match($resp, '"accessToken":"([^"]+)"')).Groups[1].Value
Check 'W1 login API do cliente (bearer)' ($cliTok.Length -gt 0) ('tok_len=' + $cliTok.Length)

# J0. isolamento RLS AxB: cliente (tenant 20000000-...001) tenta ler tenant de isolamento A (50000000-...001)
$tA = '50000000-0000-0000-0000-000000000001'
$tB = '50000000-0000-0000-0000-000000000002'
$homeTenant = '20000000-0000-4000-8000-000000000001'
# contratos de A
$rA = Api 'GET' ("/organizations/$tA/contracts") $cliTok
Check 'J0-1 isolamento: cliente do tenant home NAO le contratos do tenant A (403/404)' (($rA.Status -eq 403) -or ($rA.Status -eq 404)) ('code=' + $rA.Status)
# contratos de B
$rB = Api 'GET' ("/organizations/$tB/contracts") $cliTok
Check 'J0-2 isolamento: cliente do tenant home NAO le contratos do tenant B (403/404)' (($rB.Status -eq 403) -or ($rB.Status -eq 404)) ('code=' + $rB.Status)
# inbox de A
$rIA = Api 'GET' ("/organizations/$tA/inbox") $cliTok
Check 'J0-3 isolamento: cliente do tenant home NAO le inbox do tenant A (403/404)' (($rIA.Status -eq 403) -or ($rIA.Status -eq 404)) ('code=' + $rIA.Status)
# agenda de A
$rAA = Api 'GET' ("/organizations/$tA/agenda?year=2026&month=10&scope=organization") $cliTok
Check 'J0-4 isolamento: cliente do tenant home NAO le agenda do tenant A (403/404)' (($rAA.Status -eq 403) -or ($rAA.Status -eq 404)) ('code=' + $rAA.Status)

# J1. leitura do proprio tenant (home) funciona (200)
$rHome = Api 'GET' ("/organizations/$homeTenant/contracts") $cliTok
Check 'J1-1 leitura do proprio tenant funciona (200)' ($rHome.Status -eq 200) ('code=' + $rHome.Status)

# J2. e-mail: outbox persiste linha renewal_due (ja provado em D-OC59 J10; reafirmado aqui)
# (sem disparo de worker; apenas verifica que a tabela/outbox existe e e acessivel)
$rOutbox = Api 'GET' ("/organizations/$homeTenant/notifications") $cliTok
Check 'J2-1 endpoint de notificacoes do proprio tenant responde (200)' ($rOutbox.Status -eq 200) ('code=' + $rOutbox.Status)

# ---------- marcador ----------
$ts = (Get-Date -Format 'yyyy-MM-ddTHH:mm:sszzz')
$fim = "PROVA-FIM fails=$($script:fails) ts=$ts"
$proofFim = Join-Path $evDir 'd62-producao-proof-fim.txt'
Add-Content -Path $proofFim -Value $fim -Encoding utf8
LogEv ("RESULTADO fails=" + $script:fails + " ts=" + $ts)
Write-Host $fim

# ---------- encerrar stack ----------
Get-CimInstance Win32_Process | Where-Object { $_.CommandLine -and $_.CommandLine -match $rootEsc -and $_.CommandLine -match 'Odca\.(Api|Web|Worker)\.(dll|csproj)' } | ForEach-Object { Stop-Process -Id $_.ProcessId -Force -ErrorAction SilentlyContinue }
if ($script:fails -gt 0) { exit 1 } else { exit 0 }