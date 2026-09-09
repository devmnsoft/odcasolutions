namespace Odca.Application.Onboarding;

/// <summary>Performs syntactic validation only; it does not query Receita Federal status.</summary>
public static class BrazilianDocument
{
    public static (string Normalized, string Type) NormalizeAndValidate(string value)
    {
        var digits = new string((value ?? string.Empty).Where(char.IsDigit).ToArray());
        return digits.Length switch
        {
            11 when HasValidCpfCheckDigits(digits) => (digits, "cpf"),
            14 when HasValidCnpjCheckDigits(digits) => (digits, "cnpj"),
            _ => (digits, "invalid")
        };
    }

    private static bool HasValidCpfCheckDigits(string value)
    {
        if (value.Distinct().Count() == 1)
        {
            return false;
        }

        return CalculateDigit(value, 9, 10, 1) == value[9] - '0' &&
               CalculateDigit(value, 10, 11, 1) == value[10] - '0';
    }

    private static bool HasValidCnpjCheckDigits(string value)
    {
        if (value.Distinct().Count() == 1)
        {
            return false;
        }

        return CalculateDigit(value, 12, 5, 9) == value[12] - '0' &&
               CalculateDigit(value, 13, 6, 9) == value[13] - '0';
    }

    private static int CalculateDigit(string value, int length, int initialWeight, int resetWeight)
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
}
