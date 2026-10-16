using Odca.Application.Contracts;

namespace Odca.Application.Documents;

/// <summary>
/// D-OC32 B.3: mapeamento FECHADO dos conceitos produzidos por <see cref="DocumentContent.Suggest"/>
/// para ids de campo do rascunho do estúdio. Match exato de identificador tem prioridade; a tabela
/// de alias abaixo é completa e auditável — conceito fora dela fica explicitamente sem destino.
/// Regras conservadoras: candidatos monetários só aplicam com candidato ÚNICO no rascunho (evita
/// escrever o valor errado em documentos com vários campos financeiros) e documento de parte exige
/// campo BrazilianDocument único; em caso de múltiplos candidatos o campo fica para decisão humana.
/// </summary>
public static class ExtractionDraftMapping
{
    public const string Unmapped = "unmapped";
    public const string AmbiguousCandidates = "ambiguous_candidates";

    private static readonly Dictionary<string, string[]> Aliases = new(StringComparer.Ordinal)
    {
        // "título:" lê-se primeiro como título do documento; "objeto" só quando não há base_title.
        ["title"] = ["base_title", "object"],
        ["reference"] = ["base_reference"],
        ["start_date"] = ["start_date", "effective_date"],
        ["end_date"] = ["end_date"],
        // Sem campo numérico canônico de aviso de renovação no catálogo oficial — só match exato.
        ["renewal_notice_days"] = [],
        ["party_document"] =
        [
            "patient_document", "client_document", "buyer_document", "lessee_document", "lessor_document",
            "supplier_document", "contractor_document", "provider_document", "discloser_document",
            "recipient_document", "party_a_document", "party_b_document", "contracted_cnpj",
        ],
    };

    private static readonly string[] MoneyCandidates = ["amount", "rent_amount", "procedure_fee", "unit_price"];

    /// <summary>
    /// Resolve um conceito para um único id de campo do rascunho. Retorna <c>(fieldId, null)</c>
    /// quando há destino certo ou <c>(null, razão estável)</c> quando o conceito não pode ser
    /// aplicado sem risco (razões: <see cref="Unmapped"/>, <see cref="AmbiguousCandidates"/>).
    /// </summary>
    public static (string? FieldId, string? Reason) Resolve(string concept, string? extractedValue, IReadOnlyCollection<ContractFieldDefinition> fields)
    {
        var present = fields.ToDictionary(f => f.Id, StringComparer.Ordinal);
        if (present.ContainsKey(concept)) return (concept, null);

        switch (concept)
        {
            case "value":
                var money = MoneyCandidates.Where(present.ContainsKey).ToList();
                return money.Count == 1 ? (money[0], null) : (null, money.Count == 0 ? Unmapped : AmbiguousCandidates);
            case "party_document":
                var digits = extractedValue?.Trim() ?? string.Empty;
                if (digits.Length is not (11 or 14)) return (null, Unmapped);
                var documents = Aliases[concept]
                    .Where(id => present.TryGetValue(id, out _))
                    .ToList();
                return documents.Count == 1 ? (documents[0], null) : (null, documents.Count == 0 ? Unmapped : AmbiguousCandidates);
            case "title":
            case "reference":
            case "start_date":
            case "end_date":
            case "renewal_notice_days":
                foreach (var candidate in Aliases[concept])
                    if (present.ContainsKey(candidate)) return (candidate, null);
                return (null, Unmapped);
            default:
                return (null, Unmapped);
        }
    }
}
