namespace Odca.Application.Signatures;

/// <summary>Participante da preparação de assinatura confirmada projetado no envelope externo (metadados).</summary>
public sealed record SignatureEnvelopeParticipantInfo(Guid ClientId, string Name, string ParticipantType, string Role);

/// <summary>Payload do envelope entregue ao provedor no momento do envio.</summary>
public sealed record SignatureEnvelopeSendRequest(
    Guid TenantId,
    Guid ActorId,
    Guid PreparationId,
    Guid GeneratedVersionId,
    string ProviderCode,
    string EnvelopeRef,
    IReadOnlyList<SignatureEnvelopeParticipantInfo> Participants,
    string? PdfSha256);

public sealed record SignatureEnvelopeSendResult(bool Sent, string ProviderCode);

/// <summary>Estado externo reportado pelo provedor (accepted|declined|failed). Null = ainda desconhecido.</summary>
public sealed record SignatureEnvelopeProviderState(string Status, DateTimeOffset OccurredAt, string? Detail);

/// <summary>
/// D-OC37 (Sprint 3/A-1): contrato do provedor plugável de envelopes externos de assinatura.
/// A plataforma nunca assume fornecedor específico: o código "none" preserva a decisão
/// "proposto, não escolhido" (nenhum envelope é enviado e nenhuma franquia é consumida) e
/// "local-file" é o transporte de referência (pasta de drop) para provas de integração.
/// O provedor deve ser idempotente por referência de envelope.
/// </summary>
public interface ISignatureEnvelopeProvider
{
    string Code { get; }

    Task<SignatureEnvelopeSendResult> SendAsync(SignatureEnvelopeSendRequest request, CancellationToken ct);

    Task<SignatureEnvelopeProviderState?> GetStateAsync(Guid tenantId, string envelopeRef, CancellationToken ct);
}
