namespace Odca.Web.Models;

public static class TenancyDisplay
{
    public static readonly (string Code, string Label, string Group)[] PermissionCatalog =
    [
        ("tenant.organization.read", "Consultar organização", "Organização"),
        ("tenant.organization.manage", "Editar organização", "Organização"),
        ("tenant.team.read", "Consultar equipe e perfis", "Equipe"),
        ("tenant.team.manage", "Gerenciar equipe, perfis e convites", "Equipe")
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
