using Odca.Application.Contracts;

namespace Odca.Domain.Tests;

/// <summary>
/// D-OC22 — Bloco F: matriz de validação fim-a-fim de formatos (data/moeda) do Estúdio.
/// Fixa o contrato canônico usado nos três pontos de entrada da API (criar/editar/publicar
/// modelo, salvar minuta, gerar versão) e espelhado no client (studio-decimal.js):
/// data ISO yyyy-MM-dd com validade calendária; moeda não-negativa com até 2 casas;
/// número assinado com até 6 casas; texto pt-BR/invariante normalizado para canônico.
/// </summary>
public sealed class DateCurrencyValidationMatrixTests
{
    private static readonly ContractFieldDefinition DateField = new("inicio", "Início", ContractFieldType.Date, true);
    private static readonly ContractFieldDefinition CurrencyField = new("valor", "Valor", ContractFieldType.Currency, true);
    private static readonly ContractFieldDefinition NumberField = new("quantidade", "Quantidade", ContractFieldType.Number, true);

    [Theory]
    [InlineData("2026-02-28")]
    [InlineData("2024-02-29")]
    [InlineData("2000-02-29")]
    [InlineData("2001-01-01")]
    [InlineData("9999-12-31")]
    public void ValidCalendarDatesAreAccepted(string value)
    {
        StructuredContractDocument.ValidateValues(
            [DateField], [new ContractFieldValue(DateField.Id, value, true)], ContractValidationMode.Structural);
    }

    [Theory]
    [InlineData("2026-02-31")] // dia que não existe no mês
    [InlineData("2023-02-29")] // ano comum não tem 29 de fevereiro
    [InlineData("1900-02-29")] // século sem divisibilidade por 400 não é bissexto
    [InlineData("2026-13-01")] // mês fora de faixa
    [InlineData("31/02/2026")] // formato brasileiro não é aceito como valor armazenado
    [InlineData("2026/02/05")]
    [InlineData("2026-2-5")] // separadores exigem largura exata
    [InlineData("2026-02-05T00:00:00")]
    [InlineData("0000-01-01")] // ano fora do intervalo DateOnly
    [InlineData("10000-01-01")]
    [InlineData("abc")]
    public void InvalidOrNonIsoDatesAreRejected(string value)
    {
        var ex = Assert.Throws<InvalidDataException>(() => StructuredContractDocument.ValidateValues(
            [DateField], [new ContractFieldValue(DateField.Id, value, true)], ContractValidationMode.Structural));

        Assert.Contains("'Início'", ex.Message, StringComparison.Ordinal);
        Assert.Contains("inválido", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void DatesAreNotNormalizedOnlyValidated()
    {
        var fields = OfficialContractTemplates.SerializeFields([DateField]);
        var values = """[{"fieldId":"inicio","value":"2026-02-28","confirmed":true}]""";

        var normalized = StructuredContractDocument.NormalizeStoredValues(fields, values);
        Assert.Contains("\"value\":\"2026-02-28\"", normalized, StringComparison.Ordinal);
        Assert.DoesNotContain("\"value\":\"28/02/2026\"", normalized, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("-5")]
    [InlineData("0")]
    [InlineData("7")]
    [InlineData("0.5")]
    [InlineData("-0.5")]
    [InlineData("123.456789")]
    public void ValidStoredNumbersAreAccepted(string value)
    {
        Assert.True(CanonicalDecimal.IsStoredNumber(value), value);
        StructuredContractDocument.ValidateValues(
            [NumberField], [new ContractFieldValue(NumberField.Id, value, true)], ContractValidationMode.Structural);
    }

    [Theory]
    [InlineData("123.4567891")] // sete casas decimais
    [InlineData("01")] // leading zero
    [InlineData("1,5")] // vírgula brasileira não é forma armazenada
    [InlineData("+5")] // sinal explícito positivo não é canônico
    [InlineData("5.")] // separador decimal incompleto
    [InlineData("abc")]
    public void InvalidStoredNumbersAreRejected(string value)
    {
        Assert.False(CanonicalDecimal.IsStoredNumber(value), value);
        var ex = Assert.Throws<InvalidDataException>(() => StructuredContractDocument.ValidateValues(
            [NumberField], [new ContractFieldValue(NumberField.Id, value, true)], ContractValidationMode.Structural));

        Assert.Contains("'Quantidade'", ex.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("-10,50", "-10.50")]
    [InlineData("+7", "7")]
    [InlineData("1.250,50", "1250.50")] // número preserva as casas digitadas
    [InlineData("1,250.50", "1250.50")]
    [InlineData("2.000,001", "2000.001")]
    [InlineData("1234.123456", "1234.123456")]
    public void NumberInputIsNormalizedToCanonical(string input, string expected)
    {
        var parsed = CanonicalDecimal.Parse(input, currency: false);

        Assert.True(parsed.Succeeded, parsed.Error);
        Assert.Equal(expected, parsed.Canonical);
    }

    [Theory]
    [InlineData("1.250")] // ambíguo: milhar ou três casas decimais
    [InlineData("1234.1234567")] // excede seis casas
    [InlineData("12.34.56")]
    [InlineData("abc")]
    public void AmbiguousOrInvalidNumberInputIsRejected(string input)
    {
        var parsed = CanonicalDecimal.Parse(input, currency: false);

        Assert.False(parsed.Succeeded);
        Assert.Equal(string.Empty, parsed.Canonical);
    }

    [Theory]
    [InlineData("+150,00", "150.00")]
    [InlineData("R$1.234,56", "1234.56")] // símbolo sem espaço
    [InlineData("  1.250,50  ", "1250.50")] // espaços laterais
    [InlineData("1\u00A0234,56", "1234.56")] // espaço não rompível
    public void CurrencyInputEdgeCasesNormalize(string input, string expected)
    {
        var parsed = CanonicalDecimal.Parse(input, currency: true);

        Assert.True(parsed.Succeeded, parsed.Error);
        Assert.Equal(expected, parsed.Canonical);
    }

    [Fact]
    public void EmptyValuesAreSkippedByStorageValidationButRejectedAtInput()
    {
        // Campo vazio é ausência de valor (o pendente é tratado pela obrigatoriedade);
        // a rejeição de texto vazio acontece na fronteira de entrada (Parse), não no storage.
        Assert.False(CanonicalDecimal.IsStoredNumber(string.Empty));
        StructuredContractDocument.ValidateValues(
            [NumberField], [new ContractFieldValue(NumberField.Id, string.Empty, false)], ContractValidationMode.Structural);
        Assert.False(CanonicalDecimal.Parse(string.Empty, currency: true).Succeeded);
    }

    [Theory]
    [InlineData("10,005")] // três casas decimais em moeda
    [InlineData("12.345,678")]
    [InlineData("\u00A0")] // apenas espaço não rompível
    [InlineData("")]
    public void CurrencyInputPrecisionCasesAreRejected(string input)
    {
        var parsed = CanonicalDecimal.Parse(input, currency: true);

        Assert.False(parsed.Succeeded);
        Assert.Equal(string.Empty, parsed.Canonical);
    }

    [Fact]
    public void OptionalFieldWithMalformedValueStillFailsStructuralValidation()
    {
        var optionalCurrency = new ContractFieldDefinition("desconto", "Desconto", ContractFieldType.Currency, false);
        var optionalNumber = new ContractFieldDefinition("prorrateio", "Pró-rateio", ContractFieldType.Number, false);

        var currency = Assert.Throws<InvalidDataException>(() => StructuredContractDocument.ValidateValues(
            [optionalCurrency], [new ContractFieldValue(optionalCurrency.Id, "abc", false)], ContractValidationMode.Structural));
        Assert.Contains("'Desconto'", currency.Message, StringComparison.Ordinal);

        var number = Assert.Throws<InvalidDataException>(() => StructuredContractDocument.ValidateValues(
            [optionalNumber], [new ContractFieldValue(optionalNumber.Id, "1,5", false)], ContractValidationMode.Structural));
        Assert.Contains("'Pró-rateio'", number.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void DraftSaveNormalizationRejectsUnparseableValuesWithTheFieldLabel()
    {
        var fields = OfficialContractTemplates.SerializeFields([CurrencyField]);
        var values = """[{"fieldId":"valor","value":"1.250","confirmed":true}]""";

        var ex = Assert.Throws<InvalidDataException>(() => StructuredContractDocument.NormalizeStoredValues(fields, values));

        Assert.Contains("'Valor' é inválido", ex.Message, StringComparison.Ordinal);
        Assert.Contains("ambíguo", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void DraftSaveNormalizationWritesCanonicalFormForBothSeparatorStyles()
    {
        var fields = OfficialContractTemplates.SerializeFields([CurrencyField, NumberField]);

        var brazilian = StructuredContractDocument.NormalizeStoredValues(fields,
            """[{"fieldId":"valor","value":"1.250,50","confirmed":true},{"fieldId":"quantidade","value":"2.000,5","confirmed":true}]""");
        Assert.Contains("\"1250.50\"", brazilian, StringComparison.Ordinal);
        Assert.Contains("\"2000.5\"", brazilian, StringComparison.Ordinal);

        var invariant = StructuredContractDocument.NormalizeStoredValues(fields,
            """[{"fieldId":"valor","value":"1250.50","confirmed":true},{"fieldId":"quantidade","value":"2000.5","confirmed":true}]""");
        Assert.Equal(brazilian.Replace("\r", string.Empty), invariant.Replace("\r", string.Empty));
    }
}
