namespace Odca.Contracts.Plans;

public sealed record PlanCatalogResponse(
    string Code,
    int Version,
    string DisplayName,
    int ActiveSeats,
    long StorageBytes,
    long UserStorageBytes,
    long FileBytes,
    int OcrPagesMonthly,
    int SignatureEnvelopesMonthly);
