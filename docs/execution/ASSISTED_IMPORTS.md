# Importação assistida de contratos

## Escopo e princípio de revisão

A central acompanha uma versão documental já recebida pela biblioteca segura do contrato. PDF, PNG, JPEG e DOCX operacional são os únicos formatos anunciados. Extensão e assinatura devem coincidir; imagens são limitadas a 12.000 pixels por dimensão e cada arquivo ao limite efetivo do plano (com teto persistido de 25 MiB). O original permanece no armazenamento privado e só é pré-visualizado depois do estado `safe`.

O processamento produz **sugestões**, não cadastros. Aceitar, editar ou rejeitar cada sugestão continua sendo uma decisão do usuário. Quando não há extração, o mesmo painel permite conferir título, identificação, vigência, valor e moeda manualmente; a falta de OCR não bloqueia um documento já aprovado pelo antivírus. A confirmação bloqueia sugestões pendentes quando existirem e revalida a versão da revisão, o tenant, as permissões, o estado do antivírus e o término da extração, quando solicitada, dentro da mesma transação PostgreSQL. Repetir a confirmação devolve o contrato já associado.

## Dependências locais

Configure caminhos absolutos em `Documents`:

```json
{
  "Documents": {
    "StoragePath": "./private-documents",
    "MalwareScannerExecutable": "/usr/bin/clamscan",
    "PdfTextExecutable": "/usr/bin/pdftotext",
    "OcrExecutable": "/usr/bin/tesseract"
  }
}
```

* ClamAV é obrigatório para liberar o original. Scanner ausente resulta em `scan_failed`; o sistema nunca presume que o arquivo é seguro.
* Poppler (`pdftotext`) trata PDF com texto nativo com limite de 200 páginas e dois minutos por execução.
* Tesseract com dados `por` trata PNG/JPEG com limite de dois minutos. A ausência do executável fica registrada como diagnóstico seguro, sem texto fictício.
* Os executáveis são iniciados diretamente com `ProcessStartInfo.ArgumentList`; nomes de arquivo não são concatenados em shell.

PDF protegido ou corrompido é recusado pelo extrator e fica disponível para diagnóstico/revisão manual, nunca para tentativa de contornar senha. Para PDF digitalizado ou misto, a infraestrutura ainda precisa de renderização de páginas (`pdftoppm`) e OCR seletivo por página antes de poder declarar suporte integral; esta pendência não é mascarada por resultados inventados.

## Estados e recuperação

`received`, `security_review`, `queued`, `processing`, `awaiting_review`, `confirmed`, `failed` e `cancelled` são estados da importação, separados dos dados do contrato. Jobs usam `FOR UPDATE SKIP LOCKED`, lease com expiração, três tentativas e backoff. Após reinício, lease expirado pode ser retomado. Reprocessamento mantém sugestões revisadas anteriores no histórico; não altera uma decisão confirmada silenciosamente.

## Operação local

1. Execute a migration 019 com o Bootstrap habitual.
2. Inicie API, Web e Worker com `scripts/run-local.ps1`.
3. Envie o original pela biblioteca documental e solicite a extração.
4. Registre a versão em `POST /api/v1/organizations/{tenantId}/contract-imports`.
5. Abra `/organizacoes/{tenantId}/importacoes`, confira os dados manualmente, revise eventuais sugestões e confirme somente depois de resolver pendências.

Salvar a conferência usa versão otimista da importação e do contrato. Uma aba desatualizada recebe conflito em vez de sobrescrever a edição mais recente. Confirmar e cancelar bloqueiam a mesma linha de importação; somente um estado terminal é persistido. Depois da confirmação, a interface abre a ficha canônica, de onde revisão de consultoria, obrigação e histórico continuam nos fluxos existentes.

O conteúdo integral e os trechos extraídos não devem ser registrados em logs. Diagnósticos persistidos usam códigos seguros. O recurso adiciona controles técnicos e trilha de auditoria, mas não constitui, isoladamente, declaração de conformidade integral com a LGPD.
