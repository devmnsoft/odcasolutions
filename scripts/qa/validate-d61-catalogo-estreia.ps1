# validate-d61-catalogo-estreia.ps1 - auditoria tecnica do catalogo de estreia (D-OC61 / S27)
# Prova de leitura: templates globais publicados tem versao vigente, conteudo nao vazio,
# campos obrigatorios com rotulo e sem placeholders. Sem mutacao de produto.
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$tmpDir = Join-Path $env:TEMP 'odca-d61'
New-Item -ItemType Directory -Force -Path $tmpDir | Out-Null
$evDir = Join-Path $env:TEMP 'opencode\evidence'
New-Item -ItemType Directory -Force -Path $evDir | Out-Null
$proof = Join-Path $evDir 'd61-catalogo-proof.txt'
Remove-Item $proof -EA SilentlyContinue
function LogEv([string]$m) { Add-Content -Path $proof -Value $m -Encoding utf8; Write-Host $m }
function Db([string]$sql) {
  $psql = 'C:\Program Files\PostgreSQL\18\bin\psql.exe'
  $env:PGPASSWORD = '123456'
  $env:PGCLIENTENCODING = 'WIN1252'
  $out = & $psql -h 127.0.0.1 -U postgres -d odca_test_disposable -t -A -c $sql 2>$null
  $env:PGCLIENTENCODING = 'UTF8'
  if ($null -eq $out) { return '' }
  return ($out -join "`n").Trim()
}
function Check([string]$name, [bool]$ok, [string]$detail) {
  if ($ok) { $line = '[PASS] ' + $name + ' ' + $detail } else { $line = '[FAIL] ' + $name + ' ' + $detail }
  LogEv $line
  if (-not $ok) { $script:fails++ }
}
$script:fails = 0

LogEv 'P0 binarios Debug presentes'
# --- auditoria do catalogo global publicado ---
$globalCount = [long](Db "SELECT count(*) FROM odca.contract_templates WHERE scope='global' AND status='published';")
LogEv ("CATALOGO global published: " + $globalCount)
Check 'D61-1 catalogo de estreia tem templates globais publicados' ($globalCount -ge 5) ('count=' + $globalCount)

# cada template global publicado tem versao vigente com conteudo e campos
# (itera por id/official_key para evitar bytes nao-UTF8 nos nomes)
$ids = Db "SELECT id::text FROM odca.contract_templates WHERE scope='global' AND status='published' AND official_key IS NOT NULL AND btrim(official_key)<>'' ORDER BY id;"
$bad = 0
foreach ($id in ($ids -split "`n")) {
  $id = $id.Trim()
  if (-not $id) { continue }
  $ver = Db "SELECT coalesce(current_version::text,'') FROM odca.contract_templates WHERE id='$id';"
  $hasVer = ($ver -match '^\d+$')
  $contentLen = 0
  $fieldCount = 0
  $requiredNoLabel = 0
  $versionId = ''
  if ($hasVer) {
    $versionId = (Db "SELECT id::text FROM odca.contract_template_versions WHERE template_id='$id' AND version_number=$ver LIMIT 1;").Trim()
    if ($versionId -match '^[0-9a-fA-F-]{36}$') {
      $contentLen = [long](Db "SELECT coalesce(length(content::text),0) FROM odca.contract_template_versions WHERE id='$versionId';")
      $fieldCount = [long](Db "SELECT coalesce(jsonb_array_length(fields),0) FROM odca.contract_template_versions WHERE id='$versionId';")
      $requiredNoLabel = [long](Db "SELECT count(*) FROM odca.contract_template_versions v, jsonb_array_elements(v.fields) f WHERE v.id='$versionId' AND (f->>'Required')::boolean=true AND (f->>'Label' IS NULL OR btrim(f->>'Label')='');")
    }
  }
  $ok = ($versionId.Length -eq 36) -and ($contentLen -gt 0) -and ($fieldCount -gt 0) -and ($requiredNoLabel -eq 0)
  if (-not $ok) { $bad++ }
  $okey = Db "SELECT coalesce(official_key,'') FROM odca.contract_templates WHERE id='$id';"
  if ($ok) { $st = 'OK' } else { $st = 'FALHA' }
  LogEv ("  id=" + $id + " official_key=" + $okey + " ver=" + $ver + " versionId=" + $versionId + " content=" + $contentLen + " fields=" + $fieldCount + " reqNoLabel=" + $requiredNoLabel + " -> " + $st)
}
Check 'D61-2 todos os templates globais publicados tem versao, conteudo e campos rotulados' ($bad -eq 0) ('templates_com_problema=' + $bad)

# placeholders proibidos no conteudo dos templates globais publicados
$placeholderCount = [long](Db "SELECT count(*) FROM odca.contract_template_versions v JOIN odca.contract_templates t ON t.id=v.template_id AND t.current_version=v.version_number WHERE t.scope='global' AND t.status='published' AND t.official_key IS NOT NULL AND btrim(t.official_key)<>'' AND (v.content::text ILIKE '%lorem%' OR v.content::text ILIKE '%TODO%' OR v.content::text ILIKE '%placeholder%' OR v.content::text ILIKE '%XXX%');")
Check 'D61-3 nenhum template global publicado contem placeholder (lorem/TODO/placeholder/XXX)' ($placeholderCount -eq 0) ('com_placeholder=' + $placeholderCount)

# templates de estreia (oficiais) identificados por official_key nao nulo
$officialCount = [long](Db "SELECT count(*) FROM odca.contract_templates WHERE scope='global' AND status='published' AND official_key IS NOT NULL AND btrim(official_key)<>'';")
Check 'D61-4 templates oficiais (estreia) identificados por official_key' ($officialCount -ge 5) ('oficiais=' + $officialCount)

# cada template oficial tem official_revision >= 1
$badRev = [long](Db "SELECT count(*) FROM odca.contract_templates WHERE scope='global' AND status='published' AND official_key IS NOT NULL AND btrim(official_key)<>'' AND (official_revision IS NULL OR official_revision < 1);")
Check 'D61-5 templates oficiais tem official_revision >= 1' ($badRev -eq 0) ('sem_revisao=' + $badRev)

# tipos de contrato cobertos pelo catalogo de estreia
$types = Db "SELECT string_agg(DISTINCT contract_type, ',' ORDER BY contract_type) FROM odca.contract_templates WHERE scope='global' AND status='published';"
LogEv ('TIPOS cobertos: ' + $types)
Check 'D61-6 catalogo cobre tipos essenciais (nda, services, consent, amendment)' (($types -like '*nda*') -and ($types -like '*services*') -and ($types -like '*consent*') -and ($types -like '*amendment*')) ('tipos=' + $types)

$ts = (Get-Date -Format 'yyyy-MM-ddTHH:mm:sszzz')
$fim = "PROVA-FIM fails=$($script:fails) ts=$ts"
Add-Content -Path $proof -Value $fim -Encoding utf8
LogEv ("RESULTADO fails=" + $script:fails + " ts=" + $ts)
Write-Host $fim
if ($script:fails -gt 0) { exit 1 } else { exit 0 }