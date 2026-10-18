namespace Odca.Application.Identity;

public interface ITokenService
{
    IssuedToken Issue(UserCredential user, SessionRecord session);

    /// <summary>D-OC43 (Sprint 9/F-1): token da visão do cliente — claims do usuário-alvo na
    /// sessão dedicada da concessão, mais imp_op/imp_grant ligando o token ao operador real
    /// e à concessão (validada por request no middleware da API).</summary>
    IssuedToken IssueImpersonation(UserCredential target, SessionRecord session, Guid operatorUserId, Guid grantId);
}
