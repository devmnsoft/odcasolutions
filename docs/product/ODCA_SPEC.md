# ODCA Solutions — conceito, regras e prompt mestre para o Codex

Versão 2.0 — LGPD integrada • 8 de setembro de 2026 • Repositório: https://github.com/devmnsoft/odcasolutions

## Como executar este prompt completo

Anexe este arquivo ao Codex no repositório ODCA Solutions. Instrua: “Leia este arquivo integralmente e execute a seção 14, usando as seções 1–13 como requisitos obrigatórios. Comece pela primeira etapa incompleta e implemente, teste e documente. As regras de privacidade da seção 10.1–10.10 fazem parte dos critérios de saída de cada etapa.”

Esta versão substitui a versão 1.0 do prompt. Mantém o escopo completo de contratos, editor, OCR, planos, assinaturas e rastreabilidade e reforça privacidade com fluxos implementáveis. Os controles abaixo são requisitos de engenharia propostos; a adequação jurídica depende também das finalidades reais, contratos, fornecedores e operação. Não rotular o produto “100% em conformidade” apenas por concluir código.

## 1. Situação verificada e finalidade deste documento

O GitHub confirmou que o repositório está acessível, é público e está vazio: tamanho zero e resposta “This repository is empty” na consulta de conteúdo. Não existe código analisado ou implementação validada nesta entrega. Este documento especifica o produto e contém instruções executáveis por etapas para o Codex construir o sistema no repositório. Não incluir credenciais, contratos reais, CPFs ou dados pessoais no repositório público.

O nome comercial é **ODCA Solutions**. Proposta: uma plataforma corporativa para criar, revisar, aprovar, assinar, acompanhar e comprovar o histórico dos contratos, com operação multiempresa e uma central de consultoria.

O escopo é o ciclo de vida completo do contrato, não apenas armazenamento de PDFs. O diferencial proposto combina edição orientada por dados, OCR com origem verificável, obrigações acompanhadas e cópias rastreáveis. É uma direção de produto; não uma alegação de ineditismo comprovado no mercado.

## 2. Conceitos que não podem se confundir

| Conceito | Significado |
|---|---|
| Plataforma | A operação SaaS da ODCA Solutions, seus planos, cobranças e suporte. |
| Cliente contratante / tenant | Pessoa física ou jurídica que assina o SaaS; seus dados ficam isolados. |
| Usuário | Pessoa identificada individualmente que acessa uma ou mais organizações mediante vínculo autorizado. |
| Contraparte | Cliente, fornecedor, parceiro ou pessoa que figura no contrato do tenant. Não ganha login automaticamente. |
| Signatário | Pessoa que assina um envelope, podendo ser externa ao SaaS. |
| Modelo | Documento reutilizável com cláusulas e campos estruturados, publicado em versões. |
| Contrato | Registro com partes, versões, documentos, aprovações, assinaturas, datas e obrigações. |
| Documento | Arquivo original ou representação derivada; uma planilha anexada não vira automaticamente um contrato. |
| Envelope | Uma solicitação de assinatura de uma versão congelada, com seus signatários e evidências. |
| Emissão de cópia | Geração de um documento para download/impressão, com série própria. |

Cadastro do contratante por CPF ou CNPJ, acompanhado de responsável e e-mail verificado. CNPJ identifica a organização, não uma pessoa capaz de autenticar sozinha. Login: e-mail ou CPF do usuário + senha; quando houver múltiplas organizações, seleção após autenticação. A opção “entrar com CNPJ” identifica a organização e depois exige as credenciais pessoais do usuário. Nunca compartilhar senha entre funcionários.

## 3. Planos comerciais propostos

Os limites abaixo são uma proposta inicial de produto, configurável no banco. Preços devem ser definidos após medir armazenamento, tráfego, processamento, assinatura e suporte. Não publicar preços inventados como aprovados.

| Recurso | Basic | Intermediário | Enterprise |
|---|---:|---:|---:|
| Usuários ativos incluídos | 3 | 10 | 30 |
| Espaço total por organização | 10 GB | 100 GB | 500 GB |
| Teto inicial configurável por usuário | 5 GB | 25 GB | 100 GB |
| Tamanho máximo por arquivo | 25 MB | 100 MB | 250 MB |
| Páginas de OCR por ciclo mensal | 300 | 3.000 | 15.000 |
| Envelopes de assinatura por ciclo | 10 | 50 | 200 |
| Modelos e editor estruturado | Sim | Sim | Sim |
| Controle de versões e cópias rastreáveis | Sim | Sim | Sim |
| Alertas internos e por e-mail | Sim | Sim | Sim |
| WhatsApp, mediante configuração e franquia | Adicional | 200 notificações/mês | 1.000 notificações/mês |
| Aprovação | Etapa única | Múltiplas etapas | Regras por valor, tipo e unidade |
| Comparação de versões e biblioteca de cláusulas | Essencial | Completa | Completa + políticas por unidade |
| Relatórios | Essenciais | Gerenciais | Gerenciais + exportações agendadas |
| API de integração para o cliente | Não | Adicional | Sim |
| SSO corporativo e unidades organizacionais | Não | Adicional | Sim |
| Segurança, isolamento e controles de privacidade | Incluídos | Incluídos | Incluídos |

GB e MB comerciais significam 10^9 e 10^6 bytes. Computar internamente em bytes. O teto individual é um limitador dentro do espaço total, não espaço adicional. O administrador redistribui tetos, respeitando a organização. Documentos pertencem ao tenant mesmo após a inativação do usuário que os enviou.

Adicionais: pacotes de 25/100/500 GB, 20/100/500 envelopes, OCR por páginas, notificações por unidade e assentos adicionais. Espaço adicional é capacidade recorrente com vigência definida; não um consumível que desaparece por upload. Créditos de assinatura são consumíveis. Franquias mensais não acumulam por padrão; créditos comprados seguem validade explícita na oferta, sem expiração retroativa.

Versões, originais, anexos e derivados persistentes contam no uso apresentado. Backups operacionais não contam na cota comercial. Arquivos apenas inativados continuam ocupando e contando espaço enquanto retidos. Exibir isso antes da inativação. Redução de plano não apaga arquivos: se exceder o novo limite, bloquear novas gravações de arquivos e oferecer ajuste, preservando consulta e exportação autorizadas.

Pagamento: pedido → pendente → confirmado pelo provedor ou conciliação manual autorizada → direito concedido uma única vez. Nunca aceitar a página de retorno do checkout como prova de pagamento. Estornos e chargebacks geram eventos compensatórios e revisão de direitos, sem apagar o histórico. Não misturar carteira financeira, capacidade e saldo de envelopes numa única coluna.

## 4. Perfis e administração

Perfis iniciais: SuperAdministrador, AdministradorCliente, GestorContratos, Editor, RevisorJurídico, Aprovador, Financeiro e Leitor/Auditor. Convites externos de revisão/assinatura têm escopo, validade e identidade próprios. O administrador do cliente pode criar perfis personalizados apenas dentro das permissões delegáveis e contratadas; nunca conceder poderes de plataforma.

O SuperAdministrador inicial administra clientes, usuários, perfis, planos, modelos, faturas, créditos, bloqueios, restaurações e dashboards globais. Pode acessar conteúdo de clientes por sessão temporária de suporte autorizada, com organização, escopo, motivo, expiração, operador real e ações auditadas. O dashboard global apresenta metadados operacionais mínimos; abrir contratos exige essa sessão. A autorização pode seguir contrato de suporte e concessão do cliente, com exceção emergencial justificada e revisada conforme política. Acesso excepcional exige autenticação reforçada. MFA obrigatório para esse perfil em produção. Não permitir apagar auditoria, alterar documento já assinado ou remover o último administrador ativo por uma ação comum.

Bloqueios diferentes: usuário bloqueado; tenant suspenso; upload bloqueado por cota; contrato restrito; tratamento bloqueado por privacidade. Registrar motivo, autor, início e eventual término. Inadimplência pode suspender edição/envio após carência configurável, mantendo acesso financeiro e exportação conforme política contratual. Suspensão comercial não cancela silenciosamente envelopes já enviados nem elimina dados.

Excluir na operação normal significa inativar: `is_deleted`, `deleted_at` em UTC, `deleted_by`, `deletion_reason`. Atualização e criação registram autores e datas. Restauração exige permissão e trata conflitos de unicidade. Inativação de usuário revoga sessões e não modifica a autoria histórica.

## 5. Fluxos completos

### 5.1 Contratação do SaaS

Escolher plano → cadastrar CPF/CNPJ e responsável → verificar e-mail → registrar aceite dos termos, disponibilização/ciência do aviso de privacidade e consentimentos opcionais separados → criar tenant e administrador de forma idempotente → checkout → ativar assinatura após confirmação → onboarding → convidar usuários e distribuir cotas. Cadastro pendente expira segundo política; reenvio de confirmação tem limitação de frequência. CPF/CNPJ deve ser normalizado como texto; validar também o formato alfanumérico do CNPJ conforme especificação vigente da Receita Federal, sem remover letras por uma limpeza só numérica.

### 5.2 Upload e extração

Selecionar arquivo e tipo desejado → conferir permissão/cota → reservar bytes atomicamente → enviar para quarentena privada → validar conteúdo real e malware → preservar original com hash → enfileirar processamento → extrair texto nativo quando existente, OCR apenas onde necessário → mostrar original à esquerda e dados extraídos à direita → usuário revisa → confirmar cadastro/vínculo das contrapartes → salvar versão de trabalho.

Extração produz candidatos, nunca fatos silenciosamente aceitos: nome/razão social, CPF/CNPJ, endereço, representantes, contatos, objeto, valor, moeda, início, término, renovação automática, antecedência de denúncia, reajuste, obrigações e signatários. Cada campo registra arquivo/versão, página ou planilha/célula, trecho, confiança quando suportada e decisão humana. Não inventar confiança numérica para motores que não a fornecem.

Cadastro assistido de contraparte: procurar correspondência no mesmo tenant por documento normalizado; mostrar diferenças; escolher existente, atualizar campos selecionados ou criar novo. Se só houver nome semelhante, sugerir sem fundir automaticamente. Registrar proveniência. Não criar contas, mandar convites ou mensagens para pessoas encontradas no OCR sem confirmação do fluxo autorizado.

### 5.3 Modelo e preenchimento inteligente

Consultoria cria modelo → define tipo e versão → marca campos semânticos → define obrigatoriedade, tipo, fonte permitida e cláusulas protegidas → pré-visualiza → publica globalmente ou apenas para clientes selecionados → cliente cria contrato a partir da versão publicada.

Campos pendentes aparecem em amarelo suave, com ícone e texto acessível. Selecioná-los abre cartões/tags de sugestões: “Minha empresa”, “Contraparte selecionada”, “Representante”, “Dados extraídos”, “Digitar outro valor”. Cada sugestão mostra origem e permite visualizar antes de aplicar. Campo tipado valida datas, valores, documentos e seleção de parte. Repetições da mesma variável se atualizam juntas, com confirmação para substituir conteúdo já confirmado. Um realce amarelo importado não basta para criar automaticamente uma variável: o autor confirma a marcação.

A instância guarda um snapshot do cadastro e do modelo. Mudanças futuras no cadastro ou no modelo não alteram contratos existentes. A atualização exige ação explícita com comparação. Campos obrigatórios pendentes impedem finalizar para assinatura; rascunhos podem ser exportados com identificação apropriada.

### 5.4 Revisão, aprovação e assinatura

Estados separados para contrato e processamento do arquivo. Contrato: Rascunho → EmRevisão → EmAprovação → Aprovado → EmAssinatura → Assinado. Vigência usa datas e eventos próprios: futuro, vigente, vencido, encerrado; assinatura não implica vigência imediata. Rejeição devolve à revisão com motivo. Envio falho, cancelado ou expirado não equivale a assinado.

Aprovação referencia versão/hash. Alteração de conteúdo invalida aprovação anterior e obriga nova revisão. Envio para assinatura congela o conteúdo. Mudança posterior cria nova versão/envelope ou aditivo; nunca altera bytes assinados. Suportar ordem de signatários, autenticação exigida, recusas, prazo e lembretes.

Propor inicialmente Clicksign ou outro provedor brasileiro a validar por API, sandbox e custo. Implementar um provedor real por vez atrás de interface. “Assinatura digital” não é sinônimo de imagem de assinatura: registrar modalidade e evidências retornadas; validar exigências do caso antes de prometer equivalência jurídica. A Lei 14.063 diferencia categorias, mas não substitui a análise da norma aplicável a cada contrato. [Fonte oficial](https://www.planalto.gov.br/ccivil_03/_ato2019-2022/2020/lei/l14063.htm).

Uma unidade comercial = um envelope aceito pelo provedor, com limite de signatários explícito na oferta. Reservar antes do envio, confirmar consumo após aceitação, liberar se comprovadamente não criado. Em timeout ambíguo, reconciliar antes de reenviar ou devolver crédito. Reenvio do mesmo envelope não debita novamente. Cancelamento após aceitação segue a política comercial/provedor, com estorno compensatório quando aplicável.

### 5.5 Vencimento e obrigações

Alerta inicial três meses de calendário antes do término, calculado com regra explícita para fim de mês; não assumir que três meses são sempre 90 dias. Lembretes adicionais configuráveis em 60/30/15/7/1 dias. Alertar também antes do prazo de denúncia de renovação automática, mesmo se essa data anteceder os três meses do vencimento. Campos desconhecidos permanecem “a confirmar”; IA/OCR não inventa datas.

Alertas internos por padrão. E-mail e WhatsApp exigem contatos verificados, preferências, configuração do provedor e requisitos vigentes de consentimento/templates. Enviar resumo mínimo com link autenticado, sem expor o contrato ou CPF na mensagem. Separar alertas do gestor contratual dos signatários externos. Atualização da data cancela/reagenda alertas antigos. Deduplicar por tenant, contrato, versão da agenda, evento, destinatário e canal. Registrar tentativa, aceite do provedor, entrega e falha separadamente.

Além de vencer: acompanhar reajustes, parcelas, entregas, comprovações, garantias, SLA e obrigações. Cada obrigação tem responsável, prazo, evidência e situação. Renovação gera evento e versão/aditivo vinculados ao contrato anterior.

### 5.6 Cópias rastreáveis

Cada solicitação de emissão cria série opaca imprevisível, associada a tenant, contrato, versão, usuário, instante, finalidade e hash do PDF final. Cada página recebe rodapé/selo com série, página X/Y e QR Code/token de verificação. Interpretamos “na bola de cada página” como selo discreto no rodapé, sem prejudicar o conteúdo.

Na consulta autenticada, cliente vê apenas suas emissões; superadministrador vê conforme acesso auditado. Consulta pública opcional mostra apenas validade e metadados mínimos autorizados, sem nome do cliente, CPF ou usuário por padrão; limitar tentativas. O QR identifica o registro da emissão, mas não prova sozinho que o conteúdo de um papel não foi adulterado. Permitir verificação de integridade do arquivo pelo hash quando pertinente.

Registrar “cópia emitida por X em Y”, e não “impressão física comprovada”. O navegador não comprova a impressão nem detecta fotocópias ou reimpressão de PDF já baixado. Uma nova emissão ganha nova série; baixar novamente a mesma emissão conserva a série. Guardar original assinado intacto e criar a cópia identificada como derivada, pois adicionar marcas depois pode invalidar a assinatura do arquivo.

## 6. ODCA Contract Studio

Interface de trabalho inspirada na facilidade de um canvas visual, com estrutura documental acessível. Não implementar o texto inteiro como uma imagem/canvas HTML: deve permanecer selecionável, pesquisável e exportável.

Layout desktop: biblioteca/estrutura à esquerda, páginas do contrato no centro e inspetor contextual à direita. No celular, painéis em abas ou gavetas, leitura confortável e ações principais fixas sem cobrir texto. Modos: Escrever, Preencher, Revisar e Conferir original.

Ferramentas planejadas:

- Texto com estilos, listas, tabelas, cabeçalho, rodapé, numeração, quebras, imagens e campos; atalhos de teclado e desfazer/refazer.
- Blocos de cláusulas arrastáveis, com alternativa por teclado; ordenação e referências internas reavaliadas.
- Variáveis tipadas com tags de origem; barra de progresso de preenchimento e lista de pendências clicáveis.
- Cláusulas obrigatórias, opcionais e condicionais em regras declarativas; nunca executar JavaScript fornecido em um modelo.
- Biblioteca de cláusulas com responsável, versão, finalidade, etiquetas e alternativas aprovadas.
- Comentários ancorados, menções autorizadas, sugestões e comparação entre versões com aceitar/rejeitar.
- Salvamento automático com indicador de estado, revisão otimista e recuperação de falhas sem sobrescrever edição concorrente.
- Primeiro entregar detecção de conflito; colaboração simultânea só em etapa posterior com solução de sincronização e autorização testada.
- Mapa de partes: quem contrata, representa, aprova e assina; alertas de campos incompatíveis.
- Verificador de consistência: CPF/CNPJ divergente, data invertida, valor por extenso incompatível, anexos ausentes e referência a cláusula removida.
- “Explique este trecho” e sugestões de redação como recursos assistivos opcionais, com origem, comparação e aprovação humana; sem garantia jurídica automática.
- Quadro “O que falta para assinar?” com campos, documentos, revisões e aprovações pendentes.
- Visão de obrigações ligada ao trecho que as originou; simulação de prazos de renovação e aviso.

Avaliar Tiptap/ProseMirror como base do editor estruturado; a documentação apresenta uma estrutura extensível. Paginação, colaboração, comentários e exportação precisam de prova técnica e avaliação de licença/custo; não assumir que todos estão no núcleo gratuito. [Documentação Tiptap](https://tiptap.dev/docs/editor/getting-started/overview).

## 7. Arquivos e conversões: escopo honesto

| Entrada | Leitura e extração | Saídas planejadas |
|---|---|---|
| PDF textual | Texto/layout, páginas e origem | PDF original; DOCX reconstruído e imagens das páginas |
| PDF digitalizado | OCR por página e revisão | PDF pesquisável, texto estruturado/DOCX reconstruído, imagens |
| PNG/JPEG/TIFF | OCR, rotação e metadados mínimos | PDF, imagem normalizada e DOCX com conteúdo extraído |
| DOCX | Texto, tabelas, realces e campos suportados | DOCX, PDF e imagens das páginas renderizadas |
| XLSX | Planilhas/células/tabelas; seleção de área | PDF/imagens de áreas selecionadas e DOCX com tabelas selecionadas |
| PPTX | Texto por slide, notas permitidas e imagens | PDF, imagens por slide e DOCX de conteúdo extraído |

DOC/XLS/PPT legados entram por conversão isolada em etapa posterior. Arquivos protegidos por senha exigem fluxo explícito, não tentativa de quebrar senha. Não executar macros, links externos ou fórmulas arbitrárias; sinalizar recursos não suportados. PDF para DOCX não garante fidelidade perfeita, nem uma planilha vira um contrato equivalente automaticamente. Sempre mostrar prévia e preservar original.

Prova técnica com corpus sintético: tabelas longas, assinaturas, rodapés, acentos, quebra de página, scan inclinado, planilha extensa, slides e documento inválido. Avaliar OCR local e conversor de documentos em worker isolado, com instalação reproduzível; avaliar alternativa comercial caso fidelidade/custo exijam. Não tornar Docker pré-requisito de desenvolvimento local.

## 8. Identidade visual, menus e indicadores

Paleta: azul-marinho #102A43, branco #FFFFFF, branco-gelo #F4F7FB; azul de ação #245BDB, texto #243746 e amarelo de campos #FFF3BF. Verificar contraste em uso real. Tipografia legível, ícones consistentes, espaçamento de 8 px, bordas discretas e estados vazios com instrução útil.

Menu do cliente: Visão geral; Contratos; Modelos e cláusulas; Contrapartes; Aprovações; Assinaturas; Agenda e obrigações; Arquivos; Relatórios; Plano e créditos; Equipe e perfis; Configurações e privacidade. Itens aparecem por permissão e contratação, e a API valida novamente.

Menu da plataforma: Visão global; Clientes; Planos e adicionais; Faturas e pagamentos; Créditos e consumo; Modelos publicados; Usuários e acessos; Operações e filas; Auditoria; Privacidade; Integrações.

Dashboard do cliente: contratos por estado, próximos vencimentos, prazos de denúncia, assinatura pendente, obrigações atrasadas, valores por moeda, uso de espaço e saldo de envelopes/OCR. Cada cartão leva à lista filtrada. Dashboard global: clientes ativos, inadimplência, consumo, falhas de processamento, adoção e receita conciliada. Não somar moedas distintas sem regra explícita.

Confirmações em pop-up para inativar/restaurar, mudar perfil, bloquear, enviar para assinatura, aprovar, contratar adicionais e emitir cópia; informar efeito, custo quando houver e permitir cancelar. Avisos de sucesso discretos, erro acionável, carregamento, estado vazio e prevenção de cliques duplicados. Não abrir modal a cada tecla ou autosave. Modais acessíveis, foco correto, teclado e mensagens também próximas do campo.

## 9. Arquitetura técnica

Base proposta: ASP.NET Core em .NET 10 LTS, PostgreSQL 18, Dapper/Npgsql; MVC/Razor consumindo API por BFF; JavaScript ES modules com build de assets. Fixar SDK e versões estáveis de pacotes e testar compatibilidade. A política oficial consultada mantém .NET 10 LTS com suporte até novembro de 2028. [Microsoft](https://dotnet.microsoft.com/en-us/platform/support/policy/dotnet-core).

Começar com monólito modular e worker separado. Separar módulos por responsabilidade e contratos internos; evitar custo de microsserviços antes de necessidade medida. API e worker escalam independentemente. Arquivos em armazenamento privado por interface; PostgreSQL guarda metadados, conteúdo estruturado e auditoria. Desenvolvimento usa diretório fora de wwwroot; produção usa object storage privado com URLs curtas e autorizadas.

| Projeto | Responsabilidade e dependências |
|---|---|
| Odca.Domain | Entidades, objetos de valor, invariantes e eventos; não conhece banco, HTTP ou UI. |
| Odca.Application | Casos de uso, interfaces/ports, DTOs internos e validação de negócio; depende de Domain. |
| Odca.Contracts | Contratos HTTP e eventos de integração versionados; sem infraestrutura. |
| Odca.Infrastructure | Dapper, conexões, repositórios, transações, arquivos e implementações de provedores; depende de Application/Domain. |
| Odca.Api | Controllers finos, autenticação/autorização, mapeamento HTTP e composition root. |
| Odca.Web | MVC/Razor, ViewModels, cliente HTTP da API, BFF e assets; sem acesso direto ao banco. |
| Odca.Worker | OCR/conversões, outbox, agendas e reconciliação; chama casos de uso e implementações registradas. |
| Odca.Bootstrap | Utilitário local para setup, segredo de desenvolvimento e seed seguro. |
| Testes | Unitários de regras, integração em PostgreSQL real, arquitetura e E2E dos fluxos críticos. |

Organizar cada camada por funcionalidade: Identity, Tenancy, Billing, Contracts, Templates, Documents, Signatures, Notifications, Audit, Privacy. Um arquivo por classe/interface principal; nomes claros e formatação consistente. Evitar interfaces sem propósito, repositório genérico que esconda tenant, service gigante e DTO único para leitura/escrita.

Interface deve existir quando separa responsabilidade, infraestrutura ou contrato de uso: `ITenantContext`, `IContractRepository`, `IContractVersionRepository`, `IUnitOfWork`, `IFileStorage`, `IDocumentExtractor`, `IOcrProvider`, `IDocumentConverter`, `ISignatureProvider`, `IPaymentProvider`, `INotificationChannel`, `IQuotaService`, `IAuditWriter`, `IClock`. Cada uma em seu arquivo e implementações conforme etapa, não centenas de stubs antecipados.

Transações pertencem ao caso de uso: todos os repositórios participantes recebem a mesma conexão/transação. Dapper parametrizado; filtros e ordenação em allowlist; cancelamento e timeout; nunca concatenar valores do usuário no SQL. Mapear read models com aliases e tipos PostgreSQL/.NET explícitos, inclusive nulabilidade; verificar materialização com dados reais de teste.

Concorrência otimista com `version` e HTTP ETag/If-Match ou equivalente; conflito retorna 409/412 conforme contrato escolhido, com UI para comparar. Outbox transacional para publicar trabalho após commit e inbox para deduplicar eventos recebidos. Jobs com lease, retry limitado, backoff, fila de falhas e reprocessamento auditado. Efeitos externos não ficam dentro de transação longa de banco.

## 10. Isolamento, autenticação e privacidade

Todo registro do cliente recebe tenant_id; índices e FKs compostas impedem vincular registros de organizações distintas. Tenant vem da identidade e associação validadas, não de cabeçalho livre ou corpo enviado pelo navegador. Validar também tenant em arquivos, cache, buscas, relatórios, exports, filas e webhooks. Identificadores opacos não substituem autorização.

Adicionar RLS nas tabelas de tenant como segunda barreira. Role de aplicação não pode ser proprietária, superusuária ou BYPASSRLS; contexto local por transação e ausente deve negar acesso. Reutilização do pool não pode carregar tenant anterior. Caminho administrativo separado e controlado; nenhum parâmetro do cliente habilita bypass. RLS complementa, não elimina, a proteção da aplicação. [PostgreSQL](https://www.postgresql.org/docs/current/ddl-rowsecurity.html).

Usar autenticação mantida pelo framework com armazenamento Dapper, hash de senha compatível, bloqueio por tentativas, recuperação, verificação de e-mail, MFA e revogação. Web usa cookie HttpOnly/Secure/SameSite e antiforgery; tokens da API ficam no servidor/BFF. JWT de acesso curto com assinatura, issuer, audience, exp, nbf, jti e validação de algoritmo; refresh rotativo com hash e detecção de reutilização. Alterar perfil/bloquear usuário deve revogar sessão ou invalidar security version, sem depender apenas da expiração longa do token.

Seed do superadministrador: utilitário Development gera senha forte e chave JWT por ambiente, guarda em user-secrets/arquivo local ignorado com permissão restrita e informa como recuperar localmente. SQL insere apenas hash compatível produzido pelo utilitário, com login e flag de troca inicial. Não publicar senha/hash fixo reaproveitável ou JWT eterno. Reexecutar setup preserva conta e senha existentes; recuperação é comando explícito. Testar login real até dashboard. Produção rejeita modo Development, chaves fracas e seed de teste.

LGPD: exclusão lógica atende ao histórico operacional, mas não autoriza conservar dados pessoais indefinidamente. Implementar política de retenção, bloqueio por litígio e solicitação de direitos; avaliar eliminação/anonimização quando aplicável, preservando o que tiver fundamento de conservação. Diferenciar controlador e operador por finalidade. Validar essa política com assessoria jurídica antes da operação comercial. Referência: artigos 6, 15, 16 e 18 da [LGPD](https://www.planalto.gov.br/ccivil_03/_ato2015-2018/2018/lei/l13709.htm).

Como proposta técnica: fluxo de privacidade verifica identidade, escopo, impedimentos e aprovação; executa procedimento restrito, com evidência minimizada, alcançando originais, derivados, índices, caches, provedores e política de expiração de backups. Restore reaplica decisões de eliminação para não ressuscitar dados. Distinguir retenção do documento, metadados e logs. Não prometer conformidade apenas por instalar uma flag ou criptografia.

TLS, criptografia em repouso, chaves fora do repo, segregação de segredos por ambiente, menor privilégio e trilha de acesso administrativo. Logs não contêm senhas, tokens, contrato integral, CPF completo ou conteúdo OCR. Dados enviados a IA exigem política/configuração do tenant e avaliação de provedor; documento extraído é entrada não confiável e não pode instruir o sistema a vazar dados ou executar ações.

Uploads: validar assinatura/MIME real, extensão, quantidade de páginas, pixels, tamanho descompactado, zip bombs, malware e nomes; limitar CPU/memória/tempo e bloquear rede no processo conversor sempre que possível. Sanitizar HTML colado e retornado pelo editor; impedir XSS, path traversal, SSRF por URLs e CSV formula injection em exportação. Downloads sempre autorizados.

### 10.1 Governança e inventário de tratamento — ODCA-PRIV-001

Implementar módulo Privacy desde S00/S01. A plataforma administra finalidades próprias como cadastro, cobrança e segurança; o cliente define as finalidades dos contratos sob sua responsabilidade. Documentar a qualificação real de controlador/operador por atividade, sem assumir um papel único para toda a empresa.

Criar inventário versionado com: atividade, finalidade específica, categorias de titulares/dados, origem, necessidade, hipótese legal proposta e validação, responsável, sistemas, destinatários, países, retenção, controles e data de revisão. Não inserir CPF ou contratos reais nesse inventário público do repositório. Dados concretos de operação ficam no banco protegido.

Na aplicação, impedir ativação de nova integração de produção sem configuração aprovada de finalidade, escopo e fornecedor. Requisitos de direitos, conservação e prestação de contas orientam este desenho; não usar consentimento como fundamento universal. [LGPD — texto oficial](https://www.planalto.gov.br/ccivil_03/_ato2015-2018/2018/lei/l13709.htm).

Manter contatos de privacidade da plataforma e de cada organização; permitir configuração de encarregado quando aplicável. Documentar responsabilidades e instruções de tratamento em acordo com o cliente. Criar registro de avaliações de risco e RIPD quando indicado, com escopo, decisão e responsável; não gerar uma certificação automática.

### 10.2 Avisos, preferências e consentimentos — ODCA-PRIV-002

Criar publicações versionadas de aviso de privacidade com data, idioma, finalidade e responsável. Fluxo de cadastro separa: aceite contratual; acesso ao aviso; escolhas opcionais específicas, sem pré-seleção. Registrar evidência mínima de cada escolha e revogação. Mudança de texto não fabrica novo aceite.

Preferências por finalidade/canal, com atualização simples. Marketing fica separado de avisos operacionais. Revogar marketing não bloqueia login nem cancela o contrato; mensagens necessárias seguem finalidade e fundamento próprios. Antes de despachar mensagens em fila, reavaliar preferências e permissões atuais. Consentimento dado pelo administrador do tenant não representa automaticamente todos os titulares citados nos contratos.

Por padrão, não instalar rastreadores publicitários. Inventariar cookies; se forem adicionados recursos não essenciais dependentes de consentimento, bloquear sua ativação até a escolha correspondente. Não pedir consentimento genérico para cookies estritamente necessários à sessão.

### 10.3 Central de privacidade — ODCA-PRIV-003

Disponibilizar canal público de abertura e área autenticada de acompanhamento; signatário ou contraparte sem conta pode solicitar atendimento. Não exigir assinatura de plano, compra de créditos ou reativação financeira. A indisponibilidade do portal deve ter canal alternativo documentado.

Fluxo: Recebida → Verificação de identidade → Triagem → Em análise → Em execução → Respondida → Concluída. Acrescentar resultado parcial/indeferido fundamentado e revisão interna. Nunca revelar na resposta inicial se uma pessoa consta de um contrato. Limitar tentativas e usar identificação proporcional ao risco, evitando exigir cópias de documentos indiscriminadamente.

Tipos de solicitação contemplam confirmação/acesso, correção, informação de compartilhamento, portabilidade quando aplicável, bloqueio, anonimização/eliminação, revogação e contestação de tratamento/decisão automatizada quando pertinente. Registrar escopo, controlador responsável, responsável interno, datas, prazo aplicável, fonte/regra vigente, histórico e resposta. Encaminhamento entre ODCA e cliente não reinicia o relógio silenciosamente.

Não aplicar “15 dias” a todos os tipos por conveniência: parametrizar prazos conforme direito e regra vigente, com alertas internos anteriores ao vencimento e escalonamento. Preservar marco inicial e justificar alterações. A declaração completa de confirmação/acesso tem disciplina específica no art. 19. [LGPD](https://www.planalto.gov.br/ccivil_03/_ato2015-2018/2018/lei/l13709.htm).

Exportação é um pacote restrito ao titular/escopo, revisado para não expor dados de terceiros. Gerar arquivo temporário criptografado, com download autenticado ou token curto adequado à identidade verificada, expiração e auditoria. Não mandar contratos integrais como anexos de resposta automática. Correções em cadastros não reescrevem contratos assinados: registrar retificação/evento ou procedimento adequado.

### 10.4 Retenção, inativação e eliminação — ODCA-PRIV-004

Inativação operacional permanece padrão. A eliminação de dados pessoais tem procedimento separado e restrito, sem botão genérico de exclusão física para o superadministrador. Pseudonimização/hash reversível ou vinculável não deve ser anunciado como anonimização irreversível.

Política versionada por categoria: evento inicial da contagem, duração, fundamento/documento de suporte, responsável, decisão ao final, abrangência, revisão e exceções. Cobrir cadastro pendente, usuário encerrado, contrato, versão, OCR, miniatura, exportação temporária, mensagem, evidência de assinatura, auditoria e backup. Não impor prazo único de cinco anos nem retenção infinita por padrão. Valores de teste ficam identificados; operação real exige política definida para as categorias utilizadas.

Motor calcula candidatos; simulação exibe quantidade e dependências sem expor dados desnecessários. Antes da execução, revalidar impedimentos de retenção, decisão autorizada e versão dos registros. Um legal hold precisa de motivo, escopo, autor, revisão e encerramento — não uma flag eterna sem controle.

Estados do job: Planejado, Aprovado, EmExecução, Parcial, Concluído, Falhou. Executar em lotes idempotentes, com identidade de serviço específica e privilégios mínimos. Gerar comprovante minimizado por destino, sem replicar o conteúdo eliminado no log. Diferenciar eliminação local concluída de pedido pendente em fornecedor.

Abranger arquivos/versões, texto OCR, índices de busca, embeddings, sugestões, caches, temporários, exports e cópias dos provedores. Definir expiração de backups, restrição de acesso e reaplicação das decisões antes de liberar ambiente restaurado. Inativação não libera quota; eliminação física confirmada ajusta bytes uma vez. Nenhum pagamento libera acesso a dado sob bloqueio de privacidade.

### 10.5 Acesso administrativo e isolamento — ODCA-PRIV-005

Superadministrador mantém gestão de clientes, planos e cobrança. Conteúdo contratual exige sessão de suporte com MFA recente, escopo por tenant/recurso/ação, justificativa, duração curta e concessão conforme política. Leitura é o padrão; exportação/alteração requer escopo adicional. Sessão expirada ou revogada perde efeito na próxima operação sensível, inclusive jobs agendados.

Implementar concessão normal documentada e acesso emergencial separado, motivado, sinalizado e revisado; registrar quem concedeu e quem executou. Não permitir que superadmin limpe seus próprios eventos. Cliente autorizado consulta histórico de acesso de suporte sem visualizar dados de outras organizações. Registrar o ator real sem personificação invisível.

Busca, IA, relatórios, cache, WebSocket/colaboração e storage devem verificar organização e permissão por objeto. Remoção de usuário ou mudança de perfil invalida acessos derivados e links quando tecnicamente controláveis. Documentar que arquivos já baixados não podem ser recolhidos por revogação do sistema.

### 10.6 Fornecedores, OCR, IA e compartilhamento — ODCA-PRIV-006

Cadastro de fornecedor: serviço, dados enviados, finalidade, localizações de armazenamento/processamento/acesso remoto, suboperadores, condições contratuais, retenção, exclusão, contato de incidente, evidências e situação de avaliação. Troca de provedor/modelo exige revisão de fluxo e permissões antes da ativação.

Documentar mecanismo aplicável quando houver transferência internacional; configurar região sozinha não prova ausência de transferência. Não presumir que todo envio internacional depende de consentimento ou que contrato comercial genérico é suficiente. [ANPD — transferência internacional](https://www.gov.br/anpd/pt-br/assuntos/assuntos-internacionais/transferencia-internacional-de-dados).

IA/OCR processam apenas arquivos autorizados. Evitar enviar documento inteiro quando um trecho atende à finalidade. Não usar contratos para treino ou benchmarking compartilhado por padrão; verificar condições reais do provedor. Não armazenar prompts/respostas sensíveis em telemetria aberta. Classificação de conteúdo sensível auxilia triagem, mas não garante detectar tudo.

Revisão humana confirma extração e sugestão. Nunca autorizar assinatura, cobrança, mudança de contraparte ou envio externo apenas com uma resposta de IA. Ao adicionar decisão exclusivamente automatizada com efeitos sobre pessoas, implementar avaliação e canal de contestação antes da liberação. Dados de saúde, biometria e de crianças eventualmente presentes demandam triagem específica, sem coleta extra indiscriminada.

### 10.7 Incidentes e continuidade — ODCA-PRIV-007

Criar registro restrito de incidente: detecção, momento de conhecimento, sistemas/tenants afetados, categorias, extensão conhecida, evidências preservadas, medidas de contenção, avaliação de risco, controlador responsável e decisões de comunicação. Isolar imediatamente conforme runbook; não aguardar aprovação de texto para revogar chave comprometida.

Workflow identifica quando comunicar cliente/controlador, ANPD e titulares conforme regras aplicáveis, com relógios separados, responsáveis e comprovantes. Configurar prazos legais segundo norma vigente e regime efetivamente aplicável, com referência e versão, sem permitir estendê-los silenciosamente. Comunicação de incidente não usa fila de marketing e não é bloqueada por inadimplência. [ANPD — comunicação de incidente](https://www.gov.br/anpd/pt-br/canais_atendimento/agente-de-tratamento/comunicado-de-incidente-de-seguranca-cis).

Criar modelos de comunicação para revisão do responsável; nenhuma notificação regulatória automática sem decisão qualificada. Tratar alertas técnicos imediatos e comunicação oficial como eventos distintos. Ensaiar incidente simulado, indisponibilidade e restauração usando dados sintéticos; registrar aprendizados e tarefas corretivas.

### 10.8 Banco, API e interface do módulo Privacy — ODCA-PRIV-008

Adicionar incrementalmente ao mesmo `database/odca.sql`: processing_activities, privacy_notice_versions, notice_acknowledgements, consent_events, communication_preferences, privacy_contacts, privacy_requests, privacy_request_events, privacy_deadline_rules, privacy_exports, retention_policy_versions, retention_executions, retention_execution_items, legal_holds, processor_registry, processing_transfers, support_access_grants, support_access_sessions, security_incidents e incident_actions. Reutilizar tabelas já existentes em vez de duplicar por nome diferente.

Separar registros globais da plataforma e dados específicos de tenant, usando escopo explícito e políticas separadas. Não usar tenant_id nulo como passe livre de acesso. Eventos de consentimento/auditoria são append-only na operação normal, mas seguem política própria de retenção e descarte autorizado. FKs e autoria devem suportar desidentificação posterior sem cascata destrutiva indevida.

API versionada com casos de uso específicos para abrir/acompanhar solicitação, registrar decisão, preparar exportação, simular/aprovar retenção, conceder/revogar suporte e registrar incidente. Endpoints públicos retornam somente protocolo opaco e instrução genérica. Validar transições no backend, idempotência, autorização e concorrência.

Telas: Central de Privacidade; Minhas Solicitações; Avisos e Preferências; Tratamentos e Fornecedores; Retenção; Acessos de Suporte; Incidentes. Aplicar identidade visual do produto. Pop-up de aprovação descreve alcance e irreversibilidade quando houver. Painel de privacidade mostra pendências, prazos, acessos vigentes e jobs parciais, nunca um selo automático de conformidade.

### 10.9 Documentação operacional e entregas — ODCA-PRIV-009

Criar `docs/privacy/PROCESSING_REGISTER.md`, `RESPONSIBILITY_MATRIX.md`, `RETENTION_POLICY.md`, `DATA_SUBJECT_REQUESTS.md`, `PROCESSORS_AND_TRANSFERS.md`, `INCIDENT_RESPONSE.md` e `RELEASE_EVIDENCE.md`. São modelos e procedimentos técnicos a preencher/validar pelos responsáveis, não textos jurídicos certificados.

S00 entrega módulo, inventário inicial, política de segredos e logging; S01 entrega avisos, preferências, canal de direitos e suporte controlado. S02–S04 ampliam inventário, retenção e exclusão para arquivos/OCR e cadastro assistido. S06–S08 concluem direitos e incidentes envolvendo provedores. S10 verifica o conjunto antes de produção. Cada nova funcionalidade atualiza inventário e propagação de permissões/eliminação na mesma etapa.

Pendência de validação jurídica ou credencial não impede construir o controle técnico com dados sintéticos; impede declarar validado o respectivo uso real. Antes do lançamento, apresentar matriz com requisito, implementação, evidência, responsável e pendência. Nenhuma alegação de conformidade sem avaliação organizacional correspondente.

### 10.10 Testes obrigatórios de privacidade — ODCA-PRIV-010

- Tenant A não acessa documento, OCR, busca, relatório, solicitação ou canal de colaboração de B; testar IDs trocados, links e contexto do pool.
- Titular sem conta abre solicitação sem confirmar existência de contrato; verificação insuficiente não libera exportação.
- Conta suspensa acessa o canal de direitos; o endpoint não consome créditos nem revela dados de terceiros.
- Revogação de preferência impede despacho opcional ainda em fila; avisos necessários têm decisão própria registrada.
- Superadmin sem sessão de suporte válida não lê conteúdo; expiração, revogação e escopo de ação são efetivos no servidor.
- Logs, erros e traces não contêm segredos, documento integral ou identificadores pessoais completos; validar com dados sintéticos reconhecíveis.
- Eliminação respeita hold, alcança derivados/índices e não recria dados após restore; job parcialmente falho pode retomar sem debitar quota duas vezes.
- Exportação expira, verifica identidade e exclui dados de terceiros; documento assinado não é reescrito por correção cadastral.
- Incidente registra marcos e responsáveis; regras de prazo têm fonte/versionamento; simulação não envia comunicado real.
- Dados para IA pertencem ao escopo autorizado; texto malicioso do documento não altera instruções nem dispara ações.

## 11. Banco e script único reexecutável

Entregável obrigatório: `database/odca.sql`, SQL puro executável no Query Tool do pgAdmin contra banco previamente criado e pelo runner de setup. Não confundir arquivo SQL com dump binário para pg_restore. Preflight explica PostgreSQL/roles/privilégios necessários. Criação do banco fica no utilitário documentado, fora da transação do script.

O mesmo arquivo contém bootstrap e blocos incrementais numerados com versão e checksum em `odca.schema_migrations`. Manter blocos aplicados imutáveis; acrescentar correções ao final. `CREATE TABLE IF NOT EXISTS` sozinho não atualiza colunas/constraints. Inspecionar catálogos e aplicar alterações explícitas para índices, FKs, checks, triggers, funções e políticas. Sem DROP CASCADE ou reconstrução destrutiva como atalho.

Runner valida checksums antes de executar; SQL também registra/valida metadados esperados. Usar lock de implantação para impedir execução concorrente. Cada etapa transacional declara dono da transação; evitar transações aninhadas acidentais. Operações incompatíveis com transação têm fase explícita, detecção de conclusão parcial e retomada segura. Mudanças grandes seguem expandir → preencher em lotes → validar → migrar consumo → retirar em versão futura.

Seeds por chave estável; reexecução não sobrescreve plano customizado, preço, permissões modificadas ou senha existente sem migração explícita. Integridade financeira e auditoria são append-only para a role normal. FKs sem cascata destrutiva de histórico; CHECK coerente entre is_deleted/deleted_at/deleted_by.

Arquivo canônico sempre atualizado. Gerar cópia versionada em `database/releases/odca-vNNN.sql`, com checksum, somente em release/mudança do schema; verificar que representa o canônico daquele momento. O histórico não substitui backup dos dados. Documentar e ensaiar backup/restauração real.

Grupos de tabelas a criar incrementalmente:

- Identidade: users, tenants, memberships, roles, permissions, role_permissions, member_roles, sessions, refresh_tokens, invitations.
- Comercial: plan_versions, plan_entitlements, subscriptions, invoices, payment_events, addon_orders, entitlement_grants, credit_ledger, usage_reservations, storage_usage.
- Contratos: counterparties, contacts, contract_types, contracts, contract_parties, contract_versions, contract_events, obligations, approval_flows, approval_decisions.
- Editor: templates, template_versions, template_assignments, template_fields, clauses, clause_versions, comments, change_suggestions, field_bindings.
- Documentos: files, file_derivatives, processing_jobs, extraction_candidates, extraction_decisions, print_issues.
- Integrações: signature_envelopes, envelope_signers, provider_events, notification_rules, notifications, notification_deliveries, outbox, inbox.
- Governança: audit_events, support_access_sessions, privacy_requests, retention_rules, legal_holds e schema_migrations.

Valores monetários decimal/numeric + moeda; quotas bigint; datas de vigência date; eventos timestamptz UTC; timezone do tenant separado. Usar UUIDs para identidade técnica e códigos legíveis de negócio na interface. Índices iniciados por tenant em consultas usuais, com filtros de registros ativos quando adequados.

## 12. Logs, falhas, escala e operação

Middleware global de exceções retorna ProblemDetails seguro com traceId. Capturar exceções localmente quando for possível recuperar, compensar ou adicionar contexto útil; não colocar try/catch vazio em cada método, nem registrar o mesmo erro em todas as camadas. Workers isolam falha por job; cancelar não é sucesso e nem necessariamente erro operacional.

Auditar criação/edição/inativação/restauração, login e bloqueio, acesso excepcional, visualização/download sensível, publicação de modelo, aprovação, assinatura, créditos e exportação. Alterações críticas e sua auditoria de negócio confirmam na mesma transação. Eventos registram tenant, ator real, contexto de suporte, ação, entidade, data, resultado e correlationId; diferenças são redigidas para não duplicar dados sensíveis indiscriminadamente.

Telemetria estruturada e métricas: latência, erros, tamanho/idade das filas, taxa de OCR falho, entregas, reconciliação e divergências de quota. Logs de auditoria têm permissão de somente acréscimo e exportação protegida. Retenção dos logs é definida, não ilimitada por omissão.

Paginação no servidor, streaming de upload/download, processing assíncrono, queries parametrizadas e índices medidos. Dashboard pode usar projeções atualizadas por eventos. Não carregar milhares de contratos ou arquivos inteiros em memória. Objetivos iniciais a validar: p95 de listagens até 800 ms em ambiente/carga documentados; tela útil até 2,5 s na rede de referência; jobs reportam progresso sem bloquear a navegação. Estes números são metas, não resultados medidos.

## 13. Etapas de implementação e critérios de saída

Cada etapa entrega banco, regras, API, interface quando aplicável, validação e documentação juntos. O produto comercial completo é o conjunto das etapas; a primeira entrega não deve ser vendida como sistema final.

| Etapa | Entrega | Critério mínimo de saída |
|---|---|---|
| S00 — Fundação executável | Solução, CI, configuração, SQL inicial, setup seguro, documentação | Build e startup; banco novo e reaplicação; superadmin autentica; segredos fora do Git. |
| S01 — SaaS e acessos | Cadastro CPF/CNPJ, tenants, vínculos, perfis, planos, suspensão e dashboard inicial | Dois clientes isolados em API/banco/arquivos; cliente não consegue elevar privilégios. |
| S02 — Acervo e contratos | Tipos, contrapartes, contratos, upload privado, cotas, versões e inativação | Upload concorrente não excede quota; consulta/download autorizados; restore não perde autoria. |
| S03 — Editor e modelos | Editor funcional, modelos destinados, campos/tags, snapshot, autosave e exportação inicial | Modelo → preenchimento → salvar/reabrir → PDF/DOCX; conflito de edição explícito. |
| S04 — Extração e conversões | OCR, leitura Office, revisão lado a lado e cadastro assistido | Corpus validado; original preservado; erros legíveis; nenhum cadastro automático incorreto. |
| S05 — Revisão e aprovação | Comentários, versões comparadas, cláusulas e aprovação por regra | Mudança após aprovação a invalida; histórico preservado e permissões aplicadas. |
| S06 — Cobrança e adicionais | Integração de pagamento, faturas, carteiras e reconciliação | Evento duplicado/fora de ordem não duplica crédito; downgrade e estorno testados. |
| S07 — Assinatura | Provedor real, envelopes, signatários, evidências e créditos | Sandbox real ponta a ponta; timeout reconciliado; documento assinado não muda. |
| S08 — Agenda e mensagens | Três meses, denúncia, obrigações, e-mail e WhatsApp | Reagendamento sem duplicata; testes de fim de mês/timezone e status de entrega real. |
| S09 — Rastreabilidade e relatórios | Séries por emissão/página, verificação, relatórios e dashboards finais | Séries únicas concorrentes; cliente não consulta cópia alheia; hash validado. |
| S10 — Privacidade e operação | Conclusão dos controles de privacidade iniciados em S00/S01, fornecedores, backups, carga e segurança | Direitos, suporte, expiração de exports, eliminação propagada, restore e incidente simulado validados; evidências e pendências registradas. |
| S11 — Diferenciais avançados | Obrigações inteligentes, playbooks, colaboração, API/SSO conforme plano | Cada recurso com avaliação de qualidade, custo, direitos de acesso e regressão. |

Privacidade, auditoria e segurança começam em S00/S01; S10 conclui fluxos e valida operação, não posterga esses fundamentos. Revisitar preços/limites antes da venda, com custos reais das integrações. Não liberar externamente recursos que existam apenas em simuladores.

