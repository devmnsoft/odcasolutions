namespace Odca.Web.Models;

public static class ContractsDisplay
{
    public static string OperationalStatus(string? code) => code?.Trim().ToLowerInvariant() switch
    {
        "draft" => "Rascunho",
        "active" => "Ativo",
        "closed" => "Encerrado",
        "cancelled" => "Cancelado",
        "inactive" => "Inativo",
        _ => string.IsNullOrWhiteSpace(code) ? "Desconhecido" : code
    };

    public static string OperationalStatusClass(string? code) => code?.Trim().ToLowerInvariant() switch
    {
        "draft" => "badge badge-muted",
        "active" => "badge badge-success",
        "closed" => "badge badge-muted",
        "cancelled" => "badge badge-danger",
        "inactive" => "badge badge-warning",
        _ => "badge"
    };

    public static string TemporalStatus(string? code) => code?.Trim() switch
    {
        "Upcoming" => "A iniciar",
        "Active" => "Em vigência",
        "ApproachingEnd" => "Próximo do fim",
        "Expired" => "Vencido",
        "Indefinite" => "Prazo indeterminado",
        _ => string.IsNullOrWhiteSpace(code) ? "—" : code
    };

    public static string TemporalStatusClass(string? code) => code?.Trim() switch
    {
        "Upcoming" => "badge badge-muted",
        "Active" => "badge badge-success",
        "ApproachingEnd" => "badge badge-warning",
        "Expired" => "badge badge-danger",
        "Indefinite" => "badge badge-muted",
        _ => "badge"
    };

    public static string RenewalDecision(string? code) => code?.Trim().ToLowerInvariant() switch
    {
        "pending" => "Pendente",
        "renew" => "Renovar",
        "do_not_renew" => "Não renovar",
        "not_applicable" => "Não se aplica",
        _ => string.IsNullOrWhiteSpace(code) ? "—" : code
    };

    public static string AmountPeriodicity(string? code) => code?.Trim().ToLowerInvariant() switch
    {
        "once" => "Único",
        "monthly" => "Mensal",
        "yearly" => "Anual",
        "other" => "Outro",
        _ => string.IsNullOrWhiteSpace(code) ? "—" : code
    };

    public static string CounterpartyStatus(string? code) => code?.Trim().ToLowerInvariant() switch
    {
        "active" => "Ativa",
        "inactive" => "Inativa",
        _ => string.IsNullOrWhiteSpace(code) ? "Desconhecido" : code
    };

    public static string CounterpartyStatusClass(string? code) => code?.Trim().ToLowerInvariant() switch
    {
        "active" => "badge badge-success",
        "inactive" => "badge badge-muted",
        _ => "badge"
    };

    public static string PersonType(string? code) => code?.Trim().ToLowerInvariant() switch
    {
        "individual" => "Pessoa física",
        "organization" => "Pessoa jurídica",
        _ => string.IsNullOrWhiteSpace(code) ? "—" : code
    };

    public static string DocumentType(string? code) => code?.Trim().ToLowerInvariant() switch
    {
        "cpf" => "CPF",
        "cnpj" => "CNPJ",
        "none" => "Sem documento",
        _ => string.IsNullOrWhiteSpace(code) ? "—" : code.ToUpperInvariant()
    };

    public static string ContractTypeStatus(string? code) => code?.Trim().ToLowerInvariant() switch
    {
        "active" => "Ativo",
        "inactive" => "Inativo",
        _ => string.IsNullOrWhiteSpace(code) ? "Desconhecido" : code
    };

    public static string ContractTypeStatusClass(string? code) => code?.Trim().ToLowerInvariant() switch
    {
        "active" => "badge badge-success",
        "inactive" => "badge badge-muted",
        _ => "badge"
    };

    public static string NotificationStatus(string? code) => code?.Trim().ToLowerInvariant() switch
    {
        "unread" => "Não lida",
        "read" => "Lida",
        _ => string.IsNullOrWhiteSpace(code) ? "—" : code
    };

    public static string NotificationStatusClass(string? code) => code?.Trim().ToLowerInvariant() switch
    {
        "unread" => "badge badge-warning",
        "read" => "badge badge-muted",
        _ => "badge"
    };

    public static string ContractEvent(string? action) => action?.Trim().ToLowerInvariant() switch
    {
        "contract.created" => "Contrato criado",
        "contract.updated" => "Contrato atualizado",
        "contract.activated" => "Contrato ativado",
        "contract.renewed" => "Contrato renovado",
        "contract.closed" => "Contrato encerrado",
        "contract.cancelled" => "Contrato cancelado",
        "contract.deleted" => "Contrato excluído",
        "contract.restored" => "Contrato restaurado",
        "contract.seeded" => "Contrato provisionado",
        _ => string.IsNullOrWhiteSpace(action) ? "Evento" : action
    };

    public static string FormatTerm(DateOnly start, DateOnly? end, bool isIndefinite)
    {
        var culture = System.Globalization.CultureInfo.GetCultureInfo("pt-BR");
        if (isIndefinite)
        {
            return $"{start.ToString("dd/MM/yyyy", culture)} — indeterminado";
        }

        return end is DateOnly value
            ? $"{start.ToString("dd/MM/yyyy", culture)} — {value.ToString("dd/MM/yyyy", culture)}"
            : start.ToString("dd/MM/yyyy", culture);
    }

    public static string MaskDocument(string? display, string? documentType)
    {
        if (string.IsNullOrWhiteSpace(display))
        {
            return "—";
        }

        var digits = new string(display.Where(char.IsDigit).ToArray());
        if (string.Equals(documentType, "cpf", StringComparison.OrdinalIgnoreCase) && digits.Length == 11)
        {
            return $"***.{digits[3..6]}.{digits[6..9]}-**";
        }

        if (string.Equals(documentType, "cnpj", StringComparison.OrdinalIgnoreCase) && digits.Length == 14)
        {
            return $"**.{digits[2..5]}.{digits[5..8]}/****-{digits[12..]}";
        }

        if (display.Length <= 4)
        {
            return "****";
        }

        return $"{display[..2]}…{display[^2..]}";
    }

    public static string Classifications(bool client, bool supplier, bool partner, bool provider)
    {
        var labels = new List<string>(4);
        if (client) labels.Add("Cliente");
        if (supplier) labels.Add("Fornecedor");
        if (partner) labels.Add("Parceiro");
        if (provider) labels.Add("Prestador");
        return labels.Count == 0 ? "—" : string.Join(", ", labels);
    }
}
