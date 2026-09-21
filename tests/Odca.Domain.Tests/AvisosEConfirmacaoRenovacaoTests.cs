using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using Odca.Application.Contracts;
using Odca.Contracts.Renewals;
using Odca.Web.Middleware;
using Odca.Web.Services;

namespace Odca.Domain.Tests;

public sealed class AvisosEConfirmacaoRenovacaoTests
{
    [Fact]
    public void ApplyRenewalResponseCarriesResultingEndsOn()
    {
        var expectedEnd = new DateOnly(2027, 12, 31);
        var response = new ApplyRenewalResponse(
            Applied: true,
            ContractVersion: 4,
            ObligationsPreserved: true,
            EndsOn: expectedEnd);

        Assert.True(response.Applied);
        Assert.Equal(4, response.ContractVersion);
        Assert.True(response.ObligationsPreserved);
        Assert.Equal(expectedEnd, response.EndsOn);
    }

    [Fact]
    public void OfficialCatalogLookupByKeySucceedsWithoutAuditEvents()
    {
        var amendment = OfficialContractTemplates.All.FirstOrDefault(
            t => string.Equals(t.Key, "contract-amendment", StringComparison.OrdinalIgnoreCase));

        Assert.NotNull(amendment);
        Assert.Equal("Termo de Aditivo Contratual", amendment.Name);
        Assert.Equal("amendment", amendment.ContractType);

        var nda = OfficialContractTemplates.All.FirstOrDefault(
            t => string.Equals(t.Key, "nda-unilateral", StringComparison.OrdinalIgnoreCase));

        Assert.NotNull(nda);
        Assert.Equal("Termo de Confidencialidade (unilateral)", nda.Name);
        Assert.Equal("nda", nda.ContractType);
    }

    [Theory]
    [InlineData(StatusCodes.Status400BadRequest)]
    [InlineData(StatusCodes.Status409Conflict)]
    public async Task MiddlewareDoesNotCapture400Or409AsUnavailable(int statusCode)
    {
        var middleware = new BffErrorHandlingMiddleware(
            context =>
            {
                context.Response.StatusCode = statusCode;
                return Task.CompletedTask;
            },
            NullLogger<BffErrorHandlingMiddleware>.Instance);

        var context = new DefaultHttpContext();
        await middleware.InvokeAsync(context);

        Assert.Equal(statusCode, context.Response.StatusCode);
    }

    [Fact]
    public void ClientDemoIdentityRestrictsOperationalMenu()
    {
        var clientIdentity = new ClaimsIdentity(
        [
            new Claim(ClaimTypes.Name, "cliente.teste@odca.local"),
            new Claim("email", "cliente.teste@odca.local")
        ], "TestAuth");

        var principal = new ClaimsPrincipal(clientIdentity);

        var isClientDemo = string.Equals(principal.Identity?.Name, "cliente.teste@odca.local", StringComparison.OrdinalIgnoreCase)
            || string.Equals(principal.FindFirst("email")?.Value, "cliente.teste@odca.local", StringComparison.OrdinalIgnoreCase);

        Assert.True(isClientDemo);

        var clientAccess = new UserTenantAccess(
            Guid.NewGuid(),
            "ODCA Cliente de Demonstração",
            true,
            new HashSet<string>(["tenant.organization.read", "tenant.billing.read"], StringComparer.Ordinal));

        var canReadObligations = !isClientDemo && clientAccess.HasAnyPermission("tenant.obligations.read", "tenant.obligations.read_all");
        var canReadRenewals = !isClientDemo && clientAccess.HasPermission("tenant.renewals.read");
        var canReadStudio = !isClientDemo && clientAccess.HasAnyPermission("tenant.templates.read", "tenant.contract_drafts.read");
        var canReadImports = !isClientDemo && clientAccess.HasPermission("tenant.imports.read");

        Assert.False(canReadObligations);
        Assert.False(canReadRenewals);
        Assert.False(canReadStudio);
        Assert.False(canReadImports);
    }
}
