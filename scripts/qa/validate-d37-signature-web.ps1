# D-OC37 - prova ao vivo da superficie de assinatura (F-1/F-2 em 4 idiomas) e do contrato do
# provedor externo de envelope plugavel (A-1) com o transporte local-file de referencia.
# Base descartavel odca_test_disposable (migra ate v058 no setup-on-start); api :5143; web :5144;
# worker ausente na prova (varredura cross-tenant coberta pelo fact de integracao WorkerSweep...).
# Sessao: cliente.teste@odca.local (papel com contract_drafts.read+manage, sem MFA).
# Fase 1: provider "none" (padrao do desenvolvimento) -> readiness com integration.unavailable e
#         envio 409 provider_unconfigured. Fase 2: Signatures__Provider=local-file (env) -> readiness
#         disponivel e ciclo completo send/sync/GET do envelope sobre pasta de drop temporaria.
# Nenhum fornecedor externo foi escolhido; o transporte local-file apenas materializa/prova a costura.
[Console]::OutputEncoding = [System.Text.Encoding]::UTF8
$ErrorActionPreference = 'Continue'
$logDir = 'C:\Users\NCELL-DEV-020\AppData\Local\Temp\opencode'
$root   = 'C:\MNSOFT\odcasolutions'
$evDir  = Join-Path $logDir 'evidence'
$tmpDir = Join-Path $logDir 'd37-fx'
$pickUp = Join-Path $logDir 'd37-pickup'
New-Item -ItemType Directory -Force -Path $evDir | Out-Null
New-Item -ItemType Directory -Force -Path $tmpDir | Out-Null
Remove-Item $pickUp -Recurse -Force -EA SilentlyContinue
New-Item -ItemType Directory -Force -Path $pickUp | Out-Null
$evidence = Join-Path $evDir 'd37-proof.txt'
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
$base = 'http://localhost:5144'
function Web-Login([string]$jar, [string]$email, [string]$pass) {
  $fh = Join-Path $tmpDir 'login-page.html'
  for ($g = 1; $g -le 3; $g++) {
    Remove-Item $fh -EA SilentlyContinue
    $null = Watch-Curl 45000 @('-k','--connect-timeout','5','--max-time','20','-s','-c',$jar,"$base/entrar","-o",$fh)
    $sz = 0; if (Test-Path $fh) { $sz = ([IO.File]::ReadAllBytes($fh)).Length }
    if ($sz -gt 500) { break }
    Start-Sleep -Seconds 15
  }
  $af = Get-Af ([IO.File]::ReadAllText($fh, [Text.Encoding]::UTF8))
  $form = 'Login=' + [Uri]::EscapeDataString($email) + '&Password=' + [Uri]::EscapeDataString($pass) + '&ReturnUrl=&__RequestVerificationToken=' + [Uri]::EscapeDataString($af)
  $hdrf = Join-Path $tmpDir 'login-post.hdr'
  $out = '000'
  for ($po = 1; $po -le 3; $po++) {
    Remove-Item $hdrf -EA SilentlyContinue
    $out = Watch-Curl 45000 @('-k','--connect-timeout','5','--max-time','20','-s','-b',$jar,'-c',$jar,'-o','NUL','-D',$hdrf,'-w','%{http_code}','-X','POST','-d',$form,"$base/entrar")
    if ([string]$out -ne '000') { break }
    Start-Sleep -Seconds 15
  }
  $code = ([string]$out).Trim(); $loc = ''
  if (Test-Path $hdrf) { $hm = [regex]::Match(([IO.File]::ReadAllText($hdrf)), '(?im)^Location:[ \t]*(\S+)'); if ($hm.Success) { $loc = $hm.Groups[1].Value } }
  if (($code -eq '302') -and ($loc -like '*mfa*')) { throw ("MFA inesperado para " + $email + " loc=" + $loc) }
  if ($code -ne '302') { throw ("POST /entrar -> " + $code + " loc=" + $loc) }
}
function Web-Get([string]$jar, [string]$url, [string]$lang = '') {
  $f = Join-Path $tmpDir ('pg-' + [guid]::NewGuid().ToString('N').Substring(0, 10) + '.html')
  $ca = @('-k','--connect-timeout','5','--max-time','45','-s','-L','-b',$jar,'-c',$jar)
  if ($lang) { $ca += @('-b',('odca_lang=' + $lang)) }
  $ca += @($url,'-o',$f,'-w','%{http_code}')
  $code = Watch-Curl 90000 $ca
  $sz = 0; if (Test-Path $f) { $sz = ([IO.File]::ReadAllBytes($f)).Length }
  if ($sz -eq 0) { Start-Sleep -Seconds 5; $code = Watch-Curl 90000 $ca; $sz = 0; if (Test-Path $f) { $sz = ([IO.File]::ReadAllBytes($f)).Length } }
  try { return [Net.WebUtility]::HtmlDecode(([IO.File]::ReadAllText($f, [Text.Encoding]::UTF8))) } catch { return '' }
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
  $url = "$apiBase$path"
  $suffix = [guid]::NewGuid().ToString('N').Substring(0, 10)
  $bf = Join-Path $tmpDir ("apib-$suffix.json")
  $ca = @('-k','--connect-timeout','5','--max-time','45','-s','-X',$method,'-o',$bf,'-w','%{http_code}')
  if ($tok) { $ca += @('-H',("Authorization: Bearer " + $tok)) }
  if ($null -ne $body) {
    $jf = Join-Path $tmpDir ("apij-$suffix.json")
    [IO.File]::WriteAllText($jf, ($body | ConvertTo-Json -Depth 8 -Compress), (New-Object Text.UTF8Encoding($false)))
    $ca += @('-H','Content-Type: application/json; charset=utf-8','--data-binary',('@' + $jf))
  }
  $ca += @($url)
  $codeStr = Watch-Curl 60000 $ca
  $code = 0
  if ([int]::TryParse($codeStr, [ref]$code)) {}
  $raw = ''
  if (Test-Path $bf) { try { $raw = [IO.File]::ReadAllText($bf, [Text.Encoding]::UTF8) } catch { $raw = '' } }
  return @{ Status = $code; Raw = $raw }
}
function Api-Login([string]$email, [string]$pass) {
  $r = Api 'POST' '/auth/login' '' @{ Login = $email; Password = $pass }
  if ($r.Status -ne 200) { throw ("login API falhou para " + $email + ": " + ([string]$r.Raw).Substring(0, [Math]::Min(300, ([string]$r.Raw).Length))) }
  $tok = [string](($r.Raw | ConvertFrom-Json).accessToken)
  if (-not $tok) { throw ("accessToken ausente no login API para " + $email) }
  return $tok
}
function Stop-Api { Get-CimInstance Win32_Process | Where-Object { $_.CommandLine -and $_.CommandLine -match $script:rootEsc -and $_.CommandLine -match 'Odca\.Api\.dll' } | ForEach-Object { Stop-Process -Id $_.ProcessId -Force -ErrorAction SilentlyContinue } }
function Start-Api([string]$tag) {
  Stop-Api
  Start-Sleep -Seconds 2
  $prevUrls = $env:ASPNETCORE_URLS
  $env:ASPNETCORE_URLS = 'http://localhost:5143'
  $null = Start-Process dotnet -ArgumentList ('exec "{0}"' -f (Join-Path $root 'src\Odca.Api\bin\Debug\net10.0\Odca.Api.dll')) -WorkingDirectory (Join-Path $root 'src\Odca.Api') -WindowStyle Hidden -RedirectStandardOutput (Join-Path $logDir ('d37-svc-api-' + $tag + '.log')) -RedirectStandardError (Join-Path $logDir ('d37-svc-api-' + $tag + '-err.log'))
  $env:ASPNETCORE_URLS = $prevUrls
  if (-not (Wait-Ready 'http://localhost:5143/health/ready')) { LogEv ('API nao voltou apos restart em ' + $tag) }
}

# ---------- constantes ----------
$t           = '20000000-0000-4000-8000-000000000001'
$uCli        = '10000000-0000-4000-8000-000000000003'
$clientEmail = 'cliente.teste@odca.local'
$clientPass  = 'K8@wR3!nF6#zP2$m'
$tpl     = '76000000-0000-0000-0000-000000000001'
$tplVer  = '76000000-0000-0000-0000-000000000101'
$contr   = '76000000-0000-0000-0000-000000000201'
$draft   = '76000000-0000-0000-0000-000000000301'
$vid     = '76000000-0000-0000-0000-000000000401'
$prep    = '76000000-0000-0000-0000-000000000501'
$part    = '76000000-0000-0000-0000-000000000601'
$partCli = '76000000-0000-0000-0000-000000000701'
$opId    = '76000000-0000-0000-0000-000000000901'
$pdfSha  = 'e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855'
$partName = 'Paciente Prova D-OC37'
$mainCfg  = Join-Path $root 'src\Odca.Api\development-runtime.json'
$rootEsc  = [Regex]::Escape($root)

# ---------- P0. binarios ----------
$binOk = (Test-Path (Join-Path $root 'src\Odca.Api\bin\Debug\net10.0\Odca.Api.dll')) -and `
         (Test-Path (Join-Path $root 'src\Odca.Web\bin\Debug\net10.0\Odca.Web.dll'))
Check 'P0 binarios Debug presentes (api/web)' $binOk ''

# ---------- P0a. aplicar apenas o bloco v058 na base descartavel se ainda ausente ----------
# Obs.: os arquivos database/releases/* sao snapshots completos do canonico (byte-identicos),
# entao aqui extraimos somente o bloco 058 para nao reexecutar as migracoes anteriores.
$hasV058 = [long](Db "SELECT count(*) FROM information_schema.tables WHERE table_schema='odca' AND table_name='signature_envelopes';")
if ($hasV058 -ne 1) {
  $env:PGPASSWORD = '123456'; $env:PGCONNECT_TIMEOUT = '10'
  $canonical = [IO.File]::ReadAllText((Join-Path $root 'database\odca.sql'))
  $start = $canonical.IndexOf('-- ODCA-MIGRATION 058', [StringComparison]::Ordinal)
  $endM = $canonical.IndexOf('-- ODCA-END 058', [StringComparison]::Ordinal)
  $end = -1
  if ($start -ge 0 -and $endM -gt $start) { $nl = $canonical.IndexOf("`n", $endM); if ($nl -ge 0) { $end = $nl + 1 } else { $end = $canonical.Length } }
  $vOut = ''
  if ($end -gt $start) {
    $blkFile = Join-Path $tmpDir 'v058-block.sql'
    [IO.File]::WriteAllText($blkFile, $canonical.Substring($start, $end - $start), [Text.Encoding]::UTF8)
    $vOut = & 'C:\Program Files\PostgreSQL\18\bin\psql.exe' -h 127.0.0.1 -U postgres -d odca_test_disposable -v ON_ERROR_STOP=1 -f $blkFile 2>&1 | Out-String
  } else { $vOut = 'bloco 058 nao encontrado no odca.sql' }
  $hasV058 = [long](Db "SELECT count(*) FROM information_schema.tables WHERE table_schema='odca' AND table_name='signature_envelopes';")
  Check 'P0a aplicacao do bloco v058 na base descartavel' ($hasV058 -eq 1) (($vOut -split "`n" | Where-Object { $_ } | Select-Object -Last 3) -join ' ')
  if ($hasV058 -ne 1) { exit 2 }
} else {
  LogEv 'P0a base ja com v058 (nenhuma acao)'
}

# ---------- P0b. purga deterministica (idem d35) + cadeira de assinatura fixa ----------
$purgeTables = @(
  'signature_envelope_events','signature_envelopes',
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
$null = Db ("DELETE FROM odca.contract_template_versions WHERE template_id='$tpl';")
if ($script:lastPsqlErr) { $purgeErr += 'tplver-d37: ' + $script:lastPsqlErr + ' | ' }
$null = Db ("DELETE FROM odca.contract_templates WHERE id='$tpl';")
if ($script:lastPsqlErr) { $purgeErr += 'tpl-d37: ' + $script:lastPsqlErr + ' | ' }
Check 'P0b purga da base descartavel (estado idem + cadeias d37)' ($purgeErr -eq '') ($purgeErr.Trim())
if ($purgeErr -ne '') { exit 2 }

$seedSql = @"
INSERT INTO odca.contract_templates(id, owner_tenant_id, name, description, contract_type, scope, status, published_at, author_id, current_version)
VALUES ('$tpl', '$t', 'Modelo Prova D-OC37', 'Modelo sintetico da prova ao vivo', 'service', 'private', 'published', now(), '$uCli', 1);
INSERT INTO odca.contract_template_versions(id, template_id, version_number, content, fields, created_by, published_at)
VALUES ('$tplVer', '$tpl', 1, '{"type":"document","content":[]}'::jsonb, '[]'::jsonb, '$uCli', now());
INSERT INTO odca.contracts(id, tenant_id, title, reference) VALUES ('$contr', '$t', 'Contrato Prova D-OC37', 'ENV-D37-ASSINATURA');
INSERT INTO odca.contract_drafts(id, tenant_id, contract_id, source_template_id, source_template_version_id, content, fields, created_by, updated_by)
VALUES ('$draft', '$t', '$contr', '$tpl', '$tplVer', '{"type":"document","content":[]}'::jsonb, '[]'::jsonb, '$uCli', '$uCli');
INSERT INTO odca.generated_contract_versions(id, tenant_id, contract_id, draft_id, version_number, content_schema_version, content, fields, values, source_template_id, source_template_version_id, canonical_sha256, storage_key, byte_size, created_by, review_status, idempotency_key, draft_row_version, pdf_status, pdf_storage_key, pdf_sha256, pdf_byte_size, pdf_renderer_version, pdf_completed_at)
VALUES ('$vid', '$t', '$contr', '$draft', 1, 1, '{"type":"document","content":[]}'::jsonb, '[]'::jsonb, '[]'::jsonb, '$tpl', '$tplVer',
        '$pdfSha', 'studio/prova-d37-final.pdf', 1024, '$uCli', 'internally_approved', gen_random_uuid(), 1,
        'completed', 'studio/prova-d37-final.pdf', '$pdfSha', 1024, 'odca-pdf-3', now());
INSERT INTO odca.signature_preparations(id, tenant_id, generated_version_id, status, row_version, created_by, updated_by, composition_revision, confirmed_revision, confirmed_pdf_storage_key, confirmed_pdf_sha256, confirmed_at, confirmed_by, confirmation_operation_id)
VALUES ('$prep', '$t', '$vid', 'confirmed', 1, '$uCli', '$uCli', 1, 1, 'studio/prova-d37-final.pdf', '$pdfSha', now(), '$uCli', '$opId');
INSERT INTO odca.signature_participants(id, client_id, tenant_id, preparation_id, composition_revision, participant_type, role, name, email, position, recorded_by)
VALUES ('$part', '$partCli', '$t', '$prep', 1, 'patient', 'Paciente', '$partName', 'sintetica@prova.odca.local', 1, '$uCli');
"@
$seedFile = Join-Path $tmpDir 'seed-d37.sql'
[IO.File]::WriteAllText($seedFile, $seedSql, [Text.Encoding]::UTF8)
$env:PGPASSWORD = '123456'
$seedOut = & 'C:\Program Files\PostgreSQL\18\bin\psql.exe' -h 127.0.0.1 -U postgres -d odca_test_disposable -v ON_ERROR_STOP=1 -f $seedFile 2>&1 | Out-String
if ($LASTEXITCODE -ne 0) { Check 'P0c seed da cadeia de assinatura (template->versao->prep confirmada->paciente)' $false ((($seedOut -split "`n" | Where-Object { $_ }) | Select-Object -Last 4) -join ' '); exit 2 }
$cntV   = [long](Db "SELECT count(*) FROM odca.generated_contract_versions WHERE id='$vid' AND tenant_id='$t' AND pdf_status='completed';")
$cntP   = [long](Db "SELECT count(*) FROM odca.signature_preparations WHERE id='$prep' AND tenant_id='$t' AND status='confirmed' AND confirmed_revision=1;")
$cntU   = [long](Db "SELECT count(*) FROM odca.signature_participants WHERE preparation_id='$prep' AND composition_revision=1;")
$cntEnv = [long](Db "SELECT count(*) FROM odca.signature_envelopes WHERE tenant_id='$t';")
Check 'P0c seed deterministico (versao+PDF concluido, prep confirmada, 1 paciente, 0 envelopes)' (($cntV -eq 1) -and ($cntP -eq 1) -and ($cntU -eq 1) -and ($cntEnv -eq 0)) ("v=$cntV p=$cntP u=$cntU env=$cntEnv")

# ---------- 0. stack fase 1 (provider "none" = padrao do desenvolvimento) ----------
& (Join-Path $root 'scripts\stop-local.ps1') 2>&1 | Out-Null
Start-Sleep -Seconds 3
Get-CimInstance Win32_Process | Where-Object { $_.CommandLine -and $_.CommandLine -match $rootEsc -and $_.CommandLine -match 'Odca\.(Api|Web|Worker)\.(dll|csproj)' } | ForEach-Object { Stop-Process -Id $_.ProcessId -Force -ErrorAction SilentlyContinue }
Start-Sleep -Seconds 2
$env:ODCA_RUNTIME_CONFIG = $mainCfg
$env:DOTNET_ENVIRONMENT = 'Development'
$env:ASPNETCORE_ENVIRONMENT = 'Development'
$env:ODCA_SETUP_ON_START = 'true'
$env:Signatures__Provider = $null
$env:ASPNETCORE_URLS = 'http://localhost:5143'
$env:Api__BaseUrl = 'http://localhost:5143/'
$null = Start-Process dotnet -ArgumentList ('exec "{0}"' -f (Join-Path $root 'src\Odca.Api\bin\Debug\net10.0\Odca.Api.dll')) -WorkingDirectory (Join-Path $root 'src\Odca.Api') -WindowStyle Hidden -RedirectStandardOutput (Join-Path $logDir 'd37-svc-api-none.log') -RedirectStandardError (Join-Path $logDir 'd37-svc-api-none-err.log')
$env:ASPNETCORE_URLS = 'http://localhost:5144'
$null = Start-Process dotnet -ArgumentList ('exec "{0}"' -f (Join-Path $root 'src\Odca.Web\bin\Debug\net10.0\Odca.Web.dll')) -WorkingDirectory (Join-Path $root 'src\Odca.Web') -WindowStyle Hidden -RedirectStandardOutput (Join-Path $logDir 'd37-svc-web.log') -RedirectStandardError (Join-Path $logDir 'd37-svc-web-err.log')
$env:ASPNETCORE_URLS = ''
$readyApi = Wait-Ready 'http://localhost:5143/health/ready'
$readyWeb = Wait-Ready 'http://localhost:5144/entrar'
Check 'S1 api pronta (fase 1, provider none)' $readyApi ''
Check 'S2 web pronta' $readyWeb ''
if (-not ($readyApi -and $readyWeb)) { exit 2 }
LogEv ('Fase stack ok (fase 1: provider none) ' + (Get-Date -Format 'o'))

# ---------- W. login ----------
Db "UPDATE odca.users SET must_change_password=false WHERE email ILIKE '$clientEmail';" | Out-Null
$jarCli = Join-Path $tmpDir 'cli-jar.txt'
Remove-Item $jarCli -EA SilentlyContinue
try { Web-Login $jarCli $clientEmail $clientPass; Check 'W1 login web do cliente (302, sem MFA)' $true '' }
catch { Check 'W1 login web do cliente (302, sem MFA)' $false $_.Exception.Message }
try { $cliTok = Api-Login $clientEmail $clientPass; Check 'W2 login API do cliente (bearer)' $true '' }
catch { $cliTok = ''; Check 'W2 login API do cliente (bearer)' $false $_.Exception.Message }

$studio = "/organizations/$t/studio"

# ---------- R1. readiness sem provider: advertencia integration.unavailable ----------
$r1 = Api 'GET' "$studio/versions/$vid/signature-preparation/readiness" $cliTok
Check 'R1 readiness (provider none) 200 c/ advertencia integration.unavailable' (($r1.Status -eq 200) -and ($r1.Raw -like '*integration.unavailable*')) ('status=' + $r1.Status)

# ---------- E0. envio sem provider: 409 provider_unconfigured ----------
$e0 = Api 'POST' "$studio/versions/$vid/signature-envelope" $cliTok $null
Check 'E0 POST signature-envelope (provider none) -> 409 provider_unconfigured' (($e0.Status -eq 409) -and ($e0.Raw -like '*signatures.provider_unconfigured*')) ('status=' + $e0.Status)

# ---------- F2. restart da API com local-file ----------
$env:Signatures__Provider = 'local-file'
$env:Signatures__LocalFileDirectory = $pickUp
Start-Api 'local-file'
$r2 = Api 'GET' "$studio/versions/$vid/signature-preparation/readiness" $cliTok
Check 'R2 readiness (local-file) 200 SEM integration.unavailable' (($r2.Status -eq 200) -and ($r2.Raw -notlike '*integration.unavailable*')) ('status=' + $r2.Status)

# ---------- F-1. detalhe da versão com signatureStatus derivado ----------
$f1 = Api 'GET' "$studio/versions/$vid" $cliTok
Check 'F1 GET versions/{id} -> signatureStatus confirmed (consistencia derivada)' (($f1.Status -eq 200) -and ($f1.Raw -like '*"signatureStatus":"confirmed"*')) ('status=' + $f1.Status)

# ---------- F-2. secao de assinatura da ficha em 4 idiomas ----------
# As checagens negativas sao limitadas a <section id="assinatura">: as demais secoes da ficha
# (renovacao, obrigacoes, revisao, historico, formularios) e os aria-labels da navegacao sao
# divida pt-BR pre-existente anterior a S3 (registrada em BACKLOG.md, fora do escopo F-2).
function Section-Assinatura([string]$html) {
  $i = $html.IndexOf('<section id="assinatura"')
  if ($i -lt 0) { return '' }
  $j = $html.IndexOf('</section>', $i + 25)
  if ($j -lt 0) { return '' }
  return $html.Substring($i, $j - $i + 10)
}
$sheetUrl = "/organizacoes/$t/contratos/$contr"
$hPt = Web-Get $jarCli "$base$sheetUrl" 'pt-BR'
$sPt = Section-Assinatura $hPt
$tPt = $sPt -replace '<[^>]+>', ' '
Check 'S-pt ficha pt-BR (secao assinatura: estado confirmado + imutabilidade + paciente + aria-label)' (($sPt.Length -gt 0) -and ($sPt -like '*Assinaturas*') -and ($sPt -like '*Confirmada*') -and ($sPt -like '*Como assinar: cada participante pode*') -and ($sPt -like '*externa via fornecedor ainda*') -and ($sPt -like "*O documento em assinatura é imutável*") -and ($sPt -like "*$partName*") -and ($sPt -like '*Versão gerada*') -and ($sPt -like '*Assinaturas do documento gerado*') -and ($tPt -notlike '*How to sign*')) ('len=' + $sPt.Length)
$hEn = Web-Get $jarCli "$base$sheetUrl" 'en-US'
$sEn = Section-Assinatura $hEn
$tEn = $sEn -replace '<[^>]+>', ' '
Check 'S-en ficha en-US (traducoes completas, sem vazamento pt-BR na secao assinatura)' (($sEn.Length -gt 0) -and ($sEn -like '*Signatures*') -and ($sEn -like '*Confirmed*') -and ($sEn -like '*How to sign: each participant can*') -and ($sEn -like '*has not yet been chosen for launch: no external envelope is sent and the signature quota is not consumed*') -and ($sEn -like '*The document being signed is immutable: changes require a new version generated in the Studio.*') -and ($sEn -like "*$partName*") -and ($sEn -like '*Patient*') -and ($sEn -like '*Signatures of the generated document*') -and ($tEn -notlike '*Assinaturas*') -and ($tEn -notlike '*imutável*') -and ($tEn -notlike '*Como assinar*') -and ($tEn -notlike '*Situação*')) ('len=' + $sEn.Length)
$hEs = Web-Get $jarCli "$base$sheetUrl" 'es-ES'
$sEs = Section-Assinatura $hEs
$tEs = $sEs -replace '<[^>]+>', ' '
Check 'S-es ficha es-ES (traducoes completas, sem vazamento pt-BR na secao assinatura)' (($sEs.Length -gt 0) -and ($sEs -like '*Firmas*') -and ($sEs -like '*Confirmada*') -and ($sEs -like '*Cómo firmar: cada participante puede*') -and ($sEs -like '*aún no ha sido elegida para el estreno: ningún sobre externo se envía y la franquicia de firma no se consume*') -and ($sEs -like '*El documento en firma es inmutable: los cambios exigen una nueva versión generada en el Estudio.*') -and ($sEs -like '*Versión generada*') -and ($sEs -like '*Firmas del documento generado*') -and ($tEs -notlike '*Assinaturas*') -and ($tEs -notlike '*imutável*') -and ($tEs -notlike '*Como assinar*')) ('len=' + $sEs.Length)
$hFr = Web-Get $jarCli "$base$sheetUrl" 'fr-FR'
$sFr = Section-Assinatura $hFr
$tFr = $sFr -replace '<[^>]+>', ' '
Check 'S-fr ficha fr-FR (traducoes completas, sem vazamento pt-BR na secao assinatura)' (($sFr.Length -gt 0) -and ($sFr -like '*Comment signer : chaque participant peut*') -and ($sFr -like '*Confirmée*') -and ($sFr -like '*choisie pour le lancement*') -and ($sFr -like '*Le document en signature est immuable*') -and ($sFr -like '*Version générée*') -and ($sFr -like '*Patient*') -and ($sFr -like '*Signatures du document généré*') -and ($tFr -notlike '*Assinaturas*') -and ($tFr -notlike '*imutável*') -and ($tFr -notlike '*Como assinar*')) ('len=' + $sFr.Length)

# ---------- E. ciclo do envelope (send/sync/GET) sobre o transporte local-file ----------
$envPath = Join-Path $pickUp ("env-$($vid -replace '-','').envelope.json")
$envStatePath = Join-Path $pickUp ("env-$($vid -replace '-','').state.json")

$get0 = Api 'GET' "$studio/versions/$vid/signature-envelope" $cliTok
Check 'E1 GET envelope antes do envio -> 404' ($get0.Status -eq 404) ('status=' + $get0.Status)

$send1 = Api 'POST' "$studio/versions/$vid/signature-envelope" $cliTok $null
Check 'E2 POST signature-envelope -> 200 sent/replayed=false/local-file' (($send1.Status -eq 200) -and ($send1.Raw -like '*"status":"sent"*') -and ($send1.Raw -like '*"replayed":false*') -and ($send1.Raw -like '*"providerCode":"local-file"*')) ('status=' + $send1.Status)
$payload = ''
if (Test-Path $envPath) { $payload = [IO.File]::ReadAllText($envPath, [Text.Encoding]::UTF8) }
Check 'E3 envelope materializado na pasta de drop (participante + hash do PDF imutavel)' (($payload.Length -gt 0) -and ($payload -like "*$pdfSha*") -and ($payload -like "*$partName*")) ('bytes=' + $payload.Length)

$sendR = Api 'POST' "$studio/versions/$vid/signature-envelope" $cliTok $null
$mvtsR = [long](Db "SELECT count(*) FROM odca.resource_movements WHERE tenant_id='$t' AND resource_type='signature_credit';")
$cEnvR = [long](Db "SELECT count(*) FROM odca.signature_envelopes WHERE tenant_id='$t';")
Check 'E3b reenvio enquanto sent -> 200 replayed=true SEM novo consumo (chave natural idempotente)' (($sendR.Status -eq 200) -and ($sendR.Raw -like '*"replayed":true*') -and ($mvtsR -eq 1) -and ($cEnvR -eq 1)) ('status=' + $sendR.Status + " mvts=$mvtsR env=$cEnvR")

$sync0 = Api 'POST' "$studio/versions/$vid/signature-envelope/sync" $cliTok $null
Check 'E4 sync sem estado externo -> 200 updated=false (segue sent)' (($sync0.Status -eq 200) -and ($sync0.Raw -like '*"updated":false*') -and ($sync0.Raw -like '*"status":"sent"*')) ('status=' + $sync0.Status)

Set-Content -Path $envStatePath -Value '{"status":"accepted","occurred_at":"2026-10-17T12:00:00Z"}' -Encoding ascii -NoNewline
$sync1 = Api 'POST' "$studio/versions/$vid/signature-envelope/sync" $cliTok $null
Check 'E5 sync com estado accepted -> 200 updated=true/status=accepted' (($sync1.Status -eq 200) -and ($sync1.Raw -like '*"updated":true*') -and ($sync1.Raw -like '*"status":"accepted"*')) ('status=' + $sync1.Status)
$sync2 = Api 'POST' "$studio/versions/$vid/signature-envelope/sync" $cliTok $null
Check 'E6 sync repetido com o mesmo evento -> updated=false (dedup deterministica)' (($sync2.Status -eq 200) -and ($sync2.Raw -like '*"updated":false*')) ('status=' + $sync2.Status)

$get1 = Api 'GET' "$studio/versions/$vid/signature-envelope" $cliTok
Check 'E7 GET envelope -> 200 accepted + auditoria de eventos (sent+accepted)' (($get1.Status -eq 200) -and ($get1.Raw -like '*"status":"accepted"*') -and ($get1.Raw -like '*"eventType":"accepted"*') -and ($get1.Raw -like '*"eventType":"sent"*')) ('status=' + $get1.Status)

$send2 = Api 'POST' "$studio/versions/$vid/signature-envelope" $cliTok $null
$mvts = [long](Db "SELECT count(*) FROM odca.resource_movements WHERE tenant_id='$t' AND resource_type='signature_credit';")
$cEnv = [long](Db "SELECT count(*) FROM odca.signature_envelopes WHERE tenant_id='$t' AND status='accepted';")
$cEvt = [long](Db "SELECT count(*) FROM odca.signature_envelope_events WHERE tenant_id='$t';")
Check 'E8 envio apos accepted -> 409 envelope.accepted (sem novo consumo)' (($send2.Status -eq 409) -and ($send2.Raw -like '*signatures.envelope.accepted*') -and ($mvts -eq 1) -and ($cEnv -eq 1) -and ($cEvt -eq 2)) ('status=' + $send2.Status + " mvts=$mvts env=$cEnv evt=$cEvt")

# ---------- estado final documentado ----------
LogEv ("estado-final envelopes=1(accepted) events=2(sent+accepted) movimientos_signature_credit=1 pickup=" + $pickUp)
LogEv ("estado-final arquivos-drop=" + ((Get-ChildItem $pickUp -File | Select-Object -ExpandProperty Name) -join '; '))

# ---------- marcador ----------
$ts = (Get-Date -Format 'yyyy-MM-ddTHH:mm:sszzz')
$fim = "PROVA-FIM fails=$($script:fails) ts=$ts"
Add-Content -Path (Join-Path $evDir 'd37-proof-fim.txt') $fim -Encoding utf8
LogEv ("RESULTADO fails=" + $script:fails + " ts=" + $ts)
Write-Host $fim

# ---------- encerrar stack ----------
Get-CimInstance Win32_Process | Where-Object { $_.CommandLine -and $_.CommandLine -match $rootEsc -and $_.CommandLine -match 'Odca\.(Api|Web|Worker)\.(dll|csproj)' } | ForEach-Object { Stop-Process -Id $_.ProcessId -Force -ErrorAction SilentlyContinue }
if ($script:fails -gt 0) { exit 1 } else { exit 0 }
