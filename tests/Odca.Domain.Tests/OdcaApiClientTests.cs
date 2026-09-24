using System.Globalization;
using System.Net;
using Odca.Contracts.Patients;
using Odca.Web.Services;

namespace Odca.Domain.Tests;

public sealed class OdcaApiClientTests
{
    [Fact]
    public async Task ValidationProblemPreservesFieldErrorsAndStatus()
    {
        const string problem = """
            {"type":"https://tools.ietf.org/html/rfc9110#section-15.5.1","title":"One or more validation errors occurred.","status":400,"errors":{"fullName":["Informe o nome completo."],"representative.relationship":["Informe a relação."],"unknown":["Revise este dado."]}}
            """;
        var handler = new CapturingHandler(problem, HttpStatusCode.BadRequest);
        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://odca.test/") };
        var client = new OdcaApiClient(http);

        var result = await client.CreatePatientAsync("token", Guid.NewGuid(),
            new SavePatientRequest("", null, null, null, null, null, null, null), CancellationToken.None);

        Assert.Equal(ApiCallStatus.InvalidRequest, result.Status);
        Assert.Equal("Informe o nome completo.", result.ValidationErrors!["fullName"].Single());
        Assert.Equal("Informe a relação.", result.ValidationErrors["representative.relationship"].Single());
        Assert.Equal("Revise este dado.", result.ValidationErrors["unknown"].Single());
    }

    [Theory]
    [InlineData(HttpStatusCode.Forbidden, ApiCallStatus.Forbidden)]
    [InlineData(HttpStatusCode.NotFound, ApiCallStatus.NotFound)]
    [InlineData(HttpStatusCode.Conflict, ApiCallStatus.Conflict)]
    public async Task PatientFailuresKeepDistinctHttpSemantics(HttpStatusCode status, ApiCallStatus expected)
    {
        var handler = new CapturingHandler("{\"title\":\"Falha controlada\",\"code\":\"patient.version.conflict\"}", status);
        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://odca.test/") };
        var client = new OdcaApiClient(http);

        var result = await client.GetPatientAsync("token", Guid.NewGuid(), Guid.NewGuid(), CancellationToken.None);

        Assert.Equal(expected, result.Status);
        Assert.Equal("patient.version.conflict", result.ErrorCode);
    }

    [Theory]
    [InlineData("pt-BR")]
    [InlineData("ar-SA")]
    public async Task ImportDatesUseInvariantWireFormat(string cultureName)
    {
        var originalCulture = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(cultureName);
            var handler = new CapturingHandler();
            using var http = new HttpClient(handler) { BaseAddress = new Uri("https://odca.test/") };
            var client = new OdcaApiClient(http);

            await client.GetContractImportsAsync("token", Guid.NewGuid(), null, false, false,
                new DateOnly(2026, 9, 3), new DateOnly(2026, 10, 4), CancellationToken.None);

            Assert.Contains("from=2026-09-03", handler.RequestUri!.Query, StringComparison.Ordinal);
            Assert.Contains("to=2026-10-04", handler.RequestUri.Query, StringComparison.Ordinal);
        }
        finally
        {
            CultureInfo.CurrentCulture = originalCulture;
        }
    }

    [Theory]
    [InlineData("pt-BR")]
    [InlineData("ar-SA")]
    public async Task ObligationDatesUseInvariantWireFormatWithoutChangingCurrentCulture(string cultureName)
    {
        var originalCulture = CultureInfo.CurrentCulture;
        var originalUiCulture = CultureInfo.CurrentUICulture;
        try
        {
            var culture = CultureInfo.GetCultureInfo(cultureName);
            CultureInfo.CurrentCulture = culture;
            CultureInfo.CurrentUICulture = culture;
            var handler = new CapturingHandler();
            using var http = new HttpClient(handler) { BaseAddress = new Uri("https://odca.test/") };
            var client = new OdcaApiClient(http);

            await client.GetObligationsAsync("token", Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"),
                "mine", null, null, null, null, new DateOnly(2026, 9, 3), new DateOnly(2026, 10, 4),
                null, 1, 20, CancellationToken.None);

            Assert.Contains("from=2026-09-03", handler.RequestUri!.Query, StringComparison.Ordinal);
            Assert.Contains("to=2026-10-04", handler.RequestUri.Query, StringComparison.Ordinal);
        }
        finally
        {
            CultureInfo.CurrentCulture = originalCulture;
            CultureInfo.CurrentUICulture = originalUiCulture;
        }
    }

    [Fact]
    public async Task MissingOptionalDatesAreOmittedFromQuery()
    {
        var handler = new CapturingHandler();
        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://odca.test/") };
        var client = new OdcaApiClient(http);

        await client.GetObligationsAsync("token", Guid.NewGuid(), "mine", null, null, null, null,
            null, null, "prazo especial", 2, 20, CancellationToken.None);

        Assert.DoesNotContain("from=", handler.RequestUri!.Query, StringComparison.Ordinal);
        Assert.DoesNotContain("to=", handler.RequestUri.Query, StringComparison.Ordinal);
        Assert.Contains("search=prazo%20especial", handler.RequestUri.Query, StringComparison.Ordinal);
    }

    [Fact]
    public async Task SpecialSearchCharactersAreEncodedWithoutChangingTheirMeaning()
    {
        var handler = new CapturingHandler();
        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://odca.test/") };
        var client = new OdcaApiClient(http);

        await client.GetObligationsAsync("token", Guid.NewGuid(), "mine", null, null, null, null,
            null, null, "ação & revisão + fiscal", 1, 20, CancellationToken.None);

        Assert.Contains($"search={Uri.EscapeDataString("ação & revisão + fiscal")}", handler.RequestUri!.Query, StringComparison.Ordinal);
        Assert.DoesNotContain("ação & revisão + fiscal", handler.RequestUri.Query, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ObligationHistoryUsesTenantScopedRoute()
    {
        var handler = new CapturingHandler("{\"obligationId\":\"bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb\",\"title\":\"Relatório\",\"events\":[]}");
        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://odca.test/") };
        var client = new OdcaApiClient(http);

        var result = await client.GetObligationHistoryAsync(
            "token",
            Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"),
            Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb"),
            CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.Equal("/api/v1/organizations/aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa/obligations/bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb/history", handler.RequestUri!.AbsolutePath);
    }

    [Fact]
    public async Task ReviewFiltersAreEncodedAndUseInvariantDates()
    {
        var handler = new CapturingHandler();
        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://odca.test/") };
        var client = new OdcaApiClient(http);

        await client.GetReviewsAsync("token", Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"),
            "in_review", "assigned", null, null, new DateOnly(2026, 9, 3), new DateOnly(2026, 10, 4),
            "ação & revisão", 2, CancellationToken.None);

        Assert.Contains("from=2026-09-03", handler.RequestUri!.Query, StringComparison.Ordinal);
        Assert.Contains("to=2026-10-04", handler.RequestUri.Query, StringComparison.Ordinal);
        Assert.Contains($"search={Uri.EscapeDataString("ação & revisão")}", handler.RequestUri.Query, StringComparison.Ordinal);
        Assert.Contains("scope=assigned", handler.RequestUri.Query, StringComparison.Ordinal);
        Assert.Contains("page=2", handler.RequestUri.Query, StringComparison.Ordinal);
    }

    private sealed class CapturingHandler : HttpMessageHandler
    {
        private readonly string response;
        private readonly HttpStatusCode status;

        public CapturingHandler(string response = "{\"items\":[],\"page\":1,\"pageSize\":20,\"total\":0}", HttpStatusCode status = HttpStatusCode.OK)
        {
            this.response = response;
            this.status = status;
        }

        public Uri? RequestUri { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            RequestUri = request.RequestUri;
            return Task.FromResult(new HttpResponseMessage(status)
            {
                Content = new StringContent(response)
            });
        }
    }
}
