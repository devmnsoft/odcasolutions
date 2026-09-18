using System.Text.Json;

namespace Odca.Application.Contracts;

/// <summary>
/// Published operational starters for the studio catalog. These are not legal advice
/// and must still be reviewed before a generated version is submitted.
/// </summary>
public static class OfficialContractTemplates
{
    public static readonly IReadOnlySet<string> ContractTypes = new HashSet<string>(StringComparer.Ordinal)
    {
        "nda", "services", "amendment", "supply", "lease"
    };

    public static readonly IReadOnlySet<string> Scopes = new HashSet<string>(StringComparer.Ordinal)
    {
        "private", "consultancy", "global"
    };

    public static IReadOnlyList<OfficialContractTemplate> All { get; } =
    [
        NdaUnilateral(),
        NdaMutual(),
        ServicesAgreement(),
        ContractAmendment(),
        SupplyAgreement(),
        LeaseAgreement()
    ];

    public static string? NormalizeType(string? value) =>
        string.IsNullOrWhiteSpace(value) || !ContractTypes.Contains(value.Trim()) ? null : value.Trim();

    public static string? NormalizeScope(string? value) =>
        string.IsNullOrWhiteSpace(value) || !Scopes.Contains(value.Trim()) ? null : value.Trim();

    public static void EnsureValid()
    {
        foreach (var template in All)
        {
            StructuredContractDocument.Parse(template.Content, template.Fields);
        }
    }

    public static string SerializeFields(IReadOnlyList<ContractFieldDefinition> fields) =>
        JsonSerializer.Serialize(fields);

    private static OfficialContractTemplate NdaUnilateral() => new(
        "nda-unilateral",
        "Termo de Confidencialidade (unilateral)",
        "Modelo operacional para divulgação de informações a um destinatário. Revise partes, prazo e foro antes de gerar versão.",
        "nda",
        Document(
            Heading("Termo de Confidencialidade"),
            Paragraph(
                Text("Pelo presente instrumento, "),
                Field("discloser_name"),
                Text(", inscrito(a) sob o documento "),
                Field("discloser_document"),
                Text(" (\"Divulgador\"), disponibiliza informações ao destinatário "),
                Field("recipient_name"),
                Text(", documento "),
                Field("recipient_document"),
                Text(" (\"Destinatário\").")),
            Paragraph(
                Text("Finalidade da divulgação: "),
                Field("purpose"),
                Text(". Vigência a partir de "),
                Field("effective_date"),
                Text(", pelo prazo de "),
                Field("term_months"),
                Text(" meses.")),
            Paragraph(Text("O Destinatário obriga-se a usar as informações apenas para a finalidade declarada, a restringir o acesso a pessoas com necessidade de conhecê-las e a devolver ou eliminar cópias ao término, salvo retenção legal.")),
            Paragraph(
                Text("Foro e legislação de referência: "),
                Field("jurisdiction"),
                Text(". Este modelo é um ponto de partida operacional e não substitui revisão jurídica."))),
        [
            new("discloser_name", "Nome do divulgador", ContractFieldType.ShortText, true),
            new("discloser_document", "Documento do divulgador", ContractFieldType.BrazilianDocument, true),
            new("recipient_name", "Nome do destinatário", ContractFieldType.ShortText, true),
            new("recipient_document", "Documento do destinatário", ContractFieldType.BrazilianDocument, true),
            new("purpose", "Finalidade", ContractFieldType.LongText, true),
            new("effective_date", "Início da vigência", ContractFieldType.Date, true),
            new("term_months", "Prazo em meses", ContractFieldType.Number, true),
            new("jurisdiction", "Foro", ContractFieldType.Choice, true, ["São Paulo/SP", "Rio de Janeiro/RJ", "Brasília/DF", "Belo Horizonte/MG"])
        ]);

    private static OfficialContractTemplate NdaMutual() => new(
        "nda-mutual",
        "Acordo de Confidencialidade Recíproca",
        "Modelo operacional para troca bilateral de informações. Confirme partes, objeto e prazo no painel do estúdio.",
        "nda",
        Document(
            Heading("Acordo de Confidencialidade Recíproca"),
            Paragraph(
                Text("As partes "),
                Field("party_a_name"),
                Text(" ("),
                Field("party_a_document"),
                Text(") e "),
                Field("party_b_name"),
                Text(" ("),
                Field("party_b_document"),
                Text(") acordam proteger informações trocadas para "),
                Field("purpose"),
                Text(".")),
            Paragraph(
                Text("Vigência a partir de "),
                Field("effective_date"),
                Text(" até "),
                Field("end_date"),
                Text(". Cada parte permanece titular de suas informações e concede apenas o uso necessário à finalidade.")),
            Paragraph(Text("A obrigação de confidencialidade sobrevive ao encerramento pelo período residual indicado na revisão jurídica da organização. Foro: "), Field("jurisdiction"), Text("."))),
        [
            new("party_a_name", "Parte A", ContractFieldType.ShortText, true),
            new("party_a_document", "Documento da Parte A", ContractFieldType.BrazilianDocument, true),
            new("party_b_name", "Parte B", ContractFieldType.ShortText, true),
            new("party_b_document", "Documento da Parte B", ContractFieldType.BrazilianDocument, true),
            new("purpose", "Objeto da troca", ContractFieldType.LongText, true),
            new("effective_date", "Início", ContractFieldType.Date, true),
            new("end_date", "Término", ContractFieldType.Date, true),
            new("jurisdiction", "Foro", ContractFieldType.Choice, true, ["São Paulo/SP", "Rio de Janeiro/RJ", "Brasília/DF"])
        ]);

    private static OfficialContractTemplate ServicesAgreement() => new(
        "services-agreement",
        "Contrato de Prestação de Serviços",
        "Minuta operacional de serviços com objeto, valor e vigência estruturados. Confirme campos obrigatórios antes de gerar versão.",
        "services",
        Document(
            Heading("Contrato de Prestação de Serviços"),
            Paragraph(
                Text("Contratante: "),
                Field("client_name"),
                Text(", documento "),
                Field("client_document"),
                Text(". Contratado: "),
                Field("provider_name"),
                Text(", documento "),
                Field("provider_document"),
                Text(".")),
            Paragraph(Text("Objeto: "), Field("object")),
            Paragraph(
                Text("Valor: "),
                Field("amount"),
                Text(" com pagamento "),
                Field("payment_terms"),
                Text(". Vigência de "),
                Field("start_date"),
                Text(" a "),
                Field("end_date"),
                Text(".")),
            Paragraph(
                Text("Foro: "),
                Field("jurisdiction"),
                Text(". O contratado executa com diligência e o contratante disponibiliza as informações necessárias. Este texto não é parecer jurídico."))),
        [
            new("client_name", "Contratante", ContractFieldType.ShortText, true),
            new("client_document", "Documento do contratante", ContractFieldType.BrazilianDocument, true),
            new("provider_name", "Contratado", ContractFieldType.ShortText, true),
            new("provider_document", "Documento do contratado", ContractFieldType.BrazilianDocument, true),
            new("object", "Objeto dos serviços", ContractFieldType.LongText, true),
            new("amount", "Valor", ContractFieldType.Currency, true),
            new("payment_terms", "Condições de pagamento", ContractFieldType.Choice, true, ["mensal", "à vista", "por marco", "em 30 dias"]),
            new("start_date", "Início", ContractFieldType.Date, true),
            new("end_date", "Término", ContractFieldType.Date, true),
            new("jurisdiction", "Foro", ContractFieldType.Choice, true, ["São Paulo/SP", "Rio de Janeiro/RJ", "Brasília/DF"])
        ]);

    private static OfficialContractTemplate ContractAmendment() => new(
        "contract-amendment",
        "Termo de Aditivo Contratual",
        "Aditivo operacional preso ao contrato base. Use para registrar alteração de prazo, valor ou objeto após revisão.",
        "amendment",
        Document(
            Heading("Termo de Aditivo"),
            Paragraph(
                Text("Aditivo "),
                Field("amendment_number"),
                Text(" ao contrato "),
                Field("base_title"),
                Text(", referência "),
                Field("base_reference"),
                Text(", celebrado entre "),
                Field("party_a_name"),
                Text(" e "),
                Field("party_b_name"),
                Text(".")),
            Paragraph(
                Text("Vigência deste aditivo a partir de "),
                Field("effective_date"),
                Text(". Alteração: "),
                Field("change_summary"),
                Text(".")),
            Paragraph(Text("As cláusulas não modificadas permanecem em vigor. Foro: "), Field("jurisdiction"), Text("."))),
        [
            new("amendment_number", "Número do aditivo", ContractFieldType.ShortText, true),
            new("base_title", "Título do contrato base", ContractFieldType.ShortText, true),
            new("base_reference", "Referência do contrato base", ContractFieldType.ShortText, true),
            new("party_a_name", "Parte A", ContractFieldType.ShortText, true),
            new("party_b_name", "Parte B", ContractFieldType.ShortText, true),
            new("effective_date", "Início do aditivo", ContractFieldType.Date, true),
            new("change_summary", "Resumo da alteração", ContractFieldType.LongText, true),
            new("jurisdiction", "Foro", ContractFieldType.Choice, true, ["São Paulo/SP", "Rio de Janeiro/RJ", "Brasília/DF"])
        ]);

    private static OfficialContractTemplate SupplyAgreement() => new(
        "supply-agreement",
        "Contrato de Fornecimento",
        "Minuta operacional de fornecimento contínuo com volume, preço e prazo de aviso estruturados. Confirme campos antes de gerar versão.",
        "supply",
        Document(
            Heading("Contrato de Fornecimento"),
            Paragraph(
                Text("Fornecedor: "),
                Field("supplier_name"),
                Text(", documento "),
                Field("supplier_document"),
                Text(". Comprador: "),
                Field("buyer_name"),
                Text(", documento "),
                Field("buyer_document"),
                Text(".")),
            Paragraph(Text("Objeto e especificações dos produtos: "), Field("product_description")),
            Paragraph(
                Text("Preço unitário: "),
                Field("unit_price"),
                Text(" sob condições de entrega "),
                Field("delivery_terms"),
                Text(". Vigência de "),
                Field("start_date"),
                Text(" a "),
                Field("end_date"),
                Text(".")),
            Paragraph(
                Text("Foro: "),
                Field("jurisdiction"),
                Text(". Este modelo é um ponto de partida operacional e não substitui parecer jurídico."))),
        [
            new("supplier_name", "Fornecedor", ContractFieldType.ShortText, true),
            new("supplier_document", "Documento do fornecedor", ContractFieldType.BrazilianDocument, true),
            new("buyer_name", "Comprador", ContractFieldType.ShortText, true),
            new("buyer_document", "Documento do comprador", ContractFieldType.BrazilianDocument, true),
            new("product_description", "Especificação dos produtos", ContractFieldType.LongText, true),
            new("unit_price", "Preço unitário", ContractFieldType.Currency, true),
            new("delivery_terms", "Condições de entrega", ContractFieldType.Choice, true, ["mensal", "quinzenal", "sob demanda", "em lote único"]),
            new("start_date", "Início", ContractFieldType.Date, true),
            new("end_date", "Término", ContractFieldType.Date, true),
            new("jurisdiction", "Foro", ContractFieldType.Choice, true, ["São Paulo/SP", "Rio de Janeiro/RJ", "Brasília/DF", "Belo Horizonte/MG"])
        ]);

    private static OfficialContractTemplate LeaseAgreement() => new(
        "lease-agreement",
        "Contrato de Locação Operacional",
        "Minuta operacional de locação de bem móvel ou imóvel com objeto, aluguel e denúncia estruturados. Confirme campos antes de gerar versão.",
        "lease",
        Document(
            Heading("Contrato de Locação Operacional"),
            Paragraph(
                Text("Locador: "),
                Field("lessor_name"),
                Text(", documento "),
                Field("lessor_document"),
                Text(". Locatário: "),
                Field("lessee_name"),
                Text(", documento "),
                Field("lessee_document"),
                Text(".")),
            Paragraph(Text("Objeto e descrição do bem locado: "), Field("property_description")),
            Paragraph(
                Text("Aluguel: "),
                Field("rent_amount"),
                Text(" com pagamento "),
                Field("payment_terms"),
                Text(". Vigência de "),
                Field("start_date"),
                Text(" a "),
                Field("end_date"),
                Text(".")),
            Paragraph(
                Text("Foro: "),
                Field("jurisdiction"),
                Text(". Este modelo é um ponto de partida operacional e não substitui parecer jurídico."))),
        [
            new("lessor_name", "Locador", ContractFieldType.ShortText, true),
            new("lessor_document", "Documento do locador", ContractFieldType.BrazilianDocument, true),
            new("lessee_name", "Locatário", ContractFieldType.ShortText, true),
            new("lessee_document", "Documento do locatário", ContractFieldType.BrazilianDocument, true),
            new("property_description", "Descrição do bem", ContractFieldType.LongText, true),
            new("rent_amount", "Aluguel", ContractFieldType.Currency, true),
            new("payment_terms", "Condições de pagamento", ContractFieldType.Choice, true, ["mensal até o dia 5", "mensal até o dia 10", "mensal até o dia 20", "trimestral"]),
            new("start_date", "Início", ContractFieldType.Date, true),
            new("end_date", "Término", ContractFieldType.Date, true),
            new("jurisdiction", "Foro", ContractFieldType.Choice, true, ["São Paulo/SP", "Rio de Janeiro/RJ", "Brasília/DF", "Porto Alegre/RS"])
        ]);

    private static string Document(params string[] blocks) =>
        "{\"type\":\"document\",\"content\":[" + string.Join(',', blocks) + "]}";

    private static string Heading(string text) =>
        "{\"type\":\"heading\",\"level\":1,\"content\":[{\"type\":\"text\",\"text\":" + JsonSerializer.Serialize(text) + "}]}";

    private static string Paragraph(params string[] nodes) =>
        "{\"type\":\"paragraph\",\"alignment\":\"justify\",\"content\":[" + string.Join(',', nodes) + "]}";

    private static string Text(string value) =>
        "{\"type\":\"text\",\"text\":" + JsonSerializer.Serialize(value) + "}";

    private static string Field(string id) =>
        "{\"type\":\"field\",\"fieldId\":" + JsonSerializer.Serialize(id) + "}";
}

public sealed record OfficialContractTemplate(
    string Key,
    string Name,
    string Description,
    string ContractType,
    string Content,
    IReadOnlyList<ContractFieldDefinition> Fields);
