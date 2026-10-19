using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Odca.Configuration.Localization;
using Odca.Contracts.Onboarding;
using Odca.Contracts.Tenancy;
using Odca.Web.Models;
using Odca.Web.Services;

namespace Odca.Web.Controllers;

public sealed class OnboardingController(OdcaApiClient apiClient, IUserTenantContext tenantContext, LocalizationService loc) : Controller
{
    // D-OC48 (S14): padrão canônico de cultura (CultureMiddleware) e localização das mensagens BFF.
    private string? Culture => HttpContext.Items[Odca.Web.Middleware.CultureMiddleware.ItemsKey] as string;

    private string L(string key, params string[] args) => loc.T(key, Culture, args);
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
                ApiCallStatus.RateLimited => L("api.too_many_attempts"),
                ApiCallStatus.Timeout or ApiCallStatus.Unavailable => L("api.service_unavailable"),
                _ => L("onb.reg_start_failed")
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
            ModelState.AddModelError(string.Empty, L("onb.conf_token_invalid"));
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
        string? overviewError = null;
        var overviewResult = await apiClient.GetOrganizationOverviewAsync(token, result.Value!.TenantId, cancellationToken);
        if (overviewResult.Succeeded)
        {
            overview = overviewResult.Value;
        }
        else if (overviewResult.Status != ApiCallStatus.Forbidden)
        {
            overviewError = overviewResult.UserMessage(L("onb.err_overview_load"));
        }

        var tenantId = result.Value.TenantId;
        var access = await tenantContext.GetAccessAsync(tenantId, cancellationToken);
        var canCreateDocument = access?.HasPermission("tenant.contract_drafts.manage") == true;
        var canManagePatients = access?.HasPermission("tenant.patients.manage") == true;
        var canReadReviews = access?.HasPermission("tenant.reviews.read") == true;

        IReadOnlyList<Odca.Contracts.Studio.StudioDocumentItem> recentDocuments = [];
        string? documentsError = null;
        if (access?.HasAnyPermission("tenant.contract_drafts.read", "tenant.patients.documents.read") == true)
        {
            var docsRes = await apiClient.GetStudioDocumentsAsync(token, tenantId, null, null, null, null, 1, 5, ct: cancellationToken);
            if (docsRes.Succeeded && docsRes.Value is not null)
            {
                recentDocuments = docsRes.Value.Items;
            }
            else if (!docsRes.Succeeded && docsRes.Status != ApiCallStatus.Forbidden)
            {
                documentsError = docsRes.UserMessage(L("onb.err_documents_load"));
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
                reviewsError = reviewsRes.UserMessage(L("onb.err_reviews_load"));
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
            ReviewsError = reviewsError,
            OverviewError = overviewError
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
            new(L("gs.s1_title"), L("gs.s1_desc"),
                L("gs.s1_responsible"), L("gs.s1_cta"), "Organizations", "Edit",
                home.TenantStatus == "active", canManageOrganization),
            new(L("gs.s2_title"), L("gs.s2_desc"),
                L("gs.s1_responsible"), L("gs.s2_cta"), "Consumption", "Index",
                home.CommercialState == "active", access.HasPermission("tenant.organization.read") || canManageOrganization),
            new(L("gs.s3_title"), L("gs.s3_desc"),
                L("gs.s3_responsible"), L("gs.s3_cta"), "Organizations", "Team",
                teamStarted, canManageTeam, Optional: true),
            new(L("gs.s4_title"), L("gs.s4_desc"),
                L("gs.s4_responsible"), L("gs.s4_cta"), "Studio", "Index",
                overview?.HasContracts == true, canCreateContract),
            new(L("gs.s5_title"), L("gs.s5_desc"),
                L("gs.s5_responsible"), L("gs.s5_cta"), "Inbox", "Index",
                overview is not null && overview.Pendencies.Count == 0, canReadPendencies)
        };

        ViewData["OrganizationName"] = home.OrganizationName;
        return View(new GettingStartedPageViewModel { Home = home, Overview = overview, Steps = steps });
    }

}
