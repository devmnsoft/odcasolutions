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
        _logger.LogWarning(
            "Provedor de notificação de produção não configurado; destino omitido do log de negócio.");
        throw new InvalidOperationException("notification_provider_missing");
    }
}
