using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace Odca.IntegrationTests;

/// <summary>D-OC27/D-OC28: o texto apresentado pela API — erros e respostas de sucesso —
/// segue o catálogo via header X-Odca-Culture, com códigos estáveis no wire.</summary>
public sealed class LocalizationEndToEndTests(DatabaseFixture database) : IClassFixture<DatabaseFixture>
{
    [Theory]
    [InlineData("pt-BR", "Um ou mais campos estão inválidos.", "Informe CPF ou CNPJ em formato válido.", "Informe o nome do responsável.")]
    [InlineData("en-US", "One or more fields are invalid.", "Provide a valid-format CPF or CNPJ.", "Provide the responsible person's name.")]
    public async Task InvalidRegistrationIsPresentedInTheRequestedCulture(
        string culture, string expectedTitle, string expectedDocumentError, string expectedNameError)
    {
        await using var factory = database.CreateApi();
        using var client = factory.CreateClient();

        var payload = new
        {
            planCode = "enterprise",
            document = "abc",
            responsibleName = "A",
            email = $"d27-localization-{Guid.NewGuid():N}@odca.local",
            password = "OdcaD27#Localization",
            acceptedTerms = true,
            acknowledgedPrivacyNotice = true,
            marketingConsent = false
        };

        using var request = new HttpRequestMessage(HttpMethod.Post, "api/v1/onboarding/registrations")
        {
            Content = JsonContent.Create(payload)
        };
        request.Headers.Add("X-Odca-Culture", culture);

        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        using var document = JsonDocument.Parse(body);
        var root = document.RootElement;

        Assert.Equal(expectedTitle, root.GetProperty("title").GetString());

        // No .NET 9+ as extensions do ProblemDetails serializam como propriedades de nível
        // superior no wire (o BFF lê "code" exatamente assim).
        Assert.True(root.TryGetProperty("code", out var codeElement), $"sem 'code' no wire: {body}");
        Assert.Equal("api.onboarding.registration_invalid", codeElement.GetString());

        var errors = root.GetProperty("errors").GetProperty("registration")
            .EnumerateArray()
            .Select(element => element.GetString())
            .ToList();
        Assert.Contains(expectedDocumentError, errors);
        Assert.Contains(expectedNameError, errors);
    }

    /// <summary>D-OC28: respostas de sucesso (message) também seguem o catálogo — um
    /// cadastro válido e sua confirmação de e-mail são apresentados na cultura pedida.</summary>
    [Theory]
    [InlineData("pt-BR", "Cadastro recebido. Confirme o e-mail para liberar a organização em estado comercial pendente.", "E-mail confirmado. A organização está acessível com contratação pendente.")]
    [InlineData("en-US", "Registration received. Confirm your email to release the organization in pending commercial state.", "Email confirmed. The organization is accessible with pending onboarding.")]
    public async Task SuccessfulRegistrationFlowIsLocalizedInTheRequestedCulture(
        string culture, string expectedReceived, string expectedConfirmed)
    {
        await using var factory = database.CreateApi();
        using var client = factory.CreateClient();

        var payload = new
        {
            planCode = "basic",
            document = NewCpf(),
            responsibleName = "Cliente Sucesso",
            email = $"d28-localization-{Guid.NewGuid():N}@odca.local",
            password = "OdcaD28#Localization",
            acceptedTerms = true,
            acknowledgedPrivacyNotice = true,
            marketingConsent = false
        };

        using var request = new HttpRequestMessage(HttpMethod.Post, "api/v1/onboarding/registrations")
        {
            Content = JsonContent.Create(payload)
        };
        request.Headers.Add("X-Odca-Culture", culture);

        using var started = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.Accepted, started.StatusCode);

        Guid registrationId;
        string token;
        using (var document = JsonDocument.Parse(await started.Content.ReadAsStringAsync()))
        {
            Assert.Equal(expectedReceived, document.RootElement.GetProperty("message").GetString());
            registrationId = document.RootElement.GetProperty("registrationId").GetGuid();
            token = document.RootElement.GetProperty("developmentConfirmationToken").GetString()!;
        }

        using var confirm = new HttpRequestMessage(HttpMethod.Post, "api/v1/onboarding/confirm-email")
        {
            Content = JsonContent.Create(new { registrationId, token })
        };
        confirm.Headers.Add("X-Odca-Culture", culture);

        using var confirmed = await client.SendAsync(confirm);
        Assert.Equal(HttpStatusCode.OK, confirmed.StatusCode);
        using var confirmedDocument = JsonDocument.Parse(await confirmed.Content.ReadAsStringAsync());
        Assert.Equal(expectedConfirmed, confirmedDocument.RootElement.GetProperty("message").GetString());
    }

    /// <summary>D-OC28: o problem 503 do gate do ciclo de liberação tem título/detalhe
    /// resolvidos por catálogo; o código estável do wire (release_feature_disabled) é preservado.</summary>
    [Theory]
    [InlineData("pt-BR", "A Central de importações não está disponível nesta versão.", "O recurso será habilitado em uma versão futura. Documentos, dados e histórico já existentes permanecem preservados.")]
    [InlineData("en-US", "The Import Center is not available in this version.", "This feature will be enabled in a future version. Existing documents, data and history remain preserved.")]
    public async Task DisabledReleaseFeatureProblemIsLocalizedInTheRequestedCulture(
        string culture, string expectedTitle, string expectedDetail)
    {
        await using var factory = database.CreateApi(settings => settings["Features:ContractImports"] = "false");
        using var client = factory.CreateClient();

        using var request = new HttpRequestMessage(HttpMethod.Get, $"/api/v1/organizations/{Guid.NewGuid():N}/contract-imports");
        request.Headers.Add("X-Odca-Culture", culture);

        using var response = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);

        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var root = document.RootElement;
        Assert.Equal(expectedTitle, root.GetProperty("title").GetString());
        Assert.Equal(expectedDetail, root.GetProperty("detail").GetString());
        Assert.Equal("release_feature_disabled", root.GetProperty("code").GetString());
    }

    private static string NewCpf()
    {
        Span<char> digits = stackalloc char[11];
        for (var index = 0; index < 9; index++)
        {
            digits[index] = (char)('0' + Random.Shared.Next(10));
        }

        if (digits[..9].ToArray().Distinct().Count() == 1)
        {
            digits[8] = digits[8] == '0' ? '1' : '0';
        }

        digits[9] = Digit(digits, 9);
        digits[10] = Digit(digits, 10);
        return new string(digits);
    }

    private static char Digit(Span<char> digits, int length)
    {
        var sum = 0;
        var weight = length + 1;
        for (var index = 0; index < length; index++)
        {
            sum += (digits[index] - '0') * weight--;
        }

        var remainder = sum % 11;
        return (char)('0' + (remainder < 2 ? 0 : 11 - remainder));
    }
}
