using System.Net;
using System.Net.Mail;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Odca.Configuration.Localization;

namespace Odca.Worker.Transports;

/// <summary>
/// Transporte SMTP real (B.1 do D-OC31). Só é selecionado quando
/// Notifications:Smtp:Host e Notifications:Smtp:SenderAddress estão definidos;
/// sem configuração válida o worker continua no
/// <see cref="StubProductionNotificationTransport"/>, que falha honestamente com
/// notification_provider_missing. Sucesso no SMTP significa aceite pelo servidor
/// receptor (MTA): a entrega final na caixa de entrada não é comprovável sem
/// recibo estendido, então a outbox nunca promete entrega única ou confirmada.
/// </summary>
public sealed class SmtpNotificationTransport : INotificationTransport, IDisposable
{
    private readonly SmtpClient _client;
    private readonly string _senderAddress;
    private readonly string? _senderName;
    private readonly LocalizationService _localizer;
    private readonly ILogger<SmtpNotificationTransport> _logger;

    public SmtpNotificationTransport(IConfiguration configuration, LocalizationService localizer, ILogger<SmtpNotificationTransport> logger)
    {
        var section = configuration.GetSection("Notifications:Smtp");
        var host = section["Host"] ?? string.Empty;
        var senderAddress = section["SenderAddress"] ?? string.Empty;
        if (string.IsNullOrWhiteSpace(host) || string.IsNullOrWhiteSpace(senderAddress))
        {
            throw new InvalidOperationException(
                "Notifications:Smtp:Host e Notifications:Smtp:SenderAddress são obrigatórios para o transporte SMTP.");
        }

        var port = int.TryParse(section["Port"], out var parsedPort) ? parsedPort : 587;
        _client = new SmtpClient(host, port)
        {
            EnableSsl = !bool.TryParse(section["UseStartTls"], out var useStartTls) || useStartTls,
            Timeout = int.TryParse(section["TimeoutMilliseconds"], out var timeout) && timeout > 0 ? timeout : 30000,
        };

        var username = section["Username"];
        var password = section["Password"];
        if (!string.IsNullOrWhiteSpace(username) || !string.IsNullOrWhiteSpace(password))
        {
            _client.Credentials = new NetworkCredential(username ?? string.Empty, password ?? string.Empty);
        }

        _senderAddress = senderAddress;
        _senderName = string.IsNullOrWhiteSpace(section["SenderName"]) ? null : section["SenderName"];
        _localizer = localizer;
        _logger = logger;
    }

    public Task SendInvitationAsync(string destination, Uri link, CancellationToken cancellationToken)
    {
        var body = $"Você recebeu um convite de acesso à plataforma.\n\n{link}\n\nSe não esperava este convite, pode ignorar esta mensagem.";
        return SendAsync(destination, "Convite de acesso — ODCA Solutions", body, cancellationToken);
    }

    public Task SendUserNotificationAsync(string destination, string subject, Uri link, string language, CancellationToken cancellationToken)
    {
        // D-OC46 (Sprint 12/F-1): sentença de apoio no idioma do destinatário (fallback pt-BR);
        // o subject já foi composto localizado no enfileiramento.
        var sentence = _localizer.T("notif.body.smtp_link_sentence", language);
        var body = $"{link}\n\n{sentence}";
        return SendAsync(destination, subject, body, cancellationToken);
    }

    private async Task SendAsync(string destination, string subject, string body, CancellationToken cancellationToken)
    {
        var message = new MailMessage(_senderAddress, destination)
        {
            Subject = subject,
            Body = body,
        };
        // D-OC44 (Sprint 10): o pipeline de texto do System.Net.Mail codifica o corpo em
        // US-ASCII por padrão e substituiria acentos por '?'; UTF-8 explícito garante o
        // charset declarado no cabeçalho Content-Type do corpo.
        message.BodyEncoding = System.Text.Encoding.UTF8;
        if (!string.IsNullOrWhiteSpace(_senderName))
        {
            message.From = new MailAddress(_senderAddress, _senderName);
        }

        await _client.SendMailAsync(message, cancellationToken);
        _logger.LogInformation("Mensagem aceita pelo servidor de correio; entrega final não comprovável sem recibo.");
    }

    public void Dispose() => _client.Dispose();
}
