namespace Odca.Contracts.Account;

/// <summary>Notificação individual no feed do usuário.</summary>
public sealed record NotificationItem(
    Guid Id,
    string Kind,
    string Title,
    string Body,
    Guid? ObligationId,
    DateTimeOffset CreatedAt,
    DateTimeOffset? ReadAt,
    string? TargetType,
    Guid? TargetId,
    bool CanOpen,
    string CreatedAtFormatted);

/// <summary>Paginação do feed de notificações.</summary>
public sealed record NotificationPage(
    IReadOnlyList<NotificationItem> Items,
    int UnreadCount,
    int Page,
    int PageSize,
    int Total,
    bool HasMore);

/// <summary>Preferência por tipo (mutable para form-binding + JSON).</summary>
public sealed class NotificationKindPreference
{
    public string Kind { get; set; } = "";
    public bool Enabled { get; set; } = true;
    public bool ChannelInApp { get; set; } = true;
    public bool ChannelEmail { get; set; }
}

/// <summary>Resumo das preferências de notificação do usuário.</summary>
public sealed record NotificationPreferences(
    string PreferredLanguage,
    IReadOnlyList<NotificationKindPreference> Kinds);

/// <summary>Requisição de atualização de preferências (mutable para form-binding + JSON).</summary>
public sealed class UpdateNotificationPreferencesRequest
{
    public string PreferredLanguage { get; set; } = "pt-BR";
    public List<NotificationKindPreference>? Kinds { get; set; }
}
