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

    [Fact]
    public void RelativeDateIsAllowedInInboxAndNormalized()
    {
        var filters = new Dictionary<string, string> { ["scope"] = "mine", ["relativeDate"] = "dueThisWeek" };

        var valid = SavedViewFilterPolicy.TryNormalize("inbox", filters, null, out var normalized, out _, out var error);

        Assert.True(valid, error);
        Assert.Equal("dueThisWeek", normalized["relativeDate"]);
        Assert.DoesNotContain("page", normalized.Keys);
    }

    [Theory]
    [InlineData("from", "2026-09-01")]
    [InlineData("to", "2026-09-30")]
    public void MixingRelativeDateWithAbsoluteDatesIsRejected(string dateKey, string dateValue)
    {
        var filters = new Dictionary<string, string>
        {
            ["relativeDate"] = "dueToday",
            [dateKey] = dateValue
        };

        var valid = SavedViewFilterPolicy.TryNormalize("obligations", filters, null, out _, out _, out var error);

        Assert.False(valid);
        Assert.Contains("concomitante", error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void InvalidRelativeDateInFilterIsRejected()
    {
        var filters = new Dictionary<string, string> { ["relativeDate"] = "invalid_token" };

        var valid = SavedViewFilterPolicy.TryNormalize("inbox", filters, null, out _, out _, out var error);

        Assert.False(valid);
        Assert.Contains("relativa", error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void InboxRoutePreservesViewIdAndRelativeDateWithoutPage()
    {
        var tenant = Guid.Parse("11111111-1111-1111-1111-111111111111");
        var viewId = Guid.Parse("22222222-2222-2222-2222-222222222222");
        var view = new SavedViewItem(viewId, "Minha Vista", "inbox", new Dictionary<string, string>
        {
            ["scope"] = "mine",
            ["relativeDate"] = "overdue",
            ["kind"] = "Obligation"
        }, "", true, 1, DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch);

        var route = InboxController.BuildSavedViewRoute(tenant, view);

        Assert.Equal(tenant, route["tenantId"]);
        Assert.Equal(viewId, route["viewId"]);
        Assert.Equal("mine", route["scope"]);
        Assert.Equal("overdue", route["relativeDate"]);
        Assert.Equal("Obligation", route["kind"]);
        Assert.Equal(1, route["page"]);
    }

    private static SavedViewItem Item(Guid id, IReadOnlyDictionary<string, string> filters) =>
        new(id, "Fiscal", "obligations", filters, "due_date", false, 1, DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch);
}
