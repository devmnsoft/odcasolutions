# Primeiro início local no Windows

Os perfis de desenvolvimento da API, Web e Worker habilitam `ODCA_SETUP_ON_START=true`. Se `src/Odca.Api/development-runtime.json` não existir, o primeiro processo abre o PowerShell com `scripts/setup-local.ps1`. Informe a senha local do PostgreSQL no assistente. Os demais processos aguardam a configuração por até dez minutos; após esse prazo, podem ser reiniciados.

O script cria a configuração e uma chave JWT aleatória. Ele não executa migrations nem altera usuários ou senhas no banco. Arquivos existentes são preservados, inclusive arquivos inválidos: estes exigem reparo explícito. O arquivo pessoal continua fora do Git.

O assistente não é executado em Testing/Production, Linux, processos não interativos, ambientes com CI definido ou quando ODCA_RUNTIME_CONFIG está definido. Um caminho explícito continua sendo responsabilidade de quem o configurou. Para desabilitar o assistente, remova ODCA_SETUP_ON_START do perfil ou defina false.

Se a janela for cancelada ou o script falhar, execute na raiz do repositório:

```powershell
.\scripts\setup-local.ps1
```

Validação manual necessária no Windows: parar todos os hosts; usar um checkout de teste sem arquivo pessoal; iniciar API e Worker juntos; confirmar que somente um assistente solicita a senha; concluir; verificar ambos os hosts; reiniciar e confirmar que não há novo assistente nem rotação da chave. Testar também cancelamento e arquivo existente inválido. Não apagar a configuração pessoal em uso para fazer esse teste.

Esta mudança prepara a configuração. A disponibilidade do PostgreSQL, migrations e serviços externos deve ser verificada separadamente.
