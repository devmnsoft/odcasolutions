using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Odca.Application.Signatures;

namespace Odca.Infrastructure.Signatures;

/// <summary>Provedor não configurado ("none"): nenhum envelope é enviado e nenhuma franquia é consumida.</summary>
public sealed class DisabledSignatureEnvelopeProvider : ISignatureEnvelopeProvider
{
    public string Code => "none";

    public Task<SignatureEnvelopeSendResult> SendAsync(SignatureEnvelopeSendRequest request, CancellationToken ct)
        => throw new InvalidOperationException("Nenhum provedor de envelope de assinatura está configurado.");

    public Task<SignatureEnvelopeProviderState?> GetStateAsync(Guid tenantId, string envelopeRef, CancellationToken ct)
        => Task.FromResult<SignatureEnvelopeProviderState?>(null);
}

/// <summary>
/// D-OC37 (Sprint 3/A-1): transporte local de referência. O envelope é materializado como JSON
/// num diretório de drop (&lt;ref&gt;.envelope.json) e o estado externo é lido do arquivo
/// correspondente (&lt;ref&gt;.state.json) escrito pela contraparte. Modela a integração real
/// (entrega + evento de estado) sem assumir fornecedor específico. Formato do estado:
/// {"status":"accepted"|"declined"|"failed","occurred_at":"ISO-8601","detail":texto opcional}.
/// </summary>
public sealed class LocalFileSignatureEnvelopeProvider(string rootDirectory) : ISignatureEnvelopeProvider
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public string Code => "local-file";

    public Task<SignatureEnvelopeSendResult> SendAsync(SignatureEnvelopeSendRequest request, CancellationToken ct)
    {
        Directory.CreateDirectory(rootDirectory);
        var path = Path.Combine(rootDirectory, $"{request.EnvelopeRef}.envelope.json");
        if (!File.Exists(path))
        {
            var payload = new
            {
                request.TenantId,
                request.PreparationId,
                request.GeneratedVersionId,
                request.ProviderCode,
                participants = request.Participants.Select(participant => new { participant.ClientId, participant.Name, participant.ParticipantType, participant.Role }).ToArray(),
                pdfSha256 = request.PdfSha256,
                sentAt = DateTime.UtcNow,
            };
            File.WriteAllText(path, JsonSerializer.Serialize(payload, JsonOptions));
        }

        return Task.FromResult(new SignatureEnvelopeSendResult(true, Code));
    }

    public Task<SignatureEnvelopeProviderState?> GetStateAsync(Guid tenantId, string envelopeRef, CancellationToken ct)
    {
        var path = Path.Combine(rootDirectory, $"{envelopeRef}.state.json");
        if (!File.Exists(path)) return Task.FromResult<SignatureEnvelopeProviderState?>(null);
        using var document = JsonDocument.Parse(File.ReadAllText(path));
        var status = document.RootElement.TryGetProperty("status", out var statusElement) ? statusElement.GetString() : null;
        if (status is not ("accepted" or "declined" or "failed"))
            throw new FormatException($"Estado desconhecido no arquivo de estado do envelope: {status}");
        var occurredAt = document.RootElement.TryGetProperty("occurred_at", out var occurredElement) && occurredElement.ValueKind == JsonValueKind.String
            ? DateTimeOffset.Parse(occurredElement.GetString()!, System.Globalization.CultureInfo.InvariantCulture)
            : DateTime.UtcNow;
        var detail = document.RootElement.TryGetProperty("detail", out var detailElement) && detailElement.ValueKind == JsonValueKind.String
            ? detailElement.GetString()
            : null;
        return Task.FromResult<SignatureEnvelopeProviderState?>(new SignatureEnvelopeProviderState(status, occurredAt, detail));
    }
}

public static class SignatureEnvelopeProviderRegistration
{
    /// <summary>
    /// Seleção condicional por configuração (avaliada na resolução, não no registro):
    /// Signatures:Provider=local-file + Signatures:LocalFileDirectory resolve o transporte de
    /// referência; qualquer outro valor preserva "none" (proposto, não escolhido).
    /// </summary>
    public static IServiceCollection AddOdcaSignatureEnvelopeProvider(this IServiceCollection services)
    {
        services.AddSingleton<ISignatureEnvelopeProvider>(provider =>
        {
            var configuration = provider.GetRequiredService<IConfiguration>();
            var code = configuration.GetValue("Signatures:Provider", "none");
            var directory = configuration["Signatures:LocalFileDirectory"];
            return string.Equals(code, "local-file", StringComparison.Ordinal) && !string.IsNullOrWhiteSpace(directory)
                ? new LocalFileSignatureEnvelopeProvider(directory!)
                : new DisabledSignatureEnvelopeProvider();
        });
        return services;
    }
}
