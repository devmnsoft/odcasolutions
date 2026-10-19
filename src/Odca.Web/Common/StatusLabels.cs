namespace Odca.Web.Common;

/// <summary>
/// Traduz os estados de tenant e de assinatura (valores fixados pelas restrições do banco)
/// para rótulos em pt-BR usados nas telas administrativas. Estados desconhecidos são
/// exibidos como gravados para não ocultar novas situações.
/// </summary>
public static class StatusLabels
{
    public static string Status(string? value) => value switch
    {
        null or "" => "—",
        "pending" => "Pendente",
        "active" => "Ativa",
        "suspended" => "Suspensa",
        "cancelled" or "canceled" => "Cancelada",
        _ => value
    };

    /// <summary>D-OC50 (S16): apresentação localizada. Estados desconhecidos continuam sendo
    /// exibidos como gravados para não ocultar novas situações.</summary>
    public static string Status(string? value, Func<string, string> t) => value switch
    {
        null or "" => "—",
        "pending" => t("status.pending"),
        "active" => t("org.orgst_active"),
        "suspended" => t("org.orgst_suspended"),
        "cancelled" or "canceled" => t("status.cancelled"),
        _ => value
    };

    /// <summary>
    /// Traduz a situação comercial da assinatura (odca.subscriptions.commercial_state).
    /// D-OC31 B.5: a ativação administrativa (tenants.status) é independente do ciclo
    /// comercial; nenhum destes estados equivale a pagamento recebido.
    /// </summary>
    public static string Commercial(string? value) => value switch
    {
        null or "" => "—",
        "email_pending" => "Aguardando e-mail de boas-vindas (sem pagamento recebido)",
        "commercial_pending" => "Aguardando confirmação comercial (sem pagamento recebido)",
        "active" => "Ativa administrativa (o sistema não registra pagamento)",
        "suspended" => "Suspensa (sem pagamento recebido)",
        _ => value
    };

    /// <summary>D-OC50 (S16): apresentação localizada. A ativação administrativa permanece
    /// independente do ciclo comercial; nenhum destes estados equivale a pagamento.</summary>
    public static string Commercial(string? value, Func<string, string> t) => value switch
    {
        null or "" => "—",
        "email_pending" => t("status.commercial_email_pending"),
        "commercial_pending" => t("status.commercial_pending"),
        "active" => t("status.commercial_active"),
        "suspended" => t("status.commercial_suspended"),
        _ => value
    };
}
