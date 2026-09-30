using System.Security.Claims;
using Dapper;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Npgsql;
using Odca.Api.Controllers;
using Odca.Application.Identity;
using Odca.Contracts.Consumption;
using Odca.Infrastructure.Consumption;
using Odca.Infrastructure.Identity;

namespace Odca.IntegrationTests;

public sealed class PlatformCustomerCreationTests(DatabaseFixture database) : IClassFixture<DatabaseFixture>
{
    [Fact]
    public async Task SuperAdminCreatesNewOrganizationAndAdminCanAuthenticate()
    {
        await using var adminConn = new NpgsqlConnection(database.AdminConnectionString);
        await adminConn.OpenAsync();

        // 1. Ensure a superadmin user exists
        var superAdminId = Guid.NewGuid();
        var passwordService = new AspNetPasswordService();
        var superCred = new UserCredential(superAdminId, "super.platform@odca.local", "Super Admin Platform", string.Empty, 1, false, true, null, false, null);
        var hash = passwordService.Hash(superCred, "SuperAdmin@123");

        superAdminId = await adminConn.QuerySingleAsync<Guid>("""
            INSERT INTO odca.users(id, email, email_normalized, login_normalized, display_name, password_hash, is_platform_administrator, email_verified_at)
            VALUES (@superAdminId, 'super.platform@odca.local', 'SUPER.PLATFORM@ODCA.LOCAL', 'SUPER.PLATFORM@ODCA.LOCAL', 'Super Admin Platform', @hash, true, now())
            ON CONFLICT (email_normalized) DO UPDATE SET is_platform_administrator = true, password_hash = @hash
            RETURNING id;
            """, new { superAdminId, hash });

        // 2. Setup controller for SuperAdmin
        await using var dataSource = NpgsqlDataSource.Create(database.AppConnectionString);
        var repo = new NpgsqlConsumptionRepository(dataSource, passwordService);
        var controller = new ConsumptionController(repo);
        var httpContext = new DefaultHttpContext();
        httpContext.User = new ClaimsPrincipal(new ClaimsIdentity([
            new Claim("sub", superAdminId.ToString()),
            new Claim(ClaimTypes.NameIdentifier, superAdminId.ToString()),
            new Claim("is_platform_admin", "true")
        ], "test"));
        controller.ControllerContext = new ControllerContext { HttpContext = httpContext };

        // 3. SuperAdmin creates a new organization
        var uniqueCnpj = GenerateValidCnpj();
        var newAdminEmail = $"admin.clinica.{Guid.NewGuid():N}@odca.local";
        var newAdminPassword = "ClinicaAdmin@2026";

        var request = new CreatePlatformCustomerRequest(
            OrganizationName: "Clínica Médica São Lucas Ltda",
            Document: uniqueCnpj,
            PlanCode: "basic",
            AdminName: "Dr. Roberto Lucas",
            AdminEmail: newAdminEmail,
            InitialPassword: newAdminPassword,
            ActivateDirectly: true);

        var response = await controller.CreateCustomer(request, default);

        var createdResult = Assert.IsType<CreatedResult>(response);
        var createdValue = Assert.IsType<CreatePlatformCustomerResponse>(createdResult.Value);

        Assert.NotEqual(Guid.Empty, createdValue.TenantId);
        Assert.NotEqual(Guid.Empty, createdValue.UserId);
        Assert.Equal("Clínica Médica São Lucas Ltda", createdValue.OrganizationName);
        Assert.Equal(newAdminEmail.ToLowerInvariant(), createdValue.AdminEmail);
        Assert.Equal("active", createdValue.Status);

        // 4. Verify in database: tenant, user, membership, role, subscription, audit event
        var tenantData = await adminConn.QuerySingleAsync<(string Name, string Status, string Tz)>(
            "SELECT display_name, status, timezone FROM odca.tenants WHERE id = @TenantId",
            new { createdValue.TenantId });
        Assert.Equal("Clínica Médica São Lucas Ltda", tenantData.Name);
        Assert.Equal("active", tenantData.Status);
        Assert.Equal("America/Sao_Paulo", tenantData.Tz);

        var membershipCount = await adminConn.ExecuteScalarAsync<int>(
            "SELECT count(*)::int FROM odca.memberships WHERE tenant_id = @TenantId AND user_id = @UserId AND status = 'active'",
            new { createdValue.TenantId, createdValue.UserId });
        Assert.Equal(1, membershipCount);

        var roleCount = await adminConn.ExecuteScalarAsync<int>(
            "SELECT count(*)::int FROM odca.member_roles WHERE tenant_id = @TenantId AND user_id = @UserId",
            new { createdValue.TenantId, createdValue.UserId });
        Assert.Equal(1, roleCount);

        var subscriptionCount = await adminConn.ExecuteScalarAsync<int>(
            "SELECT count(*)::int FROM odca.subscriptions WHERE tenant_id = @TenantId AND status = 'active'",
            new { createdValue.TenantId });
        Assert.Equal(1, subscriptionCount);

        var auditCount = await adminConn.ExecuteScalarAsync<int>(
            "SELECT count(*)::int FROM odca.audit_events WHERE tenant_id = @TenantId AND action = 'organization.created_by_platform'",
            new { createdValue.TenantId });
        Assert.Equal(1, auditCount);

        // 5. Verify the newly created admin can authenticate using passwordService
        var storedHash = await adminConn.QuerySingleAsync<string>(
            "SELECT password_hash FROM odca.users WHERE id = @UserId",
            new { createdValue.UserId });
        var adminUserCred = new UserCredential(createdValue.UserId, newAdminEmail, "Dr. Roberto Lucas", string.Empty, 1, false, false, null, false, null);
        Assert.True(passwordService.Verify(adminUserCred, storedHash, newAdminPassword));

        // 6. Test duplicate prevention: trying to create again with same CNPJ returns Conflict
        var dupResponse = await controller.CreateCustomer(request, default);
        var conflictResult = Assert.IsType<ConflictObjectResult>(dupResponse);
        var problem = Assert.IsType<ProblemDetails>(conflictResult.Value);
        Assert.Equal(409, problem.Status);

        // 7. Verify first access policy: new user has email_verified_at NULL and must_change_password true
        var userAudit = await adminConn.QuerySingleAsync<(DateTime? Verified, bool MustChange)>(
            "SELECT email_verified_at, must_change_password FROM odca.users WHERE id = @UserId",
            new { createdValue.UserId });
        Assert.Null(userAudit.Verified);
        Assert.True(userAudit.MustChange);
    }

    [Fact]
    public async Task NonExistentPlanReturnsValidationProblemWithoutFallback()
    {
        await using var adminConn = new NpgsqlConnection(database.AdminConnectionString);
        await adminConn.OpenAsync();

        var superAdminId = Guid.NewGuid();
        var passwordService = new AspNetPasswordService();
        var superCred = new UserCredential(superAdminId, "super.platform2@odca.local", "Super Admin Platform 2", string.Empty, 1, false, true, null, false, null);
        var hash = passwordService.Hash(superCred, "SuperAdmin@123");

        superAdminId = await adminConn.QuerySingleAsync<Guid>("""
            INSERT INTO odca.users(id, email, email_normalized, login_normalized, display_name, password_hash, is_platform_administrator, email_verified_at)
            VALUES (@superAdminId, 'super.platform2@odca.local', 'SUPER.PLATFORM2@ODCA.LOCAL', 'SUPER.PLATFORM2@ODCA.LOCAL', 'Super Admin Platform 2', @hash, true, now())
            ON CONFLICT (email_normalized) DO UPDATE SET is_platform_administrator = true, password_hash = @hash
            RETURNING id;
            """, new { superAdminId, hash });

        await using var dataSource = NpgsqlDataSource.Create(database.AppConnectionString);
        var repo = new NpgsqlConsumptionRepository(dataSource, passwordService);
        var controller = new ConsumptionController(repo);
        var httpContext = new DefaultHttpContext();
        httpContext.User = new ClaimsPrincipal(new ClaimsIdentity([
            new Claim("sub", superAdminId.ToString()),
            new Claim(ClaimTypes.NameIdentifier, superAdminId.ToString()),
            new Claim("is_platform_admin", "true")
        ], "test"));
        controller.ControllerContext = new ControllerContext { HttpContext = httpContext };

        var request = new CreatePlatformCustomerRequest(
            OrganizationName: "Organização Teste Plano Inexistente",
            Document: GenerateValidCnpj(),
            PlanCode: "plano-fantasma-inexistente",
            AdminName: "Administrador Teste",
            AdminEmail: $"admin.fantasma.{Guid.NewGuid():N}@odca.local",
            InitialPassword: "SenhaValida@2026",
            ActivateDirectly: true);

        var response = await controller.CreateCustomer(request, default);

        var badRequestResult = Assert.IsType<BadRequestObjectResult>(response);
        var problem = Assert.IsType<ProblemDetails>(badRequestResult.Value);
        Assert.Equal(400, problem.Status);
        Assert.Contains("plano-fantasma-inexistente", problem.Detail, StringComparison.OrdinalIgnoreCase);
    }

    private static string GenerateValidCnpj()
    {
        var random = new Random();
        var numbers = new int[12];
        for (int i = 0; i < 8; i++) numbers[i] = random.Next(1, 9);
        numbers[8] = 0; numbers[9] = 0; numbers[10] = 0; numbers[11] = 1; // branch 0001

        int[] w1 = [5, 4, 3, 2, 9, 8, 7, 6, 5, 4, 3, 2];
        int sum1 = 0;
        for (int i = 0; i < 12; i++) sum1 += numbers[i] * w1[i];
        int rem1 = sum1 % 11;
        int d1 = rem1 < 2 ? 0 : 11 - rem1;

        int[] w2 = [6, 5, 4, 3, 2, 9, 8, 7, 6, 5, 4, 3, 2];
        int sum2 = 0;
        for (int i = 0; i < 12; i++) sum2 += numbers[i] * w2[i];
        sum2 += d1 * 2;
        int rem2 = sum2 % 11;
        int d2 = rem2 < 2 ? 0 : 11 - rem2;

        var sb = new System.Text.StringBuilder(14);
        for (int i = 0; i < 12; i++) sb.Append((char)('0' + numbers[i]));
        sb.Append((char)('0' + d1));
        sb.Append((char)('0' + d2));
        return sb.ToString();
    }
}
