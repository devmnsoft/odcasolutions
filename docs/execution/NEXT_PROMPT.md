# Próxima execução

Validar e fechar a integração S03 sem presumir aprovação: instalar/usar o SDK 10.0.400 e PostgreSQL 18 descartável; executar restore bloqueado, build Release, testes e v013 duas vezes; cobrir HTTP/RLS/concorrência/worker. Depois conectar o formulário BFF e a ficha do contrato aos endpoints já implementados, executar a jornada autenticada e produzir capturas reais em 360, 768, 1280 e 1440 px e zoom 200%. Não usar `development-runtime.json` como infraestrutura de teste nem tratar intenção de renovação como extensão de vigência.


## Continuação recomendada após 14/09/2026

Parta das correções já implementadas nos agregados e no cliente HTTP. Com SDK .NET 10.0.400 disponível, execute restore bloqueado, build Release e toda a solução de testes antes de ampliar a central. Em seguida, crie uma projeção de leitura autorizada (não uma tabela duplicada) que una revisões abertas, obrigações e contexto de renovação, com paginação/contagens idênticas, seletores por nome e links de retorno que preservem filtros. Conecte ficha e ações do contrato, teste concorrência/RLS com PostgreSQL descartável e só então faça capturas reais em 360, 768, 1280 e 1440 px.

Antes da ampliação, cubra em PostgreSQL o cumprimento seguido de reabertura: o evento deve continuar contendo data efetiva, observação e evidência, enquanto os campos correntes de cumprimento voltam a nulo sem violar o `CHECK`. Prove também a rejeição HTTP de `from > to` e os limites inclusivos nas duas pontas.
