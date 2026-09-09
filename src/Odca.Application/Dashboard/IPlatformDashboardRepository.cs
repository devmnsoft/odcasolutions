namespace Odca.Application.Dashboard;

public interface IPlatformDashboardRepository
{
    Task<PlatformDashboardSnapshot> GetAsync(CancellationToken cancellationToken);
}
