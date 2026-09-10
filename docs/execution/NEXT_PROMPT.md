# Próxima execução

Use o SDK 10.0.400 de `global.json`. Execute `dotnet restore Odca.sln --force-evaluate`, revise apenas os lockfiles produzidos e confirme `dotnet restore Odca.sln --locked-mode`; depois rode build e testes. A máquina da evolução 11 não possuía `dotnet` e o download oficial retornou HTTP 403, portanto nenhum lock foi alterado à mão.

Em PostgreSQL 18 descartável, prove migração vazia/upgrade/reaplicação e os cenários concorrentes de assentos, token único e último administrador. Complete bloqueio/inativação/restauração, reenvio/cancelamento e transporte configurável de produção. Verifique RLS sem contexto, tenant A × B e pool reutilizado.

Execute API, Web e Worker com contas sintéticas: escolha entre duas organizações, crie perfil, envie/aceite convite e confirme renovação da sessão. Faça QA por teclado e capturas 360/768/1440. Depois prossiga com suporte temporário/privacidade operacional e somente então S02/S03 na sequência do roadmap.
