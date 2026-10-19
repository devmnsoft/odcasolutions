using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Odca.Web.Models;

namespace Odca.Web.Controllers;

[Authorize]
[Route("modulos")]
public sealed class ModulesController(Odca.Configuration.Localization.LocalizationService loc) : Controller
{
    // D-OC49 (S15): o catálogo de módulos armazena CHAVES de localização; os textos são
    // resolvidos no request com a cultura ativa (CultureMiddleware), preservando o
    // isolamento por perfil (tenant x plataforma) já existente.

    private string? Culture => HttpContext.Items[Odca.Web.Middleware.CultureMiddleware.ItemsKey] as string;

    private string L(string key) => loc.T(key, Culture);

    private static readonly Dictionary<string, string[]> TenantModules =
        new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase)
        {
            ["contratos"] = new[] { "group_contratos", "module.tenant_contracts_desc", "module.tenant_contracts_next" },
            ["documentos"] = new[] { "module.tenant_documents_title", "module.tenant_documents_desc", "module.tenant_documents_next" },
            ["ajuda"] = new[] { "ajuda", "module.tenant_help_desc", "module.tenant_help_next" }
        };

    private static readonly Dictionary<string, string[]> PlatformModules =
        new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase)
        {
            ["usuarios"] = new[] { "usuarios-acessos", "module.platform_users_desc", "module.platform_users_next" },
            ["perfis"] = new[] { "module.platform_profiles_title", "module.platform_profiles_desc", "module.platform_profiles_next" },
            ["assinaturas"] = new[] { "assinaturas", "module.platform_signatures_desc", "module.platform_signatures_next" },
            ["biblioteca-modelos"] = new[] { "biblioteca-modelos", "module.platform_library_desc", "module.platform_library_next" },
            ["publicacao-versoes"] = new[] { "module.platform_publish_title", "module.platform_publish_desc", "module.platform_publish_next" },
            ["solicitacoes-enterprise"] = new[] { "module.platform_sola_title", "module.platform_sola_desc", "module.platform_sola_next" },
            ["cobrancas"] = new[] { "module.platform_billing_title", "module.platform_billing_desc", "module.platform_billing_next" },
            ["contratos"] = new[] { "module.platform_contracts_title", "module.platform_contracts_desc", "module.platform_contracts_next" },
            ["auditoria"] = new[] { "auditoria-plataforma", "module.platform_audit_desc", "module.platform_audit_next", "/administracao/auditoria", "module.platform_audit_open" },
            ["configuracoes"] = new[] { "configuracoes-operacionais", "module.platform_settings_desc", "module.platform_settings_next" }
        };

    [HttpGet("{module}")]
    public IActionResult Index(string module)
    {
        var platform = User.IsInRole("SuperAdministrator");
        var modules = platform ? PlatformModules : TenantModules;
        if (!modules.TryGetValue(module, out var m)) return NotFound();
        var vm = new ModuleStatusViewModel(
            L(m[0]), L(m[1]), L(m[2]), platform,
            OpenHref: m.Length > 3 ? m[3] : null,
            OpenLinkText: m.Length > 4 ? L(m[4]) : null);
        return View(vm);
    }
}
