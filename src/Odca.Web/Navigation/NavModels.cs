namespace Odca.Web.Navigation;

/// <summary>
/// Estado da solicitação atual usado para decidir visibilidade e estado ativo dos itens de
/// navegação (decisão D-OC4). Os campos espelham as variáveis já calculadas em _Layout.cshtml.
/// </summary>
public sealed class NavContext
{
    public required string? Controller { get; init; }
    public required string? Action { get; init; }
    public required IReadOnlyDictionary<string, string> Query { get; init; }
    public string? ActiveTenantId { get; init; }
    public string? TenantId { get; init; }
    public string? ContractId { get; init; }
    public bool IsPlatform { get; init; }
    public bool HasActiveMembership { get; init; }
    public bool CanReadObligations { get; init; }
    public bool CanReadRenewals { get; init; }
    public bool CanReadStudio { get; init; }
    public bool CanReadDrafts { get; init; }
    public bool CanCreateDocument { get; init; }
    public bool CanReadPatients { get; init; }
    public bool CanReadReviews { get; init; }
    public bool CanReadSolicitations { get; init; }
    public bool CanReadContracts { get; init; }
    public bool CanReadOrganization { get; init; }
    public bool CanAccessWorkspace { get; init; }
    public bool CanReadDocuments { get; init; }
    public bool SituationUnconfirmed { get; init; }
    public bool ReviewsPlanRestricted { get; init; }

    public bool HasTenant => !string.IsNullOrWhiteSpace(TenantId);
    public bool HasContract => !string.IsNullOrWhiteSpace(ContractId);

    public string? Q(string key) => Query.GetValueOrDefault(key);
}

/// <summary>
/// Item de navegação orientado a dados: chave estável, grupo, rótulo (pt-BR por ora; D-OC5
/// substituirá por chaves de catálogo i18n), ícone, rota, recurso de plano e disponibilidade.
/// Rótulos que passam pelo estado de funcionalidade usam FeatureCode (MenuFeature).
/// </summary>
public sealed record NavItem(
    string Key,
    string Label,
    string Icon,
    string Controller,
    string Action,
    string? Group = null,
    string? GroupStyle = null,
    string? FeatureCode = null,
    string? AriaLabel = null,
    string? Fragment = null,
    Func<NavContext, IDictionary<string, string>?>? BuildRoute = null,
    Func<NavContext, bool>? Available = null,
    Func<NavContext, bool>? IsActive = null);
