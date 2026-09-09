namespace Odca.Application.Plans;

public sealed record PlanCatalogItem(
    string Code,
    int Version,
    string DisplayName,
    int ActiveSeats,
    long StorageBytes,
    long UserStorageBytes,
    long FileBytes,
    int OcrPagesMonthly,
    int SignatureEnvelopesMonthly);
