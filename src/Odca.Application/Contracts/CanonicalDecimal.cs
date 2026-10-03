using System.Globalization;

namespace Odca.Application.Contracts;

/// <summary>
/// Converts Brazilian and invariant decimal text into a canonical string.
/// Binary floating point is never used. Grouping and decimal separators are
/// distinguished explicitly; an ambiguous grouping is rejected.
/// </summary>
public static class CanonicalDecimal
{
    public readonly record struct ParseOutcome(bool Succeeded, string Canonical, string? Error);

    public static bool IsStoredCurrency(string value) =>
        System.Text.RegularExpressions.Regex.IsMatch(value, @"^(?:0|[1-9]\d*)(?:\.\d{1,2})?$", System.Text.RegularExpressions.RegexOptions.CultureInvariant)
        && decimal.TryParse(value, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var amount)
        && amount >= 0;

    public static bool IsStoredNumber(string value) =>
        System.Text.RegularExpressions.Regex.IsMatch(value, @"^-?(?:0|[1-9]\d*)(?:\.\d{1,6})?$", System.Text.RegularExpressions.RegexOptions.CultureInvariant)
        && decimal.TryParse(value, NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out _);

    public static ParseOutcome Parse(string? raw, bool currency)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return new(false, string.Empty, "Informe um valor.");

        var text = raw.Trim().Replace("\u00A0", string.Empty, StringComparison.Ordinal);
        if (text.StartsWith("R$", StringComparison.OrdinalIgnoreCase))
            text = text[2..].Trim();
        if (text.Contains(' ') || text.Any(char.IsLetter))
            return new(false, string.Empty, "O valor contém texto que não faz parte do número.");

        var negative = false;
        if (text.StartsWith('+'))
            text = text[1..];
        else if (text.StartsWith('-'))
        {
            negative = true;
            text = text[1..];
        }

        if (text.Length == 0 || text.Any(ch => !char.IsDigit(ch) && ch is not '.' and not ','))
            return new(false, string.Empty, "O valor numérico é inválido.");
        if (negative && currency)
            return new(false, string.Empty, "Valor monetário negativo não é permitido.");

        if (!TrySplit(text, out var digits, out var error))
            return new(false, string.Empty, error);

        if (currency && digits.Fraction.Length > 2)
            return new(false, string.Empty, "A moeda aceita no máximo duas casas decimais.");
        if (!currency && digits.Fraction.Length > 6)
            return new(false, string.Empty, "O número excede seis casas decimais.");

        var canonicalBody = digits.Fraction.Length == 0
            ? digits.Integer
            : digits.Integer + "." + digits.Fraction;
        if (!decimal.TryParse(canonicalBody, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var amount))
            return new(false, string.Empty, "O valor numérico é inválido.");
        if (negative)
            amount = decimal.Negate(amount);

        var canonical = currency
            ? amount.ToString("0.00", CultureInfo.InvariantCulture)
            : (negative ? "-" : string.Empty) + digits.Integer + (digits.Fraction.Length == 0 ? string.Empty : "." + digits.Fraction);
        if (currency && !IsStoredCurrency(canonical))
            return new(false, string.Empty, "O valor monetário é inválido.");
        if (!currency && !IsStoredNumber(canonical))
            return new(false, string.Empty, "O valor numérico é inválido.");
        return new(true, canonical, null);
    }

    public static bool TryFormatPtBr(string stored, out string formatted)
    {
        formatted = string.Empty;
        if (!IsStoredCurrency(stored))
            return false;
        var amount = decimal.Parse(stored, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture);
        formatted = amount.ToString("N2", CultureInfo.GetCultureInfo("pt-BR"));
        return true;
    }

    private readonly record struct Parts(string Integer, string Fraction);

    private static bool TrySplit(string text, out Parts parts, out string? error)
    {
        parts = default;
        error = null;
        var comma = text.LastIndexOf(',');
        var dot = text.LastIndexOf('.');
        if (comma >= 0 && dot >= 0)
        {
            var decimalSeparator = comma > dot ? ',' : '.';
            var groupSeparator = decimalSeparator == ',' ? '.' : ',';
            var split = text.LastIndexOf(decimalSeparator);
            var fraction = text[(split + 1)..];
            if (fraction.Length is 0 or > 6 || text.IndexOf(decimalSeparator) != split)
            {
                error = "O separador decimal está incompleto ou repetido.";
                return false;
            }

            if (!TryGroups(text[..split], groupSeparator, out var integer))
            {
                error = "O separador de milhar está inconsistente.";
                return false;
            }

            parts = new(integer, fraction);
            return true;
        }

        if (comma < 0 && dot < 0)
        {
            if (text.Length > 1 && text[0] == '0')
            {
                error = "O valor numérico é inválido.";
                return false;
            }

            parts = new(text, string.Empty);
            return true;
        }

        var separator = comma >= 0 ? ',' : '.';
        var count = text.Count(ch => ch == separator);
        var last = text.LastIndexOf(separator);
        var tail = text[(last + 1)..];
        if (count == 1)
        {
            if (tail.Length == 3)
            {
                error = "O valor é ambíguo: informe as casas decimais (ex.: 1.250,00) ou o valor sem separador de milhar.";
                return false;
            }

            if (tail.Length is 0 or > 6)
            {
                error = "O valor numérico é inválido.";
                return false;
            }

            var head = text[..last];
            if (head.Length == 0 || (head.Length > 1 && head[0] == '0'))
            {
                error = "O valor numérico é inválido.";
                return false;
            }

            parts = new(head, tail);
            return true;
        }

        if (!TryGroups(text, separator, out var grouped))
        {
            error = "O separador de milhar está inconsistente.";
            return false;
        }

        parts = new(grouped, string.Empty);
        return true;
    }

    private static bool TryGroups(string value, char separator, out string digits)
    {
        digits = string.Empty;
        var groups = value.Split(separator);
        if (groups.Length < 2 || groups.Any(group => group.Length == 0 || group.Any(ch => !char.IsDigit(ch))))
            return false;
        if (groups[0].Length is < 1 or > 3 || (groups[0].Length > 1 && groups[0][0] == '0'))
            return false;
        if (groups.Skip(1).Any(group => group.Length != 3))
            return false;
        digits = string.Concat(groups);
        return true;
    }
}
