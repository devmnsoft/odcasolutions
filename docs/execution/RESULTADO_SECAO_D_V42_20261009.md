# Resultado da validação — Seção D (Design e homologação), identidade visual + documentos finais

**Data:** 09/10/2026 · **Suíte:** `scripts/qa/validate-d-web.ps1` (UTF-8+BOM) · **Veredito: ALL PASS 111/111 (run 11)**

Ambiente: API `https://localhost:7143` · Web `https://localhost:7144` · Worker ativos · PostgreSQL 18 nativo, banco `odca_test_disposable`, schema **v042**. Fixtures criadas/removidas pela própria suíte: **Aurora** (`Val D: Centro Terapeutico Aurora`, `therapy_clinic`, BASIC, id `…0000D1`) e **Harmonia** (`Val D: Clinica Cirurgica Harmonia`, `plastic_surgery`, ENTERPRISE, id `…0000D2`), com os mesmos papéis/permissões do tenant demo. Logins: `cliente.teste@odca.local` (admin/cliente das duas orgs) e `operador@odca.local` (operador; promovido a administrador de plataforma com sessão nível-MFA durante a suíte). Cleanup restaura os seeds: TC de volta ao plano BASIC, operador rebaixado, MFA limpo, fixtures removidas (`D99`). Evidências visuais (PNG 1440/768/360 + trilha de teclado/foco) ficam em `%TEMP%\opencode\evidencias-d\` — fora do repositório, pela convenção do projeto.

## Caderno de asserts (111)

| Grupo | Asserts | Resultado |
| --- | --- | --- |
| D00 setup | `setup_fixtures` · `logins` | PASS |
| D01 menu/gate de área | `menu_contratos_visivel` · `cliente_sem_administracao` | PASS |
| D02 instalação + overlay terapia | `instalar_oficiais_D1_200` · `instalar_oficiais_D2_200` · `templates_instalados` · `wizard_abre_D1` · `minuta_terapia_201` · `overlay_clausula_2A` · `overlay_campo_sessoes` · `overlay_campo_mensalidade` · `formula_recalc_presente` · `terapia_sem_aprovacao_odca` · `editor_estudio_abre` | PASS |
| D03 preenchimento canônico | `salvar_completo_200` · `formula_recalculada_800` · `moeda_canonica_200` · `total_persistido_no_banco` | PASS |
| D04 validações de campo | `cpf_invalido_400` · `obrigatorio_vazio_salvar_rascunho_200` · `obrigatorio_vazio_geracao_400_pontua_campo` · `corrigido_200` | PASS |
| D05 documento final | `restaurar_testemunhas_200` · `gerar_versao_201` · `id_da_versao` · `capa_presente` · `sumario_presente` · `capa_com_organizacao` · `sem_variavel_nao_resolvida` · `impressao_renderiza_capa` · `pdf_gerado` | PASS |
| D06 gate de perfil | `cirurgico_fora_do_perfil_409` | PASS |
| D07 aprovação ODCA pendente | `minuta_cirurgica_201` · `requiresOdcaApproval_true` · `banner_pendente_na_web` · `salvar_cirurgico_200` · `gerar_cirurgico_201` · `callout_atencao_no_html` · `sem_variavel_cirurgico` · `blocker_aprovacao_odca` · `canConfirm_false` | PASS |
| D08 decisão ODCA | `fila_contem_harmonia` · `cliente_nao_decide_403` · `decisao_aprovado_200` · `decisao_persistida` · `blocker_removido` · `confirmar_preparativo_200` · `preparativo_confirmado_no_db` | PASS |
| D09 reverter/reprovareprovar decisao | `reprovacao_curta_400` · `reprovado_200` · `blocker_volta` · `reaprovacao_final_200` | PASS |
| D10 conflito de aba | `versao_obsoleta_409` · `conteudo_preservado` | PASS |
| D11 preparativo de assinatura | `readiness_200` · `confirmar_preparativo_D1_200` · `dois_participantes_confirmados` | PASS |
| D12 proposta de renovação | `proposta_201` · `id_da_proposta` · `central_lista_200` · `status_draft_e_prioridade` · `proposta_persistida` (crítica, 5500.00, 2027-12-31) · `pagina_renovacoes_mostra_critica` | PASS |
| D13 formalização com evidência | `submeter_200` · `status_in_review` · `formalizar_200` · `status_formalized` · `aplicar_200` · `contrato_atualizado` · `aplication_applied` · `reaplicar_409` | PASS |
| D14 vigência indeterminada | `proposta_indeterminado_201` · `aplicar_sem_data_200` · `data_continua_nula_valor_novo` (`NULL|3600.00`) | PASS |
| D15 lembretes 45/20/5 | `config_45_20_5_200` · `config_lida_de_volta` · `config_persistida_45_20_5` · `config_ordem_invalida_400` | PASS |
| D16 limites do operador | `operador_prepara_201` · `operador_submit_403` · `proposta_segue_draft` · `operador_config_403` · `web_renovacoes_ok_para_admin` | PASS |
| D17 solicitações Enterprise | `abrir_enterprise_200` · `fila_odca_recebe` · `triar_200` | PASS |
| D18 pausa/mensagem ODCA | `pausar_sem_texto_400` · `pausar_200` · `paused_flag` · `pause_trail` · `msg_odca_200` · `msg_odca_visivel_ao_cliente` · `retomada_paused_false` | PASS |
| D19 isolamento entre orgs | `minuta_D2_rota_D1_404` · `minuta_D1_rota_D2_404` · `renovacao_D1_rota_D1_200` · `renovacao_D1_rota_D2_404` · `solicitacao_D2_rota_D1_404` | PASS |
| D20 tokens/contraste/foco | `paleta_SPEC_presente` (#102a43/#f4f7fb/#245bdb/#243746/#fff3bf) · `contraste_texto_sobre_fundo_45` · `contraste_texto_sobre_campo_45` · `contraste_acao_sobre_fundo_45` · `contraste_texto_sobre_navy_45` · `foco_visivel_sobre_branco_30` · `foco_visivel_sobre_navy_30` · `site_css_servida_com_tokens` · `login_usa_site_css` · `tabelas_responsivas_sem_bootstrap` | PASS |
| D99 cleanup | `fixtures_removidas` · `plano_tc_basic` | PASS |

Mapeamento cenário→assert: os 19 cenários da Seção D do plano estão cobertos pelos grupos acima (cenário ↔ grupo Dnn); matriz de ações × perfil × plano em `MATRIZ_ACOES_PERFIL_PLANO.md` (cada linha cita o assert que a prova).

## Provas "documento final sem variáveis não resolvidas" (D.2)

- Barreira na **geração**: campo obrigatório vazio → rascunho salva 200, mas `POST /versions` devolve **400 apontando o campo pendente** (`D04.obrigatorio_vazio_geracao_400_pontua_campo`).
- Render final: HTML da versão não contém `Não informado` nem a classe de marcação de campo vazio — opcionais vazios saem como travessão `—` (`D05.sem_variavel_nao_resolvida` · `D07.sem_variavel_cirurgico`).
- Capa (`document-cover`) com organização + CNPJ + versão/data, sumário automático (`document-toc`) e callout de atenção (`document-callout--attention`) presentes na tela, na impressão e no PDF (`D05.capa_presente` · `sumario_presente` · `capa_com_organizacao` · `impressao_renderiza_capa` · `pdf_gerado` · `D07.callout_atencao_no_html`).

## Regressão das etapas anteriores

- `validate-b34-web.ps1` (B.3.4): **PASS** após todas as mudanças da Seção D — ver `reg-b34.log`.
- `validate-c-web.ps1` (Seção C): **PASS** — ver `reg-c.log`. (A correção do isolamento em `SolicitationsController.Detail` não afeta os fluxos C: o detalhe continua 200 quando a solicitação pertence ao tenant da rota.)

## Restauração do ambiente conferida pós-suíte (psql direto)

- Tenants `Val D:%` (Aurora/Harmonia) e todas as linhas-filhas (contratos, minutas, versões, evidências, solicitações, aprovações, renovações): **0** (`D99.fixtures_removidas`).
- `odca.subscriptions` do tenant TC: plano de volta a **BASIC** (`D99.plano_tc_basic`); `operador@odca.local` rebaixado e estado MFA zerado (cleanup da suíte).

## Falhas reais encontradas e corrigidas durante o ciclo (todas com causa-raiz)

1. **Produto — aplicar renovação em contrato sem data fim dava 500 (`42P08`)**: o INSERT de `contract_change_applications` montava `jsonb_build_object('endDate',@oldEnd,…)` e, com `end_date NULL`, o Npgsql enviava parâmetro sem tipo e o PostgreSQL não conseguia deduzir o tipo do `$6` → casts explícitos `::date/::numeric/::text` em todos os argumentos dos dois `jsonb_build_object` (`RenewalCenterController.ApplyCore`; mesma família de regra da D-C9). Comprovado por `D14.aplicar_sem_data_200` + `D14.data_continua_nula_valor_novo`.
2. **Produto — vazamento entre organizações no detalhe de solicitação**: `GET /organizations/{tenant}/solicitations/{id}` consultava `solicitation_detail(@actor,…)` sem vincular o **tenant da rota** à propriedade da solicitação; um ator membro de duas organizações lia pelo contexto errado (observado 200 cruzando Harmonia pela rota de Aurora). Adicionada checagem de pertencimento antes do envelope → rota cruzada agora devolve **404** (`SolicitationsController.Detail`, provado por `D19.solicitacao_D2_rota_D1_404`). As demais rotas (`messages/actions`) já passavam `@tenant` às funções definer.
3. **Suíte — ordem das FKs no cleanup de fixtures**: exclusões na ordem antiga deixavam resíduos (`generated_contract_versions`→`contract_drafts`; `contract_change/review_requests`→`document_versions`; `tenant_storage_usage`/`resource_movements`→`tenants`), causando PK duplicada nas fixtures determinísticas dos runs seguintes (raiz dos crashes/404 dos runs 6–8). `Clean-Tenant` reescrito na ordem de dependência e mapeado contra todas as FKs reais de `odca.tenants` no banco.
4. **Suíte — taxonomia de evidências**: o CHECK `document_versions_detected_type_check` aceita apenas `pdf|png|jpeg|docx`; o INSERT gravava `application/pdf` → violação em runtime (`runs 8`). Corrigido para `'pdf'` (decisão D-D6).
5. **Suíte — payloads de renovação sem `responsibleId`**: `ResponsibleId` é `Guid` não anulável no servidor e virava `Guid.Empty`, violando a FK `contract_change_requests(tenant_id,responsible_id)→memberships` → incluído nos payloads D12/D14/D16.
6. **Suíte — poller de PDF subdimensionado**: o PDF do consentimento cirúrgico tem menos de 2.000 bytes e o poller exige corpo mínimo → esgotava as 45 tentativas mesmo com `pdf_status='completed'`; threshold ajustado para 500 bytes e poller condicionada ao status terminal `completed` lido direto no banco.

## Notas

- Rate limit de login (5/min/IP) continua tratado com retries de 65 s; janela total da suíte < 15 min (validade do JWT).
- A suíte aceita `ODCA_D_KEEP=1` para preservar o estado das fixtures ao final (usado na coleta das capturas de evidência).
