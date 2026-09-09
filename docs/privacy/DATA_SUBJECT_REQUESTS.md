# Solicitações de titulares

O canal público inicial de S01 está disponível em `/privacidade` e `POST /api/v1/privacy/requests`, independente de autenticação, plano ou situação financeira. Ele registra somente e-mail normalizado, tipo, relato opcional, protocolo opaco e o evento inicial. A operação autenticada do atendimento e a alternativa externa configurada ainda são pendentes.

Estados: Recebida → Verificação de identidade → Triagem → Em análise → Em execução → Respondida → Concluída. Resultado parcial ou indeferido exige justificativa e revisão interna.

Regras mínimas:

- devolver inicialmente apenas protocolo opaco e mensagem genérica, sem confirmar vínculo contratual;
- verificar identidade proporcionalmente ao risco e não pedir documento completo por padrão;
- registrar controlador responsável, escopo, marco inicial, regra/fonte versionada e responsável;
- não reiniciar silenciosamente o prazo em encaminhamentos;
- revisar exportações para dados de terceiros, criptografar, autenticar, expirar e auditar;
- correção cadastral não reescreve contrato assinado.

O retorno público é neutro e não permite consultar o protocolo. Isso evita confirmar vínculo ou existência de dados. Rate limit existe nas bordas Web e API; verificação de identidade, prazo, encaminhamento, decisão e exportação não foram implementados e não devem ser inferidos do recebimento.

Prazos serão parametrizados após validação da regra vigente; “15 dias” não será aplicado indistintamente.
