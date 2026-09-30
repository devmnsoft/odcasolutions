# Script de inicialização dos serviços ODCA Solutions para homologação local
param(
    [switch]$Background
)

$repoRoot = "C:\MNSOFT\odcasolutions"

Write-Host "==========================================================" -ForegroundColor Cyan
Write-Host "   ODCA Solutions / ODCA Legal Med - Homologação Local    " -ForegroundColor Cyan
Write-Host "==========================================================" -ForegroundColor Cyan

# 1. Iniciar API
Write-Host "`n[1/2] Iniciando Odca.Api na porta 5143..." -ForegroundColor Yellow
if ($Background) {
    Start-Process dotnet -ArgumentList "run --project src/Odca.Api --urls http://127.0.0.1:5143" -WorkingDirectory $repoRoot
} else {
    Start-Process powershell -ArgumentList "-NoExit -Command cd $repoRoot; dotnet run --project src/Odca.Api --urls http://127.0.0.1:5143"
}

# 2. Iniciar Web
Write-Host "[2/2] Iniciando Odca.Web na porta 5144..." -ForegroundColor Yellow
if ($Background) {
    Start-Process dotnet -ArgumentList "run --project src/Odca.Web --urls http://127.0.0.1:5144" -WorkingDirectory $repoRoot
} else {
    Start-Process powershell -ArgumentList "-NoExit -Command cd $repoRoot; dotnet run --project src/Odca.Web --urls http://127.0.0.1:5144"
}

Write-Host "`nServiços iniciados com sucesso!" -ForegroundColor Green
Write-Host "`nAcesse pelo navegador em: http://127.0.0.1:5144/entrar" -ForegroundColor White
Write-Host "`nCredenciais de homologação:" -ForegroundColor Cyan
Write-Host "  • Superadministrador: admin@odca.local          | Senha: V7!qM2#rL9@xT4`$p"
Write-Host "  • Operador da organização: operador@odca.local   | Senha: 32R*Aivt-+G@YkVuR_`$c"
Write-Host "  • Cliente de demonstração: cliente.teste@odca.local | Senha: K8@wR3!nF6#zP2`$m"
Write-Host "==========================================================" -ForegroundColor Cyan
