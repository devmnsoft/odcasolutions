using Odca.Application.Contracts;
using Odca.Contracts.Studio;

namespace Odca.Domain.Tests;

public sealed class CanonicalDecimalTests
{
    [Theory]
    [InlineData("150,00", "150.00")]
    [InlineData("1.250,50", "1250.50")]
    [InlineData("1,250.50", "1250.50")]
    [InlineData("150.00", "150.00")]
    [InlineData("0", "0.00")]
    [InlineData("0,00", "0.00")]
    [InlineData("R$ 150,00", "150.00")]
    [InlineData("1250", "1250.00")]
    [InlineData("1.250,00", "1250.00")]
    [InlineData("1250.00", "1250.00")]
    public void CurrencyInputBecomesCanonicalDecimal(string input, string expected)
    {
        var parsed = CanonicalDecimal.Parse(input, currency: true);

        Assert.True(parsed.Succeeded, parsed.Error);
        Assert.Equal(expected, parsed.Canonical);
        Assert.NotEqual("15000.00", parsed.Canonical);
    }

    [Theory]
    [InlineData("1.250")]
    [InlineData("1,250")]
    [InlineData("-10,00")]
    [InlineData("150,000")]
    [InlineData("abc")]
    [InlineData("")]
    [InlineData("12.34.56")]
    public void AmbiguousOrInvalidCurrencyIsRejected(string input)
    {
        var parsed = CanonicalDecimal.Parse(input, currency: true);

        Assert.False(parsed.Succeeded);
        Assert.Equal(string.Empty, parsed.Canonical);
    }

    [Fact]
    public void StoredValidationDoesNotTreatBrazilianCommaAsThousands()
    {
        var definition = new ContractFieldDefinition("amount", "Valor", ContractFieldType.Currency, true);
        var ex = Assert.Throws<InvalidDataException>(() => StructuredContractDocument.ValidateValues(
            [definition], [new("amount", "150,00", true)], ContractValidationMode.Complete));

        Assert.Contains("inválido", ex.Message, StringComparison.OrdinalIgnoreCase);
        var normalized = StructuredContractDocument.NormalizeStoredValues(
            OfficialContractTemplates.SerializeFields([definition]),
            """[{"fieldId":"amount","value":"150,00","confirmed":true}]""");
        Assert.Contains("150.00", normalized, StringComparison.Ordinal);
        Assert.DoesNotContain("15000", normalized, StringComparison.Ordinal);
    }

    [Fact]
    public void UnselectedTherapyDoesNotRequireItsPrice()
    {
        var template = OfficialContractTemplates.All.Single(item => item.Key == "multiple-therapies");
        var values = template.Fields.Select(field => field.Id switch
        {
            "fee_neuropsychology" or "fee_psychology" or "fee_occupational_therapy" or "fee_speech_therapy" or "legal_representative_info" => new ContractFieldValue(field.Id, null, false),
            "therapy_neuropsychology" or "therapy_psychology" or "therapy_occupational" or "therapy_speech" or "has_representative" => new ContractFieldValue(field.Id, "Não", true),
            "image_consent_option" => new ContractFieldValue(field.Id, "Não autorizo uso de imagem", true),
            _ when field.Type == ContractFieldType.Currency => new ContractFieldValue(field.Id, "10.00", true),
            _ when field.Type == ContractFieldType.Date => new ContractFieldValue(field.Id, "2026-09-30", true),
            _ when field.Type == ContractFieldType.BrazilianDocument => new ContractFieldValue(field.Id, "529.982.247-25", true),
            _ when field.Type == ContractFieldType.Choice => new ContractFieldValue(field.Id, field.Choices![0], true),
            _ => new ContractFieldValue(field.Id, "Informado", true)
        }).ToArray();

        StructuredContractDocument.ValidateValues(template.Fields, values, ContractValidationMode.Confirmed);
        Assert.DoesNotContain(values, value => value.FieldId == "image_consent_option" && value.Value is null);
    }

    [Fact]
    public void AutofillUsesExplicitIdentifiersAndDoesNotCopyThePatientIntoTheContractor()
    {
        var fields = new ContractFieldDefinition[]
        {
            new("patient_identification", "Paciente", ContractFieldType.ShortText, true, null, "patient"),
            new("contractor_name", "Contratante", ContractFieldType.ShortText, true, null, "contractor"),
            new("contracted_name", "Clínica", ContractFieldType.ShortText, true, null, "organization"),
            new("image_consent_option", "Imagem", ContractFieldType.Choice, true, ["Não autorizo uso de imagem"], "manual")
        };

        var values = DocumentFieldAutofill.Apply(fields,
            """{"fullName":"Ana Lima","identifierValue":"529.982.247-25"}""",
            "Clínica Viva",
            "12345678000195");

        Assert.Equal("Ana Lima — 529.982.247-25", values.Single(item => item.FieldId == "patient_identification").Value);
        Assert.Equal("Clínica Viva", values.Single(item => item.FieldId == "contracted_name").Value);
        Assert.DoesNotContain(values, item => item.FieldId is "contractor_name" or "image_consent_option");
    }

    [Fact]
    public void AutofillCopiesTheContractorOnlyFromTheSelectedParty()
    {
        var fields = new ContractFieldDefinition[]
        {
            new("contractor_name", "Contratante", ContractFieldType.ShortText, true, null, "contractor"),
            new("contractor_document", "Documento", ContractFieldType.BrazilianDocument, true, null, "contractor"),
            new("image_consent_option", "Imagem", ContractFieldType.Choice, true, ["Não autorizo uso de imagem"], "manual")
        };
        const string snapshot = """{"fullName":"Ana Lima","identifierValue":"529.982.247-25","representative":{"fullName":"João Lima","identifierValue":"529.982.247-25","relationship":"pai"}}""";

        var fromPatient = DocumentFieldAutofill.Apply(fields, snapshot, "Clínica Viva", "12345678000195", "patient");
        var fromRepresentative = DocumentFieldAutofill.Apply(fields, snapshot, "Clínica Viva", "12345678000195", "representative");

        Assert.Equal("Ana Lima", fromPatient.Single(item => item.FieldId == "contractor_name").Value);
        Assert.Equal("João Lima", fromRepresentative.Single(item => item.FieldId == "contractor_name").Value);
        Assert.Equal("529.982.247-25", fromRepresentative.Single(item => item.FieldId == "contractor_document").Value);
        Assert.DoesNotContain(fromPatient, item => item.FieldId == "image_consent_option");
        Assert.DoesNotContain(fromRepresentative, item => item.FieldId == "image_consent_option");
    }

    [Fact]
    public void RendererKeepsCanonicalValueAndShowsBrazilianCurrency()
    {
        Assert.Equal("1.250,50", AssertFormatted("1250.50"));
        Assert.False(CanonicalDecimal.TryFormatPtBr("150,00", out _));
    }

    [Fact]
    public void MappingAcceptsOfficialTemplatesAndRejectsExpressionsCyclesAndSelfReference()
    {
        foreach (var template in OfficialContractTemplates.All)
            FieldMapping.Validate(template.Fields);

        var custom = new ContractFieldDefinition("valor_sessao", "Valor", ContractFieldType.Currency, true, null, "patient", null, null, "fullName");
        Assert.Throws<InvalidDataException>(() => FieldMapping.Validate([custom]));

        var expression = new ContractFieldDefinition("nome", "Nome", ContractFieldType.ShortText, true, null, "patient", null, null, "fullName; select 1");
        Assert.Throws<InvalidDataException>(() => FieldMapping.Validate([expression]));

        var self = new ContractFieldDefinition("opcao", "Opção", ContractFieldType.Choice, true, ["Sim", "Não"], "manual", "opcao", ["Sim"]);
        Assert.Throws<InvalidDataException>(() => FieldMapping.Validate([self]));

        var missing = new ContractFieldDefinition("dependente", "Dependente", ContractFieldType.ShortText, true, null, "manual", "ausente", ["Sim"]);
        Assert.Throws<InvalidDataException>(() => FieldMapping.Validate([missing]));

        var first = new ContractFieldDefinition("a", "A", ContractFieldType.Choice, true, ["Sim"], "manual", "b", ["Sim"]);
        var second = new ContractFieldDefinition("b", "B", ContractFieldType.Choice, true, ["Sim"], "manual", "a", ["Sim"]);
        Assert.Throws<InvalidDataException>(() => FieldMapping.Validate([first, second]));
    }

    [Fact]
    public void SourcePropertyFillsPatientWithoutALegacyIdentifier()
    {
        var fields = new[]
        {
            new ContractFieldDefinition("nome_do_paciente", "Nome do paciente", ContractFieldType.ShortText, true, null, "patient", null, null, "fullName"),
            new ContractFieldDefinition("patient_name", "Paciente", ContractFieldType.ShortText, true, null, "patient")
        };

        var values = DocumentFieldAutofill.Apply(fields, """{"fullName":"Ana Lima"}""", "Clínica Viva", "12345678000195");

        Assert.Equal("Ana Lima", values.Single(item => item.FieldId == "nome_do_paciente").Value);
        Assert.Equal("Ana Lima", values.Single(item => item.FieldId == "patient_name").Value);
    }

    [Fact]
    public void RegistrationSelectionPreservesManualValuesAndClearsConfirmation()
    {
        var fields = new[]
        {
            new ContractFieldDefinition("patient_name", "Paciente", ContractFieldType.ShortText, true, null, "patient"),
            new ContractFieldDefinition("observacao", "Observação", ContractFieldType.ShortText, false, null, "manual")
        };
        var current = new[]
        {
            new ContractFieldValue("patient_name", "Ana", true, "patient"),
            new ContractFieldValue("observacao", "Texto manual", true, "manual")
        };

        var updated = DocumentFieldAutofill.ApplyRegistrationSelection(fields, current, """{"fullName":"Ana Lima"}""", ["fullName"]);

        var patient = Assert.Single(updated, item => item.FieldId == "patient_name");
        Assert.Equal("Ana Lima", patient.Value);
        Assert.False(patient.Confirmed);
        Assert.Equal("patient", patient.Source);
        Assert.Equal("Texto manual", Assert.Single(updated, item => item.FieldId == "observacao").Value);
    }

    [Fact]
    public void DescriptionDifferenceIsADistinctTemplatePayload()
    {
        Assert.False(TemplatePayloadComparison.Same("Modelo", "Antes", "services", "{}", "[]", "Modelo", "Depois", "services", "{}", "[]"));
        Assert.True(TemplatePayloadComparison.Same("Modelo", " Igual ", "services", "{\"type\":\"document\"}", "[]", "Modelo", "Igual", "services", "{\"type\":\"document\"}", "[]"));
    }

    private static string AssertFormatted(string stored)
    {
        Assert.True(CanonicalDecimal.TryFormatPtBr(stored, out var formatted));
        return formatted;
    }
}
