using Odca.Application.Tenancy;

namespace Odca.Application.Contracts;

public sealed class UserNotificationService(IUserNotificationRepository repository)
{
    public Task<QueryAccess<TenantPage<UserNotificationRecord>>> ListAsync(
        Guid actorId,
        Guid tenantId,
        bool unreadOnly,
        int? page,
        int? pageSize,
        CancellationToken cancellationToken)
    {
        var (p, s) = Pagination.Normalize(page, pageSize);
        return repository.ListAsync(actorId, tenantId, unreadOnly, p, s, cancellationToken);
    }

    public Task<QueryAccess<int>> UnreadCountAsync(Guid actorId, Guid tenantId, CancellationToken cancellationToken)
        => repository.UnreadCountAsync(actorId, tenantId, cancellationToken);

    public Task<QueryAccess<UserNotificationRecord?>> GetAsync(
        Guid actorId,
        Guid tenantId,
        Guid id,
        CancellationToken cancellationToken)
        => repository.GetAsync(actorId, tenantId, id, cancellationToken);

    public Task<MutationResult> MarkReadAsync(Guid actorId, Guid tenantId, Guid id, CancellationToken cancellationToken)
        => repository.MarkReadAsync(actorId, tenantId, id, cancellationToken);
}
