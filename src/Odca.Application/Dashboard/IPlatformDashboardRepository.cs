namespace Odca.Application.Dashboard;

public interface IPlatformDashboardRepository
{
    Task<PlatformDashboardSnapshot> GetAsync(Guid requestingUserId, CancellationToken cancellationToken);
}
