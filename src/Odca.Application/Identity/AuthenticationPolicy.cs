namespace Odca.Application.Identity;

public sealed record AuthenticationPolicy(TimeSpan SessionLifetime)
{
    /// <summary>Validade da chave MFA pendente de confirmação antes de poder ser reemitida.</summary>
    public TimeSpan PendingEnrollmentLifetime { get; init; } = TimeSpan.FromMinutes(30);
}
