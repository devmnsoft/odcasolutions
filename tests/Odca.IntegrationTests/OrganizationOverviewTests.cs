using System.Net.Http.Headers;
using System.Net.Http.Json;
using Dapper;
using Microsoft.AspNetCore.DataProtection;
using Npgsql;
using Odca.Application.Identity;
using Odca.Contracts.Identity;
using Odca.Contracts.Tenancy;
using Odca.Infrastructure.Identity;
using Odca.Infrastructure.Tenancy;

namespace Odca.IntegrationTests;

public sealed class OrganizationOverviewTests(DatabaseFixture database) : IClassFixture<DatabaseFixture>
{
    private const string Password = "Overview!Integration2026";
    private static readonly Guid ActorId = Guid.Parse("71000000-0000-0000-0000-000000000001");
    private static readonly Guid TenantA = Guid.Parse("71000000-0000-0000-0000-000000000010");
    private static readonly Guid TenantB = Guid.Parse("71000000-0000-0000-0000-000000000020");
    private const long LargeSeatLimit = (long)int.MaxValue + 42;

    [Fact]
    public async Task RealQueryMapsBigintAndKeepsMetricsIsolatedThroughApi()
    {
        await SeedAsync();
        var dataSource = NpgsqlDataSource.Create(database.AppConnectionString);
        await using (dataSource)
        {
            var keys = Directory.CreateTempSubdirectory("odca-overview-keys-");
            try
            {
                var repository = new NpgsqlTenantAdministrationRepository(
                    dataSource, DataProtectionProvider.Create(keys));

                var populated = await repository.GetOrganizationOverviewAsync(ActorId, TenantA, default);
                Assert.NotNull(populated);
                Assert.Equal(2, populated.ActiveMembers);
                Assert.Equal(1, populated.ValidInvitations);
                Assert.Equal(LargeSeatLimit, populated.SeatLimit);
                Assert.Equal(LargeSeatLimit - 3, populated.AvailableSeats);
                Assert.Contains(populated.Pendencies, item => item.StatusFilter == "failed");
                Assert.Contains(populated.Pendencies, item => item.StatusFilter == "blocked");

                var empty = await repository.GetOrganizationOverviewAsync(ActorId, TenantB, default);
                Assert.NotNull(empty);
                Assert.Equal(1, empty.ActiveMembers);
                Assert.Equal(0, empty.ValidInvitations);
                Assert.Equal(0L, empty.SeatLimit);
                Assert.Equal(0L, empty.AvailableSeats);
                Assert.Empty(empty.Pendencies);
            }
            finally
            {
                keys.Delete(recursive: true);
            }
        }

        await using var factory = database.CreateApi();
        using var client = factory.CreateClient();
        using var loginResponse = await client.PostAsJsonAsync(
            "/api/v1/auth/login", new LoginRequest("overview@odca.local", Password));
        loginResponse.EnsureSuccessStatusCode();
        var login = await loginResponse.Content.ReadFromJsonAsync<LoginResponse>();
        Assert.NotNull(login);

        using var request = new HttpRequestMessage(HttpMethod.Get, $"/api/v1/organizations/{TenantA}/overview");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", login.AccessToken);
        using var response = await client.SendAsync(request);
        response.EnsureSuccessStatusCode();
        var contract = await response.Content.ReadFromJsonAsync<OrganizationOverviewResponse>();
        Assert.NotNull(contract);
        Assert.Equal(LargeSeatLimit, contract.SeatLimit);
        Assert.Equal(LargeSeatLimit - 3, contract.AvailableSeats);
    }

    private async Task SeedAsync()
    {
        var credential = new UserCredential(ActorId, "overview@odca.local", "Leitor do resumo", string.Empty,
            1, false, false, null, false, null);
        var hash = new AspNetPasswordService().Hash(credential, Password);
        const string sql = """
            DELETE FROM odca.audit_events WHERE tenant_id IN (@tenantA, @tenantB);
            DELETE FROM odca.tenant_invitations WHERE tenant_id IN (@tenantA, @tenantB);
            DELETE FROM odca.member_roles WHERE tenant_id IN (@tenantA, @tenantB);
            DELETE FROM odca.role_permissions WHERE role_id IN (@roleA, @roleB);
            DELETE FROM odca.subscriptions WHERE tenant_id IN (@tenantA, @tenantB);
            DELETE FROM odca.memberships WHERE tenant_id IN (@tenantA, @tenantB);
            DELETE FROM odca.roles WHERE tenant_id IN (@tenantA, @tenantB);
            DELETE FROM odca.tenants WHERE id IN (@tenantA, @tenantB);
            DELETE FROM odca.sessions WHERE user_id IN (@actorId, @activeId, @blockedId);
            DELETE FROM odca.users WHERE id IN (@actorId, @activeId, @blockedId);
            DELETE FROM odca.plan_entitlements WHERE plan_version_id=@planId;
            DELETE FROM odca.plan_versions WHERE id=@planId;

            INSERT INTO odca.users(id,email,email_normalized,login_normalized,display_name,password_hash,
                                   must_change_password,email_verified_at)
            VALUES (@actorId,'overview@odca.local','OVERVIEW@ODCA.LOCAL','OVERVIEW@ODCA.LOCAL','Leitor do resumo',@hash,false,now()),
                   (@activeId,'active-overview@odca.local','ACTIVE-OVERVIEW@ODCA.LOCAL','ACTIVE-OVERVIEW@ODCA.LOCAL','Membro ativo',@hash,false,now()),
                   (@blockedId,'blocked-overview@odca.local','BLOCKED-OVERVIEW@ODCA.LOCAL','BLOCKED-OVERVIEW@ODCA.LOCAL','Membro bloqueado',@hash,false,now());
            INSERT INTO odca.tenants(id,business_code,display_name,status)
            VALUES (@tenantA,'OVERVIEW-A','Overview A','active'),(@tenantB,'OVERVIEW-B','Overview B','active');
            INSERT INTO odca.memberships(tenant_id,user_id,status)
            VALUES (@tenantA,@actorId,'active'),(@tenantA,@activeId,'active'),(@tenantA,@blockedId,'blocked'),
                   (@tenantB,@actorId,'active');
            INSERT INTO odca.roles(id,scope_type,tenant_id,code,display_name,is_system)
            VALUES (@roleA,'tenant',@tenantA,'tenant-administrator','Administrador da organização',true),
                   (@roleB,'tenant',@tenantB,'tenant-administrator','Administrador da organização',true);
            INSERT INTO odca.member_roles(tenant_id,user_id,role_id,assigned_by)
            VALUES (@tenantA,@actorId,@roleA,@actorId),(@tenantB,@actorId,@roleB,@actorId);
            INSERT INTO odca.plan_versions(id,code,version,display_name,status,effective_from)
            VALUES (@planId,'integration-overview-bigint',1,'Plano sintético bigint','published',now());
            INSERT INTO odca.plan_entitlements(plan_version_id,entitlement_code,limit_value,enabled)
            VALUES (@planId,'active_seats',@largeSeatLimit,true);
            INSERT INTO odca.subscriptions(tenant_id,plan_version_id,commercial_state,status,manual_grant_reason,created_by)
            VALUES (@tenantA,@planId,'active','active','integration-test',@actorId);
            INSERT INTO odca.tenant_invitations
                (tenant_id,recipient_email,recipient_normalized,role_id,token_hash,status,idempotency_key,expires_at,created_by)
            VALUES (@tenantA,'valid@example.test','VALID@EXAMPLE.TEST',@roleA,repeat('1',64),'sent','valid',now()+interval '1 day',@actorId),
                   (@tenantA,'expired@example.test','EXPIRED@EXAMPLE.TEST',@roleA,repeat('2',64),'pending','expired',now()-interval '1 day',@actorId),
                   (@tenantA,'failed@example.test','FAILED@EXAMPLE.TEST',@roleA,repeat('3',64),'failed','failed',now()+interval '1 day',@actorId);
            """;
        await using var connection = new NpgsqlConnection(database.AdminConnectionString);
        await connection.ExecuteAsync(sql, new
        {
            actorId = ActorId,
            tenantA = TenantA,
            tenantB = TenantB,
            activeId = Guid.Parse("71000000-0000-0000-0000-000000000002"),
            blockedId = Guid.Parse("71000000-0000-0000-0000-000000000003"),
            roleA = Guid.Parse("71000000-0000-0000-0000-000000000011"),
            roleB = Guid.Parse("71000000-0000-0000-0000-000000000021"),
            planId = Guid.Parse("71000000-0000-0000-0000-000000000030"),
            largeSeatLimit = LargeSeatLimit,
            hash
        });
    }
}
