using Odca.Application.Patients;
using Odca.Contracts.Patients;

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

    [Theory]
    [InlineData("529.982.247-25", true)]
    [InlineData("52998224725", true)]
    [InlineData("111.111.111-11", false)]
    [InlineData("abc52998224725", false)]
    [InlineData("529.982.247-24", false)]
    public void CpfUsesCheckDigitsAndRejectsArbitraryCharacters(string cpf, bool valid)
    {
        var request = Request(identifier: new("CPF", cpf));
        Assert.Equal(valid, !PatientRules.Validate(request, new DateOnly(2026, 9, 24)).ContainsKey("identifier"));
    }

    [Fact]
    public void IdentifierRemainsOptional() => Assert.Empty(PatientRules.Validate(Request(), new DateOnly(2026, 9, 24)));

    [Fact]
    public void FutureBirthDateIsDeterministic()
    {
        var request = Request() with { BirthDate = new DateOnly(2026, 9, 25) };
        Assert.Contains("birthDate", PatientRules.Validate(request, new DateOnly(2026, 9, 24)));
    }

    [Fact]
    public void RepresentativeIdentifierAndRequiredFieldsAreValidated()
    {
        var request = Request() with { Representative = new(" ", "cpf", "123", " ") };
        var errors = PatientRules.Validate(request, new DateOnly(2026, 9, 24));
        Assert.Contains("representative.fullName", errors);
        Assert.Contains("representative.relationship", errors);
        Assert.Contains("representative.identifier", errors);
    }

    [Theory]
    [InlineData("preferredName", 121)]
    [InlineData("phone", 41)]
    [InlineData("address", 501)]
    public void TextLimitsMatchDatabase(string field, int length)
    {
        var value = new string('a', length);
        var request = Request() with
        {
            PreferredName = field == "preferredName" ? value : null,
            Phone = field == "phone" ? value : null,
            Address = field == "address" ? value : null
        };
        Assert.Contains(field, PatientRules.Validate(request, new DateOnly(2026, 9, 24)));
    }

    [Fact]
    public void NormalizationTrimsTextAndCanonicalizesTypes()
    {
        var normalized = PatientRules.Normalize(Request(identifier: new(" CPF ", "529.982.247-25")) with { Email = " maria@example.test " });
        Assert.Equal("Maria da Silva", normalized.FullName);
        Assert.Equal("maria@example.test", normalized.Email);
        Assert.Equal("cpf", normalized.Identifier!.Type);
        Assert.Equal("52998224725", normalized.Identifier.Value);
    }

    private static SavePatientRequest Request(PatientIdentifierInput? identifier = null) =>
        new(" Maria da Silva ", null, null, null, null, null, identifier, null);
}
