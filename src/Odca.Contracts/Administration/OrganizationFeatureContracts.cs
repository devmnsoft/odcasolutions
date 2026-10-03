namespace Odca.Contracts.Administration;

public sealed record OrganizationFeatureStatus(
    string FeatureCode,
    string DisplayName,
    string State,
    string? Reason,
    DateTimeOffset? BlockedAt,
    string? PlanEntitlement);

public sealed record OrganizationFeatureCatalogResponse(
    Guid TenantId,
    string TenantStatus,
    IReadOnlyList<OrganizationFeatureStatus> Features);

public sealed record SetOrganizationFeatureRequest(bool Blocked, string Reason);
