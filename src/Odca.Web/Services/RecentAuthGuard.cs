using System.Text.Json;

namespace Odca.Web.Services;

/// <summary>D-OC39 (Sprint 5/F-1): espelho BFF da política RecentMfa da API.
/// Decodifica as claims do bearer (sem revalidar a assinatura — a API é autoridade) e indica
/// quando um 403 veio da exigência de reautenticação recente, para o BFF redirecionar ao
/// desafio MFA existente preservando o destino original. Mesmas isenções da API: não-superadmin,
/// troca forçada de senha (must_change_password) e superadmin sem MFA inscrito.</summary>
public static class RecentAuthGuard
{
    public static bool NeedsFreshChallenge(string? accessToken, int windowMinutes)
    {
        var claims = DecodeClaims(accessToken);
        if (claims is null) return false;

        if (!HasRole(claims, "SuperAdministrator")) return false;
        if (claims.TryGetValue("must_change_password", out var mustChange) && mustChange == "true") return false;
        if (!claims.TryGetValue("mfa_enrolled", out var enrolled) || enrolled != "true") return false;

        if (!claims.TryGetValue("mfa_verified_at", out var verifiedRaw) ||
            !long.TryParse(verifiedRaw, out var seconds) || seconds <= 0)
        {
            return true;
        }

        var window = TimeSpan.FromMinutes(Math.Max(1, windowMinutes));
        return DateTimeOffset.UtcNow - DateTimeOffset.FromUnixTimeSeconds(seconds) > window;
    }

    private static Dictionary<string, string>? DecodeClaims(string? accessToken)
    {
        if (string.IsNullOrWhiteSpace(accessToken)) return null;
        var parts = accessToken.Split('.');
        if (parts.Length < 2) return null;
        try
        {
            var base64 = parts[1].Replace('-', '+').Replace('_', '/');
            switch (base64.Length % 4)
            {
                case 2: base64 += "=="; break;
                case 3: base64 += "="; break;
                case 1: return null;
            }
            using var document = JsonDocument.Parse(Convert.FromBase64String(base64));
            var result = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var property in document.RootElement.EnumerateObject())
            {
                result[property.Name] = property.Value.ValueKind == JsonValueKind.String
                    ? property.Value.GetString()!
                    : property.Value.GetRawText();
            }
            return result;
        }
        catch (FormatException)
        {
            return null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static bool HasRole(Dictionary<string, string> claims, string role)
    {
        const string roleClaimUri = "http://schemas.microsoft.com/ws/2008/06/identity/claims/role";
        foreach (var key in new[] { roleClaimUri, "role" })
        {
            if (claims.TryGetValue(key, out var value) &&
                value?.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Contains(role, StringComparer.OrdinalIgnoreCase) == true)
            {
                return true;
            }
        }

        return false;
    }
}
