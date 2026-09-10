using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace Odca.Worker.Transports;

public sealed class FilePickupNotificationTransport : INotificationTransport
{
    private readonly string _pickupDirectory;

    public FilePickupNotificationTransport(string pickupDirectory)
    {
        _pickupDirectory = pickupDirectory ?? throw new ArgumentNullException(nameof(pickupDirectory));
        Directory.CreateDirectory(_pickupDirectory);
    }

    public async Task SendInvitationAsync(string destination, Uri link, CancellationToken cancellationToken)
    {
        var id = Guid.NewGuid();
        var content = $"Destinatário sintético/local: {destination}\n{link}\n";
        var path = Path.Combine(_pickupDirectory, $"{id:N}.txt");
        await File.WriteAllTextAsync(path, content, cancellationToken);
    }
}
