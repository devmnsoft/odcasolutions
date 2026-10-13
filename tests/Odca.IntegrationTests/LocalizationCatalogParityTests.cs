using System.Text.Json;

namespace Odca.IntegrationTests;

public sealed class LocalizationCatalogParityTests
{
    private static readonly string[] Cultures = ["pt-BR", "en-US", "fr-FR", "es-ES"];

    private static readonly string[] BagSentinels =
    [
        "studio.nav_catalog", "common.breadcrumb_aria", "common.how_to_use",
        "action.save", "action.back", "action.cancel", "action.close", "action.submit", "action.filter",
        "inbox.prev", "inbox.next", "inbox.page",
        "studio.status_published", "studio.status_archived", "studio.status_draft",
        "studio.ver_summary_aria", "studio.ver_comp_summary_aria", "studio.ver_readiness_aria",
        "studio.ver_p_origin", "studio.ver_participant_title", "studio.ver_default_role",
        "studio.ver_reopen_confirm", "studio.ver_remove_confirm", "studio.ver_psource_manual",
        "studio.ver_comp_revision", "studio.ver_cmpcomp_title", "studio.ver_evidence_pdf",
        "studio.cmpv_title",
        "studio.tpl_unmap", "studio.tpl_chip_org", "studio.tpl_chip_con", "studio.tpl_chip_manual",
        "studio.tpl_or_separator", "studio.tpl_editor_limited_mid", "studio.tpl_blocked_submit",
        "studio.tpl_condition", "studio.tpl_server_revision_line", "studio.cat_help_a",
        "studio.dec_required", "studio.dec_numeric_only", "studio.dec_invalid", "studio.dec_negative_currency",
        "studio.dec_separator_explicit", "studio.dec_ambiguous", "studio.dec_group_inconsistent",
        "studio.dec_currency_max2", "studio.dec_number_max6", "studio.dec_incomplete", "studio.dec_not_saved",
        "studio.loss_combined_marks", "studio.loss_unsupported_mark", "studio.loss_asterisk", "studio.loss_u_tag",
        "studio.loss_alignment", "studio.loss_heading_level", "studio.loss_table_blocks",
        "studio.loss_nested_lists", "studio.loss_unknown_block", "studio.loss_structure",
        "studio.formula_pending", "studio.ed_state_dirty", "studio.ed_state_saved", "studio.ed_state_saving",
        "studio.ed_state_conflict", "studio.ed_failed_save", "studio.ed_failed_comm", "studio.ed_session_expired",
        "studio.ed_unconfirmed", "studio.ed_new_block", "studio.ed_typing", "studio.ed_typing_numeric", "studio.ed_typing_document",
        "studio.field_select_option", "studio.field_formula_placeholder", "studio.field_source",
        "studio.field_confirmed_btn", "studio.field_confirm_btn", "studio.field_confirmed", "studio.field_pending",
        "studio.field_default_document", "studio.field_default_date",
        "studio.doc_required", "studio.doc_required_inline", "studio.doc_invalid", "studio.doc_invalid_inline",
        "studio.date_required_inline",
        "studio.v_line", "studio.v_option", "studio.cm_origin_version", "studio.cm_origin_review",
        "studio.cm_excerpt_found", "studio.cm_excerpt_missing", "studio.cm_reply", "studio.cm_history",
        "studio.cm_reopen", "studio.cm_resolve", "studio.cm_reopen_prompt", "studio.cm_processing",
        "studio.cl_no_reviewer", "studio.cl_reviewer", "studio.cl_submit_btn", "studio.cl_preparing",
        "studio.cl_submitting", "studio.cl_submitted_state", "studio.cl_submitted_btn", "studio.cl_retry_btn",
        "studio.cmp_select_two",
        "studio.origin_manual", "studio.origin_organization", "studio.origin_patient",
        "studio.origin_contractor", "studio.origin_representative",
        "studio.tpl_label_ph", "studio.tpl_choices_ph", "studio.tpl_help_ph", "studio.tpl_when_ph"
    ];

    private static Dictionary<string, string> Load(string culture)
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Localization", culture + ".json");
        Assert.True(File.Exists(path), $"catálogo ausente: {path}");
        using var document = JsonDocument.Parse(File.ReadAllText(path));
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var property in document.RootElement.EnumerateObject())
        {
            var value = property.Value.ValueKind == JsonValueKind.String ? property.Value.GetString() : null;
            Assert.False(string.IsNullOrEmpty(value), $"{culture}: valor ausente/nao-string para {property.Name}");
            result[property.Name] = value!;
        }
        return result;
    }

    [Fact]
    public void AllFourCulturesParseToFlatNonEmptyDictionaries()
    {
        foreach (var culture in Cultures)
        {
            var catalog = Load(culture);
            Assert.True(catalog.Count >= 600, $"{culture}: esperado >= 600 chaves, obtidas {catalog.Count}");
            foreach (var (key, value) in catalog)
            {
                Assert.False(string.IsNullOrWhiteSpace(value), $"{culture}: valor em branco em {key}");
                Assert.False(key.Contains(' '), $"{culture}: chave com espaco: {key}");
            }
        }
    }

    [Fact]
    public void KeySetsAreIdenticalAcrossAllCultures()
    {
        var reference = Load("pt-BR");
        var missing = new List<string>();
        var extra = new List<string>();
        foreach (var culture in Cultures.Where(c => c != "pt-BR"))
        {
            var catalog = Load(culture);
            missing.AddRange(catalog.Keys.Except(reference.Keys).Select(k => $"{culture}: a mais {k}"));
            extra.AddRange(reference.Keys.Except(catalog.Keys).Select(k => $"{culture}: faltando {k}"));
        }
        Assert.Empty(extra);
        Assert.Empty(missing);
    }

    [Fact]
    public void StudioAndCommonBagKeysExistInEveryCulture()
    {
        foreach (var culture in Cultures)
        {
            var catalog = Load(culture);
            foreach (var key in BagSentinels)
                Assert.True(catalog.ContainsKey(key), $"{culture}: chave ausente no bag: {key}");
        }
    }

    [Fact]
    public void EdgeValuesPreserveIntentionalWhitespace()
    {
        var pt = Load("pt-BR");
        Assert.Equal("Próximo passo: ", pt["studio.e_next_prefix"]);
        Assert.Equal(" · ajuda: ", pt["studio.tpl_help_prefix"]);
        Assert.Equal(" ou ", pt["studio.tpl_or_separator"]);
        var evidence = pt["studio.ver_evidence_pdf"];
        Assert.Equal('·', evidence[^1]); // ver_evidence_pdf termina com o separador "·"
    }
}
