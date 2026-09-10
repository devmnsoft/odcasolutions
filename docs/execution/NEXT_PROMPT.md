# Próxima execução

Antes do avanço funcional, use um ambiente com SDK .NET 10.0.400 para executar `dotnet restore Odca.sln --force-evaluate`, revisar somente as alterações geradas nos lockfiles e confirmar `dotnet restore Odca.sln --locked-mode`. Em seguida execute build e testes. Não edite hashes manualmente.

Inicie API/Web com PostgreSQL 18 descartável e valide o novo contrato de tipo de conta nos três estados: cliente, superadministrador antes do MFA e superadministrador após MFA. Faça QA por teclado e capturas em 360/768/1440; confirme contenção/retorno de foco do drawer, `Escape`, mostrar/ocultar senha e prevenção de duplo envio.

Depois conclua o percurso persistente ainda ausente: seleção explícita de organização, edição com concorrência otimista, equipe/perfis, convites processados pelo outbox, assentos sob concorrência e último administrador. Não adicione links a essas áreas antes de suas rotas e políticas existirem.

Execute primeiro `dotnet run --project src/Odca.Bootstrap -- provision-test-access --environment Development` contra a configuração local autorizada. Registre separadamente as linhas de persistência, conferência de senha, login HTTP e MFA, sem copiar senhas para documentação. Em seguida execute `show-login`, faça a troca inicial e conclua o desafio com autenticador em `https://localhost:7144`. Não use `--rotate-passwords` salvo decisão explícita do operador.

Comece confirmando a CI do commit que corrigiu CA1859 e adicionou MFA/onboarding inicial. Execute restore bloqueado, build, assets, secret scan e os testes HTTP contra PostgreSQL descartável exclusivo. Use as três variáveis documentadas no README; não toque banco de desenvolvimento por fallback.

No banco, registre vazio → 005, upgrade 001/002/003 → 005, reaplicação pelo runner e SQL direto, concorrência, falha/retomada e toda a suíte. Prove os novos cenários: senha de superadmin sem MFA negada, MFA válido autorizado, TOTP reutilizado negado, recovery code de uso único, cadastro de cliente idempotente, confirmação de e-mail de uso único, plano preso à versão e home do cliente em `commercial_pending`.

Depois faça QA responsivo 360/768/1440 px das telas de login, troca de senha, MFA, contratação, confirmação e home do cliente. Em seguida avance o primeiro percurso ainda incompleto de S01: seleção explícita de organização para múltiplos vínculos, convites/equipe/perfis e proteção concorrente do último administrador. Amplie o canal público de privacidade para operação autenticada e suporte temporário auditado. Não comece S02/S03 antes das provas A × B e dessas fronteiras.

## Continuação após prompt 08

Parta da migração 006 sem editar 001–006. Primeiro execute a suíte completa em PostgreSQL 18 descartável e corrija qualquer regressão. Em seguida conclua o outbox de confirmação e convites com material secreto protegido, worker persistente e transporte configurável. Só então entregue seleção explícita de organização, equipe/perfis, quota concorrente e proteção do último administrador. Preserve como pendentes suporte/privacidade operacional, S02, S03 e OCR; não declare multi-instância enquanto tickets permanecerem em memória.
