# D-OC39 - capturas visuais da Sprint 5: pagina de sessoes de suporte (pt-BR, 4 viewports +
# zoom 200% => 5 png). Estado deterministico montado localmente: superadministrador sintetico
# com MFA reinscrita (chave conhecida para o login do driver) e UMA janela ativa de 60 min
# para o tenant demo, exibida na tabela "Sessoes recentes".
# Limitacao registrada: a pagina e pt-first (divida i18n documentada em D-OC39); os outros
# idiomas nao sao capturados porque exibem o conteudo em portugues ate a traducao da area.
[Console]::OutputEncoding = [System.Text.Encoding]::UTF8
$root   = 'C:\MNSOFT\odcasolutions'
$logDir = 'C:\Users\NCELL-DEV-020\AppData\Local\Temp\opencode'
$evDir  = Join-Path $logDir 'evidence'
$outDir = Join-Path $evDir 'captures-d39'
New-Item -ItemType Directory -Force -Path $outDir | Out-Null
$evidence = Join-Path $evDir 'd39-captures.txt'
Set-Content -Path $evidence '' -Encoding utf8
function LogEv([string]$m) { Add-Content -Path $evidence $m -Encoding utf8 }
function Db([string]$q) {
  $env:PGPASSWORD = '123456'; $env:PGCONNECT_TIMEOUT = '10'
  $out = & 'C:\Program Files\PostgreSQL\18\bin\psql.exe' -h 127.0.0.1 -U postgres -d odca_test_disposable -t -A -c $q 2>&1 | Out-String
  return $out.Trim()
}
function Get-TotpCode([string]$secret) {
  $alpha = 'ABCDEFGHIJKLMNOPQRSTUVWXYZ234567'
  $clean = ($secret.ToUpperInvariant() -replace '[^A-Z2-7]', '')
  $bits = ''
  foreach ($ch in $clean.ToCharArray()) { $bits += [Convert]::ToString($alpha.IndexOf($ch), 2).PadLeft(5, '0') }
  $byteCount = [int][Math]::Floor($bits.Length / 8)
  $key = New-Object 'byte[]' $byteCount
  for ($i = 0; $i -lt $byteCount; $i++) { $key[$i] = [Convert]::ToByte($bits.Substring($i * 8, 8), 2) }
  $counter = [long][Math]::Floor([DateTimeOffset]::UtcNow.ToUnixTimeSeconds() / 30)
  $cb = New-Object 'byte[]' 8
  for ($j = 7; $j -ge 0; $j--) { $cb[$j] = [byte]($counter -band 0xff); $counter = $counter -shr 8 }
  $hmac = New-Object System.Security.Cryptography.HMACSHA1
  $hmac.Key = $key
  $h = $hmac.ComputeHash($cb)
  $o = $h[$h.Length - 1] -band 0x0f
  $bin = ((([int]$h[$o]) -band 0x7f) * 16777216) + ((([int]$h[$o + 1]) -band 0xff) * 65536) + ((([int]$h[$o + 2]) -band 0xff) * 256) + (([int]$h[$o + 3]) -band 0xff)
  return ('{0:D6}' -f ($bin % 1000000))
}

$t        = '20000000-0000-4000-8000-000000000001'
$uAdm     = 'c9000000-0000-4000-8000-000000000039'
$admEmail = 'suporte.prova@odca.local'
$admPass  = 'La@RsjC5p-hmq_JpReGW'   # mesma senha do operador (hash clonado no seed)

# ---------- estado: superadministrador da prova (idempotente) ----------
$cntU = [long](Db "SELECT count(*) FROM odca.users WHERE id='$uAdm';")
if ($cntU -ne 1) {
  $seedSql = @"
INSERT INTO odca.users(id,email,email_normalized,login_normalized,display_name,password_hash,must_change_password,is_platform_administrator,email_verified_at)
SELECT '$uAdm','$admEmail','SUPORTE.PROVA@ODCA.LOCAL','SUPORTE.PROVA@ODCA.LOCAL','Suporte Prova D-OC39',
       (SELECT password_hash FROM odca.users WHERE email_normalized='OPERADOR@ODCA.LOCAL'), false, true, now();
"@
  $seedFile = Join-Path $logDir 'd39-seed-capture.sql'
  [IO.File]::WriteAllText($seedFile, $seedSql, [Text.Encoding]::UTF8)
  $env:PGPASSWORD = '123456'
  & 'C:\Program Files\PostgreSQL\18\bin\psql.exe' -h 127.0.0.1 -U postgres -d odca_test_disposable -v ON_ERROR_STOP=1 -f $seedFile 2>&1 | ForEach-Object { LogEv ('SEED| ' + $_) }
  if ($LASTEXITCODE -ne 0) { Write-Host 'SEED-D39-FALHOU'; exit 2 }
}
LogEv ("estado: usuario-prova=" + $uAdm + " presente")

# ---------- stack (janela MFA padrao: 15 min) ----------
$rootEsc = [Regex]::Escape($root)
Get-CimInstance Win32_Process | Where-Object { $_.CommandLine -and $_.CommandLine -match $rootEsc -and $_.CommandLine -match 'Odca\.(Api|Web|Worker)\.(dll|csproj)' } | ForEach-Object { Stop-Process -Id $_.ProcessId -Force -ErrorAction SilentlyContinue }
Start-Sleep -Seconds 2
$env:ODCA_RUNTIME_CONFIG = Join-Path $root 'src\Odca.Api\development-runtime.json'
$env:DOTNET_ENVIRONMENT = 'Development'
$env:ASPNETCORE_ENVIRONMENT = 'Development'
$env:ODCA_SETUP_ON_START = 'true'
function Wait-Ready([string]$url) {
  for ($i = 1; $i -le 90; $i++) {
    Start-Sleep -Seconds 2
    try { $h = Invoke-WebRequest -Uri $url -UseBasicParsing -TimeoutSec 3; if ([int]$h.StatusCode -lt 500) { return $true } } catch {}
  }
  return $false
}
$apiBase = 'http://localhost:5143/api/v1'
$env:ASPNETCORE_URLS = 'http://localhost:5143'; $env:Api__BaseUrl = 'http://localhost:5143/'
$pa = Start-Process dotnet -ArgumentList ('exec "{0}"' -f (Join-Path $root 'src\Odca.Api\bin\Debug\net10.0\Odca.Api.dll')) -WorkingDirectory (Join-Path $root 'src\Odca.Api') -WindowStyle Hidden -PassThru -RedirectStandardOutput (Join-Path $logDir 'd39-cap-api.log') -RedirectStandardError (Join-Path $logDir 'd39-cap-api-err.log')
$env:ASPNETCORE_URLS = 'http://localhost:5144'
$pw = Start-Process dotnet -ArgumentList ('exec "{0}"' -f (Join-Path $root 'src\Odca.Web\bin\Debug\net10.0\Odca.Web.dll')) -WorkingDirectory (Join-Path $root 'src\Odca.Web') -WindowStyle Hidden -PassThru -RedirectStandardOutput (Join-Path $logDir 'd39-cap-web.log') -RedirectStandardError (Join-Path $logDir 'd39-cap-web-err.log')
$stackPids = @($pa.Id, $pw.Id)
if (-not (Wait-Ready 'http://localhost:5143/health/ready')) { Write-Host 'API-NUNCA-PRONTA'; exit 2 }
if (-not (Wait-Ready 'http://localhost:5144/entrar')) { Write-Host 'WEB-NUNCA-PRONTA'; exit 2 }
LogEv 'STACK pronta'

# ---------- MFA deterministica: reset + reinscricao via API (chave conhecida) ----------
try {
  Db "DELETE FROM odca.sessions WHERE user_id='$uAdm';" | Out-Null
  Db "UPDATE odca.users SET mfa_confirmed_at=NULL, mfa_pending_since=NULL, mfa_last_accepted_time_step=NULL, mfa_secret_protected=NULL WHERE id='$uAdm';" | Out-Null
  $login = Invoke-RestMethod -Uri "$apiBase/auth/login" -Method Post -ContentType 'application/json' -Body (@{ login = $admEmail; password = $admPass } | ConvertTo-Json -Compress)
  $hdr = @{ Authorization = 'Bearer ' + $login.accessToken }
  $enr = Invoke-RestMethod -Uri "$apiBase/auth/mfa/enrollment" -Method Post -Headers $hdr
  $manualKey = [string]$enr.manualKey
  Start-Sleep -Milliseconds 300
  $conf = Invoke-RestMethod -Uri "$apiBase/auth/mfa/enrollment/confirm" -Method Post -Headers $hdr -ContentType 'application/json' -Body (@{ code = (Get-TotpCode $manualKey) } | ConvertTo-Json -Compress)
  if ([bool]$conf.mfaVerified -ne $true) { throw ('confirmacao MFA sem mfaVerified: ' + ($conf | ConvertTo-Json -Compress)) }
  $admTok = [string]$conf.accessToken
  LogEv ("mfa: reinscricao ok (manualKey len=" + $manualKey.Length + ")")
} catch { LogEv ('MFA-RESET-ERRO: ' + $_.Exception.Message); $admTok = '' }
if (-not $admTok) { Write-Host 'MFA-RESET-FALHOU'; foreach ($id in $stackPids) { Stop-Process -Id $id -Force -ErrorAction SilentlyContinue }; exit 2 }

# ---------- uma janela ativa de 60 min para exibicao (idempotente) ----------
try {
  $hdrAdm = @{ Authorization = 'Bearer ' + $admTok }
  $list = Invoke-RestMethod -Uri "$apiBase/platform/support-sessions" -Method Get -Headers $hdrAdm
  foreach ($s in @($list)) {
    if ($s.tenantId -eq $t -and $s.status -eq 'active' -and [bool]$s.valid) {
      $null = Invoke-RestMethod -Uri "$apiBase/platform/support-sessions/$($s.id)/end" -Method Post -Headers $hdrAdm
    }
  }
  $opened = Invoke-RestMethod -Uri "$apiBase/platform/support-sessions" -Method Post -Headers $hdrAdm -ContentType 'application/json' -Body (@{ tenantId = $t; reason = 'Demonstracao visual D-OC39 (janela de 60 min)'; durationMinutes = 60 } | ConvertTo-Json -Compress)
  $activeNow = [long](Db "SELECT count(*) FROM odca.support_sessions WHERE superadmin_user_id='$uAdm' AND tenant_id='$t' AND status='active';")
  LogEv ("janela-demo: id=" + $opened.id + " ativa=" + $activeNow)
  if ($activeNow -ne 1) { throw ('contagem de janela ativa inesperada: ' + $activeNow) }
} catch { LogEv ('JANELA-DEMO-ERRO: ' + $_.Exception.Message); $activeNow = 0 }
if ([long]$activeNow -ne 1) { Write-Host 'JANELA-DEMO-FALHOU'; foreach ($id in $stackPids) { Stop-Process -Id $id -Force -ErrorAction SilentlyContinue }; exit 2 }

# ---------- capturas (1 tela x pt-BR x 4 viewports + zoom 2x = 5 png) ----------
$cfgObj = [ordered]@{
  base = 'http://localhost:5144'
  outDir = $outDir
  edge = 'C:\Program Files (x86)\Microsoft\Edge\Application\msedge.exe'
  langs = @('pt-BR')
  viewports = @( @(360, 780), @(768, 1024), @(1280, 800), @(1440, 900) )
  zoomAt = @(1280, 800)
  creds = [ordered]@{
    operador = [ordered]@{ email = $admEmail; pass = $admPass; mfaKey = $manualKey }
  }
  screens = @(
    [ordered]@{ id = 'd39-suporte-sessoes'; session = 'operador'; path = '/administracao/suporte-sessoes' }
  )
}
$cfgFile = Join-Path $logDir 'd39-cfg.json'
Set-Content -Path $cfgFile -Value ($cfgObj | ConvertTo-Json -Depth 10) -Encoding utf8
LogEv ('CAPTURES: telas=1 lang=pt-BR viewports=4+zoom2x => 5 png em ' + $outDir)
$driver = Join-Path $root 'scripts\qa\cdp-capture-v2.mjs'
& node $driver $cfgFile 2>&1 | ForEach-Object { LogEv ('CDP| ' + $_); Write-Host $_ }
foreach ($id in $stackPids) { Stop-Process -Id $id -Force -ErrorAction SilentlyContinue }
exit $LASTEXITCODE
