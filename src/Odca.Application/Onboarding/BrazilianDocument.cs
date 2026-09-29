namespace Odca.Application.Onboarding;

/// <summary>Performs syntactic validation only; it does not query Receita Federal status.</summary>
public static class BrazilianDocument
{
    public static (string Normalized, string Type) NormalizeAndValidate(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return (string.Empty, "invalid");

        // Clean punctuation and normalize to uppercase
        var cleaned = new string(value
            .Where(char.IsLetterOrDigit)
            .Select(char.ToUpperInvariant)
            .ToArray());

        if (cleaned.Length == 11 && cleaned.All(char.IsDigit) && HasValidCpfCheckDigits(cleaned))
        {
            return (cleaned, "cpf");
        }

        if (cleaned.Length == 14 && HasValidCnpjCheckDigits(cleaned))
        {
            return (cleaned, "cnpj");
        }

        return (cleaned, "invalid");
    }

    private static bool HasValidCpfCheckDigits(string value)
    {
        if (value.Distinct().Count() == 1)
        {
            return false;
        }

        return CalculateCpfDigit(value, 9, 10, 1) == value[9] - '0' &&
               CalculateCpfDigit(value, 10, 11, 1) == value[10] - '0';
    }

    private static bool HasValidCnpjCheckDigits(string value)
    {
        // Must have at least 2 distinct characters
        if (value.Distinct().Count() == 1)
        {
            return false;
        }

        // The check digits (positions 12 and 13) must be numeric
        if (!char.IsDigit(value[12]) || !char.IsDigit(value[13]))
        {
            return false;
        }

        // Positions 0..11 must be alphanumeric ASCII [A-Z0-9]
        for (var i = 0; i < 12; i++)
        {
            var ch = value[i];
            if (!char.IsAsciiLetterOrDigit(ch))
            {
                return false;
            }
        }

        return CalculateCnpjDigit(value, 12, 5, 9) == value[12] - '0' &&
               CalculateCnpjDigit(value, 13, 6, 9) == value[13] - '0';
    }

    private static int CalculateCpfDigit(string value, int length, int initialWeight, int resetWeight)
    {
        var sum = 0;
        var weight = initialWeight;
        for (var index = 0; index < length; index++)
        {
            sum += (value[index] - '0') * weight--;
            if (weight == 1)
            {
                weight = resetWeight;
            }
        }

        var remainder = sum % 11;
        return remainder < 2 ? 0 : 11 - remainder;
    }

    private static int CalculateCnpjDigit(string value, int length, int initialWeight, int resetWeight)
    {
        var sum = 0;
        var weight = initialWeight;
        for (var index = 0; index < length; index++)
        {
            // Official CNPJ spec (IN RFB 2229/2024): value = ASCII - 48
            // '0'..'9' -> 0..9, 'A'..'Z' -> 17..42
            var charVal = value[index] - 48;
            sum += charVal * weight--;
            if (weight == 1)
            {
                weight = resetWeight;
            }
        }

        var remainder = sum % 11;
        return remainder < 2 ? 0 : 11 - remainder;
    }
}
