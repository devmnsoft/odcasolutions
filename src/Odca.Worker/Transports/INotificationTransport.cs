using System;
using System.Threading;
using System.Threading.Tasks;

namespace Odca.Worker.Transports;

public interface INotificationTransport
{
    Task SendInvitationAsync(string destination, Uri link, CancellationToken cancellationToken);
}
