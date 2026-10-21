# D-OC39 - prova ao vivo da Sprint 5 (seguracoa/operacional):
#   F-1 reautenticacao MFA recente (janela Identity:RecentAuthMinutes) na API (403 puro)
#       e na Web (redirect para o desafio MFA com returnUrl);
#   F-2 sessao temporaria de suporte de plataforma (abrir/estender/encerrar/revogar),
#       com ator real em auditoria e leitura de nomes via funcao governada (v060).
# Uso: powershell -NoProfile -ExecutionPolicy Bypass -File scripts/qa/validate-d39-s5-web.ps1
# Pre-condicoes: binarios Debug compilados; PostgreSQL local QA com odca_test_disposable
#   (a mesma base descartavel das provas anteriores; o bloco v060 e aplicado se ausente).
# A stack sobe com Identity__RecentAuthMinutes=1 em API e Web para tornar a janela
# "stale" deterministica (70 s) sem aguardar a janela padrao de 15 minutos.
[Console]::OutputEncoding = [System.Text.Encoding]::UTF8
$root   = 'C:\MNSOFT\odcasolutions'
$logDir = 'C:\Users\NCELL-DEV-020\AppData\Local\Temp\opencode'
$evDir  = Join-Path $logDir 'evidence'
$tmpDir = Join-Path $logDir 'd39-fx'
New-Item -ItemType Directory -Force -Path $evDir | Out-Null
New-Item -ItemType Directory -Force -Path $tmpDir | Out-Null
$evidence = Join-Path $evDir 'd39-proof.txt'
Set-Content -Path $evidence '' -Encoding utf8
$script:fails = 0
$script:lastPsqlErr = ''

function Quote-CmdArg([string]$a) { if ($a -match '[ \t"\\]') { return ('"' + ($a -replace '"','\"') + '"') } return $a }
function Watch-Curl([int]$ms, [string[]]$ca) {
  $sw0 = Get-Date
  $psi = New-Object System.Diagnostics.ProcessStartInfo
  $psi.FileName = 'curl.exe'
  $psi.Arguments = ((@($ca) | ForEach-Object { Quote-CmdArg ([string]$_) }) -join ' ')
  $psi.UseShellExecute = $false
  $psi.RedirectStandardOutput = $true
  $psi.RedirectStandardError = $true
  $psi.WorkingDirectory = $tmpDir
  $psi.CreateNoWindow = $true
  $p = [System.Diagnostics.Process]::Start($psi)
  $deadline = $sw0.AddMilliseconds($ms + 8000)
  while (-not $p.HasExited) {
    if ((Get-Date) -ge $deadline) { try { $p.Kill() } catch {}; break }
    Start-Sleep -Milliseconds 300
  }
  try { $so = $p.StandardOutput.ReadToEnd() } catch { $so = '' }
  try { $p.WaitForExit() } catch {}
  if ($so.Trim()) { return $so.Trim() } else { return '000' }
}
function LogEv([string]$m) { Add-Content -Path $evidence $m -Encoding utf8 }
function Check([string]$name, [bool]$ok, [string]$detail) {
  $line = ('[' + $(if ($ok) { 'PASS' } else { 'FAIL' }) + '] {0} {1}' -f $name, ($(if ($null -eq $detail) { '' } else { [string]$detail })))
  Write-Host $line; LogEv $line
  if (-not $ok) { $script:fails++ }
}
function Db([string]$q) {
  $env:PGPASSWORD = '123456'; $env:PGCONNECT_TIMEOUT = '10'
  $ef = Join-Path $tmpDir 'psql.err'
  $out = & 'C:\Program Files\PostgreSQL\18\bin\psql.exe' -h 127.0.0.1 -U postgres -d odca_test_disposable -t -A -c $q 2>$ef | Out-String
  $script:lastPsqlErr = ((Get-Content $ef -EA SilentlyContinue) -join ' ')
  return $out.Trim()
}
function Get-Af([string]$html) {
  if ($html -match 'name="__RequestVerificationToken"[^>]*value="([^"]+)"') { return $Matches[1] }
  if ($html -match 'value="([^"]+)"[^>]*name="__RequestVerificationToken"') { return $Matches[1] }
  return ''
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
$base = 'http://localhost:5144'
function Web-Post([string]$jar, [string]$url, [string]$data) {
  $f = Join-Path $tmpDir ('pp-' + [guid]::NewGuid().ToString('N').Substring(0, 10) + '.html')
  $hf = Join-Path $tmpDir ('pp-' + [guid]::NewGuid().ToString('N').Substring(0, 10) + '.hdr')
  $ca = @('-k','--connect-timeout','5','--max-time','60','-s','-b',$jar,'-c',$jar,'-o',$f,'-D',$hf,'-w','%{http_code}','-X','POST','-d',$data,$url)
  $code = Watch-Curl 120000 $ca
  $loc = ''
  if (Test-Path $hf) { $hm = [regex]::Match(([IO.File]::ReadAllText($hf)), '(?im)^Location:[ \t]*(\S+)'); if ($hm.Success) { $loc = $hm.Groups[1].Value } }
  $body = ''
  if (Test-Path $f) { try { $body = [Net.WebUtility]::HtmlDecode(([IO.File]::ReadAllText($f, [Text.Encoding]::UTF8))) } catch { $body = '' } }
  return @{ Code = ([string]$code).Trim(); Location = $loc; Body = $body }
}
function Web-Get([string]$jar, [string]$url) {
  $f = Join-Path $tmpDir ('pg-' + [guid]::NewGuid().ToString('N').Substring(0, 10) + '.html')
  $null = Watch-Curl 90000 @('-k','--connect-timeout','5','--max-time','45','-s','-L','-b',$jar,'-c',$jar,'-H','Accept-Language: pt-BR',($url),'-o',$f,'-w','%{http_code}')
  try { return [Net.WebUtility]::HtmlDecode(([IO.File]::ReadAllText($f, [Text.Encoding]::UTF8))) } catch { return '' }
}
# Resolve o desafio MFA da Web com o passo TOTP correto (anti-replay: cada nova tentativa
# espera um timestep novo). Opcionalmente preserva o returnUrl do redirecionamento original.
function Solve-MfaChallenge([string]$jar, [string]$mfaKey, [string]$returnUrl = '') {
  for ($att = 1; $att -le 8; $att++) {
    if ($att -gt 1) {
      $nowS = [long][DateTimeOffset]::UtcNow.ToUnixTimeSeconds()
      $delta = ([Math]::Floor($nowS / 30) + 1) * 30 + 2 - $nowS
      if ($delta -gt 0) { Start-Sleep -Seconds $delta }
    }
    $fm = Join-Path $tmpDir ('mfa-challenge-' + $att + '.html')
    Remove-Item $fm -EA SilentlyContinue
    $null = Watch-Curl 45000 @('-k','--connect-timeout','5','--max-time','20','-s','-b',$jar,'-c',$jar,"$base/mfa/desafio","-o",$fm)
    if (-not (Test-Path $fm)) { continue }
    $af2 = Get-Af ([IO.File]::ReadAllText($fm, [Text.Encoding]::UTF8))
    Start-Sleep -Milliseconds 300
    $form2 = 'Code=' + [Uri]::EscapeDataString((Get-TotpCode $mfaKey)) + '&ReturnUrl=' + [Uri]::EscapeDataString($returnUrl) + '&__RequestVerificationToken=' + [Uri]::EscapeDataString($af2)
    $out2 = Watch-Curl 45000 @('-k','--connect-timeout','5','--max-time','20','-s','-b',$jar,'-c',$jar,'-o',(Join-Path $tmpDir 'mfa-resp.html'),'-w','%{http_code}','-X','POST','-d',$form2,"$base/mfa/desafio")
    LogEv ('desafio-mfa tentativa=' + $att + ' -> ' + ([string]$out2).Trim())
    if ([string]$out2 -eq '302') { return $true }
  }
  return $false
}
function Extract-ReturnUrl([string]$location) {
  if ($location -match 'returnUrl=([^&]+)') { return [System.Uri]::UnescapeDataString($Matches[1]) }
  return ''
}
function Wait-Ready([string]$url) {
  for ($i = 1; $i -le 60; $i++) {
    Start-Sleep -Seconds 2
    try { $h = Invoke-WebRequest -Uri $url -UseBasicParsing -TimeoutSec 3; if ([int]$h.StatusCode -lt 500) { return $true } } catch {}
  }
  return $false
}
$apiBase = 'http://localhost:5143/api/v1'
function Api([string]$method, [string]$path, [string]$tok, $body = $null) {
  $suffix = [guid]::NewGuid().ToString('N').Substring(0, 10)
  $bf = Join-Path $tmpDir ("apib-$suffix.json")
  $ca = @('-k','--connect-timeout','5','--max-time','45','-s','-X',$method,'-o',$bf,'-w','%{http_code}')
  if ($tok) { $ca += @('-H',("Authorization: Bearer " + $tok)) }
  if ($null -ne $body) {
    $jf = Join-Path $tmpDir ("apij-$suffix.json")
    [IO.File]::WriteAllText($jf, ($body | ConvertTo-Json -Depth 8 -Compress), (New-Object Text.UTF8Encoding($false)))
    $ca += @('-H','Content-Type: application/json; charset=utf-8','--data-binary',('@' + $jf))
  }
  $ca += @("$apiBase$path")
  $codeStr = Watch-Curl 60000 $ca
  $code = 0
  [int]::TryParse($codeStr, [ref]$code) | Out-Null
  $raw = ''
  if (Test-Path $bf) { try { $raw = [IO.File]::ReadAllText($bf, [Text.Encoding]::UTF8) } catch { $raw = '' } }
  return @{ Status = $code; Raw = $raw }
}
function Stop-Stack {
  $rootEsc = [Regex]::Escape($root)
  Get-CimInstance Win32_Process | Where-Object { $_.CommandLine -and $_.CommandLine -match $rootEsc -and $_.CommandLine -match 'Odca\.(Api|Web|Worker)\.(dll|csproj)' } | ForEach-Object { Stop-Process -Id $_.ProcessId -Force -ErrorAction SilentlyContinue }
}

# ---------- constantes ----------
$t        = '20000000-0000-4000-8000-000000000001'
$uAdm     = 'c9000000-0000-4000-8000-000000000039'
$admEmail = 'suporte.prova@odca.local'
$admPass  = 'La@RsjC5p-hmq_JpReGW'   # mesma senha do operador (hash clonado no seed)
$mainCfg  = Join-Path $root 'src\Odca.Api\development-runtime.json'

# ---------- P0. binarios ----------
$binOk = (Test-Path (Join-Path $root 'src\Odca.Api\bin\Debug\net10.0\Odca.Api.dll')) -and `
         (Test-Path (Join-Path $root 'src\Odca.Web\bin\Debug\net10.0\Odca.Web.dll'))
Check 'P0 binarios Debug presentes (api/web)' $binOk ''
if (-not $binOk) { exit 2 }

# ---------- P0a. aplicar o bloco v060 na base descartavel se ainda ausente ----------
$hasV060 = [long](Db "SELECT count(*) FROM pg_proc p JOIN pg_namespace n ON n.oid=p.pronamespace WHERE n.nspname='odca' AND p.proname IN ('extend_support_session','support_session_tenant_name');")
if ($hasV060 -ne 2) {
  $env:PGPASSWORD = '123456'; $env:PGCONNECT_TIMEOUT = '10'
  $canonical = [IO.File]::ReadAllText((Join-Path $root 'database\odca.sql'))
  $start = $canonical.IndexOf('-- ODCA-MIGRATION 060', [StringComparison]::Ordinal)
  $endM = $canonical.IndexOf('-- ODCA-END 060', [StringComparison]::Ordinal)
  $end = -1
  if ($start -ge 0 -and $endM -gt $start) { $nl = $canonical.IndexOf("`n", $endM); if ($nl -ge 0) { $end = $nl + 1 } else { $end = $canonical.Length } }
  $vOut = ''
  if ($end -gt $start) {
    $blkFile = Join-Path $tmpDir 'v060-block.sql'
    [IO.File]::WriteAllText($blkFile, $canonical.Substring($start, $end - $start), [Text.Encoding]::UTF8)
    $vOut = & 'C:\Program Files\PostgreSQL\18\bin\psql.exe' -h 127.0.0.1 -U postgres -d odca_test_disposable -v ON_ERROR_STOP=1 -f $blkFile 2>&1 | Out-String
  } else { $vOut = 'bloco 060 nao encontrado no odca.sql' }
  $hasV060 = [long](Db "SELECT count(*) FROM pg_proc p JOIN pg_namespace n ON n.oid=p.pronamespace WHERE n.nspname='odca' AND p.proname IN ('extend_support_session','support_session_tenant_name');")
  Check 'P0a aplicacao do bloco v060 na base descartavel' ($hasV060 -eq 2) ((($vOut -split "`n" | Where-Object { $_ }) | Select-Object -Last 3) -join ' ')
  if ($hasV060 -ne 2) { exit 2 }
} else {
  LogEv 'P0a base ja com v060 (nenhuma acao)'
}
$vMax = Db "SELECT max(version)::int FROM odca.schema_migrations;"
# S22/D-OC56: o canonico vigente e v063 (v062 idioma outbox, v063 IA avancada); a assercao
# congelada em v061 (S11/D-OC45) abortava a prova no checkpoint P0a apos o bloco avancar.
Check 'P0a schema_migrations em v063' ($vMax -eq '63') ('max=' + $vMax)

# ---------- P0b. superadministrador sintetico da prova (hash clonado do operador) + estado limpo ----------
$seedSql = @"
DELETE FROM odca.sessions WHERE user_id='$uAdm';
DELETE FROM odca.mfa_recovery_codes WHERE user_id='$uAdm';
DELETE FROM odca.audit_events WHERE actor_user_id='$uAdm';
UPDATE odca.users SET mfa_confirmed_at=NULL, mfa_pending_since=NULL, mfa_last_accepted_time_step=NULL, mfa_secret_protected=NULL WHERE id='$uAdm';
INSERT INTO odca.users(id,email,email_normalized,login_normalized,display_name,password_hash,must_change_password,is_platform_administrator,email_verified_at)
SELECT '$uAdm','$admEmail','SUPORTE.PROVA@ODCA.LOCAL','SUPORTE.PROVA@ODCA.LOCAL','Suporte Prova D-OC39',
       (SELECT password_hash FROM odca.users WHERE email_normalized='OPERADOR@ODCA.LOCAL'), false, true, now()
ON CONFLICT(id) DO UPDATE SET display_name=EXCLUDED.display_name, must_change_password=false, is_platform_administrator=true;
DELETE FROM odca.support_session_events WHERE session_id IN (SELECT id FROM odca.support_sessions WHERE superadmin_user_id='$uAdm');
DELETE FROM odca.support_sessions WHERE superadmin_user_id='$uAdm';
"@
$seedFile = Join-Path $tmpDir 'seed-d39.sql'
[IO.File]::WriteAllText($seedFile, $seedSql, [Text.Encoding]::UTF8)
$env:PGPASSWORD = '123456'
$seedOut = & 'C:\Program Files\PostgreSQL\18\bin\psql.exe' -h 127.0.0.1 -U postgres -d odca_test_disposable -v ON_ERROR_STOP=1 -f $seedFile 2>&1 | Out-String
$isPlat = [long](Db "SELECT count(*) FROM odca.users WHERE id='$uAdm' AND is_platform_administrator;")
Check 'P0b superadministrador sintetico da prova pronto' ($isPlat -eq 1) (($seedOut -split "`n" | Where-Object { $_ }) | Select-Object -Last 2)
if ($isPlat -ne 1) { exit 2 }

# ---------- 0. stack (Identity__RecentAuthMinutes=1 para janela deterministica) ----------
& (Join-Path $root 'scripts\stop-local.ps1') 2>&1 | Out-Null
Start-Sleep -Seconds 3
Stop-Stack
Start-Sleep -Seconds 2
$env:ODCA_RUNTIME_CONFIG = $mainCfg
$env:DOTNET_ENVIRONMENT = 'Development'
$env:ASPNETCORE_ENVIRONMENT = 'Development'
$env:ODCA_SETUP_ON_START = 'true'
$env:Identity__RecentAuthMinutes = '1'
$env:ASPNETCORE_URLS = 'http://localhost:5143'
$env:Api__BaseUrl = 'http://localhost:5143/'
$null = Start-Process dotnet -ArgumentList ('exec "{0}"' -f (Join-Path $root 'src\Odca.Api\bin\Debug\net10.0\Odca.Api.dll')) -WorkingDirectory (Join-Path $root 'src\Odca.Api') -WindowStyle Hidden -RedirectStandardOutput (Join-Path $logDir 'd39-svc-api.log') -RedirectStandardError (Join-Path $logDir 'd39-svc-api-err.log')
$env:ASPNETCORE_URLS = 'http://localhost:5144'
$null = Start-Process dotnet -ArgumentList ('exec "{0}"' -f (Join-Path $root 'src\Odca.Web\bin\Debug\net10.0\Odca.Web.dll')) -WorkingDirectory (Join-Path $root 'src\Odca.Web') -WindowStyle Hidden -RedirectStandardOutput (Join-Path $logDir 'd39-svc-web.log') -RedirectStandardError (Join-Path $logDir 'd39-svc-web-err.log')
$env:ASPNETCORE_URLS = ''
$readyApi = Wait-Ready 'http://localhost:5143/health/ready'
$readyWeb = Wait-Ready 'http://localhost:5144/entrar'
Check 'S1 api pronta' $readyApi ''
Check 'S2 web pronta' $readyWeb ''
if (-not ($readyApi -and $readyWeb)) { Stop-Stack; exit 2 }
LogEv ('stack ok ' + (Get-Date -Format 'o') + ' (Identity__RecentAuthMinutes=1)')

# ---------- A. API: login + inscricao MFA + confirmacao (token t0 fresco) ----------
try {
  $login = Api 'POST' '/auth/login' $null @{ login = $admEmail; password = $admPass }
  if ($login.Status -ne 200) { throw ('login API falhou: ' + $login.Status + ' ' + ([string]$login.Raw).Substring(0, [Math]::Min(200, ([string]$login.Raw).Length))) }
  $lj = $login.Raw | ConvertFrom-Json
  $enrollNeeded = [bool]$lj.requiresMfaEnrollment
  Check 'A1 login API superadmin exige inscricao MFA' ($enrollNeeded -eq $true) ('requiresMfaEnrollment=' + $lj.requiresMfaEnrollment)
  $enr = Api 'POST' '/auth/mfa/enrollment' $lj.accessToken $null
  if ($enr.Status -ne 200) { throw ('mfa/enrollment falhou: ' + $enr.Status + ' ' + ([string]$enr.Raw).Substring(0, [Math]::Min(200, ([string]$enr.Raw).Length))) }
  $mfaKey = [string](($enr.Raw | ConvertFrom-Json).manualKey)
  Check 'A2 inscricao MFA devolveu chave manual' ($mfaKey.Length -ge 8) ('manualKey len=' + $mfaKey.Length)
  $conf = Api 'POST' '/auth/mfa/enrollment/confirm' $lj.accessToken @{ code = (Get-TotpCode $mfaKey) }
  if ($conf.Status -ne 200) { throw ('mfa/enrollment/confirm falhou: ' + $conf.Status + ' ' + ([string]$conf.Raw).Substring(0, [Math]::Min(200, ([string]$conf.Raw).Length))) }
  $cj = $conf.Raw | ConvertFrom-Json
  $admTok = [string]$cj.accessToken
  Check 'A3 confirmacao MFA emitiu token verificado' (($cj.mfaVerified -eq $true) -and ($admTok.Length -gt 20)) ('mfaVerified=' + $cj.mfaVerified + ' tok_len=' + $admTok.Length)
} catch {
  Check 'A login+inscricao MFA na API' $false $_.Exception.Message
  Stop-Stack; exit 2
}
$t0 = Get-Date

# ---------- W1. login web com desafio MFA ----------
$jarAdm = Join-Path $tmpDir 'adm-jar.txt'
Remove-Item $jarAdm -EA SilentlyContinue
try {
  $fh = Join-Path $tmpDir 'adm-login-page.html'
  for ($g = 1; $g -le 3; $g++) {
    Remove-Item $fh -EA SilentlyContinue
    $null = Watch-Curl 45000 @('-k','--connect-timeout','5','--max-time','20','-s','-c',$jarAdm,"$base/entrar","-o",$fh)
    if ((Test-Path $fh) -and (([IO.File]::ReadAllBytes($fh)).Length -gt 500)) { break }
    Start-Sleep -Seconds 15
  }
  $af = Get-Af ([IO.File]::ReadAllText($fh, [Text.Encoding]::UTF8))
  $form = 'Login=' + [Uri]::EscapeDataString($admEmail) + '&Password=' + [Uri]::EscapeDataString($admPass) + '&ReturnUrl=&__RequestVerificationToken=' + [Uri]::EscapeDataString($af)
  $hdrf = Join-Path $tmpDir 'adm-login-post.hdr'
  Remove-Item $hdrf -EA SilentlyContinue
  $out = Watch-Curl 45000 @('-k','--connect-timeout','5','--max-time','20','-s','-b',$jarAdm,'-c',$jarAdm,'-o','NUL','-D',$hdrf,'-w','%{http_code}','-X','POST','-d',$form,"$base/entrar")
  $code = ([string]$out).Trim(); $loc = ''
  if (Test-Path $hdrf) { $hm = [regex]::Match(([IO.File]::ReadAllText($hdrf)), '(?im)^Location:[ \t]*(\S+)'); if ($hm.Success) { $loc = $hm.Groups[1].Value } }
  Check 'W1a login web do superadmin redirecionou para o desafio MFA' (($code -eq '302') -and ($loc -like '*mfa/desafio*')) ($code + ' ' + $loc)
  $chOk = Solve-MfaChallenge $jarAdm $mfaKey ''
  $jarTxt = (Get-Content $jarAdm -Raw -EA SilentlyContinue)
  Check 'W1b desafio MFA resolvido e sessao web estabelecida' ($chOk -and ($jarTxt -like '*__Host-odca-session*')) ('challenge=' + $chOk + ' cookie=' + ($jarTxt -like '*__Host-odca-session*'))
} catch {
  Check 'W1 login web com desafio MFA' $false $_.Exception.Message
}
if ($script:fails -gt 0) { LogEv 'estado parcial impediu prosseguimento (verifique acima)'; Stop-Stack; exit 1 }

# ---------- W2. pagina de suporte carrega (pt-BR) com nav de plataforma ----------
$page = Web-Get $jarAdm "$base/administracao/suporte-sessoes"
Check 'W2 pagina de sessoes de suporte carrega em pt-BR' (($page -like '*Sessões de suporte*') -and ($page -like '*Abrir nova sessão*')) ('len=' + $page.Length)
Check 'W2b navegacao de plataforma linka a area de suporte' ($page -like '*suporte-sessoes*') ''

# ---------- W3/W4/W5. ciclo aberto -> estendido -> encerrado com ator real (fase fresca) ----------
$af3 = Get-Af $page
$r3 = Web-Post $jarAdm "$base/administracao/suporte-sessoes/abrir" ('TenantId=' + $t + '&Reason=' + [Uri]::EscapeDataString('Prova ao vivo D-OC39 (janela fresca)') + '&DurationMinutes=30&__RequestVerificationToken=' + [Uri]::EscapeDataString($af3))
if ($r3.Location -like '*mfa/desafio*') {
  # tolerancia: se a fase fresca excedeu a janela de 1 min, resolve o desafio e reenvia (mesmo fluxo real)
  LogEv 'W3 janela esgotada durante a prova; resolvendo desafio e reenviando'
  $null = Solve-MfaChallenge $jarAdm $mfaKey '/administracao/suporte-sessoes'
  $page = Web-Get $jarAdm "$base/administracao/suporte-sessoes"
  $af3 = Get-Af $page
  $r3 = Web-Post $jarAdm "$base/administracao/suporte-sessoes/abrir" ('TenantId=' + $t + '&Reason=' + [Uri]::EscapeDataString('Prova ao vivo D-OC39 (janela fresca)') + '&DurationMinutes=30&__RequestVerificationToken=' + [Uri]::EscapeDataString($af3))
}
$pg3 = Web-Get $jarAdm "$base/administracao/suporte-sessoes"
Check 'W3 abrir sessao de suporte (fresca) confirma na pagina' (($r3.Code -eq '302') -and ($r3.Location -like '*/administracao/suporte-sessoes*') -and ($pg3 -like '*Sessão aberta para a organização informada*')) ($r3.Code + ' ' + $r3.Location)
$cntActive = [long](Db "SELECT count(*) FROM odca.support_sessions WHERE superadmin_user_id='$uAdm' AND tenant_id='$t' AND status='active';")
$cntStarted = [long](Db "SELECT count(*) FROM odca.support_session_events e JOIN odca.support_sessions s ON s.id=e.session_id WHERE s.superadmin_user_id='$uAdm' AND e.event_type='started';")
$cntOpenAudit = [long](Db "SELECT count(*) FROM odca.audit_events WHERE actor_user_id='$uAdm' AND action='superadmin.support_session.opened';")
Check 'W3b sessao ativa + evento started + audit com ator real' (($cntActive -eq 1) -and ($cntStarted -eq 1) -and ($cntOpenAudit -eq 1)) ('active=' + $cntActive + ' started=' + $cntStarted + ' audit=' + $cntOpenAudit)

$sid = Db "SELECT id FROM odca.support_sessions WHERE superadmin_user_id='$uAdm' AND tenant_id='$t' AND status='active' ORDER BY started_at DESC LIMIT 1;"
$expBefore = Db "SELECT expires_at FROM odca.support_sessions WHERE id='$sid';"
$af4 = Get-Af (Web-Get $jarAdm "$base/administracao/suporte-sessoes")
$r4 = Web-Post $jarAdm "$base/administracao/suporte-sessoes/estender" ('SessionId=' + $sid + '&Minutes=15&__RequestVerificationToken=' + [Uri]::EscapeDataString($af4))
if ($r4.Location -like '*mfa/desafio*') {
  LogEv 'W4 janela esgotada; resolvendo desafio e reenviando'
  $null = Solve-MfaChallenge $jarAdm $mfaKey '/administracao/suporte-sessoes'
  $af4 = Get-Af (Web-Get $jarAdm "$base/administracao/suporte-sessoes")
  $r4 = Web-Post $jarAdm "$base/administracao/suporte-sessoes/estender" ('SessionId=' + $sid + '&Minutes=15&__RequestVerificationToken=' + [Uri]::EscapeDataString($af4))
}
$expAfter = Db "SELECT expires_at FROM odca.support_sessions WHERE id='$sid';"
$expDelta = -1
try { $expDelta = (([DateTimeOffset]::Parse($expAfter, $null, [System.Globalization.DateTimeStyles]::RoundtripKind)) - ([DateTimeOffset]::Parse($expBefore, $null, [System.Globalization.DateTimeStyles]::RoundtripKind))).TotalMinutes } catch {}
$cntExtEv = [long](Db "SELECT count(*) FROM odca.support_session_events WHERE session_id='$sid' AND event_type='extended';")
$cntExtAudit = [long](Db "SELECT count(*) FROM odca.audit_events WHERE actor_user_id='$uAdm' AND action='superadmin.support_session.extended' AND entity_id='$sid';")
$pg4 = Web-Get $jarAdm "$base/administracao/suporte-sessoes"
Check 'W4 estender +15 min (expiracao avanca, sem retroceder)' (($r4.Code -eq '302') -and ($pg4 -like '*Sessão estendida em 15 minuto(s)*') -and ($expDelta -ge 14.5)) ('code=' + $r4.Code + ' delta_min=' + $expDelta)
Check 'W4b evento extended + audit extended com ator real' (($cntExtEv -eq 1) -and ($cntExtAudit -eq 1)) ('ev=' + $cntExtEv + ' audit=' + $cntExtAudit)

$af5 = Get-Af (Web-Get $jarAdm "$base/administracao/suporte-sessoes")
$r5 = Web-Post $jarAdm "$base/administracao/suporte-sessoes/encerrar" ('SessionId=' + $sid + '&__RequestVerificationToken=' + [Uri]::EscapeDataString($af5))
if ($r5.Location -like '*mfa/desafio*') {
  LogEv 'W5 janela esgotada; resolvendo desafio e reenviando'
  $null = Solve-MfaChallenge $jarAdm $mfaKey '/administracao/suporte-sessoes'
  $af5 = Get-Af (Web-Get $jarAdm "$base/administracao/suporte-sessoes")
  $r5 = Web-Post $jarAdm "$base/administracao/suporte-sessoes/encerrar" ('SessionId=' + $sid + '&__RequestVerificationToken=' + [Uri]::EscapeDataString($af5))
}
$stEnd = Db "SELECT status FROM odca.support_sessions WHERE id='$sid';"
$cntEndEv = [long](Db "SELECT count(*) FROM odca.support_session_events WHERE session_id='$sid' AND event_type='ended';")
$cntEndAudit = [long](Db "SELECT count(*) FROM odca.audit_events WHERE actor_user_id='$uAdm' AND action='superadmin.support_session.ended' AND entity_id='$sid';")
Check 'W5 encerrar (terminal, ator real)' (($r5.Code -eq '302') -and ($stEnd -eq 'ended') -and ($cntEndEv -eq 1) -and ($cntEndAudit -eq 1)) ('code=' + $r5.Code + ' status=' + $stEnd + ' ev=' + $cntEndEv + ' audit=' + $cntEndAudit)

# ---------- F1a. stale na Web: 403 da API vira redirect para o desafio com returnUrl ----------
LogEv ('envelhecimento da janela (t0=' + $t0.ToString('o') + '); dormindo 70 s com janela de 1 min')
Start-Sleep -Seconds 70
$pageSt = Web-Get $jarAdm "$base/administracao/suporte-sessoes"
$afS = Get-Af $pageSt
$rS1 = Web-Post $jarAdm "$base/administracao/suporte-sessoes/abrir" ('TenantId=' + $t + '&Reason=' + [Uri]::EscapeDataString('Prova stale D-OC39 (janela vencida)') + '&DurationMinutes=20&__RequestVerificationToken=' + [Uri]::EscapeDataString($afS))
Check 'F1a escrita stale redireciona para o desafio MFA preservando o returnUrl' (($rS1.Code -eq '302') -and ($rS1.Location -like '*mfa/desafio*') -and ($rS1.Location -like '*abrir*')) ($rS1.Code + ' ' + $rS1.Location)
$retUrl = Extract-ReturnUrl $rS1.Location
$freshAgain = Solve-MfaChallenge $jarAdm $mfaKey $retUrl
Check 'F1b desafio resolvido devolve ao ponto de origem' $freshAgain ('returnUrl=' + $retUrl)
$afS2 = Get-Af (Web-Get $jarAdm "$base/administracao/suporte-sessoes")
$rS2 = Web-Post $jarAdm "$base/administracao/suporte-sessoes/abrir" ('TenantId=' + $t + '&Reason=' + [Uri]::EscapeDataString('Prova stale D-OC39 (apos reautenticacao)') + '&DurationMinutes=20&__RequestVerificationToken=' + [Uri]::EscapeDataString($afS2))
$cntActive2 = [long](Db "SELECT count(*) FROM odca.support_sessions WHERE superadmin_user_id='$uAdm' AND tenant_id='$t' AND status='active';")
$cntOpen2 = [long](Db "SELECT count(*) FROM odca.audit_events WHERE actor_user_id='$uAdm' AND action='superadmin.support_session.opened';")
Check 'F1c apos reautenticacao a mesma escrita tem sucesso' (($rS2.Code -eq '302') -and ($cntActive2 -eq 1) -and ($cntOpen2 -eq 2)) ('code=' + $rS2.Code + ' active=' + $cntActive2 + ' opened_audit=' + $cntOpen2)

# ---------- F2. stale na API: 403 puro (sem redirect, sem mensagem de negocio) ----------
$rApiStale = Api 'POST' '/platform/support-sessions' $admTok @{ tenantId = $t; reason = 'token stale em prova ao vivo D-OC39'; durationMinutes = 20 }
Check 'F2 escrita stale na API responde 403 puro' ($rApiStale.Status -eq 403) ('status=' + $rApiStale.Status + ' body=' + ([string]$rApiStale.Raw).Substring(0, [Math]::Min(120, ([string]$rApiStale.Raw).Length)))
$rApiList = Api 'GET' '/platform/support-sessions' $admTok $null
Check 'F2b leitura nao exige MFA recente (lista segue 200)' ($rApiList.Status -eq 200) ('status=' + $rApiList.Status)

# ---------- E. evidencia final de estado ----------
$stateDump = Db "SELECT status || '|' || ((status='active' AND expires_at>now())::text) FROM odca.support_sessions WHERE superadmin_user_id='$uAdm' ORDER BY started_at;"
LogEv ('estado final das sessoes da prova: ' + ($stateDump -replace "`n", '; '))
$evtDump = Db "SELECT e.event_type || '|' || u.display_name FROM odca.support_session_events e JOIN odca.support_sessions s ON s.id=e.session_id JOIN odca.users u ON u.id=e.actor_user_id WHERE s.superadmin_user_id='$uAdm' ORDER BY e.occurred_at;"
LogEv ('eventos: ' + ($evtDump -replace "`n", '; '))
$audDump = Db "SELECT action || '|' || coalesce(u.display_name,'-') FROM odca.audit_events a LEFT JOIN odca.users u ON u.id=a.actor_user_id WHERE a.actor_user_id='$uAdm' AND a.action LIKE 'superadmin.support_session.%' ORDER BY a.created_at;"
LogEv ('audits: ' + ($audDump -replace "`n", '; '))

# ---------- resumo ----------
Stop-Stack
LogEv ('FAILS-TOTAL=' + $script:fails)
Write-Host ('=== D39 WEB PROOF: fails=' + $script:fails + ' (evidence: ' + $evidence + ') ===')
if ($script:fails -gt 0) { exit 1 }
exit 0
