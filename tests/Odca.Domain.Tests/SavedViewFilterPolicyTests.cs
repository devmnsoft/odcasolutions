using Odca.Contracts.SavedViews;
using Odca.Web.Controllers;

namespace Odca.Domain.Tests;

public sealed class SavedViewFilterPolicyTests
{
    [Fact]
    public void RouteUsesOnlyValidatedFiltersAndRestoresTrustedContext()
    {
        var tenant = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
        var viewId = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");
        var view = Item(viewId, new Dictionary<string, string>
        {
            ["search"] = "ação & revisão + fiscal",
            ["status"] = "open"
        });

        var route = ObligationsController.BuildSavedViewRoute(tenant, view);

        Assert.Equal("ação & revisão + fiscal", route["search"]);
        Assert.Equal("open", route["status"]);
        Assert.Equal(tenant, route["tenantId"]);
        Assert.Equal(viewId, route["viewId"]);
        Assert.Equal(1, route["page"]);
    }

    [Theory]
    [InlineData("tenantId")]
    [InlineData("controller")]
    [InlineData("action")]
    [InlineData("area")]
    [InlineData("returnUrl")]
    [InlineData("page")]
    public void ReservedRouteKeysAreRejected(string key)
    {
        var filters = new Dictionary<string, string> { [key] = "attacker-controlled" };

        var valid = SavedViewFilterPolicy.TryNormalize("obligations", filters, "due_date", out _, out _, out var error);

        Assert.False(valid);
        Assert.NotNull(error);
    }

    [Fact]
    public void DatesAreInvariantAndOrdered()
    {
        var filters = new Dictionary<string, string> { ["from"] = "2027-01-01", ["to"] = "2026-12-31" };

        Assert.False(SavedViewFilterPolicy.TryNormalize("obligations", filters, null, out _, out _, out var error));
        Assert.Contains("data final", error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void PageIsNeverPersisted()
    {
        var filters = new Dictionary<string, string> { ["scope"] = "mine", ["page"] = "8" };

        Assert.False(SavedViewFilterPolicy.TryNormalize("obligations", filters, null, out _, out _, out _));
    }

    private static SavedViewItem Item(Guid id, IReadOnlyDictionary<string, string> filters) =>
        new(id, "Fiscal", "obligations", filters, "due_date", false, 1, DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch);
}
