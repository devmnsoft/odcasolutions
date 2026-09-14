namespace Odca.Application.Reviews;

public enum ContractReviewStatus
{
    InReview,
    ChangesRequested,
    InternallyApproved,
    Cancelled,
    Superseded
}

public enum ReviewStepStatus { Waiting, Current, Approved, ChangesRequested, Reassigned }

public sealed record ApprovedContentSnapshot(
    string DocumentSha256,
    string Parties,
    decimal? Value,
    string? Currency,
    DateOnly? StartsOn,
    DateOnly? EndsOn)
{
    public void Validate()
    {
        if (DocumentSha256.Length != 64 || DocumentSha256.Any(c => !Uri.IsHexDigit(c)))
            throw new ContractReviewRuleException("A versão precisa ter um SHA-256 válido.");
        if (string.IsNullOrWhiteSpace(Parties))
            throw new ContractReviewRuleException("As partes do conteúdo aprovado são obrigatórias.");
        if (StartsOn.HasValue && EndsOn.HasValue && EndsOn.Value < StartsOn.Value)
            throw new ContractReviewRuleException("A vigência final não pode anteceder a inicial.");
    }
}

public sealed class ContractReview
{
    private static readonly IReadOnlyDictionary<ContractReviewStatus, ContractReviewStatus[]> Transitions =
        new Dictionary<ContractReviewStatus, ContractReviewStatus[]>
        {
            [ContractReviewStatus.InReview] = [ContractReviewStatus.ChangesRequested, ContractReviewStatus.InternallyApproved, ContractReviewStatus.Cancelled, ContractReviewStatus.Superseded],
            [ContractReviewStatus.ChangesRequested] = [ContractReviewStatus.Superseded, ContractReviewStatus.Cancelled],
            [ContractReviewStatus.InternallyApproved] = [],
            [ContractReviewStatus.Cancelled] = [],
            [ContractReviewStatus.Superseded] = []
        };

    private readonly List<ReviewStep> steps;
    private readonly List<ReviewComment> comments = [];
    private readonly List<ReviewEvent> events = [];

    private ContractReview(Guid id, Guid tenantId, Guid contractId, Guid documentVersionId,
        Guid requestedBy, DateTimeOffset openedAt, DateTimeOffset? dueAt, string? instructions,
        ApprovedContentSnapshot content, IEnumerable<Guid> reviewerIds)
    {
        if (id == Guid.Empty || tenantId == Guid.Empty || contractId == Guid.Empty || documentVersionId == Guid.Empty || requestedBy == Guid.Empty)
            throw new ContractReviewRuleException("Os identificadores da revisão são obrigatórios.");
        content.Validate();
        var reviewers = reviewerIds.ToArray();
        if (reviewers.Length == 0) throw new ContractReviewRuleException("Informe ao menos um revisor habilitado.");
        if (reviewers.Contains(requestedBy)) throw new ContractReviewRuleException("O solicitante não pode revisar a própria solicitação.");
        if (reviewers.Distinct().Count() != reviewers.Length) throw new ContractReviewRuleException("Um revisor não pode aparecer duas vezes na sequência.");
        if (instructions?.Length > 2000) throw new ContractReviewRuleException("As instruções devem ter no máximo 2.000 caracteres.");

        Id = id; TenantId = tenantId; ContractId = contractId; DocumentVersionId = documentVersionId;
        RequestedBy = requestedBy; OpenedAt = openedAt; DueAt = dueAt; Instructions = instructions?.Trim(); Content = content;
        steps = reviewers.Select((reviewer, index) => new ReviewStep(Guid.NewGuid(), index + 1, reviewer,
            index == 0 ? ReviewStepStatus.Current : ReviewStepStatus.Waiting)).ToList();
        events.Add(new ReviewEvent("review.requested", requestedBy, openedAt, null));
    }

    public Guid Id { get; }
    public Guid TenantId { get; }
    public Guid ContractId { get; }
    public Guid DocumentVersionId { get; }
    public Guid RequestedBy { get; }
    public DateTimeOffset OpenedAt { get; }
    public DateTimeOffset? DueAt { get; }
    public DateTimeOffset? CompletedAt { get; private set; }
    public string? Instructions { get; }
    public ApprovedContentSnapshot Content { get; }
    public ContractReviewStatus Status { get; private set; } = ContractReviewStatus.InReview;
    public long Revision { get; private set; } = 1;
    public IReadOnlyList<ReviewStep> Steps => steps;
    public IReadOnlyList<ReviewComment> Comments => comments;
    public IReadOnlyList<ReviewEvent> Events => events;
    public ReviewStep? CurrentStep => steps.SingleOrDefault(x => x.Status == ReviewStepStatus.Current);

    public static ContractReview Request(Guid id, Guid tenantId, Guid contractId, Guid documentVersionId,
        Guid requestedBy, IEnumerable<Guid> reviewerIds, DateTimeOffset openedAt, DateTimeOffset? dueAt,
        string? instructions, ApprovedContentSnapshot content) =>
        new(id, tenantId, contractId, documentVersionId, requestedBy, openedAt, dueAt, instructions, content, reviewerIds);

    public void Approve(Guid actorId, bool explicitlyConfirmed, long expectedRevision, DateTimeOffset now)
    {
        EnsureCurrent(actorId, expectedRevision);
        if (!explicitlyConfirmed) throw new ContractReviewRuleException("Confirme explicitamente a aprovação.");
        CurrentStep!.Decide(ReviewStepStatus.Approved, actorId, now, null);
        var next = steps.FirstOrDefault(x => x.Status == ReviewStepStatus.Waiting);
        if (next is null) TransitionTo(ContractReviewStatus.InternallyApproved, actorId, now, null);
        else { next.Start(); Revision++; events.Add(new("review.step.started", next.ReviewerId, now, null)); }
    }

    public void RequestChanges(Guid actorId, string justification, long expectedRevision, DateTimeOffset now)
    {
        EnsureCurrent(actorId, expectedRevision);
        if (string.IsNullOrWhiteSpace(justification)) throw new ContractReviewRuleException("Solicitar ajustes exige justificativa.");
        CurrentStep!.Decide(ReviewStepStatus.ChangesRequested, actorId, now, Limit(justification, 2000));
        TransitionTo(ContractReviewStatus.ChangesRequested, actorId, now, justification);
    }

    public void Cancel(Guid actorId, bool canCancel, string reason, long expectedRevision, DateTimeOffset now)
    {
        EnsureRevision(expectedRevision);
        if (!canCancel) throw new ContractReviewRuleException("O usuário não pode cancelar esta revisão.");
        if (string.IsNullOrWhiteSpace(reason)) throw new ContractReviewRuleException("O cancelamento exige motivo.");
        TransitionTo(ContractReviewStatus.Cancelled, actorId, now, reason);
    }

    public void Supersede(Guid actorId, Guid replacementVersionId, long expectedRevision, DateTimeOffset now)
    {
        EnsureRevision(expectedRevision);
        if (replacementVersionId == Guid.Empty || replacementVersionId == DocumentVersionId)
            throw new ContractReviewRuleException("A nova submissão deve apontar para outra versão imutável.");
        TransitionTo(ContractReviewStatus.Superseded, actorId, now, replacementVersionId.ToString());
    }

    public void Reassign(Guid actorId, bool canReassign, Guid replacementReviewerId, string reason, long expectedRevision, DateTimeOffset now)
    {
        EnsureRevision(expectedRevision);
        if (Status != ContractReviewStatus.InReview || CurrentStep is null) throw new ContractReviewRuleException("Não há etapa ativa para reatribuir.");
        if (!canReassign) throw new ContractReviewRuleException("O usuário não pode reatribuir esta etapa.");
        if (replacementReviewerId == Guid.Empty || replacementReviewerId == RequestedBy || steps.Any(x => x.ReviewerId == replacementReviewerId))
            throw new ContractReviewRuleException("O revisor substituto deve ser outro usuário habilitado e não repetido.");
        if (string.IsNullOrWhiteSpace(reason)) throw new ContractReviewRuleException("A reatribuição exige motivo.");
        var old = CurrentStep;
        old.Reassign(actorId, now, Limit(reason, 1000));
        var replacement = new ReviewStep(Guid.NewGuid(), old.Sequence, replacementReviewerId, ReviewStepStatus.Current);
        steps.Insert(steps.IndexOf(old) + 1, replacement);
        Revision++;
        events.Add(new("review.step.reassigned", actorId, now, $"{old.ReviewerId}:{replacementReviewerId}:{Limit(reason, 1000)}"));
    }

    public ReviewComment AddComment(Guid actorId, string body, string? reference, DateTimeOffset now)
    {
        if (Status is ContractReviewStatus.Cancelled) throw new ContractReviewRuleException("Uma revisão cancelada não recebe comentários.");
        var comment = ReviewComment.Create(Guid.NewGuid(), Id, DocumentVersionId, actorId, body, reference, now);
        comments.Add(comment); Revision++; events.Add(new("review.comment.added", actorId, now, comment.Id.ToString()));
        return comment;
    }

    public void ResolveComment(Guid commentId, Guid actorId, DateTimeOffset now)
    {
        var comment = comments.SingleOrDefault(x => x.Id == commentId) ?? throw new ContractReviewRuleException("Comentário não encontrado.");
        comment.Resolve(actorId, now); Revision++; events.Add(new("review.comment.resolved", actorId, now, commentId.ToString()));
    }

    private void EnsureCurrent(Guid actorId, long expectedRevision)
    {
        EnsureRevision(expectedRevision);
        if (Status != ContractReviewStatus.InReview || CurrentStep?.ReviewerId != actorId)
            throw new ContractReviewRuleException("Somente o revisor da etapa atual pode decidir.");
    }

    private void EnsureRevision(long expected)
    {
        if (expected != Revision) throw new ContractReviewConflictException(expected, Revision);
    }

    private void TransitionTo(ContractReviewStatus target, Guid actor, DateTimeOffset now, string? reason)
    {
        if (!Transitions[Status].Contains(target)) throw new ContractReviewRuleException($"Transição inválida: {Status} → {target}.");
        Status = target; Revision++; events.Add(new($"review.{target.ToString().ToLowerInvariant()}", actor, now, Limit(reason, 2000)));
        if (target is ContractReviewStatus.InternallyApproved or ContractReviewStatus.Cancelled or ContractReviewStatus.Superseded)
            CompletedAt = now;
    }

    private static string? Limit(string? value, int max)
    {
        if (value is null) return null;
        var clean = value.Trim();
        return clean.Length <= max ? clean : clean[..max];
    }
}

public sealed class ReviewStep(Guid id, int sequence, Guid reviewerId, ReviewStepStatus status)
{
    public Guid Id { get; } = id;
    public int Sequence { get; } = sequence;
    public Guid ReviewerId { get; } = reviewerId;
    public ReviewStepStatus Status { get; private set; } = status;
    public Guid? DecidedBy { get; private set; }
    public DateTimeOffset? DecidedAt { get; private set; }
    public string? Justification { get; private set; }
    internal void Start() => Status = ReviewStepStatus.Current;
    internal void Decide(ReviewStepStatus status, Guid actor, DateTimeOffset now, string? justification)
    { if (Status != ReviewStepStatus.Current) throw new ContractReviewRuleException("A etapa já foi decidida."); Status = status; DecidedBy = actor; DecidedAt = now; Justification = justification; }
    internal void Reassign(Guid actor, DateTimeOffset now, string reason) => Decide(ReviewStepStatus.Reassigned, actor, now, reason);
}

public sealed class ReviewComment
{
    private ReviewComment(Guid id, Guid reviewId, Guid versionId, Guid authorId, string body, string? reference, DateTimeOffset createdAt)
    { Id=id; ReviewId=reviewId; DocumentVersionId=versionId; AuthorId=authorId; Body=body; Reference=reference; CreatedAt=createdAt; }
    public Guid Id { get; } public Guid ReviewId { get; } public Guid DocumentVersionId { get; } public Guid AuthorId { get; }
    public string Body { get; } public string? Reference { get; } public DateTimeOffset CreatedAt { get; }
    public Guid? ResolvedBy { get; private set; } public DateTimeOffset? ResolvedAt { get; private set; }
    internal static ReviewComment Create(Guid id, Guid reviewId, Guid versionId, Guid author, string body, string? reference, DateTimeOffset now)
    {
        var clean = body?.Trim() ?? ""; if (clean.Length is < 1 or > 4000) throw new ContractReviewRuleException("O comentário deve ter entre 1 e 4.000 caracteres.");
        var cleanReference = reference?.Trim(); if (cleanReference?.Length > 120) throw new ContractReviewRuleException("A referência deve ter no máximo 120 caracteres.");
        return new(id, reviewId, versionId, author, clean, cleanReference, now);
    }
    internal void Resolve(Guid actor, DateTimeOffset now) { if (ResolvedAt is not null) throw new ContractReviewRuleException("O comentário já foi resolvido."); ResolvedBy=actor; ResolvedAt=now; }
}

public sealed record ReviewEvent(string Type, Guid ActorId, DateTimeOffset OccurredAt, string? Detail);
public sealed class ContractReviewRuleException(string message) : InvalidOperationException(message);
public sealed class ContractReviewConflictException(long expected, long actual) : InvalidOperationException($"A revisão foi atualizada (esperada {expected}, atual {actual}).")
{ public long Expected { get; } = expected; public long Actual { get; } = actual; }
