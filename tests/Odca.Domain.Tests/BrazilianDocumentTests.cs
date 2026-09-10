using Odca.Application.Onboarding;

namespace Odca.Domain.Tests;

public sealed class BrazilianDocumentTests
{
    [Theory]
    [InlineData("529.982.247-25", "cpf", "52998224725")]
    [InlineData("12.345.678/0001-95", "cnpj", "12345678000195")]
    public void AcceptsValidCheckDigits(string input, string type, string normalized)
    {
        var result = BrazilianDocument.NormalizeAndValidate(input);

        Assert.Equal(type, result.Type);
        Assert.Equal(normalized, result.Normalized);
    }

    [Theory]
    [InlineData("529.982.247-24")]
    [InlineData("12.345.678/0001-91")]
    [InlineData("111.111.111-11")]
    [InlineData("not-a-document")]
    public void RejectsInvalidCheckDigits(string input) =>
        Assert.Equal("invalid", BrazilianDocument.NormalizeAndValidate(input).Type);
}
