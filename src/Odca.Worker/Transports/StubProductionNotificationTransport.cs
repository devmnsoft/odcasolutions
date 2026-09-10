using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace Odca.Worker.Transports;

public sealed class StubProductionNotificationTransport : INotificationTransport
{
    private readonly ILogger<StubProductionNotificationTransport> _logger;

    public StubProductionNotificationTransport(ILogger<StubProductionNotificationTransport> logger)
    {
        _logger = logger;
    }

    public Task SendInvitationAsync(string destination, Uri link, CancellationToken cancellationToken)
    {
        _logger.LogInformation("STUB PRODUCTION ADAPTER: Sending invitation to {Destination} with link {Link}", destination, link);
        return Task.CompletedTask;
    }
}
