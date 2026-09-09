# Modelo de ameaças — fundação S00

## Ativos e fronteiras

Ativos: credenciais derivadas, sessões, dados de identidade, vínculo tenant, auditoria, configuração e inventário Privacy. Fronteiras: navegador → Web/BFF; Web → API; API/Bootstrap → PostgreSQL; operador → arquivos locais de segredo.

| Ameaça | Controle S00 | Risco remanescente / próxima ação |
|---|---|---|
| Roubo de senha | hash ASP.NET Identity, bloqueio progressivo, resposta genérica | MFA obrigatório de produção entra operacionalmente em S01 |
| Roubo/replay de token | JWT 15 min, cookie HttpOnly/Secure, sessão/jti persistidos, logout e security version | adicionar refresh rotativo quando necessário |
| CSRF no BFF | antiforgery automático em POST e SameSite | manter testes por novo endpoint Web |
| Elevação de privilégio | policy server-side e role DB mínima | testes de roles/tenant A × B em S01 |
| Vazamento entre tenants | tenant vindo da identidade e RLS default-deny sem contexto | completar contexto transacional e pool negativo em S01 |
| Vazamento em logs/erros | ProblemDetails sem stack/conteúdo; sem logging de body/token; traceId | teste automatizado de captura/redaction em S01 |
| Manipulação de migração | SHA-256 do bloco, registro imutável e advisory lock | backup/restore real antes de produção |
| Segredo no Git | geração local, `.gitignore`, Gitleaks CI | gerenciador de segredos de produção ainda não selecionado |
| Abuso do superadmin | dashboard só com metadados mínimos | sessão de suporte MFA, escopo e auditoria em S01 |
| Dependência vulnerável | versões fixas, lock files e Dependabot | triagem humana obrigatória antes de atualizar |

Logs não devem conter senha, JWT, contrato, conteúdo OCR, CPF completo ou payload sensível. Uma flag de criptografia ou inativação não será rotulada como conformidade LGPD.
