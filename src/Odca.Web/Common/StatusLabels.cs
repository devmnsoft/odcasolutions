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
}
