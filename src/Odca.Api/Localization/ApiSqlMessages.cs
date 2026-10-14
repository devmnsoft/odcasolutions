namespace Odca.Api.Localization;

/// <summary>
/// D-OC27: mapa (código, mensagem canônica PT) → chave do catálogo api.sql.*.
/// A camada SQL permanece canônica em pt-BR (odca.sql não muda); aqui a API
/// resolve apenas a exibição. Códigos que carregam mais de uma mensagem
/// canônica usam sufixo estável por mensagem; códigos com mensagem única usam
/// a chave pura api.sql.&lt;código&gt;. Sem mapeamento, o texto original é
/// preservado (regra da casa: nunca se inventa texto).
/// </summary>
public static class ApiSqlMessages
{
    private static readonly Dictionary<string, string> Keys = new(StringComparer.Ordinal)
    {
        // approvals
        ["approvals.validation_decision\u0000Decisao invalida."] = "api.sql.approvals.validation_decision",
        ["approvals.validation_note\u0000Reprovacao exige justificativa de ao menos 5 caracteres."] = "api.sql.approvals.validation_note",
        ["approvals.not_installed\u0000Modelo oficial nao instalado nesta organizacao."] = "api.sql.approvals.not_installed.not_installed",
        ["approvals.not_installed\u0000Modelo oficial nao disponivel nesta organizacao."] = "api.sql.approvals.not_installed.not_available",
        // catalog (platform_catalog_set_status)
        ["catalog.action_invalid\u0000Acao invalida."] = "api.sql.catalog.action_invalid",
        ["catalog.reason\u0000Justificativa deve ter entre 5 e 500 caracteres."] = "api.sql.catalog.reason",
        ["catalog.not_found\u0000Modelo nao existe no catalogo global da plataforma."] = "api.sql.catalog.not_found",
        // solicitations — mensagens únicas por código
        ["solicitations.permission\u0000Permissao insuficiente."] = "api.sql.solicitations.permission",
        ["solicitations.permission_odca\u0000Acao exclusiva da ODCA."] = "api.sql.solicitations.permission_odca",
        ["solicitations.not_found\u0000Solicitacao nao encontrada."] = "api.sql.solicitations.not_found",
        ["solicitations.stale\u0000A solicitacao foi atualizada por outro usuario."] = "api.sql.solicitations.stale",
        ["solicitations.contract_mismatch\u0000Contrato nao pertence a esta organizacao."] = "api.sql.solicitations.contract_mismatch",
        ["solicitations.validation_subject\u0000Assunto deve ter entre 3 e 200 caracteres."] = "api.sql.solicitations.validation_subject",
        ["solicitations.validation_fields\u0000Servico, prioridade ou chave de idempotencia invalidos."] = "api.sql.solicitations.validation_fields",
        ["solicitations.validation_priority\u0000Prioridade invalida."] = "api.sql.solicitations.validation_priority",
        ["solicitations.validation_action\u0000Acao desconhecida."] = "api.sql.solicitations.validation_action",
        ["solicitations.validation_resolution\u0000A resolucao exige resumo da solucao."] = "api.sql.solicitations.validation_resolution",
        // solicitations.plan.required — duas variantes canônicas coexistem no odca.sql
        ["solicitations.plan.required\u0000Central de solicitações disponível apenas no plano Enterprise."] = "api.sql.solicitations.plan.required",
        ["solicitations.plan.required\u0000Central de solicitacoes disponivel apenas no plano Enterprise."] = "api.sql.solicitations.plan.required.plain",
        // solicitations.assignee_invalid
        ["solicitations.assignee_invalid\u0000Responsavel deve ser administrador da plataforma."] = "api.sql.solicitations.assignee_invalid.admin_required",
        ["solicitations.assignee_invalid\u0000Informe um responsavel valido da plataforma."] = "api.sql.solicitations.assignee_invalid.invalid",
        // solicitations.transition — uma mensagem canônica por transição
        ["solicitations.transition\u0000Somente solicitacoes abertas podem ir para triagem."] = "api.sql.solicitations.transition.triage_only_open",
        ["solicitations.transition\u0000Somente solicitacoes em triagem iniciam atendimento."] = "api.sql.solicitations.transition.start_only_triage",
        ["solicitations.transition\u0000Aguardando cliente exige triagem ou atendimento em curso sem pausa ativa."] = "api.sql.solicitations.transition.waiting_requires_active",
        ["solicitations.transition\u0000Relógio já pausado ou estado não permite pausa."] = "api.sql.solicitations.transition.already_paused",
        ["solicitations.transition\u0000Nenhum relógio pausado."] = "api.sql.solicitations.transition.no_paused_clock",
        ["solicitations.transition\u0000Estado atual nao permite resolucao."] = "api.sql.solicitations.transition.resolution_not_allowed",
        ["solicitations.transition\u0000Reatribuicao indisponivel neste estado."] = "api.sql.solicitations.transition.reassignment_not_allowed",
        ["solicitations.transition\u0000Somente solicitacoes resolvidas sao encerradas."] = "api.sql.solicitations.transition.close_only_resolved",
        ["solicitations.transition\u0000Estado atual nao permite cancelamento."] = "api.sql.solicitations.transition.cancel_not_allowed",
        ["solicitations.transition\u0000Solicitacao encerrada nao aceita mensagens."] = "api.sql.solicitations.transition.closed_no_messages",
        ["solicitations.transition\u0000Triagem obrigatoria antes de responder."] = "api.sql.solicitations.transition.triage_before_reply",
        // solicitations.validation_body
        ["solicitations.validation_body\u0000Descricao deve ter entre 10 e 8000 caracteres."] = "api.sql.solicitations.validation_body.description_length",
        ["solicitations.validation_body\u0000Mensagem vazia ou muito longa."] = "api.sql.solicitations.validation_body.message_length",
        // solicitations.validation_reason
        ["solicitations.validation_reason\u0000Informe a justificativa da espera."] = "api.sql.solicitations.validation_reason.waiting_reason",
        ["solicitations.validation_reason\u0000Toda pausa exige justificativa."] = "api.sql.solicitations.validation_reason.pause_reason",
        ["solicitations.validation_reason\u0000O cancelamento exige motivo."] = "api.sql.solicitations.validation_reason.cancel_reason"
    };

    /// <summary>Chave de catálogo do par (código, mensagem); null quando o par não tem mapeamento.</summary>
    public static string? KeyFor(string code, string message) =>
        Keys.TryGetValue($"{code}\u0000{message}", out var key) ? key : null;
}
