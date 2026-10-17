# D-OC36 - prova ao vivo da i18n 4 idiomas da area de paciente (telas + validacao + cadastro rapido).
# Base descartavel odca_test_disposable; api :5143; web :5144; worker ausente (nao exigido por essas telas).
# Sessao: cliente.teste@odca.local (papel tenant-client, permissoes patients.read+manage, sem MFA).
[Console]::OutputEncoding = [System.Text.Encoding]::UTF8
$ErrorActionPreference = 'Continue'
$logDir = 'C:\Users\NCELL-DEV-020\AppData\Local\Temp\opencode'
$root   = 'C:\MNSOFT\odcasolutions'
$evDir  = Join-Path $logDir 'evidence'
$tmpDir = Join-Path $logDir 'd35-fx'
New-Item -ItemType Directory -Force -Path $evDir | Out-Null
New-Item -ItemType Directory -Force -Path $tmpDir | Out-Null
$evidence = Join-Path $evDir 'd35-proof.txt'
Set-Content -Path $evidence '' -Encoding utf8
$traceFile = Join-Path $logDir 'd35-trace.log'
Set-Content -Path $traceFile '' -Encoding utf8
$script:fails = 0
$script:lastPsqlErr = ''

function TMark([string]$tag) { $l = ((Get-Date -Format 'yyyy-MM-dd HH:mm:ss.fff') + ' ' + $tag); Add-Content -Path $traceFile $l -Encoding utf8; Write-Host $l }
function Quote-CmdArg([string]$a) { if ($a -match '[ \t"\\]') { return ('"' + ($a -replace '"','\"') + '"') } return $a }
function Watch-Curl([int]$ms, [string[]]$ca) {
  $sw0 = Get-Date
  TMark ('WC-inicio n_args=' + @($ca).Count)
  $psi = New-Object System.Diagnostics.ProcessStartInfo
  $psi.FileName = 'curl.exe'
  $psi.Arguments = ((@($ca) | ForEach-Object { Quote-CmdArg ([string]$_) }) -join ' ')
  $psi.UseShellExecute = $false
  $psi.RedirectStandardOutput = $true
  $psi.RedirectStandardError = $true
  $psi.WorkingDirectory = $tmpDir
  $psi.CreateNoWindow = $true
  $p = $null
  try { $p = [System.Diagnostics.Process]::Start($psi) } catch { TMark ('WC-START-FALHOU ' + $_.Exception.Message); return '000' }
  $deadline = $sw0.AddMilliseconds($ms + 8000)
  while (-not $p.HasExited) {
    if ((Get-Date) -ge $deadline) {
      try { $p.Kill() } catch {}
      TMark ('WC-KILL apos_ms=' + ([int]((Get-Date) - $sw0).TotalMilliseconds))
      break
    }
    Start-Sleep -Milliseconds 300
  }
  try { $so = $p.StandardOutput.ReadToEnd() } catch { $so = '' }
  try { $p.WaitForExit() } catch {}
  $ec = '?'; try { $ec = $p.ExitCode } catch {}
  TMark ('WC-done exit=' + $ec + ' apos_ms=' + ([int]((Get-Date) - $sw0).TotalMilliseconds))
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
$base = 'http://localhost:5144'
function Web-Login([string]$jar, [string]$email, [string]$pass) {
  TMark ('WL-inicio ' + $email)
  $fh = Join-Path $tmpDir 'login-page.html'
  for ($g = 1; $g -le 3; $g++) {
    Remove-Item $fh -EA SilentlyContinue
    $null = Watch-Curl 45000 @('-k','--connect-timeout','5','--max-time','20','-s','-c',$jar,"$base/entrar","-o",$fh)
    $sz = 0; if (Test-Path $fh) { $sz = ([IO.File]::ReadAllBytes($fh)).Length }
    if ($sz -gt 500) { TMark ('WL-GET-entrar-ok bytes=' + $sz); break }
    Start-Sleep -Seconds 15
  }
  $af = Get-Af ([IO.File]::ReadAllText($fh, [Text.Encoding]::UTF8))
  $form = 'Login=' + [Uri]::EscapeDataString($email) + '&Password=' + [Uri]::EscapeDataString($pass) + '&ReturnUrl=&__RequestVerificationToken=' + [Uri]::EscapeDataString($af)
  $hdrf = Join-Path $tmpDir 'login-post.hdr'
  $out = '000'
  for ($po = 1; $po -le 3; $po++) {
    Remove-Item $hdrf -EA SilentlyContinue
    $out = Watch-Curl 45000 @('-k','--connect-timeout','5','--max-time','20','-s','-b',$jar,'-c',$jar,'-o','NUL','-D',$hdrf,'-w','%{http_code}','-X','POST','-d',$form,"$base/entrar")
    TMark ('WL-POST-entrar tent=' + $po + ' -> ' + ([string]$out).Trim())
    if ([string]$out -ne '000') { break }
    Start-Sleep -Seconds 15
  }
  $code = ([string]$out).Trim(); $loc = ''
  if (Test-Path $hdrf) { $hm = [regex]::Match(([IO.File]::ReadAllText($hdrf)), '(?im)^Location:[ \t]*(\S+)'); if ($hm.Success) { $loc = $hm.Groups[1].Value } }
  if (($code -eq '302') -and ($loc -like '*mfa*')) { throw ("MFA inesperado para " + $email + " loc=" + $loc) }
  if ($code -ne '302') { throw ("POST /entrar -> " + $code + " loc=" + $loc) }
  TMark ('WL-fim ' + $code)
}
function Web-Get([string]$jar, [string]$url, [string]$lang = '') {
  $f = Join-Path $tmpDir ('pg-' + [guid]::NewGuid().ToString('N').Substring(0, 10) + '.html')
  $ca = @('-k','--connect-timeout','5','--max-time','45','-s','-L','-b',$jar,'-c',$jar)
  if ($lang) { $ca += @('-b',('odca_lang=' + $lang)) }
  $ca += @($url,'-o',$f,'-w','%{http_code}')
  $code = Watch-Curl 90000 $ca
  $sz = 0; if (Test-Path $f) { $sz = ([IO.File]::ReadAllBytes($f)).Length }
  if ($sz -eq 0) { Start-Sleep -Seconds 5; $code = Watch-Curl 90000 $ca; $sz = 0; if (Test-Path $f) { $sz = ([IO.File]::ReadAllBytes($f)).Length } }
  TMark ('GET-end code=' + $code + ' bytes=' + $sz + ' url=' + $url)
  try { return [Net.WebUtility]::HtmlDecode(([IO.File]::ReadAllText($f, [Text.Encoding]::UTF8))) } catch { return '' }
}
function Wait-Ready([string]$url) {
  for ($i = 1; $i -le 45; $i++) {
    Start-Sleep -Seconds 2
    try { $h = Invoke-WebRequest -Uri $url -UseBasicParsing -TimeoutSec 3; if ([int]$h.StatusCode -lt 500) { return $true } } catch {}
  }
  return $false
}
function Ensure-Api([string]$tag) {
  if (Wait-Ready 'http://localhost:5143/health/ready') { return }
  LogEv ('API fora do ar em ' + $tag + ' — reiniciando processo')
  Get-CimInstance Win32_Process | Where-Object { $_.CommandLine -and $_.CommandLine -match $rootEsc -and $_.CommandLine -match 'Odca\.Api\.dll' } | ForEach-Object { Stop-Process -Id $_.ProcessId -Force -ErrorAction SilentlyContinue }
  Start-Sleep -Seconds 2
  $prevUrls = $env:ASPNETCORE_URLS
  $env:ASPNETCORE_URLS = 'http://localhost:5143'
  $null = Start-Process dotnet -ArgumentList ('exec "{0}"' -f (Join-Path $root 'src\Odca.Api\bin\Debug\net10.0\Odca.Api.dll')) -WorkingDirectory (Join-Path $root 'src\Odca.Api') -WindowStyle Hidden -RedirectStandardOutput (Join-Path $logDir 'd35-svc-api.log') -RedirectStandardError (Join-Path $logDir 'd35-svc-api-err.log')
  $env:ASPNETCORE_URLS = $prevUrls
  if (-not (Wait-Ready 'http://localhost:5143/health/ready')) { LogEv ('API nao voltou apos restart em ' + $tag) }
}
function Ensure-Web([string]$tag) {
  if (Wait-Ready 'http://localhost:5144/entrar') { return }
  LogEv ('WEB fora do ar em ' + $tag + ' — reiniciando processo')
  Get-CimInstance Win32_Process | Where-Object { $_.CommandLine -and $_.CommandLine -match $rootEsc -and $_.CommandLine -match 'Odca\.Web\.dll' } | ForEach-Object { Stop-Process -Id $_.ProcessId -Force -ErrorAction SilentlyContinue }
  Start-Sleep -Seconds 2
  $prevUrls = $env:ASPNETCORE_URLS
  $env:ASPNETCORE_URLS = 'http://localhost:5144'
  $null = Start-Process dotnet -ArgumentList ('exec "{0}"' -f (Join-Path $root 'src\Odca.Web\bin\Debug\net10.0\Odca.Web.dll')) -WorkingDirectory (Join-Path $root 'src\Odca.Web') -WindowStyle Hidden -RedirectStandardOutput (Join-Path $logDir 'd35-svc-web.log') -RedirectStandardError (Join-Path $logDir 'd35-svc-web-err.log')
  $env:ASPNETCORE_URLS = $prevUrls
  if (-not (Wait-Ready 'http://localhost:5144/entrar')) { LogEv ('WEB nao voltou apos restart em ' + $tag) }
}
function Web-Post([string]$jar, [string]$url, [hashtable]$fields, [string]$af, [string]$lang = '') {
  $sb = New-Object Text.StringBuilder
  foreach ($k in $fields.Keys) {
    if ($sb.Length -gt 0) { [void]$sb.Append('&') }
    [void]$sb.Append(([Uri]::EscapeDataString($k) + '=' + [Uri]::EscapeDataString([string]$fields[$k])))
  }
  $f = Join-Path $tmpDir ('po-' + [guid]::NewGuid().ToString('N').Substring(0, 10) + '.html')
  $hf = Join-Path $tmpDir ('ph-' + [guid]::NewGuid().ToString('N').Substring(0, 10) + '.hdr')
  $ca = @('-k','--connect-timeout','5','--max-time','60','-s','-b',$jar,'-c',$jar,'-X','POST','-H','Content-Type: application/x-www-form-urlencoded','-H',('RequestVerificationToken: ' + $af),'-d',$sb.ToString(),'-o',$f,'-D',$hf,'-w','%{http_code}')
  if ($lang) { $ca += @('-b',('odca_lang=' + $lang)) }
  $ca += @($url)
  $out = Watch-Curl 120000 $ca
  if ([string]$out -eq '000') { Start-Sleep -Seconds 10; $out = Watch-Curl 120000 $ca }
  try { $body = [IO.File]::ReadAllText($f, [Text.Encoding]::UTF8) } catch { $body = '' }
  $loc = ''
  if (Test-Path $hf) { $hm = [regex]::Match(([IO.File]::ReadAllText($hf)), '(?im)^Location:[ \t]*(\S+)'); if ($hm.Success) { $loc = $hm.Groups[1].Value } }
  return [pscustomobject]@{ Code = ([string]$out).Trim(); Location = $loc; Body = $body }
}

# ---------- constantes ----------
$t           = '20000000-0000-4000-8000-000000000001'
$patientA    = '75c87013-4595-41dd-93d9-ac2e9c1f8fc4'
$uCli        = '10000000-0000-4000-8000-000000000003'
$clientEmail = 'cliente.teste@odca.local'
$clientPass  = 'K8@wR3!nF6#zP2$m'
$tplArchive  = 'd30353b9-797c-451d-9dca-6a195826fb9e'
$tplProfile  = 'c711edc7-a0c9-471e-98b6-5a6cfe8cb0f1'
$mainCfg  = Join-Path $root 'src\Odca.Api\development-runtime.json'
$rootEsc  = [Regex]::Escape($root)

# ---------- P0. binarios ----------
$binOk = (Test-Path (Join-Path $root 'src\Odca.Api\bin\Debug\net10.0\Odca.Api.dll')) -and `
         (Test-Path (Join-Path $root 'src\Odca.Web\bin\Debug\net10.0\Odca.Web.dll'))
Check 'P0 binarios Debug presentes (api/web)' $binOk ''

# ---------- P0b. purga deterministica (idem d34; sem plano/prefs/pickup: nao exigidos por S2) ----------
$purgeTables = @(
  'extraction_suggestions','extraction_results','extraction_reviews','extraction_jobs',
  'contract_review_notifications','contract_review_comments','contract_review_events','contract_review_steps','contract_review_requests',
  'signature_preparation_operations','signature_preparation_events','signature_participants','signature_preparations',
  'user_notifications','obligation_evidence','obligation_events','obligation_reminders','contract_obligations','obligation_series',
  'contract_renewal_cycles','contract_change_applications','contract_change_events','contract_change_requests',
  'studio_comment_events','studio_comments','draft_save_receipts','draft_extraction_reviews','contract_copy_issuances',
  'generated_contract_versions',
  'storage_reservations','resource_movements','additional_storage_requests',
  'document_versions','contract_documents',
  'contract_drafts','contract_events','contract_import_events','contract_imports',
  'support_sessions',
  'solicitation_messages','solicitation_events','solicitation_pauses','solicitations',
  'notification_outbox',
  'privacy_legal_holds','privacy_request_actions','privacy_requests',
  'contracts','patients','patient_representatives'
)
$purgeErr = ''
$null = Db ("DELETE FROM odca.support_session_events WHERE session_id IN (SELECT id FROM odca.support_sessions WHERE tenant_id='$t');")
if ($script:lastPsqlErr) { $purgeErr += 'support_session_events: ' + $script:lastPsqlErr + ' | ' }
$null = Db ("DELETE FROM odca.privacy_request_events WHERE privacy_request_id IN (SELECT id FROM odca.privacy_requests WHERE tenant_id='$t');")
if ($script:lastPsqlErr) { $purgeErr += 'privacy_request_events: ' + $script:lastPsqlErr + ' | ' }
foreach ($tbl in $purgeTables) {
  $null = Db ("DELETE FROM odca." + $tbl + " WHERE tenant_id='$t';")
  if ($script:lastPsqlErr) { $purgeErr += $tbl + ': ' + $script:lastPsqlErr + ' | ' }
}
$null = Db ("UPDATE odca.contract_templates SET status='published', archive_reason=NULL WHERE id='$tplArchive';")
if ($script:lastPsqlErr) { $purgeErr += 'tpl-archive: ' + $script:lastPsqlErr + ' | ' }
$null = Db ("UPDATE odca.contract_templates SET required_activity_profile=NULL WHERE id='$tplProfile';")
if ($script:lastPsqlErr) { $purgeErr += 'tpl-profile: ' + $script:lastPsqlErr + ' | ' }
$null = Db ("INSERT INTO odca.patients (id, tenant_id, full_name, email, identifier_type, identifier_value, identifier_normalized, row_version, created_by, updated_by) VALUES ('$patientA', '$t', 'Ana Paula Ferreira da Costa', 'ana.paula.prova@odca.local', 'cpf', '11144777735', '11144777735', 1, '$uCli', '$uCli') ON CONFLICT (id) DO NOTHING;")
if ($script:lastPsqlErr) { $purgeErr += 'patient-fixture: ' + $script:lastPsqlErr + ' | ' }
$patientFixed = [long](Db "SELECT count(*) FROM odca.patients WHERE id='$patientA' AND tenant_id='$t' AND inactive_at IS NULL;")
if ($patientFixed -ne 1) { $purgeErr += "patient-fixture-count=$patientFixed | " }
Check 'P0b purga da base descartavel (estado idem ao setup + fixture de paciente)' ($purgeErr -eq '') ($purgeErr.Trim())
if ($purgeErr -ne '') { exit 2 }

# ---------- 0. stack (api+web; worker nao exigido pelas telas da S2) ----------
Set-Location $root
& (Join-Path $root 'scripts\stop-local.ps1') 2>&1 | Out-Null
Start-Sleep -Seconds 3
Get-CimInstance Win32_Process | Where-Object { $_.CommandLine -and $_.CommandLine -match $rootEsc -and $_.CommandLine -match 'Odca\.(Api|Web|Worker)\.(dll|csproj)' } | ForEach-Object { Stop-Process -Id $_.ProcessId -Force -ErrorAction SilentlyContinue }
Start-Sleep -Seconds 2
$env:ODCA_RUNTIME_CONFIG = $mainCfg
$env:DOTNET_ENVIRONMENT = 'Development'
$env:ASPNETCORE_ENVIRONMENT = 'Development'
$env:ODCA_SETUP_ON_START = 'true'
$env:ASPNETCORE_URLS = 'http://localhost:5143'
$env:Api__BaseUrl = 'http://localhost:5143/'
$null = Start-Process dotnet -ArgumentList ('exec "{0}"' -f (Join-Path $root 'src\Odca.Api\bin\Debug\net10.0\Odca.Api.dll')) -WorkingDirectory (Join-Path $root 'src\Odca.Api') -WindowStyle Hidden -RedirectStandardOutput (Join-Path $logDir 'd35-svc-api.log') -RedirectStandardError (Join-Path $logDir 'd35-svc-api-err.log')
$env:ASPNETCORE_URLS = 'http://localhost:5144'
$null = Start-Process dotnet -ArgumentList ('exec "{0}"' -f (Join-Path $root 'src\Odca.Web\bin\Debug\net10.0\Odca.Web.dll')) -WorkingDirectory (Join-Path $root 'src\Odca.Web') -WindowStyle Hidden -RedirectStandardOutput (Join-Path $logDir 'd35-svc-web.log') -RedirectStandardError (Join-Path $logDir 'd35-svc-web-err.log')
$env:ASPNETCORE_URLS = ''
$readyApi = Wait-Ready 'http://localhost:5143/health/ready'
$readyWeb = Wait-Ready 'http://localhost:5144/entrar'
Check 'S1 api pronta' $readyApi ''
Check 'S2 web pronta' $readyWeb ''
if (-not ($readyApi -and $readyWeb)) { exit 2 }
LogEv ('Fase stack ok ' + (Get-Date -Format 'o'))

# ---------- W1. login do cliente (sem MFA) ----------
$jarCli = Join-Path $tmpDir 'cli-jar.txt'
Remove-Item $jarCli -EA SilentlyContinue
try { Web-Login $jarCli $clientEmail $clientPass; Check 'W1 login web do cliente (302, sem MFA)' $true '' }
catch { Check 'W1 login web do cliente (302, sem MFA)' $false $_.Exception.Message }

# ---------- P1-1. indice em en-US ----------
Ensure-Api 'P1-1'; Ensure-Web 'P1-1'
$hIdxEn = Web-Get $jarCli "$base/organizacoes/$t/pacientes" 'en-US'
$ok = ($hIdxEn -like '*Patient center*') -and ($hIdxEn -like '*Register patient*') -and ($hIdxEn -like '*Ana Paula Ferreira da Costa*') -and ($hIdxEn -like '*Active*') -and ($hIdxEn -notlike '*Central de pacientes*')
Check 'P1-1 indice en-US (titulo/CTA/linha ativa, sem pt-BR)' $ok ('len=' + $hIdxEn.Length)

# ---------- P1-2. formulario nos 3 idiomas nao-pt ----------
$hFormEn = Web-Get $jarCli "$base/organizacoes/$t/pacientes/novo" 'en-US'
Check 'P1-2a formulario en-US (rotulos de identificacao/contatos/representante)' (($hFormEn -like '*Full name*') -and ($hFormEn -like '*Identifier type*') -and ($hFormEn -like '*This patient has a representative*')) ('len=' + $hFormEn.Length)
$hFormEs = Web-Get $jarCli "$base/organizacoes/$t/pacientes/novo" 'es-ES'
Check 'P1-2b formulario es-ES' (($hFormEs -like '*Nombre completo*') -and ($hFormEs -like '*Tipo de identificador*') -and ($hFormEs -like '*Registrar paciente*')) ('len=' + $hFormEs.Length)
$hFormFr = Web-Get $jarCli "$base/organizacoes/$t/pacientes/novo" 'fr-FR'
Check 'P1-2c formulario fr-FR' (($hFormFr -like '*Nom complet*') -and ($hFormFr -like '*Type d''identifiant*') -and ($hFormFr -like '*Enregistrer un patient*')) ('len=' + $hFormFr.Length)

# ---------- P1-3. ficha do paciente A em en-US (estado vazio do acervo + data localizada) ----------
$hDetEn = Web-Get $jarCli "$base/organizacoes/$t/pacientes/$patientA" 'en-US'
$ok = ($hDetEn -like '*Patient record*') -and ($hDetEn -like '*Registration data*') -and ($hDetEn -like '*No document generated*') -and ($hDetEn -like '*Deactivate record*') -and ($hDetEn -like '*updated at*') -and ($hDetEn -like '*10/17/2026*') -and ($hDetEn -notlike '*Acervo documental*')
Check 'P1-3 ficha en-US (secoes + acervo vazio + data en 10/17/2026)' $ok ('len=' + $hDetEn.Length)

# ---------- P1-4. criacao em en-US via web ----------
$hFormEn2 = Web-Get $jarCli "$base/organizacoes/$t/pacientes/novo" 'en-US'
$afEn = Get-Af $hFormEn2
$rCre = Web-Post $jarCli "$base/organizacoes/$t/pacientes/novo" @{
  FullName='Segunda Paciente D-OC36'; PreferredName=''; BirthDate=''; Email='segunda.paciente.prova@odca.local';
  Phone=''; Address=''; IdentifierType='cpf'; IdentifierValue='22255777738'; HasRepresentative='false';
  RepresentativeFullName=''; RepresentativeRelationship=''; RepresentativeIdentifierType=''; RepresentativeIdentifierValue='';
  ExpectedVersion='0'; returnUrl=''
} $afEn 'en-US'
$pidB = ''
if ($rCre.Location -match '/pacientes/([0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12})$') { $pidB = $Matches[1] }
Check 'P1-4a POST /pacientes/novo en-US -> 302 p/ ficha' (($rCre.Code -eq '302') -and ($pidB -ne '')) ('code=' + $rCre.Code + ' loc=' + $rCre.Location)
if ($pidB) {
  $hBEn = Web-Get $jarCli "$base/organizacoes/$t/pacientes/$pidB" 'en-US'
  Check 'P1-4b ficha do novo paciente com alert de sucesso en-US' (($hBEn -like '*Patient registered successfully.*') -and ($hBEn -like '*Segunda Paciente D-OC36*')) ('len=' + $hBEn.Length)
  $cntB = [long](Db "SELECT count(*) FROM odca.patients WHERE tenant_id='$t' AND full_name='Segunda Paciente D-OC36';")
  Check 'P1-4c registro persistido (count=1)' ($cntB -eq 1) ('count=' + $cntB)
} else {
  Check 'P1-4b ficha do novo paciente com alert de sucesso en-US' $false 'sem patientId no redirect'
  Check 'P1-4c registro persistido (count=1)' $false 'sem patientId no redirect'
}

# ---------- P1-5. validacao em en-US (nome vazio) ----------
$rVal = Web-Post $jarCli "$base/organizacoes/$t/pacientes/novo" @{
  FullName=''; PreferredName=''; BirthDate=''; Email=''; Phone=''; Address=''; IdentifierType=''; IdentifierValue='';
  HasRepresentative='false'; RepresentativeFullName=''; RepresentativeRelationship=''; RepresentativeIdentifierType='';
  RepresentativeIdentifierValue=''; ExpectedVersion='0'; returnUrl=''
} $afEn 'en-US'
Check 'P1-5 validacao en-US (mensagem do catalogo, sem pt-BR)' (($rVal.Code -eq '200') -and ($rVal.Body -like '*Enter the full name.*') -and ($rVal.Body -notlike '*Informe o nome completo.*')) ('code=' + $rVal.Code)

# ---------- P1-6. cadastro rapido no novo documento (en-US) ----------
$hdNuevo = Web-Get $jarCli "$base/organizacoes/$t/documentos/novo" 'en-US'
Check 'P1-6a formulario rapido presente e traduzido em en-US' (($hdNuevo -like '*Quick registration*') -and ($hdNuevo -like '*not on the list*') -and ($hdNuevo -like '*Register and select*')) ('len=' + $hdNuevo.Length)
$afDoc = Get-Af $hdNuevo
$rQuick = Web-Post $jarCli "$base/organizacoes/$t/documentos/novo/paciente" @{
  fullName='Terceira Paciente D-OC36'; identifierType='cpf'; identifierValue='52998224725'; birthDate='';
  representativeName=''; representativeRelation=''
} $afDoc 'en-US'
$pidC = ''
if ($rQuick.Location -match 'patientId=([0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12})') { $pidC = $Matches[1] }
Check 'P1-6b POST /documentos/novo/paciente -> 302 c/ patientId' (($rQuick.Code -eq '302') -and ($pidC -ne '')) ('code=' + $rQuick.Code + ' loc=' + $rQuick.Location)
if ($rQuick.Location) {
  $locUrl = if ($rQuick.Location -like 'http*') { $rQuick.Location } else { $base + $rQuick.Location }
  $hQok = Web-Get $jarCli $locUrl 'en-US'
  Check 'P1-6c alert de cadastro rapido em en-US' ($hQok -like '*Patient registered. Other fields can be completed later in the record.*') ('len=' + $hQok.Length)
} else {
  Check 'P1-6c alert de cadastro rapido em en-US' $false 'sem Location'
}

# ---------- P1-7. ciclo inativar/restaurar em en-US ----------
if ($pidB) {
  $v1 = [long](Db "SELECT row_version FROM odca.patients WHERE id='$pidB';")
  $hDb1 = Web-Get $jarCli "$base/organizacoes/$t/pacientes/$pidB" 'en-US'
  $afB = Get-Af $hDb1
  $rDeact = Web-Post $jarCli "$base/organizacoes/$t/pacientes/$pidB/situacao" @{ active='false'; expectedVersion=[string]$v1 } $afB 'en-US'
  Check 'P1-7a POST situacao active=false -> 302' ($rDeact.Code -eq '302') ('code=' + $rDeact.Code)
  $hDeact = Web-Get $jarCli "$base/organizacoes/$t/pacientes/$pidB" 'en-US'
  Check 'P1-7b ficha mostra inativacao preservando acervo (en-US)' (($hDeact -like '*Patient deactivated; documents and history were preserved.*') -and ($hDeact -like '*Inactive patient: the archive remains*')) ('len=' + $hDeact.Length)
  $hInat = Web-Get $jarCli "$base/organizacoes/$t/documentos/novo?patientId=$pidB" 'en-US'
  Check 'P1-7c paciente inativo nao selecionavel p/ novas emissoes (en-US)' ($hInat -like '*Inactive patients cannot be selected for new issuances.*') ('len=' + $hInat.Length)
  $v2 = [long](Db "SELECT row_version FROM odca.patients WHERE id='$pidB';")
  $hDb2 = Web-Get $jarCli "$base/organizacoes/$t/pacientes/$pidB" 'en-US'
  $rRest = Web-Post $jarCli "$base/organizacoes/$t/pacientes/$pidB/situacao" @{ active='true'; expectedVersion=[string]$v2 } (Get-Af $hDb2) 'en-US'
  $hRest = Web-Get $jarCli "$base/organizacoes/$t/pacientes/$pidB" 'en-US'
  Check 'P1-7d restauracao em en-US' (($rRest.Code -eq '302') -and ($hRest -like '*Patient restored.*')) ('code=' + $rRest.Code)
} else {
  Check 'P1-7a..d ciclo inativar/restaurar (en-US)' $false 'paciente B ausente (P1-4 falhou)'
}

# ---------- P1-8. regressao pt-BR ----------
$hIdxPt = Web-Get $jarCli "$base/organizacoes/$t/pacientes" 'pt-BR'
Check 'P1-8a indice pt-BR' (($hIdxPt -like '*Central de pacientes*') -and ($hIdxPt -like '*Cadastrar paciente*') -and ($hIdxPt -like '*Incluir inativos*')) ('len=' + $hIdxPt.Length)
$hFormPt = Web-Get $jarCli "$base/organizacoes/$t/pacientes/novo" 'pt-BR'
Check 'P1-8b formulario pt-BR' (($hFormPt -like '*Nome completo*') -and ($hFormPt -like '*Este paciente possui representante*') -and ($hFormPt -like '*Dados adicionais podem ser exigidos pelo modelo escolhido*')) ('len=' + $hFormPt.Length)
$hDetPt = Web-Get $jarCli "$base/organizacoes/$t/pacientes/$patientA" 'pt-BR'
Check 'P1-8c ficha pt-BR (data 17/10/2026 + secoes)' (($hDetPt -like '*Ficha do paciente*') -and ($hDetPt -like '*Acervo documental*') -and ($hDetPt -like '*atualizado em*') -and ($hDetPt -like '*17/10/2026*')) ('len=' + $hDetPt.Length)

# ---------- P1-9. indices es-ES e fr-FR ----------
$hIdxEs = Web-Get $jarCli "$base/organizacoes/$t/pacientes" 'es-ES'
Check 'P1-9a indice es-ES' (($hIdxEs -like '*Centro de pacientes*') -and ($hIdxEs -like '*Registrar paciente*') -and ($hIdxEs -like '*Incluir inactivos*')) ('len=' + $hIdxEs.Length)
$hIdxFr = Web-Get $jarCli "$base/organizacoes/$t/pacientes" 'fr-FR'
Check 'P1-9b indice fr-FR' (($hIdxFr -like '*Centre patients*') -and ($hIdxFr -like '*Enregistrer un patient*') -and ($hIdxFr -like '*Inclure les inactifs*')) ('len=' + $hIdxFr.Length)

# ---------- P1-10. estado final deterministico ----------
$cntAll = [long](Db "SELECT count(*) FROM odca.patients WHERE tenant_id='$t' AND inactive_at IS NULL;")
$cntC   = [long](Db "SELECT count(*) FROM odca.patients WHERE tenant_id='$t' AND full_name='Terceira Paciente D-OC36' AND inactive_at IS NULL;")
Check 'P1-10 estado final: 3 pacientes ativos (fixture + criado + rapido)' (($cntAll -eq 3) -and ($cntC -eq 1)) ("total=$cntAll rapido=$cntC")

# ---------- marcador ----------
$ts = (Get-Date -Format 'yyyy-MM-ddTHH:mm:sszzz')
$fim = "PROVA-FIM fails=$($script:fails) ts=$ts"
Add-Content -Path (Join-Path $evDir 'd35-proof-fim.txt') $fim -Encoding utf8
LogEv ("RESULTADO fails=" + $script:fails + " ts=" + $ts)
Write-Host $fim

# ---------- encerrar stack ----------
Get-CimInstance Win32_Process | Where-Object { $_.CommandLine -and $_.CommandLine -match $rootEsc -and $_.CommandLine -match 'Odca\.(Api|Web|Worker)\.(dll|csproj)' } | ForEach-Object { Stop-Process -Id $_.ProcessId -Force -ErrorAction SilentlyContinue }
if ($script:fails -gt 0) { exit 1 } else { exit 0 }
