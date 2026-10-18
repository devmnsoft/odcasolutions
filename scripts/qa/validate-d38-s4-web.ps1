# D-OC38 - prova ao vivo da Sprint 4 (atendimento, avisos e prazos) sobre a base descartavel
# odca_test_disposable (aplica o bloco v059 se ausente); api :5143; web :5144; worker ausente.
# Sessao: cliente.teste@odca.local (fixture: papel com permissoes de obrigacoes concedido).
# F-1 sino: envio do envelope de assinatura materializa UMA linha in-app por participante
#     vinculado (dedup deterministica por source_id, v059) e o replay nao duplica.
# F-2 i18n: central de solicitacoes e pagina de notificacoes em 4 idiomas (positivos +
#     negativos anti-vazamento pt-BR); formulario de nova solicitacao em pt/en spot-check.
# A-1 obrigacoes: cadastro com serie mensal pelo web (3 prazos + auditoria), leitura individual
#     via API (evidenceRequired), acoes com versao otimista e conflito 409 preservando valores.
# F-3 (NAO-defeito): evidencia executavel nos fatos de integracao OutboxLeaseClaimTests (lease
#     atomico FOR UPDATE SKIP LOCKED + token de conclusao) - nao reexecutada aqui de proposito.
[Console]::OutputEncoding = [System.Text.Encoding]::UTF8
$ErrorActionPreference = 'Continue'
$logDir = 'C:\Users\NCELL-DEV-020\AppData\Local\Temp\opencode'
$root   = 'C:\MNSOFT\odcasolutions'
$evDir  = Join-Path $logDir 'evidence'
$tmpDir = Join-Path $logDir 'd38-fx'
$pickUp = Join-Path $logDir 'd38-pickup'
New-Item -ItemType Directory -Force -Path $evDir | Out-Null
New-Item -ItemType Directory -Force -Path $tmpDir | Out-Null
Remove-Item $pickUp -Recurse -Force -EA SilentlyContinue
New-Item -ItemType Directory -Force -Path $pickUp | Out-Null
$evidence = Join-Path $evDir 'd38-proof.txt'
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
function Web-Post([string]$jar, [string]$url, [string]$data, [string]$lang = '') {
  $f = Join-Path $tmpDir ('pp-' + [guid]::NewGuid().ToString('N').Substring(0, 10) + '.html')
  $hf = Join-Path $tmpDir ('pp-' + [guid]::NewGuid().ToString('N').Substring(0, 10) + '.hdr')
  $ca = @('-k','--connect-timeout','5','--max-time','60','-s','-b',$jar,'-c',$jar,'-o',$f,'-D',$hf,'-w','%{http_code}','-X','POST','-d',$data)
  if ($lang) { $ca += @('-b',('odca_lang=' + $lang)) }
  $ca += @($url)
  $code = Watch-Curl 120000 $ca
  $loc = ''
  if (Test-Path $hf) { $hm = [regex]::Match(([IO.File]::ReadAllText($hf)), '(?im)^Location:[ \t]*(\S+)'); if ($hm.Success) { $loc = $hm.Groups[1].Value } }
  $body = ''
  if (Test-Path $f) { try { $body = [Net.WebUtility]::HtmlDecode(([IO.File]::ReadAllText($f, [Text.Encoding]::UTF8))) } catch { $body = '' } }
  return @{ Code = ([string]$code).Trim(); Location = $loc; Body = $body }
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
  $null = Start-Process dotnet -ArgumentList ('exec "{0}"' -f (Join-Path $root 'src\Odca.Api\bin\Debug\net10.0\Odca.Api.dll')) -WorkingDirectory (Join-Path $root 'src\Odca.Api') -WindowStyle Hidden -RedirectStandardOutput (Join-Path $logDir ('d38-svc-api-' + $tag + '.log')) -RedirectStandardError (Join-Path $logDir ('d38-svc-api-' + $tag + '-err.log'))
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
$part    = '76000000-0000-4000-8000-000000000601'
$partCli = '76000000-0000-4000-8000-000000000701'
$opId    = '76000000-0000-4000-8000-000000000901'
$pdfSha  = 'e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855'
$roleD38 = '77000000-0000-4000-8000-000000000001'
$oblTitle  = 'Prova obrigacao mensal D-OC38'
$mainCfg  = Join-Path $root 'src\Odca.Api\development-runtime.json'
$rootEsc  = [Regex]::Escape($root)

# ---------- P0. binarios ----------
$binOk = (Test-Path (Join-Path $root 'src\Odca.Api\bin\Debug\net10.0\Odca.Api.dll')) -and `
         (Test-Path (Join-Path $root 'src\Odca.Web\bin\Debug\net10.0\Odca.Web.dll'))
Check 'P0 binarios Debug presentes (api/web)' $binOk ''
if (-not $binOk) { exit 2 }

# ---------- P0a. aplicar o bloco v059 na base descartavel se ainda ausente ----------
$hasV059 = [long](Db "SELECT count(*) FROM pg_indexes WHERE schemaname='odca' AND indexname='user_notifications_source_uq';")
if ($hasV059 -ne 1) {
  $env:PGPASSWORD = '123456'; $env:PGCONNECT_TIMEOUT = '10'
  $canonical = [IO.File]::ReadAllText((Join-Path $root 'database\odca.sql'))
  $start = $canonical.IndexOf('-- ODCA-MIGRATION 059', [StringComparison]::Ordinal)
  $endM = $canonical.IndexOf('-- ODCA-END 059', [StringComparison]::Ordinal)
  $end = -1
  if ($start -ge 0 -and $endM -gt $start) { $nl = $canonical.IndexOf("`n", $endM); if ($nl -ge 0) { $end = $nl + 1 } else { $end = $canonical.Length } }
  $vOut = ''
  if ($end -gt $start) {
    $blkFile = Join-Path $tmpDir 'v059-block.sql'
    [IO.File]::WriteAllText($blkFile, $canonical.Substring($start, $end - $start), [Text.Encoding]::UTF8)
    $vOut = & 'C:\Program Files\PostgreSQL\18\bin\psql.exe' -h 127.0.0.1 -U postgres -d odca_test_disposable -v ON_ERROR_STOP=1 -f $blkFile 2>&1 | Out-String
  } else { $vOut = 'bloco 059 nao encontrado no odca.sql' }
  $hasV059 = [long](Db "SELECT count(*) FROM pg_indexes WHERE schemaname='odca' AND indexname='user_notifications_source_uq';")
  Check 'P0a aplicacao do bloco v059 na base descartavel' ($hasV059 -eq 1) (($vOut -split "`n" | Where-Object { $_ } | Select-Object -Last 3) -join ' ')
  if ($hasV059 -ne 1) { exit 2 }
} else {
  LogEv 'P0a base ja com v059 (nenhuma acao)'
}

# ---------- P0b. purga deterministica (idem d37) ----------
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
if ($script:lastPsqlErr) { $purgeErr += 'tplver-d38: ' + $script:lastPsqlErr + ' | ' }
$null = Db ("DELETE FROM odca.contract_templates WHERE id='$tpl';")
if ($script:lastPsqlErr) { $purgeErr += 'tpl-d38: ' + $script:lastPsqlErr + ' | ' }
Check 'P0b purga da base descartavel (estado idem)' ($purgeErr -eq '') ($purgeErr.Trim())
if ($purgeErr -ne '') { exit 2 }

# ---------- P0c. seed deterministico (cadeia de assinatura + fixture de obrigacoes) ----------
$seedSql = @"
INSERT INTO odca.contract_templates(id, owner_tenant_id, name, description, contract_type, scope, status, published_at, author_id, current_version)
VALUES ('$tpl', '$t', 'Modelo Prova D-OC38', 'Modelo sintetico da prova ao vivo', 'service', 'private', 'published', now(), '$uCli', 1);
INSERT INTO odca.contract_template_versions(id, template_id, version_number, content, fields, created_by, published_at)
VALUES ('$tplVer', '$tpl', 1, '{"type":"document","content":[]}'::jsonb, '[]'::jsonb, '$uCli', now());
INSERT INTO odca.contracts(id, tenant_id, title, reference) VALUES ('$contr', '$t', 'Contrato Prova D-OC38', 'S4-D38-OBRIGACOES');
INSERT INTO odca.contract_drafts(id, tenant_id, contract_id, source_template_id, source_template_version_id, content, fields, created_by, updated_by)
VALUES ('$draft', '$t', '$contr', '$tpl', '$tplVer', '{"type":"document","content":[]}'::jsonb, '[]'::jsonb, '$uCli', '$uCli');
INSERT INTO odca.generated_contract_versions(id, tenant_id, contract_id, draft_id, version_number, content_schema_version, content, fields, values, source_template_id, source_template_version_id, canonical_sha256, storage_key, byte_size, created_by, review_status, idempotency_key, draft_row_version, pdf_status, pdf_storage_key, pdf_sha256, pdf_byte_size, pdf_renderer_version, pdf_completed_at)
VALUES ('$vid', '$t', '$contr', '$draft', 1, 1, '{"type":"document","content":[]}'::jsonb, '[]'::jsonb, '[]'::jsonb, '$tpl', '$tplVer',
        '$pdfSha', 'studio/prova-d38-final.pdf', 1024, '$uCli', 'internally_approved', gen_random_uuid(), 1,
        'completed', 'studio/prova-d38-final.pdf', '$pdfSha', 1024, 'odca-pdf-3', now());
INSERT INTO odca.signature_preparations(id, tenant_id, generated_version_id, status, row_version, created_by, updated_by, composition_revision, confirmed_revision, confirmed_pdf_storage_key, confirmed_pdf_sha256, confirmed_at, confirmed_by, confirmation_operation_id)
VALUES ('$prep', '$t', '$vid', 'confirmed', 1, '$uCli', '$uCli', 1, 1, 'studio/prova-d38-final.pdf', '$pdfSha', now(), '$uCli', '$opId');
INSERT INTO odca.signature_participants(id, client_id, tenant_id, preparation_id, composition_revision, participant_type, role, name, email, position, recorded_by, identity_membership_id)
VALUES ('$part', '$partCli', '$t', '$prep', 1, 'patient', 'Paciente', 'Paciente Prova D-OC38', 'sintetica@prova.odca.local', 1, '$uCli',
        (SELECT m.id FROM odca.memberships m WHERE m.tenant_id='$t' AND m.user_id='$uCli'));
DELETE FROM odca.member_roles WHERE tenant_id='$t' AND user_id='$uCli' AND role_id='$roleD38';
DELETE FROM odca.role_permissions WHERE role_id='$roleD38';
DELETE FROM odca.roles WHERE id='$roleD38';
INSERT INTO odca.roles(id, scope_type, tenant_id, code, display_name, is_system)
VALUES ('$roleD38', 'tenant', '$t', 'prova-obligacoes-d38', 'Prova Obrigacoes D-OC38', true);
INSERT INTO odca.role_permissions(role_id, permission_code)
SELECT '$roleD38', code FROM odca.permissions
WHERE code IN ('tenant.obligations.read','tenant.obligations.read_all','tenant.obligations.manage','tenant.obligations.fulfill','tenant.obligations.cancel','tenant.obligations.reopen','tenant.obligations.assign');
INSERT INTO odca.member_roles(tenant_id, user_id, role_id, assigned_by) VALUES ('$t', '$uCli', '$roleD38', '$uCli');
"@
$seedFile = Join-Path $tmpDir 'seed-d38.sql'
[IO.File]::WriteAllText($seedFile, $seedSql, [Text.Encoding]::UTF8)
$env:PGPASSWORD = '123456'
$seedOut = & 'C:\Program Files\PostgreSQL\18\bin\psql.exe' -h 127.0.0.1 -U postgres -d odca_test_disposable -v ON_ERROR_STOP=1 -f $seedFile 2>&1 | Out-String
if ($LASTEXITCODE -ne 0) { Check 'P0c seed deterministico (assinatura + papel de obrigacoes)' $false ((($seedOut -split "`n" | Where-Object { $_ }) | Select-Object -Last 4) -join ' '); exit 2 }
$cntP   = [long](Db "SELECT count(*) FROM odca.signature_preparations WHERE id='$prep' AND tenant_id='$t' AND status='confirmed';")
$cntPart= [long](Db "SELECT count(*) FROM odca.signature_participants WHERE id='$part' AND identity_membership_id IS NOT NULL AND signed_at IS NULL;")
$cntRole= [long](Db "SELECT count(*) FROM odca.member_roles mr JOIN odca.role_permissions rp ON rp.role_id=mr.role_id WHERE mr.tenant_id='$t' AND mr.user_id='$uCli' AND rp.permission_code='tenant.obligations.manage';")
Check 'P0c seed valido (prep confirmada + participante vinculado + papel com manage)' (($cntP -eq 1) -and ($cntPart -eq 1) -and ($cntRole -ge 1)) ("p=$cntP part=$cntPart role=$cntRole")
if (-not (($cntP -eq 1) -and ($cntPart -eq 1) -and ($cntRole -ge 1))) { exit 2 }

# ---------- 0. stack (provider none -> local-file mais adiante) ----------
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
$null = Start-Process dotnet -ArgumentList ('exec "{0}"' -f (Join-Path $root 'src\Odca.Api\bin\Debug\net10.0\Odca.Api.dll')) -WorkingDirectory (Join-Path $root 'src\Odca.Api') -WindowStyle Hidden -RedirectStandardOutput (Join-Path $logDir 'd38-svc-api-none.log') -RedirectStandardError (Join-Path $logDir 'd38-svc-api-none-err.log')
$env:ASPNETCORE_URLS = 'http://localhost:5144'
$null = Start-Process dotnet -ArgumentList ('exec "{0}"' -f (Join-Path $root 'src\Odca.Web\bin\Debug\net10.0\Odca.Web.dll')) -WorkingDirectory (Join-Path $root 'src\Odca.Web') -WindowStyle Hidden -RedirectStandardOutput (Join-Path $logDir 'd38-svc-web.log') -RedirectStandardError (Join-Path $logDir 'd38-svc-web-err.log')
$env:ASPNETCORE_URLS = ''
$readyApi = Wait-Ready 'http://localhost:5143/health/ready'
$readyWeb = Wait-Ready 'http://localhost:5144/entrar'
Check 'S1 api pronta' $readyApi ''
Check 'S2 web pronta' $readyWeb ''
if (-not ($readyApi -and $readyWeb)) { exit 2 }
LogEv ('Stack ok ' + (Get-Date -Format 'o'))

# ---------- W. login ----------
Db "UPDATE odca.users SET must_change_password=false WHERE email ILIKE '$clientEmail';" | Out-Null
$jarCli = Join-Path $tmpDir 'cli-jar.txt'
Remove-Item $jarCli -EA SilentlyContinue
try { Web-Login $jarCli $clientEmail $clientPass; Check 'W1 login web do cliente (302, sem MFA)' $true '' }
catch { Check 'W1 login web do cliente (302, sem MFA)' $false $_.Exception.Message }
try { $cliTok = Api-Login $clientEmail $clientPass; Check 'W2 login API do cliente (bearer)' $true '' }
catch { $cliTok = ''; Check 'W2 login API do cliente (bearer)' $false $_.Exception.Message }
if (-not $cliTok) { exit 2 }

$solUrl  = "/organizacoes/$t/central-solicitacoes"
$notifUrl = '/minha-conta/notificacoes'

# ---------- F-2. central de solicitacoes em 4 idiomas ----------
$hPt = Web-Get $jarCli "$base$solUrl" 'pt-BR'
Check 'L-pt central solicitacoes pt-BR (titulo + botao + estado vazio traduzidos)' (($hPt -like '*Central de solicitações*') -and ($hPt -like '*Nova solicitação*') -and ($hPt -like '*Nenhuma solicitação encontrada*') -and ($hPt -like '*Serviço*')) ('len=' + $hPt.Length)
$hEn = Web-Get $jarCli "$base$solUrl" 'en-US'
Check 'L-en central solicitacoes en-US (traduzido, sem vazamento pt-BR)' (($hEn -like '*Request center*') -and ($hEn -like '*New request*') -and ($hEn -like '*No requests found*') -and ($hEn -notlike '*Central de solicitações*') -and ($hEn -notlike '*Nova solicitação*')) ('len=' + $hEn.Length)
$hEs = Web-Get $jarCli "$base$solUrl" 'es-ES'
Check 'L-es central solicitacoes es-ES (traduzido, sem vazamento pt-BR)' (($hEs -like '*Centro de solicitudes*') -and ($hEs -like '*Nueva solicitud*') -and ($hEs -notlike '*Central de solicitudes*'.Replace('solicitudes','solicitações')) -and ($hEs -notlike '*Nova solicitud*')) ('len=' + $hEs.Length)
$hFr = Web-Get $jarCli "$base$solUrl" 'fr-FR'
Check 'L-fr central solicitacoes fr-FR (traduzido, sem vazamento pt-BR)' (($hFr -like '*Centre de demandes*') -and ($hFr -like '*Nouvelle demande*') -and ($hFr -notlike '*Centre de solicitudes*')) ('len=' + $hFr.Length)

# ---------- F-2. pagina de notificacoes em 4 idiomas (antes do sino) ----------
$nPt = Web-Get $jarCli "$base$notifUrl" 'pt-BR'
Check 'N-pt notificacoes pt-BR (subtitulo + estado vazio traduzidos)' (($nPt -like '*Avisos direcionados a você nesta organização*') -and ($nPt -like '*Nenhuma notificação*') -and ($nPt -like '*Você ainda não recebeu avisos*')) ('len=' + $nPt.Length)
$nEn = Web-Get $jarCli "$base$notifUrl" 'en-US'
Check 'N-en notificacoes en-US (chrome traduzido, sem vazamento pt-BR)' (($nEn -like '*Alerts addressed to you in this organization*') -and ($nEn -like '*No notifications*') -and ($nEn -notlike '*Avisos direcionados*')) ('len=' + $nEn.Length)
$nEs = Web-Get $jarCli "$base$notifUrl" 'es-ES'
Check 'N-es notificacoes es-ES (chrome traduzido, sem vazamento pt-BR)' (($nEs -like '*Avisos dirigidos a usted en esta organización*') -and ($nEs -like '*Sin notificaciones*') -and ($nEs -notlike '*Avisos dirigidos a usted en esta organizacion*'.Replace('organización','organização'))) ('len=' + $nEs.Length)
$nFr = Web-Get $jarCli "$base$notifUrl" 'fr-FR'
Check 'N-fr notificacoes fr-FR (chrome traduzido, sem vazamento pt-BR)' (($nFr -like '*Alertes adressées à vous dans cette organisation*') -and ($nFr -like '*Aucune notification*')) ('len=' + $nFr.Length)

# ---------- F-2. nova solicitacao em pt/en (spot-check do formulario) ----------
$newPt = Web-Get $jarCli "$base$solUrl/nova" 'pt-BR'
Check 'F-pt nova solicitacao pt-BR (formulario traduzido)' (($newPt -like '*Descreva a demanda com detalhes suficientes*') -and ($newPt -like '*Abrir solicitação*')) ('len=' + $newPt.Length)
$newEn = Web-Get $jarCli "$base$solUrl/nova" 'en-US'
Check 'F-en nova solicitacao en-US (descricao em ingles, sem vazamento pt-BR)' (($newEn -like '*Describe your need in enough detail*') -and ($newEn -notlike '*Descreva sua necessidade*')) ('len=' + $newEn.Length)

# ---------- F-1. sino no envio do envelope (dedup por source_id) ----------
$env:Signatures__Provider = 'local-file'
$env:Signatures__LocalFileDirectory = $pickUp
Start-Api 'local-file'
$studio = "/organizations/$t/studio"
$send1 = Api 'POST' "$studio/versions/$vid/signature-envelope" $cliTok $null
Check 'B1 POST signature-envelope (local-file) -> 200 sent' (($send1.Status -eq 200) -and ($send1.Raw -like '*"status":"sent"*')) ('status=' + $send1.Status)
$bellCnt = [long](Db "SELECT count(*) FROM odca.user_notifications WHERE tenant_id='$t' AND user_id='$uCli' AND kind='signature_pending';")
$outboxCnt = [long](Db "SELECT count(*) FROM odca.notification_outbox WHERE tenant_id='$t';")
Check 'B2 exata 1 linha de sino signature_pending p/ cliente vinculado; outbox de e-mail vazia (padrao)' (($bellCnt -eq 1) -and ($outboxCnt -eq 0)) ("bell=$bellCnt outbox=$outboxCnt")
$sendR = Api 'POST' "$studio/versions/$vid/signature-envelope" $cliTok $null
$bellCnt2 = [long](Db "SELECT count(*) FROM odca.user_notifications WHERE tenant_id='$t' AND user_id='$uCli' AND kind='signature_pending';")
Check 'B3 replay do envio -> replayed=true SEM duplicar a linha do sino' (($sendR.Raw -like '*"replayed":true*') -and ($bellCnt2 -eq 1)) ("replay=" + $sendR.Raw.Substring(0,[Math]::Min(120,$sendR.Raw.Length)) + " bell=$bellCnt2")
$pPt = Web-Get $jarCli "$base$notifUrl" 'pt-BR'
Check 'B4 pagina notificacoes pt-BR exibe o aviso (titulo + corpo do evento armazenado)' (($pPt -like '*Envelope de assinatura enviado*') -and ($pPt -like '*Um envelope externo de assinatura foi enviado*')) ('len=' + $pPt.Length)
$pEn = Web-Get $jarCli "$base$notifUrl" 'en-US'
Check 'B5 pagina notificacoes en-US: chrome traduzido; conteudo segue pt-BR (divida registrada)' (($pEn -like '*Mark all as read*') -and ($pEn -like '*Envelope de assinatura enviado*') -and ($pEn -notlike '*Marcar todas como lidas*')) ('len=' + $pEn.Length)

# ---------- A-1. centro de obrigacoes ----------
$oblUrl = "/organizacoes/$t/obrigacoes"
$oIdx = Web-Get $jarCli "$base$oblUrl" 'pt-BR'
Check 'O1 central obrigacoes: botao Nova habilitado (link para /nova; texto placeholder antigo ausente)' (($oIdx -like '*href="/organizacoes/' + $t + '/obrigacoes/nova"*' -or $oIdx -like '*obrigacoes/nova*') -and ($oIdx -like '*Nova obrigação*') -and ($oIdx -notlike '*Cadastro pela central será conectado na próxima etapa*')) ('len=' + $oIdx.Length)

$oNov = Web-Get $jarCli "$base$oblUrl/nova" 'pt-BR'
$novAf = Get-Af $oNov
$novOk = (($oNov -like '*Contrato Prova D-OC38*') -and ($oNov -like '*Responsável na organização*') -and ($oNov -like '*Recorrência mensal*') -and ($novAf -ne ''))
Check 'O2 formulario nova obrigacao: contrato ativo listado + responsavel + recorrencia + antiforgery' $novOk ('len=' + $oNov.Length + " af=" + ($novAf -ne ''))

$novData = 'contractId=' + $contr +
  '&title=' + [Uri]::EscapeDataString($oblTitle) +
  '&description=' + [Uri]::EscapeDataString('Serie mensal da prova D-OC38') +
  '&obligatedParty=' + [Uri]::EscapeDataString('Contratada') +
  '&ownerId=' + $uCli +
  '&dueDate=2026-11-15' +
  '&category=other&priority=normal' +
  '&clauseDocumentVersionId=&evidenceRequired=false&postTermReason=' +
  '&recurrenceEnabled=true&recurrenceBaseDate=2026-11-15&recurrenceIntendedDay=15&recurrenceEndsOn=2027-01-15&recurrenceOccurrenceCount=&recurrenceTimeZone=' + [Uri]::EscapeDataString('America/Sao_Paulo') +
  '&reminderDays=7&reminderDays=1&reminderDays=0' +
  '&__RequestVerificationToken=' + [Uri]::EscapeDataString($novAf)
$res = Web-Post $jarCli "$base$oblUrl/nova" $novData 'pt-BR'
$seriesCnt = [long](Db ("SELECT count(*) FROM odca.contract_obligations WHERE tenant_id='$t' AND contract_id='$contr' AND title LIKE '$oblTitle%' AND due_date IN ('2026-11-15','2026-12-15','2027-01-15');"))
$evCnt = [long](Db ("SELECT count(*) FROM odca.obligation_events oe JOIN odca.contract_obligations co2 ON co2.id=oe.obligation_id WHERE co2.tenant_id='$t' AND co2.contract_id='$contr' AND co2.title LIKE '$oblTitle%' AND oe.event_type='created';"))
Check 'O3 POST nova (serie mensal 15/nov-15/jan) -> 302 p/ central + 3 prazos criados + 3 eventos created' (([int]$res.Code -eq 302) -and ($res.Location -like "*obrigacoes*") -and ($seriesCnt -eq 3) -and ($evCnt -eq 3)) ("code=" + $res.Code + " loc=" + $res.Location + " series=" + $seriesCnt + " ev=" + $evCnt)
if ($seriesCnt -ne 3) { exit 2 }

$oblId = Db ("SELECT id FROM odca.contract_obligations WHERE tenant_id='$t' AND contract_id='$contr' AND title LIKE '$oblTitle%' AND due_date='2026-11-15' LIMIT 1;")
$g1 = Api 'GET' "/organizations/$t/obligations/$oblId" $cliTok
Check 'O4 API GET obrigacao individual -> 200 c/ evidenceRequired=false e status open' (($g1.Status -eq 200) -and ($g1.Raw -like '*"evidenceRequired":false*') -and ($g1.Raw -like '*"status":"open"*')) ('status=' + $g1.Status)

$aUrl = "$base$oblUrl/$oblId/acoes"
$aPg = Web-Get $jarCli $aUrl 'pt-BR'
$aAf = Get-Af $aPg
Check 'O5 pagina de acoes: titulo + versao esperada + antiforgery presentes' (($aPg -like '*Ações da obrigação*') -and ($aPg -like '*name="expectedVersion" value="1"*') -and ($aAf -ne '')) ('len=' + $aPg.Length)

$startData = 'operation=start&expectedVersion=1&__RequestVerificationToken=' + [Uri]::EscapeDataString($aAf)
$rStart = Web-Post $jarCli $aUrl $startData 'pt-BR'
$st1 = Db ("SELECT status||'|'||row_version FROM odca.contract_obligations WHERE id='$oblId';")
Check 'O6 acao start (v1) -> 302 + status in_progress v2' (([int]$rStart.Code -eq 302) -and ($st1 -eq 'in_progress|2')) ("code=" + $rStart.Code + " st=" + $st1)

$aPg2 = Web-Get $jarCli $aUrl 'pt-BR'
$aAf2 = Get-Af $aPg2
$confData = 'operation=cancel&expectedVersion=1&reason=' + [Uri]::EscapeDataString('Conflito de prova D-OC38') + '&__RequestVerificationToken=' + [Uri]::EscapeDataString($aAf2)
$rConf = Web-Post $jarCli $aUrl $confData 'pt-BR'
$st2 = Db ("SELECT status||'|'||row_version FROM odca.contract_obligations WHERE id='$oblId';")
$confOk = (([int]$rConf.Code -eq 200) -and ($rConf.Body -like '*mudou em outra sessão*') -and ($rConf.Body -like '*Conflito de prova D-OC38*') -and ($rConf.Body -like '*name="expectedVersion" value="2"*') -and ($st2 -eq 'in_progress|2'))
Check 'O7 conflito 409 (versao 1 obsoleta): pagina 200 c/ aviso, valor preservado, versao atualizada p/ 2, status intocado' $confOk ("code=" + $rConf.Code + " st=" + $st2)

$finData = 'operation=cancel&expectedVersion=2&reason=' + [Uri]::EscapeDataString('Conflito de prova D-OC38') + '&__RequestVerificationToken=' + [Uri]::EscapeDataString($aAf2)
$rFin = Web-Post $jarCli $aUrl $finData 'pt-BR'
$st3 = Db ("SELECT status||'|'||row_version FROM odca.contract_obligations WHERE id='$oblId';")
$evtCnt = [long](Db ("SELECT count(*) FROM odca.obligation_events WHERE obligation_id='$oblId' AND event_type IN ('start','cancel');"))
Check 'O8 resubmit cancel (v2) -> 302 + status cancelled + trilha start/cancel' (([int]$rFin.Code -eq 302) -and ($st3 -eq 'cancelled|3') -and ($evtCnt -eq 2)) ("code=" + $rFin.Code + " st=" + $st3 + " evt=" + $evtCnt)

# ---------- marcador ----------
$ts = (Get-Date -Format 'yyyy-MM-ddTHH:mm:sszzz')
$fim = "PROVA-FIM fails=$($script:fails) ts=$ts"
Add-Content -Path (Join-Path $evDir 'd38-proof-fim.txt') $fim -Encoding utf8
LogEv ("estado-final bell=1(outbox=0) serie=3(prazos) obr=cancelled(v3) events=created*3+start+cancel")
LogEv ("RESULTADO fails=" + $script:fails + " ts=" + $ts)
Write-Host $fim

# ---------- encerrar stack ----------
Get-CimInstance Win32_Process | Where-Object { $_.CommandLine -and $_.CommandLine -match $rootEsc -and $_.CommandLine -match 'Odca\.(Api|Web|Worker)\.(dll|csproj)' } | ForEach-Object { Stop-Process -Id $_.ProcessId -Force -ErrorAction SilentlyContinue }
if ($script:fails -gt 0) { exit 1 } else { exit 0 }
