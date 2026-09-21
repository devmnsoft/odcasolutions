using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Odca.Web.Middleware;
using Odca.Web.Services;

namespace Odca.Domain.Tests;

public sealed class BffAccessAndIndisponibilidadeTests
{
    [Fact]
    public void OperatorPermissionsGrantOperationalAccess()
    {
        var operatorPermissions = new HashSet<string>(
        [
            "tenant.obligations.read",
            "tenant.obligations.read_all",
            "tenant.obligations.manage",
            "tenant.obligations.fulfill",
            "tenant.obligations.reopen",
            "tenant.obligations.cancel",
            "tenant.obligations.assign",
            "tenant.documents.download",
            "tenant.renewals.read",
            "tenant.renewals.prepare",
            "tenant.templates.read",
            "tenant.contract_drafts.read",
            "tenant.reviews.read",
            "tenant.imports.read",
            "tenant.saved_views.manage"
        ], StringComparer.Ordinal);

        var operatorAccess = new UserTenantAccess(
            Guid.NewGuid(),
            "Organização Demonstração",
            true,
            operatorPermissions);

        Assert.True(operatorAccess.IsActiveMembership);
        Assert.True(operatorAccess.HasAnyPermission("tenant.obligations.read", "tenant.obligations.read_all"));
        Assert.True(operatorAccess.HasPermission("tenant.renewals.read"));
        Assert.True(operatorAccess.HasAnyPermission("tenant.templates.read", "tenant.contract_drafts.read"));
        Assert.True(operatorAccess.HasPermission("tenant.imports.read"));
    }

    [Fact]
    public void ClientPermissionsDenyOperationalAccess()
    {
        var clientPermissions = new HashSet<string>(
        [
            "tenant.organization.read",
            "tenant.billing.read"
        ], StringComparer.Ordinal);

        var clientAccess = new UserTenantAccess(
            Guid.NewGuid(),
            "Organização Demonstração",
            true,
            clientPermissions);

        Assert.True(clientAccess.IsActiveMembership);
        Assert.False(clientAccess.HasAnyPermission("tenant.obligations.read", "tenant.obligations.read_all"));
        Assert.False(clientAccess.HasPermission("tenant.renewals.read"));
        Assert.False(clientAccess.HasAnyPermission("tenant.templates.read", "tenant.contract_drafts.read"));
        Assert.False(clientAccess.HasPermission("tenant.imports.read"));
        Assert.True(clientAccess.HasPermission("tenant.organization.read"));
    }

    [Fact]
    public void ExtractTenantIdResolvesFromRouteQueryOrPath()
    {
        var expected = Guid.NewGuid();

        // 1. From route values
        var routeContext = new DefaultHttpContext();
        routeContext.Request.RouteValues["tenantId"] = expected.ToString();
        Assert.Equal(expected, BffErrorHandlingMiddleware.ExtractTenantId(routeContext));

        // 2. From query string
        var queryContext = new DefaultHttpContext();
        queryContext.Request.QueryString = new QueryString($"?tenantId={expected}");
        Assert.Equal(expected, BffErrorHandlingMiddleware.ExtractTenantId(queryContext));

        // 3. From path /organizacoes/{tenantId}/caixa
        var pathContext = new DefaultHttpContext();
        pathContext.Request.Path = $"/organizacoes/{expected}/caixa";
        Assert.Equal(expected, BffErrorHandlingMiddleware.ExtractTenantId(pathContext));
    }

    [Fact]
    public void ExtractSubResolvesFromUserClaims()
    {
        var context = new DefaultHttpContext();
        Assert.Null(BffErrorHandlingMiddleware.ExtractSub(context));

        var sub = Guid.NewGuid().ToString();
        var identity = new ClaimsIdentity([new Claim("sub", sub)], "TestAuth");
        context.User = new ClaimsPrincipal(identity);

        Assert.Equal(sub, BffErrorHandlingMiddleware.ExtractSub(context));
    }
}
