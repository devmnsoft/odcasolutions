using Odca.Configuration.Localization;

namespace Odca.Api.Localization;

/// <summary>
/// D-OC27: mapa de mensagens canônicas (pt-BR, camadas Odca.Domain/Odca.Application)
/// para chaves do catálogo api.*. O texto canônico NUNCA muda com o idioma; aqui a
/// API apenas troca a exibição. Texto fora do mapa → o chamador preserva o original
/// (regra da casa: nunca se inventa texto).
/// </summary>
public static class ApiMessages
{
    /// <summary>Chave estável de cada mensagem canônica que a API relê em tempo de execução.</summary>
    private static readonly Dictionary<string, string> KnownKeys = new(StringComparer.Ordinal)
    {
        // Identity (Odca.Application.Identity / Odca.Domain.Identity)
        ["E-mail/CPF ou senha inválidos."] = "api.auth.invalid_credentials",
        ["A senha atual está incorreta."] = "api.auth.current_password_incorrect",
        ["A senha deve ter pelo menos 12 caracteres."] = "api.auth.password_length",
        ["A senha deve conter uma letra maiúscula."] = "api.auth.password_uppercase",
        ["A senha deve conter uma letra minúscula."] = "api.auth.password_lowercase",
        ["A senha deve conter um número."] = "api.auth.password_digit",
        ["A senha deve conter um símbolo."] = "api.auth.password_symbol",
        // Onboarding (Odca.Application.Onboarding.CustomerOnboardingService)
        ["Plano não encontrado ou indisponível para contratação."] = "api.onboarding.plan_unavailable",
        ["Já existe cadastro ativo para este documento e e-mail."] = "api.onboarding.duplicate_registration",
        ["Confirmação inválida."] = "api.onboarding.confirm_invalid",
        ["Token inválido, expirado ou já utilizado."] = "api.onboarding.token_invalid",
        ["Aceite os termos de uso para criar a conta."] = "api.onboarding.terms_required",
        ["Confirme a ciência do aviso de privacidade."] = "api.onboarding.privacy_ack_required",
        ["Informe CPF ou CNPJ em formato válido."] = "api.onboarding.document_invalid",
        ["Informe o nome do responsável."] = "api.onboarding.responsible_required",
        // Pacientes (Odca.Application.Patients.PatientRules)
        ["Informe o nome completo (2 a 160 caracteres)."] = "api.patients.validation.full_name",
        ["Informe um e-mail válido."] = "api.patients.validation.email",
        ["A data de nascimento não pode estar no futuro."] = "api.patients.validation.birth_date_future",
        ["Informe o nome do representante (2 a 160 caracteres)."] = "api.patients.validation.representative_name",
        ["Informe a relação do representante (até 80 caracteres)."] = "api.patients.validation.representative_relationship",
        ["Informe juntos o tipo e o valor do identificador."] = "api.patients.validation.identifier_pair",
        ["O identificador informado não é suportado."] = "api.patients.validation.identifier_type",
        ["O identificador deve ter no máximo 80 caracteres."] = "api.patients.validation.identifier_max",
        ["Informe um CPF válido."] = "api.patients.validation.cpf",
        ["Informe um CNPJ válido."] = "api.patients.validation.cnpj",
        ["O identificador deve ter ao menos 2 caracteres."] = "api.patients.validation.identifier_min",
        // Agenda mensal (Odca.Application.Operations)
        ["O intervalo da agenda não pode ter início depois do término."] = "api.inbox.agenda_range_invalid",
        // Vistas salvas (Odca.Contracts.SavedViews.SavedViewFilterPolicy)
        ["Tipo de listagem inválido."] = "api.saved_views.listing_invalid",
        ["Ordenação não permitida."] = "api.saved_views.sort_not_allowed",
        ["A vista contém filtros não permitidos."] = "api.saved_views.filters_not_allowed",
        ["A vista contém um identificador inválido."] = "api.saved_views.identifier_invalid",
        ["As datas devem usar o formato AAAA-MM-DD."] = "api.saved_views.date_format",
        ["A vista contém uma definição de data relativa inválida."] = "api.saved_views.relative_date_invalid",
        ["A vista contém um valor não permitido."] = "api.saved_views.value_not_allowed",
        ["Não é permitido o uso concomitante de data relativa e datas absolutas na mesma vista."] = "api.saved_views.relative_absolute_conflict",
        ["O nome deve ter entre 1 e 80 caracteres."] = "api.saved_views.name_invalid",
        // Compartilhada (SavedViewFilterPolicy + ObligationsController)
        ["A data final deve ser igual ou posterior à data inicial."] = "api.validation.date_range",
        // Documentos (Odca.Application.Documents.DocumentContent)
        ["A extensão e a assinatura não correspondem a PDF, PNG, JPEG ou DOCX suportado."] = "api.documents.extension_mismatch",
        ["A imagem precisa permitir validação estrutural."] = "api.documents.image_not_seekable",
        ["Imagem corrompida ou sem dimensões válidas."] = "api.documents.image_corrupted",
        ["O conteúdo descompactado excede 100 MB."] = "api.documents.docx_uncompressed_limit",
        ["DOCX corrompido: documento principal ausente."] = "api.documents.docx_corrupted",
        // Importação de contratos (Odca.Api.ContractImportsController.Validate)
        ["A identificação do contrato é obrigatória."] = "api.imports.issue_required_data",
        ["O título deve ter no máximo 160 caracteres."] = "api.imports.issue_title_too_long",
        ["O término não pode ser anterior ao início."] = "api.imports.issue_end_before_start",
        ["Informe a moeda do valor contratual."] = "api.imports.issue_missing_currency",
        ["O valor contratual não pode ser negativo."] = "api.imports.issue_negative_value",
        ["Use o código de moeda ISO com três letras."] = "api.imports.issue_invalid_currency",
        ["O prazo de comunicação não pode ser negativo."] = "api.imports.issue_invalid_notice",
        ["Há sugestões ainda não revisadas."] = "api.imports.issue_unreviewed",
        // Estúdio — validação estrutural de documento/campos (Odca.Application.Contracts)
        ["O documento excede o limite de 2 MB."] = "api.studio.document_size_limit",
        ["O conteúdo estruturado é inválido."] = "api.studio.structured_content_invalid",
        ["Os campos precisam de identificadores únicos e estáveis."] = "api.studio.unique_field_ids",
        ["Não é permitido valor com identificador de campo ausente."] = "api.studio.value_without_field_id",
        ["Cada campo deve possuir somente um valor confirmado."] = "api.studio.one_value_per_field",
        ["Foi informado valor para um campo que não pertence ao documento."] = "api.studio.value_unknown_field",
        ["As condições de obrigatoriedade formam um ciclo."] = "api.studio.condition_cycle",
        ["Cada nó deve ser um objeto."] = "api.studio.node_must_be_object",
        ["Nó sem tipo."] = "api.studio.node_missing_type",
        ["A raiz deve ser um documento."] = "api.studio.root_must_be_document",
        ["O documento contém atributo não suportado."] = "api.studio.unsupported_attribute",
        ["Variante de caixa de atenção não suportada."] = "api.studio.unsupported_callout_variant",
        ["Bloco de texto muito extenso."] = "api.studio.text_block_too_long",
        ["Formatação não suportada."] = "api.studio.unsupported_formatting",
        ["Ocorrência aponta para campo inexistente."] = "api.studio.occurrence_unknown_field",
        ["O nível do título deve estar entre 1 e 3."] = "api.studio.heading_level_invalid",
        ["Alinhamento não suportado."] = "api.studio.unsupported_alignment",
        ["Contêiner sem conteúdo."] = "api.studio.container_empty",
        ["Nó vazio."] = "api.studio.node_empty",
        ["Todo campo definido precisa ter ao menos uma ocorrência no documento."] = "api.studio.field_needs_occurrence",
        // Estúdio — análise/comparação síncrona (ContractStudioAnalysis)
        ["A comparação excede o limite de 10.000 alterações."] = "api.studio.compare_change_limit",
        ["Documento grande demais para processamento síncrono."] = "api.studio.sync_document_too_large",
        // Estúdio — renderer de documento (Odca.Application.Contracts.ContractDocumentRenderer)
        ["O conteúdo do elemento deve ser uma lista."] = "api.studio.element_content_not_list",
        // Estúdio — conferência de paciente e mensagens soltas dos controladores
        ["Selecione os valores que devem entrar na minuta ou confirme que os demais permanecem como estão. Nada foi gravado."] = "api.studio.reconfirm_selection_required",
        ["O documento enviado está em formato inválido."] = "api.studio.document_format_invalid",
        ["A conferência não pôde ser calculada."] = "api.studio.readiness_unavailable",
        // Estúdio — participantes de assinatura (ContractStudioController.ValidateParticipants/Sources)
        ["A chave da confirmação é obrigatória."] = "api.studio.confirm_key_required",
        ["Informe a coleção de participantes."] = "api.studio.participants_collection_required",
        ["Informe de 1 a 20 participantes."] = "api.studio.participants_count",
        ["O identificador da linha é obrigatório."] = "api.studio.participant_id_required",
        ["Selecione um tipo permitido."] = "api.studio.participant_type_invalid",
        ["Informe um nome de 2 a 160 caracteres."] = "api.studio.participant_name_length",
        ["Informe um papel de até 120 caracteres."] = "api.studio.participant_role_length",
        ["Informe um e-mail válido de até 254 caracteres."] = "api.studio.participant_email_invalid",
        ["O telefone deve ter até 40 caracteres."] = "api.studio.participant_phone_length",
        ["Informe e-mail ou telefone."] = "api.studio.participant_contact_required",
        ["A posição deve estar entre 1 e 20."] = "api.studio.participant_position_range",
        ["Selecione explicitamente a pessoa de origem."] = "api.studio.participant_source_required",
        ["A identidade anterior não pode ser mantida ao trocar para um participante manual."] = "api.studio.participant_source_manual_conflict",
        ["Há identificadores de linha duplicados."] = "api.studio.participant_ids_duplicated",
        ["Há posições duplicadas."] = "api.studio.participant_positions_duplicated",
        ["O paciente não pertence ao snapshot desta versão."] = "api.studio.patient_snapshot_mismatch",
        ["Este documento não possui paciente ou representante no snapshot."] = "api.studio.snapshot_without_patient",
        ["O representante selecionado não pertence ao snapshot e à relação deste paciente."] = "api.studio.representative_snapshot_mismatch"
    };

    /// <summary>
    /// Templates dinâmicos de mensagens canônicas que embutem valores de tempo de
    /// execução (rótulos de campo, tamanhos, códigos). Avaliados em ordem de
    /// declaração — padrões mais específicos antes dos genéricos; os grupos de
    /// captura alimentam os placeholders {n} do catálogo.
    /// </summary>
    private static readonly System.Text.RegularExpressions.Regex[] TemplatePatterns =
    [
        new(@"^O valor de '(.*)' é inválido\. (.+)$", System.Text.RegularExpressions.RegexOptions.CultureInvariant),
        new(@"^O valor de '(.*)' é inválido\.$", System.Text.RegularExpressions.RegexOptions.CultureInvariant),
        new(@"^O campo obrigatório '(.*)' está pendente\.$", System.Text.RegularExpressions.RegexOptions.CultureInvariant),
        new(@"^A condição de '(.*)' usa um valor que não existe em '(.*)'\.$", System.Text.RegularExpressions.RegexOptions.CultureInvariant),
        new(@"^A fórmula de '(.*)' aponta para um campo inexistente\.$", System.Text.RegularExpressions.RegexOptions.CultureInvariant),
        new(@"^A fórmula de '(.*)' só aceita quantidade em campos numéricos\.$", System.Text.RegularExpressions.RegexOptions.CultureInvariant),
        new(@"^A fórmula de '(.*)' só aceita valores monetários\.$", System.Text.RegularExpressions.RegexOptions.CultureInvariant),
        new(@"^A fórmula de '(.*)' não pode depender do próprio campo\.$", System.Text.RegularExpressions.RegexOptions.CultureInvariant),
        new(@"^A fórmula de '(.*)' precisa declarar ao menos um termo\.$", System.Text.RegularExpressions.RegexOptions.CultureInvariant),
        new(@"^O campo calculado '(.*)' não aceita propriedade de cadastro\.$", System.Text.RegularExpressions.RegexOptions.CultureInvariant),
        new(@"^A condição de '(.*)' aponta para um campo inexistente\.$", System.Text.RegularExpressions.RegexOptions.CultureInvariant),
        new(@"^A condição de '(.*)' não pode depender do próprio campo\.$", System.Text.RegularExpressions.RegexOptions.CultureInvariant),
        new(@"^Informe os valores que tornam '(.*)' obrigatório\.$", System.Text.RegularExpressions.RegexOptions.CultureInvariant),
        new(@"^Somente campos calculados aceitam termos de fórmula \('(.*)'\)\.$", System.Text.RegularExpressions.RegexOptions.CultureInvariant),
        new(@"^A ajuda de '(.*)' deve ter no máximo 200 caracteres\.$", System.Text.RegularExpressions.RegexOptions.CultureInvariant),
        new(@"^A origem de '(.*)' não é permitida\.$", System.Text.RegularExpressions.RegexOptions.CultureInvariant),
        new(@"^O campo manual '(.*)' não aceita propriedade de cadastro\.$", System.Text.RegularExpressions.RegexOptions.CultureInvariant),
        new(@"^A propriedade de '(.*)' não pertence à origem selecionada\.$", System.Text.RegularExpressions.RegexOptions.CultureInvariant),
        new(@"^O tipo de '(.*)' não é compatível com a propriedade selecionada\.$", System.Text.RegularExpressions.RegexOptions.CultureInvariant),
        new(@"^Elemento não suportado: (.+)\.$", System.Text.RegularExpressions.RegexOptions.CultureInvariant),
        new(@"^O valor de '(.*)' não confere com a fórmula declarada \(esperado: (.+)\)\.$", System.Text.RegularExpressions.RegexOptions.CultureInvariant),
        new(@"^O campo deve ter no máximo (\d+) caracteres\.$", System.Text.RegularExpressions.RegexOptions.CultureInvariant),
        new(@"^O plano informado '(.*)' não existe ou não está publicado\.$", System.Text.RegularExpressions.RegexOptions.CultureInvariant),
        new(@"^A imagem excede o limite de (\d+) pixels por dimensão\.$", System.Text.RegularExpressions.RegexOptions.CultureInvariant)
    ];

    /// <summary>Chave estável do template dinâmico na mesma posição de <see cref="TemplatePatterns"/>.</summary>
    private static readonly string[] TemplateKeys =
    [
        "api.studio.field_value_invalid_detail",
        "api.studio.field_value_invalid",
        "api.studio.field_required_pending",
        "api.studio.condition_value_missing",
        "api.studio.formula_field_missing",
        "api.studio.formula_quantity_numeric",
        "api.studio.formula_monetary_only",
        "api.studio.formula_self_reference",
        "api.studio.formula_needs_term",
        "api.studio.calculated_no_registration_property",
        "api.studio.condition_field_missing",
        "api.studio.condition_self_reference",
        "api.studio.condition_values_required",
        "api.studio.formula_terms_only_calculated",
        "api.studio.help_too_long",
        "api.studio.origin_not_allowed",
        "api.studio.manual_no_registration_property",
        "api.studio.property_wrong_origin",
        "api.studio.type_property_incompatible",
        "api.studio.element_unsupported",
        "api.studio.formula_mismatch",
        "api.patients.validation.max_length",
        "api.consumption.invalid_plan_message",
        "api.documents.image_too_large"
    ];

    /// <summary>Sufixos estáveis dos textos canônicos do MFA (MfaService). O prefixo
    /// (api.auth.mfa_confirm_ / api.auth.mfa_challenge_) vem do escopo do endpoint.</summary>
    private static readonly Dictionary<string, string> MfaSuffixes = new(StringComparer.Ordinal)
    {
        ["Inscrição MFA indisponível para esta sessão."] = "enrollment_unavailable",
        ["Não foi possível validar o autenticador. A chave de segurança registrada está inacessível neste ambiente. Contate o suporte técnico."] = "key_inaccessible",
        ["Código de autenticação inválido ou expirado."] = "code_invalid_or_expired",
        ["Não foi possível confirmar o MFA. Inicie a inscrição novamente."] = "confirm_failed",
        ["Desafio MFA indisponível para esta sessão."] = "challenge_unavailable",
        ["Muitas tentativas de MFA. Faça login novamente."] = "too_many_attempts",
        ["Não foi possível validar sua chave de segurança. Contate o suporte técnico."] = "key_invalid",
        ["Código de autenticação já utilizado."] = "code_already_used",
        ["Código de autenticação inválido, expirado ou já utilizado."] = "code_invalid_or_used"
    };

    /// <summary>Traduz uma mensagem canônica conhecida; retorna null quando não há registro (chamador mantém o original).
    /// Argumentos (args) alimentam placeholders {n} em catálogos parametrizados.</summary>
    /// <summary>Chave estável correspondente ao texto canônico (mapa ou template); null quando desconhecido.</summary>
    public static string? KeyFor(string? canonicalText)
    {
        if (canonicalText is null) return null;
        if (KnownKeys.TryGetValue(canonicalText, out var key)) return key;
        for (var i = 0; i < TemplatePatterns.Length; i++)
            if (TemplatePatterns[i].IsMatch(canonicalText)) return TemplateKeys[i];
        return null;
    }

    public static string? Localize(LocalizationService localizer, string? canonicalText, string culture, params string[] args)
    {
        if (canonicalText is null) return null;
        if (KnownKeys.TryGetValue(canonicalText, out var key))
            return localizer.T(key, culture, args);
        for (var i = 0; i < TemplatePatterns.Length; i++)
        {
            var match = TemplatePatterns[i].Match(canonicalText);
            if (!match.Success) continue;
            var parts = new string[match.Groups.Count - 1];
            for (var g = 1; g < match.Groups.Count; g++) parts[g - 1] = match.Groups[g].Value;
            return localizer.T(TemplateKeys[i], culture, parts);
        }
        return null;
    }

    /// <summary>Argumentos ({n}) extraídos dos grupos do template correspondente; vazio quando o texto é exato.</summary>
    public static string[] ArgsFor(string? canonicalText)
    {
        if (canonicalText is null) return [];
        for (var i = 0; i < TemplatePatterns.Length; i++)
        {
            var match = TemplatePatterns[i].Match(canonicalText);
            if (!match.Success) continue;
            var parts = new string[match.Groups.Count - 1];
            for (var g = 1; g < match.Groups.Count; g++) parts[g - 1] = match.Groups[g].Value;
            return parts;
        }
        return [];
    }

    public static string? MfaErrorSuffix(string? canonicalText)
        => canonicalText is not null && MfaSuffixes.TryGetValue(canonicalText, out var suffix)
            ? suffix
            : null;
}
