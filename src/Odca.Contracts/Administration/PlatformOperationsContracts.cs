namespace Odca.Contracts.Administration;

// Catálogo global oficial de modelos (gestão da plataforma, decisão D-OC1/D-OC7):
// listar linhas globais publicadas/retiradas, versões e publicar/retirar com justificativa.
public sealed record TemplateCatalogVersion(
    int VersionNumber,
    string? CreatedAt,
    string? PublishedAt);

public sealed record TemplateCatalogItem(
    string OfficialKey,
    string Name,
    string Description,
    string ContractType,
    string Status,
    int CurrentRevision,
    DateTimeOffset? CreatedAt,
    DateTimeOffset? PublishedAt,
    IReadOnlyList<TemplateCatalogVersion> Versions);

public sealed record TemplateCatalogResponse(
    IReadOnlyList<TemplateCatalogItem> Items);

public sealed record TemplateCatalogStatusRequest(
    string Action,
    string Reason);

// Busca global de usuários (identidade + vínculos por organização).
public sealed record PlatformUserLink(
    Guid? TenantId,
    string? TenantName,
    string? MembershipStatus,
    string? RoleCode);

public sealed record PlatformUserRow(
    Guid UserId,
    string Email,
    string DisplayName,
    bool IsPlatformAdministrator,
    bool IsDeleted,
    IReadOnlyList<PlatformUserLink> Links);

// Políticas de SLA vigentes (visão de leitura das configurações operacionais).
public sealed record SlaPolicyRow(
    string PlanCode,
    string Service,
    string Priority,
    int FirstResponseMinutes,
    int ResolutionMinutes,
    string Timezone,
    string Calendar,
    string BusinessStart,
    string BusinessEnd,
    bool Enabled);
