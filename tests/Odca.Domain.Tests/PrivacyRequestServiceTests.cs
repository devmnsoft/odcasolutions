using Odca.Application.Privacy;

namespace Odca.Domain.Tests;

public sealed class PrivacyRequestServiceTests
{
    [Fact]
    public async Task SubmissionReturnsOpaqueProtocolAndNormalizesEmail()
    {
        var repository = new CapturingRepository();
        var service = new PrivacyRequestService(repository);

        var protocol = await service.SubmitAsync(
            "Case-Mail@Example.Test ",
            "access",
            "  Contexto mínimo  ",
            default);

        Assert.Matches("^[A-F0-9]{24}$", protocol);
        Assert.NotNull(repository.Submission);
        Assert.Equal("case-mail@example.test", repository.Submission.Email);
        Assert.Equal("Contexto mínimo", repository.Submission.Details);
    }

    [Fact]
    public async Task SubmissionRejectsUnknownRequestType()
    {
        var service = new PrivacyRequestService(new CapturingRepository());

        await Assert.ThrowsAsync<ArgumentException>(() =>
            service.SubmitAsync("person@example.test", "unknown", null, default));
    }

    private sealed class CapturingRepository : IPrivacyRequestRepository
    {
        public PrivacyRequestSubmission? Submission { get; private set; }

        public Task InsertAsync(PrivacyRequestSubmission submission, CancellationToken cancellationToken)
        {
            Submission = submission;
            return Task.CompletedTask;
        }
    }
}
