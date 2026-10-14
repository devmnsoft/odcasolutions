using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace Odca.IntegrationTests;

/// <summary>D-OC27: um único pedido inválido de cadastro, apresentado em duas culturas
/// via header X-Odca-Culture — código estável no wire, título e erros de campo resolvidos
/// pelo catálogo (texto canônico PT → chave reversa).</summary>
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
}
