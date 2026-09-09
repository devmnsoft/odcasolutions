namespace Odca.Application.Identity;

public interface ITokenService
{
    IssuedToken Issue(UserCredential user, SessionRecord session);
}
