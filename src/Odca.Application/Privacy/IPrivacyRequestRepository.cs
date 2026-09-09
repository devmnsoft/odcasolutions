namespace Odca.Application.Privacy;

public interface IPrivacyRequestRepository
{
    Task InsertAsync(PrivacyRequestSubmission submission, CancellationToken cancellationToken);
}
