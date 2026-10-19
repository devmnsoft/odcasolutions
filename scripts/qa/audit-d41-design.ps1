# D-OC41 - Sprint 7 / F-1..F-4: auditoria de design estendida em 4 idiomas das superficies
# da jornada do cliente (documentos, obrigações, privacidade) + regressao das rotas do piloto.
# Mede overflow horizontal (360/768/1280/1440) e autenticacao por rota/idioma em navegador real
# (cdp-design-audit.mjs). Diferenca vs audit-d40: as superficies documentos/obrigacoes/privacidade
# passam a ser cobertas em pt-BR/en-US/es-ES/fr-FR (antes pt-first). Superficies de plataforma
# (atendimento ODCA, suporte, auditoria) permanecem em pt-BR — divida registrada para a S8.
# S8 (D-OC42): as superficies da plataforma ficaram em 4 idiomas no codigo; a auditoria segue
# medindo-as em pt-BR (regressao) e adiciona contratos/ficha/agenda do cliente em 4 idiomas.
# Sessoes: cliente.teste@odca.local (sem MFA) e o superadmin sintetico da prova (MFA reinscrita
# com chave conhecida para o login do driver). PNGs somente das falhas (triagem visual).
[Console]::OutputEncoding = [System.Text.Encoding]::UTF8
$root   = 'C:\MNSOFT\odcasolutions'
$logDir = 'C:\Users\NCELL-DEV-020\AppData\Local\Temp\opencode'
$evDir  = Join-Path $logDir 'evidence'
$outDir = Join-Path $evDir 'design-audit-d41'
New-Item -ItemType Directory -Force -Path $outDir | Out-Null
$evidence = Join-Path $evDir 'd41-design.txt'
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
function Wait-Ready([string]$url) {
  for ($i = 1; $i -le 90; $i++) {
    Start-Sleep -Seconds 2
    try { $h = Invoke-WebRequest -Uri $url -UseBasicParsing -TimeoutSec 3; if ([int]$h.StatusCode -lt 500) { return $true } } catch {}
  }
  return $false
}

$T        = '20000000-0000-4000-8000-000000000001'
$uAdm     = 'c9000000-0000-4000-8000-000000000039'
$admEmail = 'suporte.prova@odca.local'
$admPass  = 'La@RsjC5p-hmq_JpReGW'
$cliEmail = 'cliente.teste@odca.local'
$cliPass  = 'K8@wR3!nF6#zP2$m'
$apiBase  = 'http://localhost:5143/api/v1'

# ---------- estado minimo: superadmin da prova (idempotente) ----------
$cntU = [long](Db "SELECT count(*) FROM odca.users WHERE id='$uAdm';")
if ($cntU -ne 1) {
  $seedSql = @"
INSERT INTO odca.users(id,email,email_normalized,login_normalized,display_name,password_hash,must_change_password,is_platform_administrator,email_verified_at)
SELECT '$uAdm','$admEmail','SUPORTE.PROVA@ODCA.LOCAL','SUPORTE.PROVA@ODCA.LOCAL','Suporte Prova D-OC39',
       (SELECT password_hash FROM odca.users WHERE email_normalized='OPERADOR@ODCA.LOCAL'), false, true, now();
"@
  $seedFile = Join-Path $logDir 'd41-seed-audit.sql'
  [IO.File]::WriteAllText($seedFile, $seedSql, [Text.Encoding]::UTF8)
  $env:PGPASSWORD = '123456'
  & 'C:\Program Files\PostgreSQL\18\bin\psql.exe' -h 127.0.0.1 -U postgres -d odca_test_disposable -v ON_ERROR_STOP=1 -f $seedFile 2>&1 | ForEach-Object { LogEv ('SEED| ' + $_) }
  if ($LASTEXITCODE -ne 0) { Write-Host 'SEED-D41-FALHOU'; exit 2 }
}
$cntCli = [long](Db "SELECT count(*) FROM odca.users WHERE email_normalized='CLIENTE.TESTE@ODCA.LOCAL';")
if ($cntCli -ne 1) { Write-Host ('CLIENTE-AUSENTE cnt=' + $cntCli + ' (execute antes um estagio do ensaio que semeia os fixtures)'); exit 2 }
LogEv ('estado: cliente presente; superadmin-prova=' + $uAdm)

# ---------- stack (padrao; janela MFA 15 min) ----------
$rootEsc = [Regex]::Escape($root)
Get-CimInstance Win32_Process | Where-Object { $_.CommandLine -and $_.CommandLine -match $rootEsc -and $_.CommandLine -match 'Odca\.(Api|Web|Worker)\.(dll|csproj)' } | ForEach-Object { Stop-Process -Id $_.ProcessId -Force -ErrorAction SilentlyContinue }
Start-Sleep -Seconds 2
$env:ODCA_RUNTIME_CONFIG = Join-Path $root 'src\Odca.Api\development-runtime.json'
$env:DOTNET_ENVIRONMENT = 'Development'
$env:ASPNETCORE_ENVIRONMENT = 'Development'
$env:ODCA_SETUP_ON_START = 'true'
$env:ASPNETCORE_URLS = 'http://localhost:5143'; $env:Api__BaseUrl = 'http://localhost:5143/'
$pa = Start-Process dotnet -ArgumentList ('exec "{0}"' -f (Join-Path $root 'src\Odca.Api\bin\Debug\net10.0\Odca.Api.dll')) -WorkingDirectory (Join-Path $root 'src\Odca.Api') -WindowStyle Hidden -PassThru -RedirectStandardOutput (Join-Path $logDir 'd41-aud-api.log') -RedirectStandardError (Join-Path $logDir 'd41-aud-api-err.log')
$env:ASPNETCORE_URLS = 'http://localhost:5144'
$pw = Start-Process dotnet -ArgumentList ('exec "{0}"' -f (Join-Path $root 'src\Odca.Web\bin\Debug\net10.0\Odca.Web.dll')) -WorkingDirectory (Join-Path $root 'src\Odca.Web') -WindowStyle Hidden -PassThru -RedirectStandardOutput (Join-Path $logDir 'd41-aud-web.log') -RedirectStandardError (Join-Path $logDir 'd41-aud-web-err.log')
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
  $confirmed = $false; $conf = $null
  # Anti-replay: so posta no meio do timestep (pelo menos 5s restantes no passo de 30s).
  for ($att = 1; $att -le 3 -and -not $confirmed; $att++) {
    while ([DateTimeOffset]::UtcNow.ToUnixTimeSeconds() % 30 -gt 25) { Start-Sleep -Milliseconds 500 }
    try {
      $conf = Invoke-RestMethod -Uri "$apiBase/auth/mfa/enrollment/confirm" -Method Post -Headers $hdr -ContentType 'application/json' -Body (@{ code = (Get-TotpCode $manualKey) } | ConvertTo-Json -Compress)
      if ([bool]$conf.mfaVerified) { $confirmed = $true }
    } catch { }
    if (-not $confirmed) { Start-Sleep -Seconds ($att * 31) }
  }
  if (-not $confirmed) { throw ('confirmacao MFA sem mfaVerified apos 3 tentativas') }
  LogEv ('mfa: reinscricao ok (manualKey len=' + $manualKey.Length + ')')
} catch { LogEv ('MFA-RESET-ERRO: ' + $_.Exception.Message); $manualKey = '' }
if (-not $manualKey) { Write-Host 'MFA-RESET-FALHOU'; foreach ($id in $stackPids) { Stop-Process -Id $id -Force -ErrorAction SilentlyContinue }; exit 2 }

# Guarda: garantir que o tenant chegou intacto ate o cfg (mesmo padrao D-OC40: nunca nomear
# variaveis de loop $t/$T — PowerShell nao distingue maiuscula em nomes de variavel).
if ([string]$T -ne '20000000-0000-4000-8000-000000000001') { Write-Host ('TENANT-CORROMPIDO T=' + $T); foreach ($id in $stackPids) { Stop-Process -Id $id -Force -ErrorAction SilentlyContinue }; exit 2 }

# ---------- cfg do driver de auditoria ----------
# S7: documentos/obrigacoes/privacidade em 4 idiomas (nova cobr.) + regressao das rotas do piloto.
# S8 (D-OC42): + contratos (lista), ficha do contrato (contrato fixo da prova ...201) e agenda
# (cliente, 4 idiomas) + notificacoes da plataforma (superadmin, pt-BR).
# S13 (D-OC47): + renovacoes, solicitacoes (revisoes) e acoes do cliente em 4 idiomas.
# S14 (D-OC48): + central de organizacao (picker, equipe, editar, visao geral do cliente,
# primeiros passos) e funil anonimo (aceitar convite, contratar, confirmar e-mail) em 4 idiomas.
# 95 par (rota x idioma) x 4 viewports = 380 medidas previstas.
$cfgObj = [ordered]@{
  base = 'http://localhost:5144'
  outDir = $outDir
  edge = 'C:\Program Files (x86)\Microsoft\Edge\Application\msedge.exe'
  viewports = @( @(360, 780), @(768, 1024), @(1280, 800), @(1440, 900) )
  creds = [ordered]@{
    cliente    = [ordered]@{ email = $cliEmail; pass = $cliPass }
    superadmin = [ordered]@{ email = $admEmail; pass = $admPass; mfaKey = $manualKey }
  }
  routes = @(
    [ordered]@{ id = 'home';               path = '/';                                  session = 'cliente';    langs = @('pt-BR', 'en-US', 'es-ES', 'fr-FR') },
    [ordered]@{ id = 'documentos';         path = '/organizacoes/' + $T + '/documentos';            session = 'cliente';    langs = @('pt-BR', 'en-US', 'es-ES', 'fr-FR') },
    [ordered]@{ id = 'documentos-novo';    path = '/organizacoes/' + $T + '/documentos/novo';       session = 'cliente';    langs = @('pt-BR', 'en-US', 'es-ES', 'fr-FR') },
    [ordered]@{ id = 'estudio';            path = '/organizacoes/' + $T + '/estudio';    session = 'cliente';    langs = @('pt-BR') },
    [ordered]@{ id = 'pacientes';          path = '/organizacoes/' + $T + '/pacientes';  session = 'cliente';    langs = @('pt-BR', 'en-US') },
    [ordered]@{ id = 'central-solicitacoes'; path = '/organizacoes/' + $T + '/central-solicitacoes'; session = 'cliente'; langs = @('pt-BR', 'en-US', 'es-ES', 'fr-FR') },
    [ordered]@{ id = 'obrigacoes';         path = '/organizacoes/' + $T + '/obrigacoes';            session = 'cliente';    langs = @('pt-BR', 'en-US', 'es-ES', 'fr-FR') },
    [ordered]@{ id = 'obrigacoes-nova';    path = '/organizacoes/' + $T + '/obrigacoes/nova';       session = 'cliente';    langs = @('pt-BR', 'en-US', 'es-ES', 'fr-FR') },
    [ordered]@{ id = 'notificacoes';       path = '/minha-conta/notificacoes';           session = 'cliente';    langs = @('pt-BR', 'en-US', 'es-ES', 'fr-FR') },
    [ordered]@{ id = 'privacidade';        path = '/privacidade';                        session = 'anon';       langs = @('pt-BR', 'en-US', 'es-ES', 'fr-FR') },
    [ordered]@{ id = 'atendimento-odca';   path = '/administracao/atendimento-odca';     session = 'superadmin'; langs = @('pt-BR') },
    [ordered]@{ id = 'suporte-sessoes';    path = '/administracao/suporte-sessoes';      session = 'superadmin'; langs = @('pt-BR') },
    [ordered]@{ id = 'plataforma-notificacoes'; path = '/administracao/notificacoes';    session = 'superadmin'; langs = @('pt-BR') },
    [ordered]@{ id = 'auditoria';          path = '/administracao/auditoria';            session = 'superadmin'; langs = @('pt-BR') },
    [ordered]@{ id = 'contratos';          path = '/organizacoes/' + $T + '/contratos';  session = 'cliente';    langs = @('pt-BR', 'en-US', 'es-ES', 'fr-FR') },
    [ordered]@{ id = 'ficha-contrato';     path = '/organizacoes/' + $T + '/contratos/76000000-0000-0000-0000-000000000201'; session = 'cliente'; langs = @('pt-BR', 'en-US', 'es-ES', 'fr-FR') },
    [ordered]@{ id = 'agenda';             path = '/organizacoes/' + $T + '/agenda';     session = 'cliente';    langs = @('pt-BR', 'en-US', 'es-ES', 'fr-FR') },
    [ordered]@{ id = 'renovacoes';         path = '/organizacoes/' + $T + '/renovacoes'; session = 'cliente';    langs = @('pt-BR', 'en-US', 'es-ES', 'fr-FR') },
    [ordered]@{ id = 'solicitacoes';       path = '/organizacoes/' + $T + '/solicitacoes'; session = 'cliente';  langs = @('pt-BR', 'en-US', 'es-ES', 'fr-FR') },
    [ordered]@{ id = 'acoes';              path = '/organizacoes/' + $T + '/acoes';      session = 'cliente';    langs = @('pt-BR', 'en-US', 'es-ES', 'fr-FR') },
    [ordered]@{ id = 'org-picker';         path = '/organizacoes';                       session = 'cliente';    langs = @('pt-BR', 'en-US', 'es-ES', 'fr-FR') },
    [ordered]@{ id = 'equipe';             path = '/organizacoes/' + $T + '/equipe';     session = 'cliente';    langs = @('pt-BR', 'en-US', 'es-ES', 'fr-FR') },
    [ordered]@{ id = 'editar-org';         path = '/organizacoes/' + $T + '/editar';     session = 'cliente';    langs = @('pt-BR', 'en-US', 'es-ES', 'fr-FR') },
    [ordered]@{ id = 'visao-geral-cliente'; path = '/cliente';                          session = 'cliente';    langs = @('pt-BR', 'en-US', 'es-ES', 'fr-FR') },
    [ordered]@{ id = 'primeiros-passos';   path = '/organizacoes/' + $T + '/primeiros-passos'; session = 'cliente'; langs = @('pt-BR', 'en-US', 'es-ES', 'fr-FR') },
    [ordered]@{ id = 'aceitar-convite';    path = '/convites/aceitar?invitationId=11111111-2222-3333-4444-555555555555'; session = 'anon'; langs = @('pt-BR', 'en-US', 'es-ES', 'fr-FR') },
    [ordered]@{ id = 'contratar';          path = '/contratar';                          session = 'anon';       langs = @('pt-BR', 'en-US', 'es-ES', 'fr-FR') },
    [ordered]@{ id = 'confirmar-email';    path = '/confirmar-email?registrationId=11111111-2222-3333-4444-555555555555'; session = 'anon'; langs = @('pt-BR', 'en-US', 'es-ES', 'fr-FR') }
  )
}
$cfgFile = Join-Path $logDir 'd41-audit-cfg.json'
Set-Content -Path $cfgFile -Value ($cfgObj | ConvertTo-Json -Depth 10) -Encoding utf8
LogEv 'AUDIT: 28 rotas x idiomas x 4 viewports (95 pares; 380 medidas previstas); PNG apenas de falhas em ' + $outDir

# ---------- execucao ----------
$driver = Join-Path $root 'scripts\qa\cdp-design-audit.mjs'
& node $driver $cfgFile 2>&1 | ForEach-Object { LogEv ($_); Write-Host $_ }
$auditExit = $LASTEXITCODE
foreach ($id in $stackPids) { Stop-Process -Id $id -Force -ErrorAction SilentlyContinue }
LogEv ('AUDIT-EXIT=' + $auditExit)
if ($auditExit -ne 0) { exit 1 }
exit 0
