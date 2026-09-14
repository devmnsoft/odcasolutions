using Odca.Application.Reviews;

namespace Odca.Domain.Tests;

public sealed class ContractReviewTests
{
    private static readonly Guid Requester = Guid.NewGuid();
    private static readonly Guid Reviewer1 = Guid.NewGuid();
    private static readonly Guid Reviewer2 = Guid.NewGuid();
    private static readonly DateTimeOffset Now = new(2026, 9, 14, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void SequentialApprovalOnlyAllowsCurrentReviewerAndRequiresConfirmation()
    {
        var review = Create([Reviewer1, Reviewer2]);
        Assert.Throws<ContractReviewRuleException>(() => review.Approve(Reviewer2, true, 1, Now));
        Assert.Throws<ContractReviewRuleException>(() => review.Approve(Reviewer1, false, 1, Now));

        review.Approve(Reviewer1, true, 1, Now);
        Assert.Equal(3, review.Revision);
        Assert.Equal(Reviewer2, review.CurrentStep!.ReviewerId);
        review.Approve(Reviewer2, true, 3, Now.AddMinutes(1));

        Assert.Equal(ContractReviewStatus.InternallyApproved, review.Status);
        Assert.NotNull(review.CompletedAt);
        Assert.All(review.Steps, step => Assert.Equal(ReviewStepStatus.Approved, step.Status));
    }

    [Fact]
    public void RequesterAndDuplicateReviewerAreRejected()
    {
        Assert.Throws<ContractReviewRuleException>(() => Create([Requester]));
        Assert.Throws<ContractReviewRuleException>(() => Create([Reviewer1, Reviewer1]));
    }

    [Fact]
    public void ChangesNeedJustificationAndNeverMeanApproval()
    {
        var review = Create([Reviewer1]);
        Assert.Throws<ContractReviewRuleException>(() => review.RequestChanges(Reviewer1, " ", 1, Now));
        review.RequestChanges(Reviewer1, "Corrigir a vigência.", 1, Now);
        var comment = review.AddComment(Reviewer1, "Validar com a contraparte.", "Página 2", Now);
        review.ResolveComment(comment.Id, Requester, Now.AddMinutes(1));

        Assert.Equal(ContractReviewStatus.ChangesRequested, review.Status);
        Assert.NotNull(comment.ResolvedAt);
        Assert.NotEqual(ContractReviewStatus.InternallyApproved, review.Status);
    }

    [Fact]
    public void StaleOrRepeatedDecisionConflicts()
    {
        var review = Create([Reviewer1, Reviewer2]);
        review.Approve(Reviewer1, true, 1, Now);
        var conflict = Assert.Throws<ContractReviewConflictException>(() => review.Approve(Reviewer1, true, 1, Now));
        Assert.Equal(1, conflict.Expected);
        Assert.Equal(3, conflict.Actual);
    }

    [Fact]
    public void ReassignmentPreservesOldAssignmentAndRequiresPermissionAndReason()
    {
        var review = Create([Reviewer1]);
        Assert.Throws<ContractReviewRuleException>(() => review.Reassign(Requester, false, Reviewer2, "bloqueado", 1, Now));
        Assert.Throws<ContractReviewRuleException>(() => review.Reassign(Requester, true, Reviewer2, "", 1, Now));
        review.Reassign(Requester, true, Reviewer2, "Acesso do titular bloqueado.", 1, Now);

        Assert.Equal(ReviewStepStatus.Reassigned, review.Steps[0].Status);
        Assert.Equal(Reviewer2, review.CurrentStep!.ReviewerId);
        Assert.Contains(review.Events, item => item.Type == "review.step.reassigned");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void ReassignmentRejectsMissingReasonWithoutPartialMutation(string? reason)
    {
        var review = Create([Reviewer1]);

        Assert.Throws<ContractReviewRuleException>(() => review.Reassign(Requester, true, Reviewer2, reason, 1, Now));

        Assert.Equal(1, review.Revision);
        Assert.Single(review.Steps);
        Assert.Equal(Reviewer1, review.CurrentStep!.ReviewerId);
        Assert.Equal(ReviewStepStatus.Current, review.CurrentStep.Status);
    }

    [Fact]
    public void ReassignmentNormalizesReasonAndAuditsActorAndReviewers()
    {
        var review = Create([Reviewer1]);

        review.Reassign(Requester, true, Reviewer2, "  Vínculo do responsável encerrado.  ", 1, Now);

        Assert.Equal(Requester, review.Steps[0].DecidedBy);
        Assert.Equal("Vínculo do responsável encerrado.", review.Steps[0].Justification);
        var audit = Assert.Single(review.Events, item => item.Type == "review.step.reassigned");
        Assert.Equal(Requester, audit.ActorId);
        Assert.Equal($"{Reviewer1}:{Reviewer2}:Vínculo do responsável encerrado.", audit.Detail);
    }

    [Fact]
    public void OversizedReassignmentReasonIsTruncatedOnceForDecisionAndAudit()
    {
        var review = Create([Reviewer1]);
        review.Reassign(Requester, true, Reviewer2, $"  {new string('a', 1001)}  ", 1, Now);

        var normalizedReason = new string('a', 1000);
        Assert.Equal(ReviewStepStatus.Reassigned, review.Steps[0].Status);
        Assert.Equal(normalizedReason, review.Steps[0].Justification);
        Assert.Equal($"{Reviewer1}:{Reviewer2}:{normalizedReason}",
            Assert.Single(review.Events, item => item.Type == "review.step.reassigned").Detail);
    }

    [Fact]
    public void SupersedingKeepsApprovalBoundToOriginalImmutableVersion()
    {
        var review = Create([Reviewer1]);
        var originalVersion = review.DocumentVersionId;
        var replacement = Guid.NewGuid();
        review.Supersede(Requester, replacement, 1, Now);

        Assert.Equal(ContractReviewStatus.Superseded, review.Status);
        Assert.Equal(originalVersion, review.DocumentVersionId);
        Assert.NotEqual(replacement, review.DocumentVersionId);
        Assert.Throws<ContractReviewRuleException>(() => review.Approve(Reviewer1, true, 2, Now));
    }

    [Fact]
    public void MaterialMetadataAndHashAreCapturedInSnapshot()
    {
        var review = Create([Reviewer1]);
        Assert.Equal("Contratante A | Contratada B", review.Content.Parties);
        Assert.Equal(1500m, review.Content.Value);
        Assert.Equal("BRL", review.Content.Currency);
        Assert.Equal(new string('a', 64), review.Content.DocumentSha256);
    }

    private static ContractReview Create(Guid[] reviewers) => ContractReview.Request(
        Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Requester, reviewers, Now,
        Now.AddDays(2), "Revisar conteúdo e metadados materiais.",
        new ApprovedContentSnapshot(new string('a', 64), "Contratante A | Contratada B", 1500m, "BRL",
            new DateOnly(2026, 9, 1), new DateOnly(2027, 9, 1)));
}
