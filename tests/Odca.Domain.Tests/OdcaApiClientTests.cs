using System.Globalization;
using System.Net;
using Microsoft.AspNetCore.Http;
using Odca.Contracts.Patients;
using Odca.Web.Middleware;
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
        var client = new OdcaApiClient(http, NoHttpContextAccessor.Instance);

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
        var client = new OdcaApiClient(http, NoHttpContextAccessor.Instance);

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
            var client = new OdcaApiClient(http, NoHttpContextAccessor.Instance);

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
            var client = new OdcaApiClient(http, NoHttpContextAccessor.Instance);

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
        var client = new OdcaApiClient(http, NoHttpContextAccessor.Instance);

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
        var client = new OdcaApiClient(http, NoHttpContextAccessor.Instance);

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
        var client = new OdcaApiClient(http, NoHttpContextAccessor.Instance);

        var result = await client.GetObligationHistoryAsync(
            "token",
            Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"),
            Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb"),
            CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.Equal("/api/v1/organizations/aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa/obligations/bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb/history", handler.RequestUri!.AbsolutePath);
    }

    /// <summary>D-OC38 (Sprint 4/A-1): o formulário de ações relê a obrigação individual para obter
    /// a versão fresca; a rota mantém o escopo do tenant.</summary>
    [Fact]
    public async Task ObligationSingleGetUsesTenantScopedRouteAndParsesEvidenceFlag()
    {
        var handler = new CapturingHandler("""
            {"id":"bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb","contractId":"cccccccc-cccc-cccc-cccc-cccccccccccc","contract":"Contrato","title":"Prazo","category":"other","obligatedParty":"Contratada","ownerId":"dddddddd-dddd-dddd-dddd-dddddddddddd","owner":"Dona","dueDate":"2026-12-01","priority":"normal","status":"open","overdue":false,"ownerBlocked":false,"version":1,"amount":null,"currency":null,"nextAction":"Andamento","evidenceRequired":true}
            """);
        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://odca.test/") };
        var client = new OdcaApiClient(http, NoHttpContextAccessor.Instance);

        var result = await client.GetObligationAsync("token",
            Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"),
            Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb"), CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.True(result.Value!.EvidenceRequired);
        Assert.Equal(1, result.Value.Version);
        Assert.Equal("/api/v1/organizations/aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa/obligations/bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb", handler.RequestUri!.AbsolutePath);
    }

    /// <summary>D-OC38 (Sprint 4/A-1): o 409 da ação deve carregar <c>currentVersion</c> para o BFF
    /// atualizar a versão esperada e o usuário repetir sem perder os valores digitados.</summary>
    [Fact]
    public async Task ObligationActionConflictExposesCurrentVersion()
    {
        var handler = new CapturingHandler(
            "{\"title\":\"Conflito de versão da obrigação.\",\"code\":\"obligation.version.conflict\",\"currentVersion\":7}",
            HttpStatusCode.Conflict);
        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://odca.test/") };
        var client = new OdcaApiClient(http, NoHttpContextAccessor.Instance);

        var (status, title, code, currentVersion) = await client.PerformObligationActionDetailedAsync("token",
            Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"),
            Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb"),
            "start", new Odca.Contracts.Obligations.ObligationActionRequest(6, null, null, null, null, null, null), CancellationToken.None);

        Assert.Equal(ApiCallStatus.Conflict, status);
        Assert.Equal("obligation.version.conflict", code);
        Assert.Equal(7, currentVersion);
        Assert.Contains("Conflito", title, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ObligationActionSuccessDoesNotRequireConflictPayload()
    {
        var handler = new CapturingHandler("{\"status\":\"in_progress\",\"version\":2}");
        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://odca.test/") };
        var client = new OdcaApiClient(http, NoHttpContextAccessor.Instance);

        var (status, _, _, currentVersion) = await client.PerformObligationActionDetailedAsync("token",
            Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"),
            Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb"),
            "start", new Odca.Contracts.Obligations.ObligationActionRequest(1, null, null, null, null, null, null), CancellationToken.None);

        Assert.Equal(ApiCallStatus.Success, status);
        Assert.Null(currentVersion);
    }

    [Fact]
    public async Task ReviewFiltersAreEncodedAndUseInvariantDates()
    {
        var handler = new CapturingHandler();
        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://odca.test/") };
        var client = new OdcaApiClient(http, NoHttpContextAccessor.Instance);

        await client.GetReviewsAsync("token", Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"),
            "in_review", "assigned", null, null, new DateOnly(2026, 9, 3), new DateOnly(2026, 10, 4),
            "ação & revisão", 2, CancellationToken.None);

        Assert.Contains("from=2026-09-03", handler.RequestUri!.Query, StringComparison.Ordinal);
        Assert.Contains("to=2026-10-04", handler.RequestUri.Query, StringComparison.Ordinal);
        Assert.Contains($"search={Uri.EscapeDataString("ação & revisão")}", handler.RequestUri.Query, StringComparison.Ordinal);
        Assert.Contains("scope=assigned", handler.RequestUri.Query, StringComparison.Ordinal);
        Assert.Contains("page=2", handler.RequestUri.Query, StringComparison.Ordinal);
    }

    /// <summary>D-OC27: BFF em requisição HTTP com cultura ativa (CultureMiddleware) — o header
    /// X-Odca-Culture deve viajar para a API em toda chamada autenticada.</summary>
    [Theory]
    [InlineData("en-US")]
    [InlineData("es-ES")]
    public async Task ActiveBffCultureTravelsToTheApiAsXOdcaCultureHeader(string cultureName)
    {
        var handler = new CapturingHandler("{\"title\":\"Campo inválido.\"}", HttpStatusCode.BadRequest);
        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://odca.test/") };
        var client = new OdcaApiClient(http, new HttpContextAccessorWithCulture(cultureName));

        await client.GetPatientAsync("token", Guid.NewGuid(), Guid.NewGuid(), CancellationToken.None);

        Assert.True(handler.LastRequest!.Headers.TryGetValues("X-Odca-Culture", out var values));
        Assert.Equal(new[] { cultureName }, values.ToArray());
    }

    [Fact]
    public async Task MissingBffHttpContextSendsNoCultureHeader()
    {
        var handler = new CapturingHandler("{\"title\":\"Campo inválido.\"}", HttpStatusCode.BadRequest);
        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://odca.test/") };
        var client = new OdcaApiClient(http, NoHttpContextAccessor.Instance);

        await client.GetPatientAsync("token", Guid.NewGuid(), Guid.NewGuid(), CancellationToken.None);

        Assert.False(handler.LastRequest!.Headers.Contains("X-Odca-Culture"));
    }

    /// <summary>D-OC27: BFF fora de uma requisição HTTP — sem HttpContext, sem header X-Odca-Culture.</summary>
    private sealed class NoHttpContextAccessor : IHttpContextAccessor
    {
        public static readonly NoHttpContextAccessor Instance = new();
        public HttpContext? HttpContext { get => null; set { } }
    }

    private sealed class HttpContextAccessorWithCulture(string culture) : IHttpContextAccessor
    {
        public HttpContext? HttpContext { get; set; } = CreateHttpContext(culture);

        private static DefaultHttpContext CreateHttpContext(string culture)
        {
            var context = new DefaultHttpContext();
            context.Items[CultureMiddleware.ItemsKey] = culture;
            return context;
        }
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
        public HttpRequestMessage? LastRequest { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            RequestUri = request.RequestUri;
            LastRequest = request;
            return Task.FromResult(new HttpResponseMessage(status)
            {
                Content = new StringContent(response)
            });
        }
    }
}
