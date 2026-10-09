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
        "nda", "services", "amendment", "supply", "lease", "care", "consent"
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
        LeaseAgreement(),
        MultipleTherapiesAgreement(),
        InformedConsent(),
        SurgicalConsent()
    ];

    /// <summary>B.3.4 (D3): official templates that cannot leave draft/preview state until
    /// ODCA approves them (approval workflow lands with section C).</summary>
    public static bool RequiresOdcaApproval(string? officialKey) =>
        string.Equals(officialKey, "surgical-consent", StringComparison.Ordinal);

    /// <summary>Section C: official keys tracked in the ODCA approval queue.</summary>
    public static readonly IReadOnlyList<string> ApprovalRequiredKeys = ["surgical-consent"];

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

    public static string? PurposeFor(OfficialContractTemplate template) => template.Key switch
    {
        "nda-unilateral" or "nda-mutual" => "confidentiality",
        "services-agreement" => "service_agreement",
        "multiple-therapies" => "care_agreement",
        "informed-consent" => "consent",
        "surgical-consent" => "consent",
        "contract-amendment" => "amendment",
        "supply-agreement" => "supply",
        "lease-agreement" => "lease",
        _ => template.Purpose
    };

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

    private static OfficialContractTemplate MultipleTherapiesAgreement() => new(
        "multiple-therapies",
        "Contrato de Acompanhamento Contínuo — Múltiplas Terapias",
        "Modelo para prestação de serviços terapêuticos (Neuropsicologia, Psicologia, Terapia Ocupacional e Fonoaudiologia) com discriminação de valores por sessão e identificação de paciente/contratante/responsável.",
        "services",
        Document(
            Heading("CONTRATO DE ACOMPANHAMENTO TERAPÊUTICO CONTÍNUO INDIVIDUAL — MÚLTIPLAS TERAPIAS"),
            Paragraph(
                Text("CONTRATADO(A): "), Field("contracted_name"),
                Text(", inscrito(a) no CNPJ sob o nº "), Field("contracted_cnpj"),
                Text(", com sede em "), Field("contracted_address"),
                Text(", CEP "), Field("contracted_cep"),
                Text(", e-mail "), Field("contracted_email"), Text(".")),
            Paragraph(
                Text("CONTRATANTE: "), Field("contractor_name"),
                Text(", documento "), Field("contractor_document"),
                Text(", residente em "), Field("contractor_address"),
                Text(", CEP "), Field("contractor_cep"),
                Text(", e-mail "), Field("contractor_email"),
                Text(", telefone "), Field("contractor_phone"), Text(".")),
            Paragraph(
                Text("Há representante legal distinto do paciente? "), Field("has_representative"),
                Text(". Responsável legal, quando houver: "), Field("legal_representative_info"),
                Text(". Identificação do Paciente: "), Field("patient_identification"), Text(".")),
            ClauseHeading("CLÁUSULA 1ª — DO OBJETO"),
            Paragraph(
                Text("Prestação de serviços terapêuticos continuados, com seleção independente. Neuropsicologia: "),
                Field("therapy_neuropsychology"),
                Text(". Psicologia: "), Field("therapy_psychology"),
                Text(". Terapia ocupacional: "), Field("therapy_occupational"),
                Text(". Fonoaudiologia: "), Field("therapy_speech"),
                Text(". Os trabalhos desenvolvidos configuram-se como obrigação de meio e não de resultado, com duração média de 60 minutos por sessão. Este texto é um ponto de partida operacional e não reproduz integralmente o instrumento original.")),
            ClauseHeading("CLÁUSULA 2ª — DO INVESTIMENTO"),
            Paragraph(
                Text("O(A) CONTRATANTE pagará os seguintes valores individuais por sessão realizada:"),
                Text(" Neuropsicologia: R$ "), Field("fee_neuropsychology"),
                Text("; Psicologia: R$ "), Field("fee_psychology"),
                Text("; Terapia Ocupacional: R$ "), Field("fee_occupational_therapy"),
                Text("; Fonoaudiologia: R$ "), Field("fee_speech_therapy"), Text(".")),
            ClauseHeading("DA FORMA E VENCIMENTO DO PAGAMENTO"),
            Paragraph(
                Text("Pagamento mensal com vencimento selecionado: "),
                Field("payment_due_day"),
                Text(". Dados para pagamento: "), Field("payment_banking_details"), Text(".")),
            ClauseHeading("CLÁUSULA 3ª — SIGILO, PROTEÇÃO DE DADOS E IMAGEM"),
            Paragraph(
                Text("O tratamento dos dados do paciente e contratante observa estritamente a LGPD. Autorização de imagem: "),
                Field("image_consent_option"), Text(".")),
            ClauseHeading("CLÁUSULA 4ª — DO FORO E ENCERRAMENTO"),
            Paragraph(
                Text("As partes elegem a Comarca de "),
                Field("jurisdiction_city"), Text(" para dirimir eventuais controvérsias. Firmam o presente em "),
                Field("signing_date"), Text(", com os participantes e testemunhas identificados: "),
                Field("witnesses_identification"), Text("."))),
        [
            new("contracted_name", "Nome / Razão Social da Clínica", ContractFieldType.ShortText, true, null, "organization"),
            new("contracted_cnpj", "CNPJ da Clínica", ContractFieldType.BrazilianDocument, true, null, "organization"),
            new("contracted_address", "Endereço da Clínica", ContractFieldType.ShortText, true, null, "organization"),
            new("contracted_cep", "CEP da Clínica", ContractFieldType.ShortText, true, null, "organization"),
            new("contracted_email", "E-mail da Clínica", ContractFieldType.ShortText, true, null, "organization"),
            new("contractor_name", "Nome do Contratante", ContractFieldType.ShortText, true, null, "contractor"),
            new("contractor_document", "CPF/CNPJ do Contratante", ContractFieldType.BrazilianDocument, true, null, "contractor"),
            new("contractor_address", "Endereço do Contratante", ContractFieldType.ShortText, true, null, "contractor"),
            new("contractor_cep", "CEP do Contratante", ContractFieldType.ShortText, true, null, "contractor"),
            new("contractor_email", "E-mail do Contratante", ContractFieldType.ShortText, true, null, "contractor"),
            new("contractor_phone", "Telefone do Contratante", ContractFieldType.ShortText, true, null, "contractor"),
            new("has_representative", "Há representante legal?", ContractFieldType.Choice, true, ["Sim", "Não"], "manual"),
            new("legal_representative_info", "Responsável Legal (Nome, CPF e Relação)", ContractFieldType.ShortText, true, null, "representative", "has_representative", ["Sim"]),
            new("patient_identification", "Identificação do Paciente (Nome e Documento)", ContractFieldType.ShortText, true, null, "patient"),
            new("therapy_neuropsychology", "Neuropsicologia", ContractFieldType.Choice, true, ["Sim", "Não"], "manual"),
            new("therapy_psychology", "Psicologia", ContractFieldType.Choice, true, ["Sim", "Não"], "manual"),
            new("therapy_occupational", "Terapia Ocupacional", ContractFieldType.Choice, true, ["Sim", "Não"], "manual"),
            new("therapy_speech", "Fonoaudiologia", ContractFieldType.Choice, true, ["Sim", "Não"], "manual"),
            new("fee_neuropsychology", "Valor por Sessão — Neuropsicologia", ContractFieldType.Currency, true, null, "manual", "therapy_neuropsychology", ["Sim"]),
            new("fee_psychology", "Valor por Sessão — Psicologia", ContractFieldType.Currency, true, null, "manual", "therapy_psychology", ["Sim"]),
            new("fee_occupational_therapy", "Valor por Sessão — Terapia Ocupacional", ContractFieldType.Currency, true, null, "manual", "therapy_occupational", ["Sim"]),
            new("fee_speech_therapy", "Valor por Sessão — Fonoaudiologia", ContractFieldType.Currency, true, null, "manual", "therapy_speech", ["Sim"]),
            new("payment_due_day", "Vencimento da Mensalidade", ContractFieldType.Choice, true, ["Todo dia 05 do mês", "Todo dia 10 do mês", "Todo dia 15 do mês"]),
            new("payment_banking_details", "Dados de Pagamento (PIX / Agência / Conta)", ContractFieldType.ShortText, true),
            new("image_consent_option", "Autorização de Imagem", ContractFieldType.Choice, true, ["Não autorizo uso de imagem", "Autorizo exclusivamente para fins de estudo de caso com anonimato"]),
            new("jurisdiction_city", "Cidade do Foro", ContractFieldType.Choice, true, ["Belém/PA", "São Paulo/SP", "Rio de Janeiro/RJ", "Brasília/DF"]),
            new("signing_date", "Data de Assinatura", ContractFieldType.Date, true),
            new("witnesses_identification", "Testemunhas", ContractFieldType.ShortText, true, null, "manual")
        ],
        "care_agreement");

    private static OfficialContractTemplate InformedConsent() => new(
        "informed-consent",
        "Termo de Consentimento para Atendimento",
        "Ponto de partida operacional para registrar o consentimento do paciente ou de seu representante. Não substitui revisão jurídica nem autoriza imagem automaticamente.",
        "consent",
        Document(
            Heading("Termo de Consentimento para Atendimento"),
            Paragraph(
                Text("A organização "), Field("organization_name"),
                Text(" registra que o paciente "), Field("patient_name"),
                Text(", documento "), Field("patient_document"),
                Text(", recebe informação sobre a finalidade "), Field("purpose"),
                Text(". Há representante? "), Field("has_representative"),
                Text(". Representante, quando houver: "), Field("representative_name"),
                Text(". Data: "), Field("consent_date"),
                Text(". Autorização de imagem: "), Field("image_consent_option"),
                Text(". Este modelo não envia o termo para assinatura."))),
        [
            new("organization_name", "Organização", ContractFieldType.ShortText, true, null, "organization"),
            new("patient_name", "Paciente", ContractFieldType.ShortText, true, null, "patient"),
            new("patient_document", "Documento do paciente", ContractFieldType.BrazilianDocument, true, null, "patient"),
            new("purpose", "Finalidade informada", ContractFieldType.ShortText, true, null, "manual"),
            new("has_representative", "Há representante?", ContractFieldType.Choice, true, ["Sim", "Não"], "manual"),
            new("representative_name", "Representante", ContractFieldType.ShortText, true, null, "representative", "has_representative", ["Sim"]),
            new("consent_date", "Data", ContractFieldType.Date, true, null, "manual"),
            new("image_consent_option", "Autorização de Imagem", ContractFieldType.Choice, true, ["Não autorizo uso de imagem", "Autorizo exclusivamente para fins de estudo de caso com anonimato"], "manual")
        ],
        "consent");

    // B.3.4 (D3): minimal official surgical starter. Creation is gated to the
    // plastic_surgery profile (Enterprise) and the draft stays visibly pending until
    // section C delivers the full ODCA approval workflow.
    private static OfficialContractTemplate SurgicalConsent() => new(
        "surgical-consent",
        "Termo de Consentimento para Procedimento Cirúrgico — Cirurgia Plástica",
        "Modelo cirúrgico mínimo para registrar o procedimento planejado, paciente/representante, anestesia, honorários e riscos. Exige aprovação da ODCA antes do preparativo de assinatura e não substitui revisão jurídica.",
        "consent",
        Document(
            Heading("TERMO DE CONSENTIMENTO PARA PROCEDIMENTO CIRÚRGICO — CIRURGIA PLÁSTICA"),
            Paragraph(
                Text("PROCEDIMENTO: cirurgia plástica descrita como "), Field("procedure_description"),
                Text(", a ser realizada em "), Field("surgery_date"),
                Text(" na unidade "), Field("facility_name"),
                Text(", sob "), Field("anesthesia_type"), Text(".")),
            Paragraph(
                Text("PACIENTE: "), Field("patient_name"),
                Text(", documento "), Field("patient_document"),
                Text(". Há representante legal distinto do paciente? "), Field("has_representative"),
                Text(". Responsável legal, quando houver: "), Field("legal_representative_info"), Text(".")),
            Paragraph(
                Text("PROFISSIONAL E ORGANIZAÇÃO: cirurgião responsável "), Field("surgeon_name"),
                Text(", documento "), Field("surgeon_document"),
                Text(", atuando junto à organização "), Field("organization_name"), Text(".")),
            Paragraph(
                Text("HONORÁRIOS E PAGAMENTO: honorários de R$ "), Field("procedure_fee"),
                Text(", com a condição "), Field("payment_terms"), Text(".")),
            Callout(Paragraph(
                Text("RISCOS: o paciente declara ciência dos riscos e benefícios do procedimento: "), Field("risks_acknowledgement"),
                Text(". Este modelo é um ponto de partida operacional e não substitui revisão jurídica."))),
            Paragraph(
                Text("FORO E ASSINATURA: foro na comarca de "), Field("jurisdiction_city"),
                Text(". Data de assinatura: "), Field("signing_date"), Text("."))),
        [
            new("organization_name", "Organização / centro cirúrgico", ContractFieldType.ShortText, true, null, "organization"),
            new("surgeon_name", "Cirurgião responsável", ContractFieldType.ShortText, true, null, "manual"),
            new("surgeon_document", "Documento do cirurgião", ContractFieldType.BrazilianDocument, true, null, "manual"),
            new("patient_name", "Paciente", ContractFieldType.ShortText, true, null, "patient"),
            new("patient_document", "Documento do paciente", ContractFieldType.BrazilianDocument, true, null, "patient"),
            new("procedure_description", "Procedimento planejado", ContractFieldType.LongText, true, null, "manual"),
            new("surgery_date", "Data do procedimento", ContractFieldType.Date, true, null, "manual"),
            new("facility_name", "Unidade / hospital", ContractFieldType.ShortText, true, null, "manual"),
            new("anesthesia_type", "Tipo de anestesia", ContractFieldType.Choice, true,
                ["Anestesia geral", "Sedação intravenosa", "Anestesia local", "Anestesia local com sedação"], "manual"),
            new("has_representative", "Há representante legal distinto?", ContractFieldType.Choice, true, ["Sim", "Não"], "manual"),
            new("legal_representative_info", "Responsável legal", ContractFieldType.ShortText, false, null, "representative", "has_representative", ["Sim"]),
            new("procedure_fee", "Honorários do procedimento", ContractFieldType.Currency, true, null, "manual"),
            new("payment_terms", "Condição de pagamento", ContractFieldType.Choice, true,
                ["À vista", "Em parcelas mensais", "Parcela única no dia do procedimento"], "manual"),
            new("risks_acknowledgement", "Ciência dos riscos", ContractFieldType.Choice, true,
                ["Li e compreendi os riscos descritos", "Ainda desejo esclarecimentos adicionais"], "manual"),
            new("signing_date", "Data de assinatura", ContractFieldType.Date, true, null, "manual"),
            new("jurisdiction_city", "Comarca (foro)", ContractFieldType.Choice, true,
                ["Belém/PA", "São Paulo/SP", "Rio de Janeiro/RJ", "Brasília/DF"], "manual")
        ],
        "consent");

    private static string Document(params string[] blocks) =>
        "{\"type\":\"document\",\"content\":[" + string.Join(',', blocks) + "]}";

    private static string Heading(string text) =>
        "{\"type\":\"heading\",\"level\":1,\"content\":[{\"type\":\"text\",\"text\":" + JsonSerializer.Serialize(text) + "}]}";

    // Section D (D2): clause titles are real level-2 sections so the renderer can build
    // the sumário; the literal CLÁUSULA numbering stays in the heading text (no duplicates).
    private static string ClauseHeading(string text) =>
        "{\"type\":\"heading\",\"level\":2,\"content\":[{\"type\":\"text\",\"text\":" + JsonSerializer.Serialize(text) + "}]}";

    // Section D (D3): legal-design attention box — a first-class document node.
    private static string Callout(params string[] blocks) =>
        "{\"type\":\"callout\",\"variant\":\"attention\",\"content\":[" + string.Join(',', blocks) + "]}";

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
    IReadOnlyList<ContractFieldDefinition> Fields,
    string? Purpose = null);
