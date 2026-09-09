using System.Security.Cryptography;
using Odca.Application.Common;
using Odca.Application.Identity;
using Odca.Application.Plans;

namespace Odca.Application.Onboarding;

public sealed class CustomerOnboardingService(
    ICustomerOnboardingRepository repository,
    IPlanCatalogRepository plans,
    IPasswordService passwordService,
    IClock clock)
{
    public async Task<CustomerRegistrationResult> StartRegistrationAsync(
        CustomerRegistrationSubmission submission,
        bool exposeDevelopmentToken,
        CancellationToken cancellationToken)
    {
        var errors = Validate(submission);
        if (errors.Count > 0)
        {
            return new(false, errors, null, null);
        }

        var plan = await plans.FindPublishedAsync(NormalizePlanCode(submission.PlanCode), cancellationToken);
        if (plan is null)
        {
            return new(false, ["Plano não encontrado ou indisponível para contratação."], null, null);
        }

        var now = clock.UtcNow;
        var userId = Guid.NewGuid();
        var tenantId = Guid.NewGuid();
        var document = NormalizeDocument(submission.Document);
        var email = submission.Email.Trim().ToLowerInvariant();
        var template = new UserCredential(
            userId,
            email,
            submission.ResponsibleName.Trim(),
            string.Empty,
            1,
            false,
            false,
            null,
            false,
            null);
        var token = Convert.ToHexString(RandomNumberGenerator.GetBytes(32)).ToLowerInvariant();
        var draft = new CustomerRegistrationDraft(
            Guid.NewGuid(),
            userId,
            tenantId,
            plan.Id,
            plan.Plan.Version,
            plan.Plan.Code,
            plan.Plan.DisplayName,
            document.Normalized,
            document.Type,
            submission.ResponsibleName.Trim(),
            email,
            email.ToUpperInvariant(),
            passwordService.Hash(template, submission.Password),
            submission.MarketingConsent,
            now.AddHours(24));
        var created = await repository.CreateRegistrationAsync(
            draft,
            IdempotencyKey(document.Normalized, email, plan.Plan.Code),
            HashToken(token),
            exposeDevelopmentToken ? token : null,
            now,
            cancellationToken);

        return created is null
            ? new(false, ["Já existe cadastro ativo para este documento e e-mail."], null, null)
            : new(true, [], created.Id, exposeDevelopmentToken ? token : null);
    }

    public async Task<CustomerConfirmationResult> ConfirmRegistrationAsync(
        Guid registrationId,
        string token,
        CancellationToken cancellationToken)
    {
        if (registrationId == Guid.Empty || string.IsNullOrWhiteSpace(token))
        {
            return new(false, ["Confirmação inválida."]);
        }

        var confirmed = await repository.ConfirmRegistrationAsync(
            registrationId,
            HashToken(token),
            clock.UtcNow,
            cancellationToken);
        return confirmed
            ? new(true, [])
            : new(false, ["Token inválido, expirado ou já utilizado."]);
    }

    public Task<CustomerHome?> GetHomeAsync(
        Guid userId,
        Guid? tenantId,
        CancellationToken cancellationToken) =>
        repository.GetHomeAsync(userId, tenantId, cancellationToken);

    private static List<string> Validate(CustomerRegistrationSubmission submission)
    {
        var errors = new List<string>();
        if (!submission.AcceptedTerms)
        {
            errors.Add("Aceite os termos de uso para criar a conta.");
        }

        if (!submission.AcknowledgedPrivacyNotice)
        {
            errors.Add("Confirme a ciência do aviso de privacidade.");
        }

        if (NormalizeDocument(submission.Document).Type == "invalid")
        {
            errors.Add("Informe CPF ou CNPJ em formato válido.");
        }

        if (submission.ResponsibleName.Trim().Length < 3)
        {
            errors.Add("Informe o nome do responsável.");
        }

        return errors;
    }

    private static string HashToken(string token) =>
        Convert.ToHexString(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(token.Trim()))).ToLowerInvariant();

    private static string IdempotencyKey(string document, string email, string planCode) =>
        HashToken($"{document}:{email}:{planCode}");

    private static string NormalizePlanCode(string code) => code.Trim().ToLowerInvariant();

    private static (string Normalized, string Type) NormalizeDocument(string value)
    {
        var normalized = new string(value.Where(char.IsLetterOrDigit).ToArray()).ToUpperInvariant();
        return normalized.Length switch
        {
            11 when normalized.All(char.IsDigit) => (normalized, "cpf"),
            14 => (normalized, "cnpj"),
            _ => (normalized, "invalid")
        };
    }
}
