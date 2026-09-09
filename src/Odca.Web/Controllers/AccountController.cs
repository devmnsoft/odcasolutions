using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Odca.Contracts.Identity;
using Odca.Web.Models;
using Odca.Web.Services;

namespace Odca.Web.Controllers;

public sealed class AccountController(OdcaApiClient apiClient) : Controller
{
    [AllowAnonymous]
    [HttpGet("entrar")]
    public IActionResult Login() => View(new LoginViewModel());

    [AllowAnonymous]
    [HttpPost("entrar")]
    public async Task<IActionResult> Login(LoginViewModel model, CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid)
        {
            return View(model);
        }

        var result = await apiClient.LoginAsync(new LoginRequest(model.Login, model.Password), cancellationToken);
        if (!result.Succeeded)
        {
            ModelState.AddModelError(string.Empty, result.Status switch
            {
                ApiCallStatus.RateLimited => "Muitas tentativas. Aguarde um minuto e tente novamente.",
                ApiCallStatus.Timeout => "O serviço demorou para responder. Tente novamente.",
                ApiCallStatus.Unavailable => "O serviço está temporariamente indisponível.",
                _ => "E-mail/CPF ou senha inválidos."
            });
            return View(model);
        }

        var response = result.Value!;
        await SignInAsync(response);
        return RedirectToAction(response.MustChangePassword ? nameof(ChangePassword) : "Index", response.MustChangePassword ? "Account" : "Home");
    }

    [Authorize]
    [HttpGet("alterar-senha")]
    public IActionResult ChangePassword() => View(new ChangePasswordViewModel());

    [Authorize]
    [HttpPost("alterar-senha")]
    public async Task<IActionResult> ChangePassword(ChangePasswordViewModel model, CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid)
        {
            return View(model);
        }

        var token = await HttpContext.GetTokenAsync("access_token");
        if (token is null)
        {
            return RedirectToAction(nameof(Login));
        }

        var result = await apiClient.ChangePasswordAsync(
            token,
            new ChangePasswordRequest(model.CurrentPassword, model.NewPassword),
            cancellationToken);
        if (!result.Succeeded)
        {
            if (result.Status == ApiCallStatus.Unauthorized)
            {
                await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
                return RedirectToAction(nameof(Login));
            }

            ModelState.AddModelError(string.Empty, result.Status switch
            {
                ApiCallStatus.Forbidden => "Você não tem permissão para alterar esta senha.",
                ApiCallStatus.RateLimited => "Muitas tentativas. Aguarde e tente novamente.",
                ApiCallStatus.Timeout or ApiCallStatus.Unavailable => "O serviço está temporariamente indisponível.",
                _ => "Não foi possível alterar a senha. Confira a senha atual e os requisitos informados."
            });
            return View(model);
        }

        await SignInAsync(result.Value!);
        TempData["Success"] = "Senha alterada com segurança.";
        return RedirectToAction("Index", "Home");
    }

    [Authorize]
    [HttpPost("sair")]
    public async Task<IActionResult> Logout(CancellationToken cancellationToken)
    {
        var token = await HttpContext.GetTokenAsync("access_token");
        var remotelyRevoked = token is null;
        try
        {
            if (token is not null)
            {
                remotelyRevoked = await apiClient.LogoutAsync(token, cancellationToken);
            }
        }
        finally
        {
            await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        }

        if (!remotelyRevoked)
        {
            TempData["LogoutWarning"] = "A sessão local foi encerrada. A revogação remota não pôde ser confirmada e o acesso expirará automaticamente.";
        }
        return RedirectToAction(nameof(Login));
    }

    [AllowAnonymous]
    [HttpGet("acesso-negado")]
    public IActionResult AccessDenied() => View();

    private async Task SignInAsync(LoginResponse response)
    {
        var claims = new List<Claim>
        {
            new(ClaimTypes.Name, response.DisplayName),
            new("must_change_password", response.MustChangePassword ? "true" : "false")
        };
        var identity = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);
        var properties = new AuthenticationProperties
        {
            IsPersistent = false,
            ExpiresUtc = response.ExpiresAt,
            AllowRefresh = false
        };
        properties.StoreTokens([new AuthenticationToken { Name = "access_token", Value = response.AccessToken }]);
        await HttpContext.SignInAsync(
            CookieAuthenticationDefaults.AuthenticationScheme,
            new ClaimsPrincipal(identity),
            properties);
    }
}
