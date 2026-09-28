using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Odca.Contracts.Onboarding;
using Odca.Contracts.Tenancy;
using Odca.Web.Models;
using Odca.Web.Services;

namespace Odca.Web.Controllers;

public sealed class OnboardingController(OdcaApiClient apiClient, IUserTenantContext tenantContext) : Controller
{
    [AllowAnonymous]
    [HttpGet("contratar/{planCode?}")]
    public IActionResult Register(string? planCode) =>
        View(new CustomerRegistrationViewModel { PlanCode = planCode ?? string.Empty });

    [AllowAnonymous]
    [HttpPost("contratar/{planCode?}")]
    public async Task<IActionResult> Register(CustomerRegistrationViewModel model, CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid)
        {
            return View(model);
        }

        var result = await apiClient.StartCustomerRegistrationAsync(
            new StartCustomerRegistrationRequest(
                model.PlanCode,
                model.Document,
                model.ResponsibleName,
                model.Email,
                model.Password,
                model.AcceptedTerms,
                model.AcknowledgedPrivacyNotice,
                model.MarketingConsent),
            cancellationToken);
        if (!result.Succeeded)
        {
            ModelState.AddModelError(string.Empty, result.Status switch
            {
                ApiCallStatus.RateLimited => "Muitas tentativas. Aguarde e tente novamente.",
                ApiCallStatus.Timeout or ApiCallStatus.Unavailable => "O serviço está temporariamente indisponível.",
                _ => "Não foi possível iniciar o cadastro. Confira os dados informados."
            });
            return View(model);
        }

        return View("RegistrationStarted", new CustomerRegistrationStartedViewModel
        {
            RegistrationId = result.Value!.RegistrationId,
            Message = result.Value.Message,
            DevelopmentConfirmationToken = result.Value.DevelopmentConfirmationToken
        });
    }

    [AllowAnonymous]
    [HttpGet("confirmar-email")]
    public IActionResult ConfirmEmail(Guid registrationId, string? token) =>
        View(new ConfirmEmailViewModel { RegistrationId = registrationId, Token = token ?? string.Empty });

    [AllowAnonymous]
    [HttpPost("confirmar-email")]
    public async Task<IActionResult> ConfirmEmail(ConfirmEmailViewModel model, CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid)
        {
            return View(model);
        }

        var result = await apiClient.ConfirmCustomerRegistrationAsync(
            new ConfirmCustomerRegistrationRequest(model.RegistrationId, model.Token),
            cancellationToken);
        if (!result.Succeeded)
        {
            ModelState.AddModelError(string.Empty, "Token inválido, expirado ou já utilizado.");
            return View(model);
        }

        TempData["Success"] = result.Value!.Message;
        return RedirectToAction("Login", "Account");
    }

    [Authorize]
    [HttpGet("cliente")]
    public async Task<IActionResult> CustomerHome(CancellationToken cancellationToken)
    {
        var token = await HttpContext.GetTokenAsync("access_token");
        if (token is null)
        {
            return RedirectToAction("Login", "Account");
        }

        var result = await apiClient.GetCustomerHomeAsync(token, cancellationToken);
        if (!result.Succeeded)
        {
            return result.Status == ApiCallStatus.Unauthorized
                ? RedirectToAction("Login", "Account")
                : RedirectToAction("AccessDenied", "Account");
        }

        OrganizationOverviewResponse? overview = null;
        var overviewResult = await apiClient.GetOrganizationOverviewAsync(token, result.Value!.TenantId, cancellationToken);
        if (overviewResult.Succeeded)
        {
            overview = overviewResult.Value;
        }

        var tenantId = result.Value.TenantId;
        var access = await tenantContext.GetAccessAsync(tenantId, cancellationToken);
        var canCreateDocument = access?.HasAnyPermission("tenant.contract_drafts.manage", "tenant.templates.read") == true;
        var canManagePatients = access?.HasAnyPermission("tenant.patients.manage", "tenant.patients.read") == true;
        var canReadReviews = access?.HasPermission("tenant.reviews.read") == true;

        IReadOnlyList<Odca.Contracts.Studio.StudioDocumentItem> recentDocuments = [];
        string? documentsError = null;
        if (access?.HasAnyPermission("tenant.contract_drafts.read", "tenant.patients.documents.read") == true)
        {
            var docsRes = await apiClient.GetStudioDocumentsAsync(token, tenantId, null, null, null, null, 1, 5, cancellationToken);
            if (docsRes.Succeeded && docsRes.Value is not null)
            {
                recentDocuments = docsRes.Value.Items;
            }
            else if (!docsRes.Succeeded && docsRes.Status != ApiCallStatus.Forbidden)
            {
                documentsError = docsRes.UserMessage("Não foi possível carregar os documentos recentes.");
            }
        }

        IReadOnlyList<Odca.Contracts.Reviews.ReviewQueueItem> assignedReviews = [];
        string? reviewsError = null;
        if (canReadReviews)
        {
            var reviewsRes = await apiClient.GetReviewsAsync(token, tenantId, "open", "assigned_to_me", null, null, null, null, null, 1, cancellationToken);
            if (reviewsRes.Succeeded && reviewsRes.Value is not null)
            {
                assignedReviews = reviewsRes.Value.Items;
            }
            else if (!reviewsRes.Succeeded && reviewsRes.Status != ApiCallStatus.Forbidden)
            {
                reviewsError = reviewsRes.UserMessage("Não foi possível carregar as revisões atribuídas.");
            }
        }

        ViewData["OrganizationName"] = result.Value.OrganizationName;
        return View(new CustomerHomePageViewModel
        {
            Home = result.Value,
            Overview = overview,
            RecentDocuments = recentDocuments,
            AssignedReviews = assignedReviews,
            CanCreateDocument = canCreateDocument,
            CanManagePatients = canManagePatients,
            CanReadReviews = canReadReviews,
            DocumentsError = documentsError,
            ReviewsError = reviewsError
        });
    }
    [Authorize]
    [HttpGet("organizacoes/{tenantId:guid}/primeiros-passos")]
    public async Task<IActionResult> GettingStarted(Guid tenantId, CancellationToken cancellationToken)
    {
        var token = await HttpContext.GetTokenAsync("access_token");
        if (token is null)
        {
            return RedirectToAction("Login", "Account");
        }

        var homeResult = await apiClient.GetCustomerHomeAsync(token, tenantId, cancellationToken);
        if (!homeResult.Succeeded || homeResult.Value!.TenantId != tenantId)
        {
            return homeResult.Status == ApiCallStatus.Unauthorized
                ? RedirectToAction("Login", "Account")
                : Forbid();
        }

        OrganizationOverviewResponse? overview = null;
        var overviewResult = await apiClient.GetOrganizationOverviewAsync(token, tenantId, cancellationToken);
        if (overviewResult.Succeeded)
        {
            overview = overviewResult.Value;
        }

        var home = homeResult.Value;
        var access = await tenantContext.GetAccessAsync(tenantId, cancellationToken);
        if (access is null)
        {
            return Forbid();
        }

        var canManageTeam = access.HasPermission("tenant.team.manage");
        var canManageOrganization = access.HasPermission("tenant.organization.manage");
        var canCreateContract = access.HasPermission("tenant.contract_drafts.manage");
        var canReadPendencies = access.HasAnyPermission("tenant.obligations.read", "tenant.obligations.read_all");
        var teamStarted = overview is not null && (overview.ActiveMembers > 1 || overview.ValidInvitations > 0);
        var steps = new List<GettingStartedStepViewModel>
        {
            new("Confira os dados da organização", "Revise nome, fuso horário e situação antes de iniciar a operação.",
                "Administrador da organização", "Conferir cadastro", "Organizations", "Edit",
                home.TenantStatus == "active", canManageOrganization),
            new("Confira o plano e os recursos", "Veja a versão contratada, limites e o estado comercial definido pelo servidor.",
                "Administrador da organização", "Ver plano e consumo", "Consumption", "Index",
                home.CommercialState == "active", access.HasPermission("tenant.organization.read") || canManageOrganization),
            new("Convide sua equipe", "Convites reservam assentos até expirar. Escolha somente perfis dentro da sua autoridade.",
                "Quem administra a equipe", "Abrir convites", "Organizations", "Team",
                teamStarted, canManageTeam, Optional: true),
            new("Crie o primeiro contrato", "Use um modelo publicado para criar uma minuta persistida e gerar a primeira versão.",
                "Quem pode editar minutas", "Abrir modelos", "Studio", "Index",
                overview?.HasContracts == true, canCreateContract),
            new("Consulte as próximas ações", "Abra a central para acompanhar obrigações, prazos e itens que exigem atenção.",
                "Quem consulta obrigações", "Abrir pendências", "Inbox", "Index",
                overview is not null && overview.Pendencies.Count == 0, canReadPendencies)
        };

        ViewData["OrganizationName"] = home.OrganizationName;
        return View(new GettingStartedPageViewModel { Home = home, Overview = overview, Steps = steps });
    }

}
