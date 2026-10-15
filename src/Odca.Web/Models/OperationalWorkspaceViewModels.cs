using Odca.Contracts.Contracts;
using Odca.Contracts.Operations;
using Odca.Contracts.Tenancy;
using Odca.Contracts.SavedViews;

namespace Odca.Web.Models;

public sealed class InboxWorkspaceViewModel
{
    public OperationalInboxPageDto Page { get; init; } = new([], 1, 20, 0, 0, 0, 0);
    public string Scope { get; init; } = "mine";
    public string? Kind { get; init; }
    public string? Urgency { get; init; }
    public string? Error { get; init; }
    public IReadOnlyList<SavedViewItem> Views { get; init; } = [];
    public SavedViewItem? CurrentView { get; init; }
}

public sealed class AgendaWorkspaceViewModel
{
    public MonthlyAgendaPageDto Page { get; init; } = new(1, 1, DateOnly.MinValue, DateOnly.MinValue, DateOnly.MinValue, [], 0);
    public string Scope { get; init; } = "mine";
    public string? Kind { get; init; }
    public int? Day { get; init; }
    public string? Error { get; init; }
}

public sealed class ContractListViewModel
{
    public Guid TenantId { get; init; }
    public IReadOnlyList<ContractListItem> Contracts { get; init; } = [];
    public int Page { get; init; } = 1;
    public int PageSize { get; init; } = 20;
    public int Total { get; init; }
    public string? Search { get; init; }
    public string Status { get; init; } = "active";
    public bool CanCreateDocument { get; init; }
    public bool CanManageContracts { get; init; }
    public bool CanReadHistory { get; init; }
    public string? Error { get; init; }
}

public sealed class ContractSheetViewModel
{
    public ContractSheetDto? Sheet { get; init; }
    public string? Error { get; init; }
    public IReadOnlyList<TeamMemberResponse> ActiveMembers { get; init; } = [];
    public Guid? SelectedObligationId { get; init; }
    public string? FromSource { get; init; }
    public int? Year { get; init; }
    public int? Month { get; init; }
    public string? Scope { get; init; }
    public string? Kind { get; init; }
    public string? Urgency { get; init; }
    public Guid? ViewId { get; init; }
    public IReadOnlyList<Odca.Contracts.Studio.StudioCommentItem> Comments { get; init; } = [];
    public IReadOnlyList<Odca.Contracts.Studio.StudioVersionItem> Versions { get; init; } = [];
    public IReadOnlyList<Odca.Contracts.Studio.StudioReviewerItem> Reviewers { get; init; } = [];
    // B.3.2c — jornada completa na ficha (assinatura, ações de ciclo de vida, histórico)
    public IReadOnlyList<Odca.Contracts.Contracts.ContractEventItem> Events { get; init; } = [];
    public int EventTotal { get; init; }
    public int EventPage { get; init; } = 1;
    public bool CanManageContracts { get; init; }
    public bool CanReadHistory { get; init; }
    public bool CanSignDocuments { get; init; }
    public bool CanReadDocuments { get; init; }
    // D-OC31 B.3 — resumo contínuo da jornada (próxima ação + pendências + bloqueio)
    public ContractJourneySummary? Journey { get; init; }
}

/// <summary>Pendência projetada no resumo da jornada, com âncora para a seção da ficha.</summary>
public sealed record ContractJourneyPending(string LabelKey, int Count, string Anchor);

/// <summary>
/// D-OC31 B.3: situação consolidada do contrato em um único bloco — próxima ação,
/// pendências abertas por seção e motivo de bloqueio quando o ciclo estiver encerrado.
/// Rótulos são chaves de catálogo (traduzidas na vista); o motivo é conteúdo do usuário
/// e nunca é traduzido.
/// </summary>
public sealed class ContractJourneySummary
{
    public string NextActionKey { get; init; } = "sheet.journey_next_none";
    public string? BlockKey { get; init; }
    public string? BlockReason { get; init; }
    public IReadOnlyList<ContractJourneyPending> Pending { get; init; } = [];
}

internal static class ContractJourney
{
    public static ContractJourneySummary Build(ContractSheetDto s)
    {
        var pending = new List<ContractJourneyPending>();
        if (s.ArchivedAt is not null)
        {
            return new ContractJourneySummary { NextActionKey = "sheet.journey_next_archived", BlockKey = "sheet.journey_block_archived", BlockReason = s.ArchiveReason };
        }
        if (s.ClosedAt is not null)
        {
            return new ContractJourneySummary { NextActionKey = "sheet.journey_next_closed", BlockKey = "sheet.journey_block_closed", BlockReason = s.ClosureReason };
        }

        var openObligations = s.OpenObligations.Count;
        var overdueObligations = s.OpenObligations.Count(item => item.Urgency == "Overdue");
        var reviewOpen = s.CurrentReview is not null && s.CurrentReview.Status is "in_review" or "changes_requested";
        var openChanges = (s.PendingChanges ?? []).Count(change => change.Status is "draft" or "in_review" or "internally_approved" or "awaiting_formalization");
        var signatureRemaining = s.Signature is not null && s.Signature.PreparationStatus == "confirmed"
            ? s.Signature.Participants.Count(participant => participant.SignedAt is null)
            : 0;
        var openSolicitations = (s.Solicitations ?? []).Count(item => item.Status is not ("resolvida" or "encerrada" or "cancelada"));
        var renewalDue = s.Renewal is not null && (s.Renewal.InThreeMonthWindow || (s.Renewal.EndsOn is not null && s.Today is not null && s.Renewal.EndsOn <= s.Today.Value));

        if (openObligations > 0) pending.Add(new ContractJourneyPending("sheet.journey_pend_obligations", openObligations, "#obrigacoes"));
        if (reviewOpen) pending.Add(new ContractJourneyPending("sheet.journey_pend_review", 1, "#revisao"));
        if (openChanges > 0) pending.Add(new ContractJourneyPending("sheet.journey_pend_changes", openChanges, "#alteracoes"));
        if (signatureRemaining > 0) pending.Add(new ContractJourneyPending("sheet.journey_pend_signatures", signatureRemaining, "#assinatura"));
        if (openSolicitations > 0) pending.Add(new ContractJourneyPending("sheet.journey_pend_solicitations", openSolicitations, "#solicitacoes"));
        if (renewalDue) pending.Add(new ContractJourneyPending("sheet.journey_pend_renewal", 1, "#renovacao"));

        string nextAction = "sheet.journey_next_none";
        if (overdueObligations > 0) nextAction = "sheet.journey_next_overdue_obligation";
        else if (reviewOpen) nextAction = "sheet.journey_next_review";
        else if (openChanges > 0) nextAction = "sheet.journey_next_change";
        else if (renewalDue) nextAction = "sheet.journey_next_renewal";
        else if (signatureRemaining > 0) nextAction = "sheet.journey_next_signature";
        else if (openSolicitations > 0) nextAction = "sheet.journey_next_solicitation";
        else if (openObligations > 0) nextAction = "sheet.journey_next_obligations";

        return new ContractJourneySummary { NextActionKey = nextAction, Pending = pending };
    }
}
