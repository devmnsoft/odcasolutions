using Microsoft.AspNetCore.Identity;
using Odca.Application.Identity;

namespace Odca.Infrastructure.Identity;

public sealed class AspNetPasswordService : IPasswordService
{
    private readonly PasswordHasher<UserCredential> _hasher = new();

    public string Hash(UserCredential user, string password) => _hasher.HashPassword(user, password);

    public bool Verify(UserCredential user, string passwordHash, string password) =>
        _hasher.VerifyHashedPassword(user, passwordHash, password) is not PasswordVerificationResult.Failed;
}
