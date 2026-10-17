# D-OC34 — Prova A–N, rodada 9 (17/10/2026): falhas classificadas e cura

Rodada 9 (base `27b19b6`, stack Debug sobre `odca_test_disposable`): 25 checks em FAIL antes da
cura. Classificação completa abaixo — **nenhuma das 25 foi defeito de produto**; todas derivam de
cinco causas no script de prova / ambiente, mais uma expectativa desatualizada vs. implementação
canônica. A rodada final após a cura está em `d34-proof-an-rodada-final.txt` (mesma pasta).

## Causas independentes (as demais são cascata)

1. **B7-3 — bug de contagem no script.** O `countPBefore` era coletado *antes* da criação válida
   do B7-2; o total "depois" do CPF inválido incluiu o paciente válido recém-criado
   (antes=1 depois=2), mesmo nenhum paciente inválido ter nascido. Cura: asserção por nome
   (`full_name='Paciente Prova Invalida'` count=0) + erro renderizado na página.
2. **J0 → cascata inteira da jornada J (J1…J10, B8–B10).** Precedência do PowerShell:
   `'[' + $array -join ',' + ']'` aplica a concatenação primeiro (separador de espaço entre os
   objetos) → JSON inválido → `::jsonb` lança erro que o `Out-Null` engolia → `values` da minuta
   permanecia vazio → geração bloqueada com "O campo obrigatório 'Parte A' está pendente.".
   Nota: o alert "Há funcionalidade fora do plano atual…" visível na página é informativo e
   permanente na edição; não era a causa do bloqueio. Cura: parênteses explícitos
   `'[' + ($arr -join ',') + ']'`.
3. **X0 → X1-1/X1-2/N1 (bytes=0).** `wkhtmltopdf 0.12.6` não aceita `-o` (saída é argumento
   posicional) → PDF nunca escrito. Cura: `wkhtmltopdf --enable-local-file-access -q <in.html>
   <out.pdf>` (validado ao vivo: 37.946 bytes, ~1,1 s para 25 páginas).
4. **X2-1 — zip do DOCX com backslash.** `ZipFile.CreateFromDirectory` do .NET Framework (PS 5.1)
   cria entradas com `word\document.xml`; o parser do produto (net10) espera o nome OOXML com barra
   → `InvalidDataException` ("DOCX corrompido: documento principal ausente.") → `invalid-document`.
   Cura: montar o zip com entradas explícitas (`CreateEntry('word/document.xml')`).
5. **T1 — fixture de OCR pendurado não pendurava.** O `d34sleep.exe` anterior terminava na hora
   (ausência do `runtimeconfig.json` ao lado do apphost) → OCR "concluído" com código não-zero →
   `InvalidDataException` → `invalid-document` em vez de `extraction-timeout`. Cura: exe reconstruído
   (`Thread.Sleep(int.MaxValue)`) com dll+exe+runtimeconfig copiados juntos; o script agora
   autoverifica (`Ensure-SleepExe`: sobe o binário, espera 3 s; se já saiu, reconstrói via
   `dotnet build` do fonte embutido) e a cadeia T usa polling 170×3 s (~8,5 min > 3 tentativas ×
   timeout de 2 min + backoff de requeue).

## Expectativa desatualizada vs. implementação canônica

6. **N2.** O worker emite **`extractor-not-configured`** quando o `OcrExecutable` está ausente
   (`Odca.Worker/DocumentWorker.cs`, ramificação dedicada antes da chamada) — distinto de
   **`extractor-failed`**, reservado a executável presente que falhou. A prova assercionava o código
   genérico; a implementação (D-OC34) é mais honesta (configurado ≠ ausente). Cura: prova passa a
   asserir `extractor-not-configured`; a doc D-OC34 (seção F2) lista os quatro códigos estáveis:
   `extraction-timeout`, `extractor-not-configured`, `extractor-failed`, `pdf-no-text-layer`
   (+ `invalid-document` por entrada não suportada), centralizados em `MarkExtractionFailed`.

## Repetibilidade adicionada à prova

- **Purging determinístico pré-execução (check P0b):** o script agora zera, na base descartável,
  as linhas de negócio do tenant da prova (contratos, minutas, versões geradas, documentos,
  extrações, revisões, assinaturas, obrigações, solicitações, outbox, privacidade, pacientes) na
  ordem de dependência de FKs canônica (mesma ordem do wipe
  `DevelopmentAccessProvisioningTests.ResetScenarioAsync`), e devolve os templates de prova ao
  estado do seed (`published`/sem motivo de arquivamento; sem perfil exigido). O
  `ODCA_SETUP_ON_START=true` recria os fixtures ancorados no boot seguinte — cada rodada parte do
  estado do setup, independente de resíduos de rodadas anteriores.

## Como reproduzir

```
# 1) gate (binários Debug presentes)
dotnet build Odca.sln -c Debug

# 2) prova A-N (sobe a stack http pura :5143/:5144, worker ativo, base odca_test_disposable)
powershell -NoProfile -ExecutionPolicy Bypass -File scripts/qa/validate-d34-an-web.ps1
# fim: d34-proof-fim.txt com "PROVA-FIM fails=0"

# 3) capturas visuais (100 PNGs; exige a prova anterior finalizada)
powershell -NoProfile -ExecutionPolicy Bypass -File d34-captures.ps1   # driver d31-cdp-v2.mjs (msedge headless)
```

Pré-requisitos externos (máquina de desenvolvimento, fora do repositório): PostgreSQL 18 local,
`wkhtmltopdf 0.12.6` (poppler/Tesseract conforme D-OC33), Node 24 + Edge headless para capturas.
Credenciais sintéticas do seed de desenvolvimento (mesmas dos demais `scripts/qa/validate-*.ps1`).
