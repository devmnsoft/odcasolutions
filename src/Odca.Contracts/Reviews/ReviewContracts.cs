namespace Odca.Contracts.Reviews;

public sealed record ReviewQueuePage(IReadOnlyList<ReviewQueueItem> Items, int Page, int PageSize, int Total);
public sealed record ReviewQueueItem(Guid Id, Guid ContractId, string Contract, string Status, string Requester,
    string? Assignee, DateTimeOffset OpenedAt, DateTimeOffset UpdatedAt, DateTimeOffset? DueAt, long Version,
    int PublicMessages, int PendingComments);
public sealed record ReviewDetail(Guid Id, Guid ContractId, string Contract, string Status, string Requester,
    string? Assignee, string? Instructions, DateTimeOffset OpenedAt, DateTimeOffset UpdatedAt,
    DateTimeOffset? DueAt, long Version, IReadOnlyList<ReviewMessage> Messages, IReadOnlyList<ReviewHistoryItem> History);
public sealed record ReviewMessage(Guid Id, string Body, string? Reference, string Author, string Visibility,
    DateTimeOffset CreatedAt, bool Resolved);
public sealed record ReviewHistoryItem(long Id, string Type, string Actor, DateTimeOffset OccurredAt);
public sealed record AddReviewMessageRequest(string Body, string Visibility, string? Reference, Guid IdempotencyKey);
public sealed record DecideReviewRequest(string Action, string Justification, long ExpectedVersion, Guid IdempotencyKey);
