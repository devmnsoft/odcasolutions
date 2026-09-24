using Odca.Application.Patients;

namespace Odca.Domain.Tests;

public sealed class PatientRulesTests
{
    [Fact]
    public void MinimalRegistrationDoesNotRequireClinicalOrSignatureData()
        => Assert.Empty(PatientRules.Validate("Maria da Silva", null, null, null));

    [Theory]
    [InlineData("cpf", "123.456.789-00", "12345678900")]
    [InlineData("passport", " br 123 ", "BR 123")]
    public void IdentifierIsNormalizedForTenantUniqueness(string type, string value, string expected)
        => Assert.Equal(expected, PatientRules.NormalizeIdentifier(type, value));

    [Fact]
    public void IdentifierTypeAndValueAreAtomic()
        => Assert.Contains("identifier", PatientRules.Validate("Maria", null, "cpf", null).Keys);
}
