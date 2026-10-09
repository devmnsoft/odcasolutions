namespace Odca.Contracts.Solicitations;

/// <summary>Seção C: abertura de solicitação cliente -> ODCA. A chave de idempotência evita
/// duplicatas em reenvio do formulário.</summary>
public sealed record OpenSolicitationRequest(string Service, string? Priority, string Subject, string Body, Guid IdempotencyKey);

public sealed record SolicitationMessageRequest(string Body, long RowVersion, Guid? TenantId = null);

/// <summary>Ações da máquina de estados: triar, iniciar, aguardar, pausar, retomar, resolver,
/// reatribuir, encerrar, cancelar. Campos extras conforme a ação (text p/ justificativa e
/// mensagem, priority p/ triagem, assigneeUserId p/ reatribuição).</summary>
public sealed record SolicitationActionRequest(string Action, string? Text, string? Priority, Guid? AssigneeUserId, long RowVersion, Guid? TenantId = null);

public sealed record ApprovalDecisionRequest(string Decision, string? Note);

/// <summary>Linha da fila de solicitações (tenant e plataforma compartilham a forma).</summary>
public sealed record SolicitationListItem(Guid Id, Guid TenantId, string OrganizationName, string Protocol, string Service, string Priority,
    string Status, string Subject, DateTime? OpenedAt, DateTime? UpdatedAt, string? AssigneeName, DateTime? FirstResponseDueAt,
    DateTime? ResolutionDueAt, DateTime? FirstResponseBreachAt, DateTime? ResolutionBreachAt, bool Paused, DateTime? ResolvedAt,
    DateTime? ClosedAt, DateTime? CancelledAt, string? CancellationReason);

/// <summary>Fila ODCA de modelos oficiais que exigem aprovação (instalação por organização).</summary>
public sealed record ApprovalQueueRow(Guid TenantId, string OrganizationName, string? ActivityProfile, string OfficialKey,
    string? Decision, string? DecisionNote, DateTime? DecidedAt, string? DecidedByName, long GeneratedCount, DateTime? LastGeneratedAt);
