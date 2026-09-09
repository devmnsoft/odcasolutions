using System.Net.Mail;
using System.Security.Cryptography;

namespace Odca.Application.Privacy;

public sealed class PrivacyRequestService(IPrivacyRequestRepository repository)
{
    private static readonly HashSet<string> AllowedTypes =
    [
        "access", "correction", "sharing", "portability", "blocking", "deletion", "revocation", "review"
    ];

    public async Task<string> SubmitAsync(
        string email,
        string requestType,
        string? details,
        CancellationToken cancellationToken)
    {
        var normalizedEmail = email.Trim().ToLowerInvariant();
        if (!MailAddress.TryCreate(normalizedEmail, out var address) ||
            !string.Equals(address.Address, normalizedEmail, StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException("Informe um e-mail válido.", nameof(email));
        }

        if (!AllowedTypes.Contains(requestType))
        {
            throw new ArgumentException("Tipo de solicitação inválido.", nameof(requestType));
        }

        var normalizedDetails = string.IsNullOrWhiteSpace(details) ? null : details.Trim();
        if (normalizedDetails?.Length > 2000)
        {
            throw new ArgumentException("O relato deve ter até 2.000 caracteres.", nameof(details));
        }

        var protocol = Convert.ToHexString(RandomNumberGenerator.GetBytes(12));
        await repository.InsertAsync(
            new PrivacyRequestSubmission(Guid.NewGuid(), protocol, normalizedEmail, requestType, normalizedDetails),
            cancellationToken);
        return protocol;
    }
}
