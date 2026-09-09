namespace Odca.Application.Identity;

public interface IPasswordService
{
    string Hash(UserCredential user, string password);

    bool Verify(UserCredential user, string passwordHash, string password);
}
