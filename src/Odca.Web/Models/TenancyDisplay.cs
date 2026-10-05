namespace Odca.Web.Models;

public static class TenancyDisplay
{
    public static readonly (string Code, string Label, string Group, string? Module)[] PermissionCatalog =
    [
        ("tenant.organization.read", "Consultar organização", "Organização", null),
        ("tenant.organization.manage", "Editar organização", "Organização", null),
        ("tenant.team.read", "Consultar equipe", "Equipe", null),
        ("tenant.team.manage", "Administrar equipe e convites", "Equipe", null),
        ("tenant.patients.read", "Consultar pacientes", "Pacientes", "patients"),
        ("tenant.patients.manage", "Cadastrar e editar pacientes", "Pacientes", "patients"),
        ("tenant.patients.documents.read", "Consultar acervo do paciente", "Pacientes", "patients"),
        ("tenant.templates.read", "Consultar modelos", "Modelos", "templates"),
        ("tenant.templates.manage", "Editar e publicar modelos", "Modelos", "templates"),
        ("tenant.contract_drafts.read", "Consultar minutas", "Minutas", "contract_drafts"),
        ("tenant.contract_drafts.manage", "Editar minutas e gerar documento", "Minutas", "contract_drafts"),
        ("tenant.contract_copies.issue", "Emitir cópia rastreável", "Minutas", "contract_drafts"),
        ("tenant.documents.download", "Baixar documento e PDF", "Documentos", "documents"),
        ("tenant.documents.manage", "Editar documentos", "Documentos", "documents"),
        ("tenant.contracts.read", "Consultar contratos", "Documentos", "documents"),
        ("tenant.contracts.history", "Consultar histórico contratual", "Documentos", "documents"),
        ("tenant.extractions.request", "Solicitar extração", "Documentos", "documents"),
        ("tenant.extractions.review", "Revisar extração", "Documentos", "documents"),
        ("tenant.extractions.apply", "Aplicar extração", "Documentos", "documents"),
        ("tenant.reviews.read", "Consultar revisão", "Revisões", "reviews"),
        ("tenant.reviews.request", "Solicitar revisão", "Revisões", "reviews"),
        ("tenant.reviews.decide", "Revisar e aprovar", "Revisões", "reviews"),
        ("tenant.reviews.reassign", "Atribuir responsável da revisão", "Revisões", "reviews"),
        ("tenant.reviews.cancel", "Cancelar revisão", "Revisões", "reviews"),
        ("tenant.reviews.history", "Consultar histórico da revisão", "Revisões", "reviews"),
        ("tenant.imports.read", "Consultar importações", "Importações", "imports"),
        ("tenant.imports.manage", "Cadastrar e reprocessar importações", "Importações", "imports"),
        ("tenant.imports.confirm", "Confirmar importação", "Importações", "imports"),
        ("tenant.billing.read", "Consultar plano, consumo e auditoria", "Plano e auditoria", null),
        ("tenant.billing.manage", "Solicitar mudanças comerciais", "Plano e auditoria", null),
        ("tenant.obligations.read", "Consultar obrigações próprias", "Obrigações", null),
        ("tenant.obligations.read_all", "Consultar obrigações da organização", "Obrigações", null),
        ("tenant.obligations.manage", "Cadastrar e editar obrigações", "Obrigações", null),
        ("tenant.obligations.assign", "Atribuir responsável", "Obrigações", null),
        ("tenant.obligations.fulfill", "Registrar cumprimento", "Obrigações", null),
        ("tenant.obligations.reopen", "Reabrir obrigação", "Obrigações", null),
        ("tenant.obligations.cancel", "Cancelar obrigação", "Obrigações", null),
        ("tenant.obligations.recurrence", "Gerenciar recorrência", "Obrigações", null),
        ("tenant.renewals.read", "Consultar renovações", "Renovações", null),
        ("tenant.renewals.prepare", "Preparar renovação", "Renovações", null),
        ("tenant.renewals.submit", "Encaminhar renovação", "Renovações", null),
        ("tenant.renewals.decide", "Decidir renovação", "Renovações", null),
        ("tenant.renewals.register", "Registrar renovação", "Renovações", null),
        ("tenant.renewals.formalize", "Formalizar renovação", "Renovações", null),
        ("tenant.renewals.apply", "Aplicar renovação", "Renovações", null),
        ("tenant.renewals.cancel", "Cancelar renovação", "Renovações", null),
        ("tenant.saved_views.manage", "Gerenciar filtros salvos", "Trabalho", null),
        ("tenant.privacy.requests.triage", "Triar solicitações de privacidade", "Privacidade", null),
        ("tenant.privacy.requests.respond", "Responder solicitações de privacidade", "Privacidade", null),
        ("tenant.privacy.legal_holds.manage", "Gerenciar bloqueios legais", "Privacidade", null)
    ];

    public static string MemberStatus(string? code) => code switch
    {
        "active" => "Ativo",
        "blocked" => "Bloqueado",
        "inactive" => "Inativo",
        _ => string.IsNullOrWhiteSpace(code) ? "Desconhecido" : code
    };

    public static string MemberStatusClass(string? code) => code switch
    {
        "active" => "badge badge-success",
        "blocked" => "badge badge-danger",
        "inactive" => "badge badge-muted",
        _ => "badge"
    };

    public static string InvitationStatus(string? code) => code switch
    {
        "pending" => "Pendente",
        "sent" => "Enviado",
        "failed" => "Falha no envio",
        "accepted" => "Aceito",
        "cancelled" => "Cancelado",
        "expired" => "Expirado",
        _ => string.IsNullOrWhiteSpace(code) ? "Desconhecido" : code
    };

    public static string InvitationStatusClass(string? code) => code switch
    {
        "pending" => "badge badge-warning",
        "sent" => "badge badge-success",
        "failed" => "badge badge-danger",
        "accepted" => "badge badge-success",
        "cancelled" => "badge badge-muted",
        "expired" => "badge badge-muted",
        _ => "badge"
    };

    public static string DeliveryStatus(string? code) => code switch
    {
        "pending" => "Na fila",
        "leased" => "Em processamento",
        "sent" => "Entregue ao transporte",
        "failed" => "Falha na entrega",
        _ => string.IsNullOrWhiteSpace(code) ? "Indisponível" : code
    };

    public static string DeliveryStatusClass(string? code) => code switch
    {
        "pending" => "badge badge-warning",
        "leased" => "badge badge-warning",
        "sent" => "badge badge-success",
        "failed" => "badge badge-danger",
        _ => "badge"
    };

    public static string OrganizationStatus(string? code) => code switch
    {
        "active" => "Ativa",
        "suspended" => "Suspensa",
        "inactive" => "Inativa",
        _ => string.IsNullOrWhiteSpace(code) ? "Desconhecido" : code
    };

    public static string RolePresentation(string? name) =>
        string.Equals(name, "Administrador da organização", StringComparison.Ordinal)
            ? "Superadministrador da organização"
            : string.IsNullOrWhiteSpace(name) ? "Perfil" : name;

    public static string PermissionLabel(string code)
    {
        foreach (var item in PermissionCatalog)
        {
            if (string.Equals(item.Code, code, StringComparison.Ordinal))
            {
                return item.Label;
            }
        }

        return code;
    }

    public static string DeliveryErrorMessage(string? code) => code switch
    {
        null or "" => string.Empty,
        "transport_unavailable" => "O transporte de notificação está indisponível no momento.",
        "invalid_destination" => "O destino do convite foi recusado pelo transporte.",
        "rate_limited" => "O envio foi limitado temporariamente. Tente reenviar mais tarde.",
        _ => "Não foi possível concluir a entrega. A fila não significa entrega confirmada."
    };

    public static bool IsSafeLocalUrl(string? url) =>
        !string.IsNullOrWhiteSpace(url)
        && url.StartsWith('/')
        && !url.StartsWith("//", StringComparison.Ordinal)
        && !url.Contains('\\', StringComparison.Ordinal);
}
