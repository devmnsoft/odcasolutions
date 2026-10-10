namespace Odca.Contracts.Account;

public sealed record NotificationItem(Guid Id, string Kind, string Title, string Body, Guid? ObligationId, DateTimeOffset CreatedAt, DateTimeOffset? ReadAt);

public sealed record NotificationPage(IReadOnlyList<NotificationItem> Items, int UnreadCount);
