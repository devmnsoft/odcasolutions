using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Text.RegularExpressions;

namespace Odca.Contracts.SavedViews;

/// <summary>
/// Domain resolver for dynamic relative date tokens in saved views.
/// Evaluates dates based on the tenant's civil date (TimeProvider + timezone),
/// preventing absolute DateOnly persistence in view definitions.
/// </summary>
public static partial class RelativeDateResolver
{
    public const string Today = "today";
    public const string Overdue = "overdue";
    public const string DueToday = "dueToday";
    public const string DueThisWeek = "dueThisWeek";

    [GeneratedRegex(@"^nextDays:(?<days>\d+)$", RegexOptions.IgnoreCase | RegexOptions.Compiled)]
    private static partial Regex NextDaysRegex();

    public static bool IsValid(string? token) => IsValidToken(token);

    public static bool IsValidToken(string? token)
    {
        if (string.IsNullOrWhiteSpace(token)) return false;
        var trimmed = token.Trim();
        if (string.Equals(trimmed, Today, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(trimmed, Overdue, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(trimmed, DueToday, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(trimmed, DueThisWeek, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        var match = NextDaysRegex().Match(trimmed);
        if (match.Success && int.TryParse(match.Groups["days"].Value, NumberStyles.None, CultureInfo.InvariantCulture, out var days))
        {
            return days is >= 0 and <= 365;
        }

        return false;
    }

    public static bool TryNormalize(string? token, [NotNullWhen(true)] out string? normalized) =>
        TryNormalizeToken(token, out normalized);

    public static bool TryNormalizeToken(string? token, [NotNullWhen(true)] out string? normalized)
    {
        if (!IsValidToken(token))
        {
            normalized = null;
            return false;
        }

        normalized = NormalizeToken(token!);
        return true;
    }

    public static string Normalize(string token) => NormalizeToken(token);

    public static string NormalizeToken(string token)
    {
        ArgumentNullException.ThrowIfNull(token);
        var trimmed = token.Trim();
        if (string.Equals(trimmed, Today, StringComparison.OrdinalIgnoreCase)) return Today;
        if (string.Equals(trimmed, Overdue, StringComparison.OrdinalIgnoreCase)) return Overdue;
        if (string.Equals(trimmed, DueToday, StringComparison.OrdinalIgnoreCase)) return DueToday;
        if (string.Equals(trimmed, DueThisWeek, StringComparison.OrdinalIgnoreCase)) return DueThisWeek;

        var match = NextDaysRegex().Match(trimmed);
        if (match.Success && int.TryParse(match.Groups["days"].Value, NumberStyles.None, CultureInfo.InvariantCulture, out var days) && days is >= 0 and <= 365)
        {
            return $"nextDays:{days}";
        }

        throw new ArgumentException($"Token de data relativa inválido: '{token}'.", nameof(token));
    }

    public static DateOnly ResolveCivilDate(TimeProvider timeProvider, TimeZoneInfo timeZone) =>
        ResolveCivilToday(timeProvider, timeZone);

    public static DateOnly ResolveCivilToday(TimeProvider timeProvider, TimeZoneInfo timeZone)
    {
        ArgumentNullException.ThrowIfNull(timeProvider);
        ArgumentNullException.ThrowIfNull(timeZone);

        var utcNow = timeProvider.GetUtcNow();
        var localDateTime = TimeZoneInfo.ConvertTime(utcNow, timeZone);
        return DateOnly.FromDateTime(localDateTime.DateTime);
    }

    public static (DateOnly? From, DateOnly? To) ResolveRange(string token, DateOnly today) =>
        ResolveDateRange(token, today);

    public static (DateOnly? From, DateOnly? To) ResolveDateRange(string token, DateOnly today)
    {
        var normalized = NormalizeToken(token);
        return normalized switch
        {
            Today => (today, today),
            DueToday => (today, today),
            Overdue => (null, today.AddDays(-1)),
            DueThisWeek => (today, today.AddDays(7)),
            _ when NextDaysRegex().Match(normalized) is { Success: true } match =>
                (today, today.AddDays(int.Parse(match.Groups["days"].Value, CultureInfo.InvariantCulture))),
            _ => throw new InvalidOperationException($"Token não suportado: '{token}'.")
        };
    }

    public static string? MapToUrgency(string token)
    {
        if (!IsValidToken(token)) return null;
        var normalized = NormalizeToken(token);
        return normalized switch
        {
            Overdue => "Overdue",
            Today or DueToday => "DueToday",
            DueThisWeek => "DueThisWeek",
            _ => null
        };
    }
}
