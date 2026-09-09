# Próxima execução

Conclua a validação pendente descrita em `STATUS.md` e `BACKLOG.md`: configure uma credencial autorizada para o PostgreSQL 18 nativo, execute upgrade `001 → 002`, reaplicação, fluxo integrado API/login/dashboard/planos/logout e QA visual. Migrações históricas são imutáveis; toda correção usa a próxima versão livre, nunca recalcula checksum aplicado. Depois avance S01 por cadastro transacional de organização, seleção de tenant, equipe/perfis e ODCA-PRIV-002/003/005/010; S02/S03 permanecem posteriores a esses controles.
