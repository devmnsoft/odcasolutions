# Próxima execução

Comece confirmando a CI do commit que corrigiu CA1859 e adicionou MFA/onboarding inicial. Execute restore bloqueado, build, assets, secret scan e os testes HTTP contra PostgreSQL descartável exclusivo. Use as três variáveis documentadas no README; não toque banco de desenvolvimento por fallback.

No banco, registre vazio → 005, upgrade 001/002/003 → 005, reaplicação pelo runner e SQL direto, concorrência, falha/retomada e toda a suíte. Prove os novos cenários: senha de superadmin sem MFA negada, MFA válido autorizado, TOTP reutilizado negado, recovery code de uso único, cadastro de cliente idempotente, confirmação de e-mail de uso único, plano preso à versão e home do cliente em `commercial_pending`.

Depois faça QA responsivo 360/768/1440 px das telas de login, troca de senha, MFA, contratação, confirmação e home do cliente. Em seguida avance o primeiro percurso ainda incompleto de S01: seleção explícita de organização para múltiplos vínculos, convites/equipe/perfis e proteção concorrente do último administrador. Amplie o canal público de privacidade para operação autenticada e suporte temporário auditado. Não comece S02/S03 antes das provas A × B e dessas fronteiras.

## Continuação após prompt 08

Parta da migração 006 sem editar 001–006. Primeiro execute a suíte completa em PostgreSQL 18 descartável e corrija qualquer regressão. Em seguida conclua o outbox de confirmação e convites com material secreto protegido, worker persistente e transporte configurável. Só então entregue seleção explícita de organização, equipe/perfis, quota concorrente e proteção do último administrador. Preserve como pendentes suporte/privacidade operacional, S02, S03 e OCR; não declare multi-instância enquanto tickets permanecerem em memória.
