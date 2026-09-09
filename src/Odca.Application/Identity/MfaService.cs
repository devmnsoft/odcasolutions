using System.Security.Cryptography;
using Odca.Application.Common;

namespace Odca.Application.Identity;

public sealed class MfaService(
    IIdentityRepository repository,
    IMfaSecretProtector secretProtector,
    TotpService totp,
    ITokenService tokenService,
    IClock clock,
    AuthenticationPolicy policy)
{
    private const int MaxFailedAttempts = 5;

    public async Task<MfaEnrollment?> StartEnrollmentAsync(
        Guid userId,
        Guid sessionId,
        int securityVersion,
        CancellationToken cancellationToken)
    {
        var now = clock.UtcNow;
        var user = await repository.FindByIdAsync(userId, cancellationToken);
        if (user is null ||
            !user.IsPlatformAdministrator ||
            user.MustChangePassword ||
            user.MfaConfirmedAt is not null ||
            !await repository.IsSessionValidAsync(userId, sessionId, securityVersion, now, cancellationToken))
        {
            return null;
        }

        var secret = TotpService.GenerateSecret();
        await repository.SavePendingMfaSecretAsync(
            userId,
            secretProtector.Protect(secret),
            now,
            cancellationToken);
        return new MfaEnrollment(secret, TotpService.BuildOtpAuthUri("ODCA Solutions", user.Email, secret));
    }

    public async Task<MfaVerificationOutcome> ConfirmEnrollmentAsync(
        Guid userId,
        Guid sessionId,
        int securityVersion,
        string code,
        CancellationToken cancellationToken)
    {
        var now = clock.UtcNow;
        var user = await repository.FindByIdAsync(userId, cancellationToken);
        var protectedSecret = await repository.GetProtectedMfaSecretAsync(userId, cancellationToken);
        if (user is null ||
            !user.IsPlatformAdministrator ||
            user.MustChangePassword ||
            user.MfaConfirmedAt is not null ||
            protectedSecret is null)
        {
            return Failed("Inscrição MFA indisponível para esta sessão.");
        }

        var secret = secretProtector.Unprotect(protectedSecret);
        var verification = totp.Verify(secret, code);
        if (verification is null)
        {
            await repository.RecordFailedMfaChallengeAsync(userId, sessionId, now, cancellationToken);
            return Failed("Código de autenticação inválido ou expirado.");
        }

        var recoveryCodes = GenerateRecoveryCodes();
        var confirmed = await repository.ConfirmMfaAsync(
            userId,
            sessionId,
            securityVersion,
            now,
            verification.TimeStep,
            recoveryCodes.Select(codeValue => HashRecoveryCode(userId, codeValue)).ToArray(),
            cancellationToken);
        if (!confirmed)
        {
            return Failed("Não foi possível confirmar o MFA. Inicie a inscrição novamente.");
        }

        var updatedUser = user with { MfaConfirmedAt = now };
        var session = new SessionRecord(sessionId, userId, securityVersion, now.Add(policy.SessionLifetime), "mfa", now);
        return new(
            true,
            [],
            updatedUser,
            session,
            tokenService.Issue(updatedUser, session),
            recoveryCodes);
    }

    public async Task<MfaVerificationOutcome> VerifyChallengeAsync(
        Guid userId,
        Guid sessionId,
        int securityVersion,
        string code,
        CancellationToken cancellationToken)
    {
        var now = clock.UtcNow;
        var user = await repository.FindByIdAsync(userId, cancellationToken);
        var state = await repository.GetMfaChallengeStateAsync(
            userId,
            sessionId,
            securityVersion,
            now,
            cancellationToken);
        if (user is null ||
            !user.IsPlatformAdministrator ||
            user.MustChangePassword ||
            state is null ||
            state.ConfirmedAt is null)
        {
            return Failed("Desafio MFA indisponível para esta sessão.");
        }

        if (state.FailedAttempts >= MaxFailedAttempts)
        {
            return Failed("Muitas tentativas de MFA. Faça login novamente.");
        }

        var secret = secretProtector.Unprotect(state.ProtectedSecret);
        var verification = totp.Verify(secret, code);
        string? recoveryCodeHash = null;
        if (verification is null)
        {
            recoveryCodeHash = HashRecoveryCode(userId, code);
        }
        else if (state.LastAcceptedTimeStep is not null &&
                 verification.TimeStep <= state.LastAcceptedTimeStep.Value)
        {
            await repository.RecordFailedMfaChallengeAsync(userId, sessionId, now, cancellationToken);
            return Failed("Código de autenticação já utilizado.");
        }

        var completed = await repository.CompleteMfaChallengeAsync(
            userId,
            sessionId,
            securityVersion,
            now,
            verification?.TimeStep,
            recoveryCodeHash,
            cancellationToken);
        if (!completed)
        {
            await repository.RecordFailedMfaChallengeAsync(userId, sessionId, now, cancellationToken);
            return Failed("Código de autenticação inválido, expirado ou já utilizado.");
        }

        var session = new SessionRecord(sessionId, userId, securityVersion, now.Add(policy.SessionLifetime), "mfa", now);
        return new(true, [], user, session, tokenService.Issue(user, session), []);
    }

    public static string HashRecoveryCode(Guid userId, string code)
    {
        var normalized = new string(code.Where(char.IsLetterOrDigit).ToArray()).ToUpperInvariant();
        var bytes = System.Text.Encoding.UTF8.GetBytes($"{userId:N}:{normalized}");
        return Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
    }

    private static MfaVerificationOutcome Failed(string error) =>
        new(false, [error], null, null, null, []);

    private static string[] GenerateRecoveryCodes()
    {
        var codes = new string[8];
        for (var index = 0; index < codes.Length; index++)
        {
            var bytes = new byte[10];
            RandomNumberGenerator.Fill(bytes);
            codes[index] = Convert.ToHexString(bytes).Insert(10, "-");
        }

        return codes;
    }
}
