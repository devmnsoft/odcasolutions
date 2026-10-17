# D-OC37 - capturas visuais da secao de assinatura da ficha do contrato (4 idiomas x 4 viewports + zoom 200%).
# Tela: /organizacoes/{t}/contratos/{contr}/versoes/{vid} com preparacao confirmada + 1 paciente.
# Requer que a prova d37 tenha rodado (ou insere a cadeia deterministica 7600... se ausente).
[Console]::OutputEncoding = [System.Text.Encoding]::UTF8
$root   = 'C:\MNSOFT\odcasolutions'
$logDir = 'C:\Users\NCELL-DEV-020\AppData\Local\Temp\opencode'
$evDir  = Join-Path $logDir 'evidence'
$outDir = Join-Path $evDir 'captures-d37'
New-Item -ItemType Directory -Force -Path $outDir | Out-Null
$evidence = Join-Path $evDir 'd37-captures.txt'
Set-Content -Path $evidence '' -Encoding utf8
function LogEv([string]$m) { Add-Content -Path $evidence $m -Encoding utf8 }
function Db([string]$q) {
  $env:PGPASSWORD = '123456'; $env:PGCONNECT_TIMEOUT = '10'
  $out = & 'C:\Program Files\PostgreSQL\18\bin\psql.exe' -h 127.0.0.1 -U postgres -d odca_test_disposable -t -A -c $q 2>&1 | Out-String
  return $out.Trim()
}

$t       = '20000000-0000-4000-8000-000000000001'
$uCli    = '10000000-0000-4000-8000-000000000003'
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

$cntV = [long](Db "SELECT count(*) FROM odca.generated_contract_versions WHERE id='$vid' AND tenant_id='$t' AND pdf_status='completed';")
if ($cntV -ne 1) {
  LogEv 'cadeia d37 ausente — inserindo estado deterministico'
  Db ("DELETE FROM odca.signature_envelopes WHERE tenant_id='$t';") | Out-Null
  Db ("DELETE FROM odca.signature_participants WHERE preparation_id='$prep';") | Out-Null
  Db ("DELETE FROM odca.signature_preparations WHERE id='$prep';") | Out-Null
  Db ("DELETE FROM odca.generated_contract_versions WHERE id='$vid';") | Out-Null
  Db ("DELETE FROM odca.contract_drafts WHERE id='$draft';") | Out-Null
  Db ("DELETE FROM odca.contracts WHERE id='$contr';") | Out-Null
  Db ("DELETE FROM odca.contract_template_versions WHERE template_id='$tpl';") | Out-Null
  Db ("DELETE FROM odca.contract_templates WHERE id='$tpl';") | Out-Null
  $seedSql = @"
INSERT INTO odca.contract_templates(id, owner_tenant_id, name, description, contract_type, scope, status, published_at, author_id, current_version)
VALUES ('$tpl', '$t', 'Modelo Prova D-OC37', 'Modelo sintetico da prova ao vivo', 'service', 'private', 'published', now(), '$uCli', 1);
INSERT INTO odca.contract_template_versions(id, template_id, version_number, content, fields, created_by, published_at)
VALUES ('$tplVer', '$tpl', 1, '{"type":"document","content":[]}'::jsonb, '[]'::jsonb, '$uCli', now());
INSERT INTO odca.contracts(id, tenant_id, title, reference) VALUES ('$contr', '$t', 'Contrato Prova D-OC37', 'ENV-D37-ASSINATURA');
INSERT INTO odca.contract_drafts(id, tenant_id, contract_id, source_template_id, source_template_version_id, content, fields, created_by, updated_by)
VALUES ('$draft', '$t', '$contr', '$tpl', '$tplVer', '{"type":"document","content":[]}'::jsonb, '[]'::jsonb, '$uCli', '$uCli');
INSERT INTO odca.generated_contract_versions(id, tenant_id, contract_id, draft_id, version_number, content_schema_version, content, fields, values, source_template_id, source_template_version_id, canonical_sha256, storage_key, byte_size, created_by, review_status, idempotency_key, draft_row_version, pdf_status, pdf_storage_key, pdf_sha256, pdf_byte_size, pdf_renderer_version, pdf_completed_at)
VALUES ('$vid', '$t', '$contr', '$draft', 1, 1, '{"type":"document","content":[]}'::jsonb, '[]'::jsonb, '[]'::jsonb, '$tpl', '$tplVer', '$pdfSha', 'studio/prova-d37-final.pdf', 1024, '$uCli', 'internally_approved', gen_random_uuid(), 1, 'completed', 'studio/prova-d37-final.pdf', '$pdfSha', 1024, 'odca-pdf-3', now());
INSERT INTO odca.signature_preparations(id, tenant_id, generated_version_id, status, row_version, created_by, updated_by, composition_revision, confirmed_revision, confirmed_pdf_storage_key, confirmed_pdf_sha256, confirmed_at, confirmed_by, confirmation_operation_id)
VALUES ('$prep', '$t', '$vid', 'confirmed', 1, '$uCli', '$uCli', 1, 1, 'studio/prova-d37-final.pdf', '$pdfSha', now(), '$uCli', '$opId');
INSERT INTO odca.signature_participants(id, client_id, tenant_id, preparation_id, composition_revision, participant_type, role, name, email, position, recorded_by)
VALUES ('$part', '$partCli', '$t', '$prep', 1, 'patient', 'Paciente', '$partName', 'sintetica@prova.odca.local', 1, '$uCli');
"@
  New-Item -ItemType Directory -Force -Path $logDir | Out-Null
  $seedFile = Join-Path $logDir 'd37-seed-capture.sql'
  [IO.File]::WriteAllText($seedFile, $seedSql, [Text.Encoding]::UTF8)
  $env:PGPASSWORD = '123456'
  & 'C:\Program Files\PostgreSQL\18\bin\psql.exe' -h 127.0.0.1 -U postgres -d odca_test_disposable -v ON_ERROR_STOP=1 -f $seedFile 2>&1 | ForEach-Object { LogEv ('SEED| ' + $_) }
  if ($LASTEXITCODE -ne 0) { Write-Host 'SEED-D37-FALHOU'; exit 2 }
  $cntV = [long](Db "SELECT count(*) FROM odca.generated_contract_versions WHERE id='$vid' AND tenant_id='$t' AND pdf_status='completed';")
}
LogEv ("d37-chain count=$cntV")

# ---------- stack http-pura (api+web; worker nao exigido pelas telas da S3) ----------
$rootEsc = [Regex]::Escape($root)
Get-CimInstance Win32_Process | Where-Object { $_.CommandLine -and $_.CommandLine -match $rootEsc -and $_.CommandLine -match 'Odca\.(Api|Web|Worker)\.(dll|csproj)' } | ForEach-Object { Stop-Process -Id $_.ProcessId -Force -ErrorAction SilentlyContinue }
Start-Sleep -Seconds 2
$env:ODCA_RUNTIME_CONFIG = Join-Path $root 'src\Odca.Api\development-runtime.json'
$env:DOTNET_ENVIRONMENT = 'Development'
$env:ASPNETCORE_ENVIRONMENT = 'Development'
$env:ODCA_SETUP_ON_START = 'true'
$env:Signatures__Provider = $null
function Wait-Ready([string]$url) {
  for ($i = 1; $i -le 90; $i++) {
    Start-Sleep -Seconds 2
    try { $h = Invoke-WebRequest -Uri $url -UseBasicParsing -TimeoutSec 3; if ([int]$h.StatusCode -lt 500) { return $true } } catch {}
  }
  return $false
}
$env:ASPNETCORE_URLS = 'http://localhost:5143'; $env:Api__BaseUrl = 'http://localhost:5143/'
$pa = Start-Process dotnet -ArgumentList ('exec "{0}"' -f (Join-Path $root 'src\Odca.Api\bin\Debug\net10.0\Odca.Api.dll')) -WorkingDirectory (Join-Path $root 'src\Odca.Api') -WindowStyle Hidden -PassThru -RedirectStandardOutput (Join-Path $logDir 'd37-cap-api.log') -RedirectStandardError (Join-Path $logDir 'd37-cap-api-err.log')
$env:ASPNETCORE_URLS = 'http://localhost:5144'
$pw = Start-Process dotnet -ArgumentList ('exec "{0}"' -f (Join-Path $root 'src\Odca.Web\bin\Debug\net10.0\Odca.Web.dll')) -WorkingDirectory (Join-Path $root 'src\Odca.Web') -WindowStyle Hidden -PassThru -RedirectStandardOutput (Join-Path $logDir 'd37-cap-web.log') -RedirectStandardError (Join-Path $logDir 'd37-cap-web-err.log')
$stackPids = @($pa.Id, $pw.Id)
if (-not (Wait-Ready 'http://localhost:5143/health/ready')) { Write-Host 'API-NUNCA-PRONTA'; exit 2 }
if (-not (Wait-Ready 'http://localhost:5144/entrar')) { Write-Host 'WEB-NUNCA-PRONTA'; exit 2 }
LogEv 'STACK pronta (http puro)'

Db "UPDATE odca.users SET must_change_password=false WHERE email ILIKE 'cliente.teste@odca.local';" | Out-Null

$cfgObj = [ordered]@{
  base = 'http://localhost:5144'
  outDir = $outDir
  edge = 'C:\Program Files (x86)\Microsoft\Edge\Application\msedge.exe'
  langs = @('pt-BR', 'en-US', 'es-ES', 'fr-FR')
  viewports = @( @(360, 780), @(768, 1024), @(1280, 800), @(1440, 900) )
  zoomAt = @(1280, 800)
  creds = [ordered]@{
    cliente = [ordered]@{ email = 'cliente.teste@odca.local'; pass = 'K8@wR3!nF6#zP2$m' }
  }
  screens = @(
    [ordered]@{ id = 'd37-sheet-assinatura'; session = 'cliente'; path = "/organizacoes/$t/contratos/$contr" }
  )
}
$cfgFile = Join-Path $logDir 'd37-cfg.json'
Set-Content -Path $cfgFile -Value ($cfgObj | ConvertTo-Json -Depth 10) -Encoding utf8
LogEv ('CAPTURES: telas=1 langs=4 viewports=4+zoom2x => 20 png em ' + $outDir)
$driver = Join-Path $root 'scripts\qa\cdp-capture-v2.mjs'
if (-not (Test-Path $driver)) { $driver = Join-Path $logDir 'd31-cdp-v2.mjs' }
& node $driver $cfgFile 2>&1 | ForEach-Object { LogEv ('CDP| ' + $_); Write-Host $_ }
foreach ($id in $stackPids) { Stop-Process -Id $id -Force -ErrorAction SilentlyContinue }
exit $LASTEXITCODE
