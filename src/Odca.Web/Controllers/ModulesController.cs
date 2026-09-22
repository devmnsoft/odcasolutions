using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Odca.Web.Models;

namespace Odca.Web.Controllers;

[Authorize]
[Route("modulos")]
public sealed class ModulesController : Controller
{
    private static readonly Dictionary<string, ModuleStatusViewModel> TenantModules =
        new Dictionary<string, ModuleStatusViewModel>(StringComparer.OrdinalIgnoreCase)
        {
            ["contratos"] = new("Contratos", "A visão consolidada de contratos está sendo preparada. As fichas existentes continuam disponíveis pela Caixa operacional.", "Use a Caixa para abrir uma pendência e acessar a ficha do contrato relacionado.", false),
            ["documentos"] = new("Documentos", "A biblioteca consolidada de documentos está em evolução. A importação segura já está disponível no menu.", "Use Importações para enviar e revisar documentos enquanto a biblioteca é concluída.", false),
            ["ajuda"] = new("Ajuda e como usar", "Encontre rapidamente o caminho para as rotinas disponíveis nesta versão.", "Comece pela Visão geral, acompanhe pendências na Caixa e consulte prazos em Obrigações e Renovações.", false)
        };

    private static readonly Dictionary<string, ModuleStatusViewModel> PlatformModules =
        new Dictionary<string, ModuleStatusViewModel>(StringComparer.OrdinalIgnoreCase)
        {
            ["usuarios"] = new("Usuários globais", "A administração centralizada de identidades está em construção controlada.", "Gerencie os usuários de cada empresa pela área Equipe e perfis até a visão global ser liberada.", true),
            ["perfis"] = new("Perfis e permissões", "A gestão de perfis por empresa já existe; a visão global será disponibilizada em uma próxima evolução.", "Selecione uma empresa e use Equipe e perfis para administrar acessos com segurança.", true),
            ["assinaturas"] = new("Assinaturas", "A estrutura de planos e assinaturas está ativa, e esta visão operacional está em construção.", "Consulte o Catálogo de planos e Clientes e consumo para acompanhar a configuração atual.", true),
            ["cobrancas"] = new("Cobranças e faturas", "A base de faturamento está preparada, sem integração de pagamento nesta sprint.", "Nenhuma cobrança automática será realizada. A operação financeira será habilitada somente após validação.", true),
            ["contratos"] = new("Contratos da plataforma", "O acesso global consolidado a contratos está em construção controlada.", "Selecione uma empresa para trabalhar no contexto autorizado, sem misturar dados entre clientes.", true),
            ["auditoria"] = new("Auditoria", "Os eventos importantes já são registrados; a consulta global amigável está em construção.", "Até a tela ser liberada, preserve os eventos existentes e use consultas operacionais autorizadas no banco.", true),
            ["configuracoes"] = new("Configurações", "As configurações globais serão reunidas aqui após a estabilização dos fluxos principais.", "Use apenas as configurações de ambiente documentadas; nenhuma mudança é aplicada nesta tela.", true)
        };

    [HttpGet("{module}")]
    public IActionResult Index(string module)
    {
        var modules = User.IsInRole("SuperAdministrator") ? PlatformModules : TenantModules;
        return modules.TryGetValue(module, out var model) ? View(model) : NotFound();
    }
}
