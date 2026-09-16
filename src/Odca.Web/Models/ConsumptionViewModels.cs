using Odca.Contracts.Consumption;
namespace Odca.Web.Models;
public sealed record ConsumptionViewModel(ConsumptionSummary Summary,IReadOnlyList<StoragePackage> Packages,IReadOnlyList<AdditionalStorageRequest> Requests);
public sealed record CustomerListViewModel(IReadOnlyList<PlatformCustomer> Customers,string? Search);
