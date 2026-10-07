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
            ["contratos"] = new("Contratos", "A visão consolidada de contratos já está disponível em Meus contratos, com estados de ativo, arquivado e encerrado.", "Use o menu Contratos → Meus contratos para ver todos os contratos da organização e abrir cada ficha detalhada.", false),
            ["documentos"] = new("Documentos", "A biblioteca consolidada de documentos está em evolução. A importação de arquivos externos estará disponível em uma próxima versão.", "Enquanto a Central de Importações não for liberada, use o estúdio de modelos para gerar novas minutas; os documentos existentes continuam consultáveis.", false),
            ["ajuda"] = new("Ajuda e como usar", "Encontre rapidamente o caminho para as rotinas disponíveis nesta versão.", "Comece pela tela inicial, acompanhe pendências na Caixa e consulte prazos em Obrigações e Renovações.", false)
        };

    private static readonly Dictionary<string, ModuleStatusViewModel> PlatformModules =
        new Dictionary<string, ModuleStatusViewModel>(StringComparer.OrdinalIgnoreCase)
        {
            ["usuarios"] = new("Usuários e acessos", "A administração centralizada de identidades está em construção controlada.", "Gerencie os usuários de cada empresa pela área Equipe e permissões até a visão global ser liberada.", true),
            ["perfis"] = new("Perfis e permissões", "A gestão de perfis por empresa já existe; a visão global será disponibilizada em uma próxima evolução.", "Selecione uma empresa e use Equipe e permissões para administrar acessos com segurança.", true),
            ["assinaturas"] = new("Assinaturas", "A estrutura de planos e assinaturas está ativa, e esta visão operacional está em construção.", "Consulte Planos e módulos e Usuários e acessos para acompanhar a configuração atual.", true),
            ["biblioteca-modelos"] = new("Biblioteca de modelos", "A biblioteca central de modelos da plataforma está em construção controlada.", "Os modelos publicados por empresa continuam disponíveis no estúdio de cada organização.", true),
            ["publicacao-versoes"] = new("Publicação e versões", "O painel global de publicação e versões de modelos está em construção controlada.", "Acompanhe as revisões publicadas diretamente no estúdio de cada organização até o painel global ser liberado.", true),
            ["solicitacoes-enterprise"] = new("Solicitações Enterprise e SLA", "A visão consolidada das solicitações Enterprise e do SLA está em construção controlada.", "As solicitações por empresa continuam disponíveis na área Solicitações ODCA de cada organização.", true),
            ["cobrancas"] = new("Cobranças e faturas", "A base de faturamento está preparada, sem integração de pagamento nesta sprint.", "Nenhuma cobrança automática será realizada. A operação financeira será habilitada somente após validação.", true),
            ["contratos"] = new("Contratos da plataforma", "O acesso global consolidado a contratos está em construção controlada.", "Selecione uma empresa para trabalhar no contexto autorizado, sem misturar dados entre clientes.", true),
            ["auditoria"] = new("Auditoria", "Os eventos importantes da plataforma já podem ser consultados em uma tela com busca, filtro por organização, paginação e metadados preservados.", "Use o menu Auditoria na administração global ou abra a tela diretamente para pesquisar os eventos.", true, OpenHref: "/administracao/auditoria", OpenLinkText: "Abrir auditoria da plataforma"),
            ["configuracoes"] = new("Configurações operacionais", "As configurações globais serão reunidas aqui após a estabilização dos fluxos principais.", "Use apenas as configurações de ambiente documentadas; nenhuma mudança é aplicada nesta tela.", true)
        };

    [HttpGet("{module}")]
    public IActionResult Index(string module)
    {
        var modules = User.IsInRole("SuperAdministrator") ? PlatformModules : TenantModules;
        return modules.TryGetValue(module, out var model) ? View(model) : NotFound();
    }
}
