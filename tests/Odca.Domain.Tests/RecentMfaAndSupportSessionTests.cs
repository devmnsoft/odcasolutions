using System.Globalization;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Options;
using Odca.Api;
using Odca.Application.Common;
using Odca.Application.Identity;
using Odca.Infrastructure.Identity;
using Odca.Web.Services;
using Odca.Worker;

namespace Odca.Domain.Tests;

/// <summary>
/// D-OC39 (Sprint 5): F-1 reautenticacao MFA recente (handler da politica RecentMfa, claims do
/// token e espelho BFF) e A-1 selecao de candidatos a purga da area reconciled.
/// </summary>
public sealed class RecentMfaHandlerTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 17, 12, 0, 0, TimeSpan.Zero);

    private static AuthorizationHandlerContext BuildContext(ClaimsPrincipal principal, RecentMfaRequirement requirement)
        => new([requirement], principal, null);

    private static ClaimsPrincipal Principal(
        bool superAdmin,
        bool mustChangePassword = false,
        bool enrolled = true,
        DateTimeOffset? mfaVerifiedAt = null)
    {
        var claims = new List<Claim>();
        if (superAdmin)
        {
            claims.Add(new Claim(ClaimTypes.Role, "SuperAdministrator"));
        }

        claims.Add(new Claim("must_change_password", mustChangePassword ? "true" : "false"));
        claims.Add(new Claim("mfa_enrolled", enrolled ? "true" : "false"));
        if (mfaVerifiedAt is not null)
        {
            claims.Add(new Claim("mfa_verified_at", ((long)mfaVerifiedAt.Value.ToUnixTimeSeconds()).ToString(CultureInfo.InvariantCulture)));
        }

        return new ClaimsPrincipal(new ClaimsIdentity(claims, "test"));
    }

    private static async Task<AuthorizationHandlerContext> EvaluateAsync(ClaimsPrincipal principal, int windowMinutes = 15)
    {
        var handler = new RecentMfaHandler(new FixedClock(Now), Options.Create(new RecentMfaOptions { RecentAuthMinutes = windowMinutes }));
        var requirement = new RecentMfaRequirement();
        var context = BuildContext(principal, requirement);
        await handler.HandleAsync(context);
        return context;
    }

    [Fact]
    public async Task NaoSuperadminNuncaExigeRecente()
    {
        var result = await EvaluateAsync(Principal(superAdmin: false));
        Assert.True(result.HasSucceeded);
    }

    [Fact]
    public async Task TrocaForcadaDeSenhaEIsenta()
    {
        var result = await EvaluateAsync(Principal(superAdmin: true, mustChangePassword: true));
        Assert.True(result.HasSucceeded);
    }

    [Fact]
    public async Task SuperadminSemMfaInscritoEIsento()
    {
        var result = await EvaluateAsync(Principal(superAdmin: true, enrolled: false));
        Assert.True(result.HasSucceeded);
    }

    [Fact]
    public async Task SuperadminInscritoSemVerificacaoNaSessaoFalha()
    {
        var result = await EvaluateAsync(Principal(superAdmin: true, mfaVerifiedAt: null));
        Assert.False(result.HasSucceeded);
        Assert.Equal("api.auth.recent_auth_required", result.FailureReasons!.Single().Message);
    }

    [Fact]
    public async Task VerificacaoFrescaPassa()
    {
        var result = await EvaluateAsync(Principal(superAdmin: true, mfaVerifiedAt: Now.AddMinutes(-10)));
        Assert.True(result.HasSucceeded);
    }

    [Fact]
    public async Task VerificacaoNoLimiteDaJanelaPassa()
    {
        var result = await EvaluateAsync(Principal(superAdmin: true, mfaVerifiedAt: Now.AddMinutes(-15)));
        Assert.True(result.HasSucceeded);
    }

    [Fact]
    public async Task VerificacaoObsoletaFalhaComCodigoEstavel()
    {
        var result = await EvaluateAsync(Principal(superAdmin: true, mfaVerifiedAt: Now.AddSeconds(-15 * 60 - 1)));
        Assert.False(result.HasSucceeded);
        Assert.Equal("api.auth.recent_auth_required", result.FailureReasons!.Single().Message);
    }

    [Fact]
    public async Task JanelaConfiguravelRespeitaMenorJanela()
    {
        var principal = Principal(superAdmin: true, mfaVerifiedAt: Now.AddSeconds(-90));
        var insideWindow = await EvaluateAsync(principal, windowMinutes: 5);
        var outsideWindow = await EvaluateAsync(principal, windowMinutes: 1);
        Assert.True(insideWindow.HasSucceeded);
        Assert.False(outsideWindow.HasSucceeded);
    }

    private sealed class FixedClock(DateTimeOffset now) : IClock
    {
        public DateTimeOffset UtcNow => now;
    }
}

/// <summary>D-OC39 (Sprint 5/F-1): claims mfa_enrolled e mfa_verified_at emitidas no bearer.</summary>
public sealed class JwtTokenServiceRecentAuthClaimsTests
{
    private static readonly DateTimeOffset Anchor = DateTimeOffset.UtcNow;
    private static readonly DateTimeOffset Expires = DateTimeOffset.UtcNow.AddHours(1);

    private static IssuedToken Issue(bool enrolled, DateTimeOffset? mfaCompletedAt)
    {
        var options = new JwtOptions
        {
            Issuer = "odca.test",
            Audience = "odca.test",
            SigningKey = "chave-de-assinatura-de-teste-com-trinta-e-dois-bytes"
        };
        var service = new JwtTokenService(Options.Create(options));
        var user = new UserCredential(
            Guid.NewGuid(), "super@teste.local", "Super Teste", "hash", 1,
            MustChangePassword: false, IsPlatformAdministrator: true,
            MfaConfirmedAt: enrolled ? Anchor : null, IsDeleted: false, LockedUntil: null);
        var session = new SessionRecord(
            Guid.NewGuid(), user.Id, 1, Expires,
            mfaCompletedAt is not null ? "mfa" : "password", mfaCompletedAt);
        return service.Issue(user, session);
    }

    private static Dictionary<string, string> Payload(string token)
    {
        var segment = token.Split('.')[1];
        var base64 = segment.Replace('-', '+').Replace('_', '/');
        var padding = (base64.Length % 4) switch { 2 => "==", 3 => "=", _ => "" };
        base64 += padding;
        using var document = JsonDocument.Parse(Encoding.UTF8.GetString(Convert.FromBase64String(base64)));
        return document.RootElement.EnumerateObject()
            .ToDictionary(
                p => p.Name,
                p => p.Value.ValueKind == JsonValueKind.String ? p.Value.GetString()! : p.Value.GetRawText(),
                StringComparer.Ordinal);
    }

    [Fact]
    public void MfaVerificadoNaSessaoEmiteInstanteUnix()
    {
        var expected = ((long)Anchor.AddMinutes(-2).ToUnixTimeSeconds()).ToString(CultureInfo.InvariantCulture);
        var payload = Payload(Issue(enrolled: true, mfaCompletedAt: Anchor.AddMinutes(-2)).AccessToken);
        Assert.Equal("true", payload["mfa_enrolled"]);
        Assert.Equal(expected, payload["mfa_verified_at"]);
        Assert.Equal("true", payload["mfa_verified"]);
    }

    [Fact]
    public void SemMfaNaoEmiteInstanteNemInscricao()
    {
        var payload = Payload(Issue(enrolled: false, mfaCompletedAt: null).AccessToken);
        Assert.Equal("false", payload["mfa_enrolled"]);
        Assert.Equal("0", payload["mfa_verified_at"]);
        Assert.Equal("false", payload["mfa_verified"]);
    }
}

/// <summary>D-OC39 (Sprint 5/F-1): espelho BFF — decide redirecionar ao desafio quando o 403
/// veio da exigencia de MFA recente (mesmas isencoes da politica da API).</summary>
public sealed class RecentAuthGuardTests
{
    private static readonly DateTimeOffset Now = DateTimeOffset.UtcNow;

    private static string Token(
        bool superAdmin,
        bool mustChangePassword = false,
        bool enrolled = true,
        DateTimeOffset? mfaVerifiedAt = null)
    {
        var options = new JwtOptions
        {
            Issuer = "odca.test",
            Audience = "odca.test",
            SigningKey = "chave-de-assinatura-de-teste-com-trinta-e-dois-bytes"
        };
        var service = new JwtTokenService(Options.Create(options));
        var user = new UserCredential(
            Guid.NewGuid(), "user@teste.local", "Usuario Teste", "hash", 1,
            MustChangePassword: mustChangePassword, IsPlatformAdministrator: superAdmin,
            MfaConfirmedAt: enrolled ? Now : null, IsDeleted: false, LockedUntil: null);
        var session = new SessionRecord(
            Guid.NewGuid(), user.Id, 1, Now.AddHours(1),
            mfaVerifiedAt is not null ? "mfa" : "password", mfaVerifiedAt);
        return service.Issue(user, session).AccessToken;
    }

    [Fact]
    public void SuperadminComMfaObsoletoExigeDesafio()
        => Assert.True(RecentAuthGuard.NeedsFreshChallenge(Token(true, mfaVerifiedAt: Now.AddMinutes(-30)), 15));

    [Fact]
    public void SuperadminComMfaFrescoNaoExigeDesafio()
        => Assert.False(RecentAuthGuard.NeedsFreshChallenge(Token(true, mfaVerifiedAt: Now.AddMinutes(-5)), 15));

    [Fact]
    public void SuperadminSemVerificacaoNaSessaoExigeDesafio()
        => Assert.True(RecentAuthGuard.NeedsFreshChallenge(Token(true, mfaVerifiedAt: null), 15));

    [Fact]
    public void NaoSuperadminNuncaExigeDesafio()
        => Assert.False(RecentAuthGuard.NeedsFreshChallenge(Token(false, mfaVerifiedAt: Now.AddYears(1)), 15));

    [Fact]
    public void TrocaForcadaDeSenhaEIsenta()
        => Assert.False(RecentAuthGuard.NeedsFreshChallenge(Token(true, mustChangePassword: true, mfaVerifiedAt: null), 15));

    [Fact]
    public void SemMfaInscritoEIsento()
        => Assert.False(RecentAuthGuard.NeedsFreshChallenge(Token(true, enrolled: false, mfaVerifiedAt: null), 15));

    [Theory]
    [InlineData((object?)null)]
    [InlineData("")]
    [InlineData("nao-um-token")]
    [InlineData("a.b.c.d")]
    public void TokenInvalidoNuncaExigeDesafio(string? token)
        => Assert.False(RecentAuthGuard.NeedsFreshChallenge(token, 15));
}

/// <summary>D-OC39 (Sprint 5/A-1): selecao de arquivos reconciliados para purga fisica.</summary>
public sealed class DocumentWorkerPurgeSelectionTests
{
    [Fact]
    public void SoSelecionaArquivosMaisVelhosQueACorteEmTodosOsMeses()
    {
        var root = Path.Combine(Path.GetTempPath(), "odca-purge-test-" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(Path.Combine(root, "2026-08"));
            Directory.CreateDirectory(Path.Combine(root, "2026-10"));
            var old = Path.Combine(root, "2026-08", "antigo.bin");
            var fresh = Path.Combine(root, "2026-10", "recente.bin");
            File.WriteAllText(old, "antigo");
            File.WriteAllText(fresh, "recente");
            File.SetLastWriteTimeUtc(old, DateTime.UtcNow.AddDays(-40));
            File.SetLastWriteTimeUtc(fresh, DateTime.UtcNow.AddHours(-1));

            var cutoff = DateTime.UtcNow.AddDays(-30);
            var selected = DocumentWorker.SelectReconciledPurgeCandidates(root, cutoff).ToList();

            Assert.Single(selected);
            Assert.Equal(old, selected[0]);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void AreaInexistenteNaoProduzCandidatosNemFalha()
    {
        var missing = Path.Combine(Path.GetTempPath(), "odca-purge-missing-" + Guid.NewGuid().ToString("N"));
        Assert.Empty(DocumentWorker.SelectReconciledPurgeCandidates(missing, DateTime.UtcNow).ToList());
    }
}
