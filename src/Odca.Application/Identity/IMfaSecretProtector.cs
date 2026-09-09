namespace Odca.Application.Identity;

public interface IMfaSecretProtector
{
    string Protect(string secret);

    string Unprotect(string protectedSecret);
}
