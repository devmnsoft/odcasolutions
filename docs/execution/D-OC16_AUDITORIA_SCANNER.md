# D-OC16 — Auditoria do Auto-Approve do Scanner (Bloco A, item 4)

Data: 2026-10-12 · Branch: codex/s00-foundation

## Problema identificado na auditoria

O `DocumentWorker.ScanOne` tinha dois caminhos que produziam **evidência falsa de segurança**:

1. Scanner não configurado em Development → `security_status='safe'`, engine `auto-approved-dev`.
2. Scanner configurado mas **execução falhou** (binário ausente, timeout, crash) em Development → também `safe`, engine `auto-approved-dev`.

No caminho 2, a indisponibilidade era indistinguível de uma verificação simulada deliberada — violando o critério "Indisponibilidade não deve produzir evidência falsa de segurança". No ambiente local (Windows, sem ClamAV), TODOS os documentos saíam pelo caminho 2.

## Regra canônica adotada (4 estados distintos)

| Estado | security_status | security_engine | Quando |
|--------|-----------------|-----------------|--------|
| **Arquivo verificado** | `safe` | nome do binário real (ex.: `clamscan`) | Scanner executou e retornou 0 |
| **Ameaça detectada** | `rejected` | nome do binário real | Scanner executou e retornou ≠ 0 |
| **Verificação indisponível** | `scan_failed` | `scanner-not-configured` ou `scanner-unavailable` | Sem binário, ou execução falhou — **em qualquer ambiente, nunca vira `safe`** |
| **Resultado simulado em teste** | `safe` | `simulated-scanner-dev` | Somente com simulação explícita habilitada |

## Simulação: identificação + proteção contra ativação acidental

- Nova configuração `Documents:SimulateScanner` (bool, default `true`).
- A simulação só se ativa quando **AMBOS**:
  - `ASPNETCORE_ENVIRONMENT == "Development"` (guarda dura em código — em Produção a flag é ignorada);
  - `Documents:SimulateScanner != false`.
- Habilitada explicitamente em `src/Odca.Worker/appsettings.Development.json` (`"SimulateScanner": true`) — mantém a jornada local de formalização funcionando sem ClamAV.
- Cada simulação grava `security_engine = 'simulated-scanner-dev'` e emite warning log com o id da versão.
- **Identificação visível ao usuário**: a ficha do contrato agora expõe o engine; documentos verificados por simulação exibem o selo "Verificação simulada (ambiente de teste)" junto do status de segurança (chave `sheet.doc_simulated`, 4 idiomas).
- Consequência operacional: documento simulado continua servível para a jornada (status `safe`), mas qualquer auditor visual pode distinguir "verificado de verdade" de "resultado de teste". Documento `scan_failed` segue em quarentena (sem download, sem formalização).

## Verificação (executada com o worker real, 2026-10-12)

Linha fixture `D-OC16 Verif A` (document_versions id `256e45e0…`, tenant de teste), worker executado via `dotnet exec Odca.Worker.dll` com runtime local apontando para `odca_test_disposable`:

| Fase | Configuração | Resultado observado | Esperado | Status |
|------|--------------|--------------------|----------|--------|
| 1a | Sem binário configurado, `SimulateScanner=false` | `scan_failed` / `scanner-not-configured` | nunca `safe` | **PASS** |
| 1b | Binário ausente configurado (`C:\definitely-missing\clamscan.exe`), `SimulateScanner=false` | `scan_failed` / `scanner-unavailable` | nunca `safe` (antes: `safe` fake em dev) | **PASS** |
| 2 | `SimulateScanner=true`, mesmo binário ausente | `safe` / `simulated-scanner-dev` + warning log com o id da versão | simulação explícita e identificável | **PASS** |

Observações:
- `claim_document_scan()` só reivindica `pending`/`scan_failed`; o fixture precisou ser reinserido como `pending` entre fases (re-scan idempotente verificado).
- A guarda dura contra ativação em produção é o check `ASPNETCORE_ENVIRONMENT == "Development"` no código; a fase 1b comprova a mesma branch de decisão com flag desligada (indisponibilidade não simula).
- Cenários de scan real (exit 0 → `safe`; exit ≠ 0 → `rejected`) exigem binário ClamAV instalado — fora do alcance desta verificação local; a regra é determinística no código.
- Os fixtures `D-OC16 Verif A/B` permanecem na base de teste com marca identificável.
