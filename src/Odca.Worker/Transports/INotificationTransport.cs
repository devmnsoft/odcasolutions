using System;
using System.Threading;
using System.Threading.Tasks;

namespace Odca.Worker.Transports;

public interface INotificationTransport
{
    Task SendInvitationAsync(string destination, Uri link, CancellationToken cancellationToken);

    /// <summary>D-OC46 (Sprint 12/F-1): <paramref name="language"/> é o idioma preferido do
    /// destinatário gravado no enfileiramento (v062, fallback pt-BR), usado para compor o
    /// corpo da mensagem; o subject já chega localizado no payload protegido.</summary>
    Task SendUserNotificationAsync(string destination, string subject, Uri link, string language, CancellationToken cancellationToken);
}
