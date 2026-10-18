using Odca.Contracts.Obligations;

namespace Odca.Web.Models;

// D-OC38 (Sprint 4/A-1): superfícies de cadastro e ações de obrigações na central do cliente.
// Pt-first (a localização das duas telas é dívida registrada em docs/execution). A API continua
// sendo a única autoridade de validação, permissão, versão e auditoria; o BFF apenas conecta o
// formulário ao endpoint e preserva os valores do usuário após conflito 409 de versão.

/// <summary>Opção do seletor de contrato no cadastro de obrigação.</summary>
public sealed record ObligationContractOption(Guid Id, string Title, string? EndDate);

/// <summary>Opção do seletor de responsável (membro ativo da organização).</summary>
public sealed record ObligationMemberOption(Guid Id, string Name, string Email);

/// <summary>Opção de versão de documento (cláusula no cadastro, evidência no cumprimento).</summary>
public sealed record ObligationDocumentOption(Guid VersionId, string Label, bool Safe);

/// <summary>Formulário de nova obrigação. Catálogos vêm da API; campos preservam valores na falha.</summary>
public sealed class ObligationCreateModel
{
    public IReadOnlyList<ObligationContractOption> Contracts { get; set; } = [];
    public IReadOnlyList<ObligationMemberOption> Members { get; set; } = [];

    public Guid? ContractId { get; set; }
    public string? Title { get; set; }
    public string? Description { get; set; }
    public string Category { get; set; } = "other";
    public string? ObligatedParty { get; set; }
    public Guid? OwnerId { get; set; }
    public DateOnly? DueDate { get; set; }
    public string Priority { get; set; } = "normal";
    public decimal? Amount { get; set; }
    public string? Currency { get; set; }
    public Guid? ClauseDocumentVersionId { get; set; }
    public bool EvidenceRequired { get; set; }
    public string? PostTermReason { get; set; }
    public bool RecurrenceEnabled { get; set; }
    public DateOnly? RecurrenceBaseDate { get; set; }
    public int RecurrenceIntendedDay { get; set; } = 15;
    public DateOnly? RecurrenceEndsOn { get; set; }
    public int? RecurrenceOccurrenceCount { get; set; }
    public string? RecurrenceTimeZone { get; set; } = "America/Sao_Paulo";
    public string[] ReminderDays { get; set; } = ["7", "1", "0"];

    public string? ErrorMessage { get; set; }
}

/// <summary>Formulário de ação sobre uma obrigação (start/fulfill/cancel/reschedule/reassign/reopen).
/// Em conflito 409 a versão é atualizada e os valores submetidos voltam preenchidos.</summary>
public sealed class ObligationActionModel
{
    public ObligationItem Item { get; set; } = null!;
    public long ExpectedVersion { get; init; }
    public IReadOnlyList<ObligationMemberOption> Members { get; init; } = [];
    public IReadOnlyList<ObligationDocumentOption> Documents { get; init; } = [];

    public string? ConflictMessage { get; set; }
    public string? OperationError { get; set; }
    // Valores preservados ao repetir a ação após conflito 409 de versão.
    public string? Reason { get; set; }
    public string? Note { get; set; }
    public DateOnly? DueDate { get; set; }
    public Guid? OwnerId { get; set; }
    public Guid? EvidenceDocumentVersionId { get; set; }
}
