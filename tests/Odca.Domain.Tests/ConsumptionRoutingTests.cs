using Microsoft.AspNetCore.Mvc;

namespace Odca.Domain.Tests;

public sealed class ConsumptionRoutingTests
{
    [Fact]
    public void ApiSubmissionHasBusinessNameAndPreservesPublicPostRoute()
    {
        var method = typeof(global::Odca.Api.Controllers.ConsumptionController)
            .GetMethod("SubmitStorageRequest");

        Assert.NotNull(method);
        var route = Assert.Single(method!.GetCustomAttributes(typeof(HttpPostAttribute), true)
            .Cast<HttpPostAttribute>());
        Assert.Equal("api/v1/organizations/{tenantId:guid}/storage-requests", route.Template);
        Assert.Null(typeof(global::Odca.Api.Controllers.ConsumptionController).GetMethod("Request"));
    }

    [Fact]
    public void WebSubmissionHasBusinessNamePreservesRouteAndRequiresAntiforgery()
    {
        var method = typeof(global::Odca.Web.Controllers.ConsumptionController)
            .GetMethod("CreateStorageRequest");

        Assert.NotNull(method);
        var route = Assert.Single(method!.GetCustomAttributes(typeof(HttpPostAttribute), true)
            .Cast<HttpPostAttribute>());
        Assert.Equal("solicitacoes", route.Template);
        Assert.Single(method.GetCustomAttributes(typeof(ValidateAntiForgeryTokenAttribute), true));
        Assert.Null(typeof(global::Odca.Web.Controllers.ConsumptionController).GetMethod("Request"));
    }

    [Fact]
    public void OrganizationStatusActionsAreExplicitPostsAndWebRequiresAntiforgery()
    {
        var api = typeof(global::Odca.Api.Controllers.ConsumptionController);
        Assert.Equal("api/v1/platform/customers/{tenantId:guid}/suspend", Assert.Single(api.GetMethod("Suspend")!.GetCustomAttributes(typeof(HttpPostAttribute), true).Cast<HttpPostAttribute>()).Template);
        Assert.Equal("api/v1/platform/customers/{tenantId:guid}/restore", Assert.Single(api.GetMethod("Restore")!.GetCustomAttributes(typeof(HttpPostAttribute), true).Cast<HttpPostAttribute>()).Template);

        var web = typeof(global::Odca.Web.Controllers.CustomersController).GetMethod("ChangeStatus");
        Assert.NotNull(web);
        Assert.Single(web!.GetCustomAttributes(typeof(HttpPostAttribute), true));
        Assert.Single(web.GetCustomAttributes(typeof(ValidateAntiForgeryTokenAttribute), true));
    }
}
