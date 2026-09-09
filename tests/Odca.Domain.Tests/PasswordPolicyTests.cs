using Odca.Domain.Identity;

namespace Odca.Domain.Tests;

public sealed class PasswordPolicyTests
{
    [Fact]
    public void ValidateAcceptsStrongPassword()
    {
        var errors = PasswordPolicy.Validate("UmaSenha!Forte2026");

        Assert.Empty(errors);
    }

    [Theory]
    [InlineData("curta")]
    [InlineData("somenteletrasminusculas")]
    [InlineData("SOMENTELETRASMAIUSCULAS")]
    [InlineData("SemSimbolo123456")]
    [InlineData("SemNumero!Senha")]
    public void ValidateRejectsWeakPasswords(string password)
    {
        var errors = PasswordPolicy.Validate(password);

        Assert.NotEmpty(errors);
    }
}
