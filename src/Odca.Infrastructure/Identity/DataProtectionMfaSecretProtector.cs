using Microsoft.AspNetCore.DataProtection;
using Odca.Application.Identity;

namespace Odca.Infrastructure.Identity;

public sealed class DataProtectionMfaSecretProtector(IDataProtectionProvider provider) : IMfaSecretProtector
{
    private readonly IDataProtector _protector = provider.CreateProtector("Odca.Identity.MfaSecret.v1");

    public string Protect(string secret) => _protector.Protect(secret);

    public string Unprotect(string protectedSecret) => _protector.Unprotect(protectedSecret);
}
