# Auditoria e incremento de fundação ODCA Legal Med — 23/09/2026

## Checkout e ambiente

- Branch inicial: `work`; HEAD inicial: `2b2cc0390fd60de1f158ec7b9ee6ac92a0a328b9`;
  árvore de trabalho limpa.
- Não há `AGENTS.md` no checkout nem em seu diretório pai.
- Ambiente observado: Linux, Node.js `20.20.2`; sem `dotnet`, Docker ou cliente/servidor
  PostgreSQL executável. O SDK requerido pelo repositório é .NET `10.0.400` e o Node
  suportado é 24 ou superior.
- Foram lidos `README.md`, a seção 1.1 e o restante de `ODCA_SPEC.md`, documentos de
  execução/auditoria/backlog, CI, projetos, migrations 001–022 e testes afetados.

## Mapa do estado atual

### Corrigido neste incremento

1. Os testes dos releases 020 e 021 confundiam snapshot histórico com versão corrente:
   ambos exigiam `DatabaseSchema.CurrentVersion` antigo e o 021 exigia igualdade com o
   canônico já evoluído para 022. Agora releases históricos são validados por checksum e
   prefixo imutável, enquanto o snapshot calculado pela versão corrente deve ser idêntico
   ao SQL canônico.
2. O login só reconhecia tenant `active`. O responsável confirmado de um tenant
   `pending`, com assinatura ainda `commercial_pending`, ficava sem qualquer sessão.
   Agora a exceção é estreita: membership ativo, papel `tenant-administrator` do mesmo
   tenant e assinatura `commercial_pending`. Tenant suspenso/inativado e membro sem esse
   vínculo continuam inelegíveis; nenhum pagamento ou ativação é criado.
3. A ficha exigia permissão de obrigações mesmo para revisão, renovação ou operação
   documental. O acesso passou a considerar finalidade **e atribuição no contrato**;
   permissão de módulo isolada não abre a ficha. A listagem de obrigações continua
   limitada ao responsável, salvo `tenant.obligations.read_all`.

### Já existente, mas ainda parcial

- Catálogo/estúdio, snapshots de versões geradas, documentos privados, revisões,
  obrigações, renovações, caixa e agenda existem e devem ser reutilizados.
- `contracts.counterparty` é apenas texto e não equivale a cadastro de paciente.
- Não existem entidade de paciente/representação, vínculo documental ao paciente,
  envelope/signatários/evidências nem provedor real de assinatura. Os limites comerciais
  de envelopes não constituem integração de assinatura.

### Bloqueios e itens não executados

- **BLOQUEADA:** compilação e testes .NET, porque `dotnet` não está instalado.
- **BLOQUEADA:** Gate A com banco descartável/role restrita, migrations, RLS entre dois
  tenants, login HTTP e negativas reais, porque não há PostgreSQL nem Docker.
- **NÃO EXECUTADA:** implementação dependente dos blocos B–F. A ordem solicitada proíbe
  avançar esses blocos antes da evidência do Gate A; não foi criada migration de paciente
  nem integração fictícia de assinatura.
- **NÃO EXECUTADA:** QA visual e exportação PDF/DOCX real, pois os hosts não podem iniciar.

## Como concluir o Gate A

Em executor com .NET 10.0.400, Node 24+ e PostgreSQL 18, executar os quatro comandos de
verificação do `README.md` e a suíte de integração com `ODCA_TEST_ENVIRONMENT`,
`ODCA_TEST_ADMIN_CONNECTION` e `ODCA_TEST_APP_CONNECTION` apontando exclusivamente para
um banco descartável `odca_test*`. Só depois disso numerar a próxima migration (023) e
implementar paciente, geração vinculada e assinatura.

## Roteiro de homologação após o Gate A

1. Criar tenants sintéticos A e B e usuários distintos; confirmar o responsável de A
   ainda em pendência comercial e validar acesso restrito, sem direito contratado.
2. Validar que usuário suspenso, membership bloqueado e membro sem papel autorizado não
   autenticam ou perdem a sessão já emitida.
3. Em A, atribuir respectivamente obrigação, revisão e renovação a três perfis limitados;
   cada perfil abre apenas ficha relacionada. Repetir IDs no contexto B e esperar 403/404
   sem conteúdo identificável.
4. Confirmar que `read_all` amplia somente o escopo esperado e que download continua
   revalidado pela API.

## Marco

**Parcial.** Este incremento estabiliza pré-condições do Bloco A. A jornada Legal Med não
está finalizada nem homologada. Além do Gate A, dependem de decisão externa os modelos e
textos aprovados, campos mínimos por finalidade, representação, papéis de signatários e o
provedor/credenciais de assinatura para sandbox real.
