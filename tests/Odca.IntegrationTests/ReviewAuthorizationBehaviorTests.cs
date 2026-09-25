using System.Security.Claims;
using Dapper;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Npgsql;
using Odca.Api.Controllers;
using Odca.Contracts.Reviews;

namespace Odca.IntegrationTests;

public sealed class ReviewAuthorizationBehaviorTests(DatabaseFixture database) : IClassFixture<DatabaseFixture>
{
    private static readonly Guid TenantId = Guid.Parse("72000000-0000-0000-0000-000000000010");
    private static readonly Guid ReviewerId = Guid.Parse("72000000-0000-0000-0000-000000000001");
    private static readonly Guid ReaderId = Guid.Parse("72000000-0000-0000-0000-000000000002");
    private static readonly Guid BlockedId = Guid.Parse("72000000-0000-0000-0000-000000000003");
    private static readonly Guid ReviewerRoleId = Guid.Parse("72000000-0000-0000-0000-000000000011");

    [Fact]
    public async Task CanonicalPermissionAndAssigneeEndpointEnforceMembershipAndDoNotLeakRlsContext()
    {
        await SeedAsync();
        var builder = new NpgsqlConnectionStringBuilder(database.AppConnectionString)
        {
            MaxPoolSize = 1,
            MinPoolSize = 0
        };
        await using var dataSource = NpgsqlDataSource.Create(builder.ConnectionString);

        await using (var connection = await dataSource.OpenConnectionAsync())
        {
            Assert.True(await connection.ExecuteScalarAsync<bool>(
                "SELECT odca.tenant_actor_has_permission(@actor,@tenant,'tenant.reviews.decide')",
                new { actor = ReviewerId, tenant = TenantId }));
            Assert.False(await connection.ExecuteScalarAsync<bool>(
                "SELECT odca.tenant_actor_has_permission(@actor,@tenant,'tenant.reviews.decide')",
                new { actor = ReaderId, tenant = TenantId }));
            Assert.False(await connection.ExecuteScalarAsync<bool>(
                "SELECT odca.tenant_actor_has_permission(@actor,@tenant,'tenant.reviews.decide')",
                new { actor = BlockedId, tenant = TenantId }));
        }

        var authorized = Controller(dataSource, ReviewerId);
        var response = Assert.IsType<OkObjectResult>(await authorized.Assignees(TenantId, default));
        var assignees = Assert.IsAssignableFrom<IReadOnlyCollection<ReviewAssignee>>(response.Value);
        var reviewer = Assert.Single(assignees);
        Assert.Equal((ReviewerId, "Revisor sintético"), (reviewer.Id, reviewer.Name));

        var queueResponse = Assert.IsType<OkObjectResult>(await authorized.List(
            TenantId, status: null, scope: "all", ct: default));
        var queue = Assert.IsType<ReviewQueuePage>(queueResponse.Value);
        Assert.Empty(queue.Items);
        Assert.Equal(0, queue.Total);

        // The endpoint uses transaction-local RLS identity. With a one-connection pool,
        // this proves that returning the physical connection does not expose its tenant
        // or actor to the next borrower.
        await using (var reused = await dataSource.OpenConnectionAsync())
        {
            var context = await reused.QuerySingleAsync<ConnectionContext>(
                "SELECT current_setting('odca.tenant_id',true) AS \"Tenant\", current_setting('odca.user_id',true) AS \"Actor\"");
            Assert.True(string.IsNullOrEmpty(context.Tenant));
            Assert.True(string.IsNullOrEmpty(context.Actor));
        }

        Assert.IsType<ForbidResult>(await Controller(dataSource, ReaderId).Assignees(TenantId, default));
        Assert.IsType<ForbidResult>(await Controller(dataSource, BlockedId).Assignees(TenantId, default));

        await SetTenantStatusAsync("suspended");
        Assert.IsType<ForbidResult>(await Controller(dataSource, ReviewerId).Assignees(TenantId, default));
    }

    private static ReviewRequestsController Controller(NpgsqlDataSource dataSource, Guid actor)
    {
        var controller = new ReviewRequestsController(dataSource);
        controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext
            {
                User = new ClaimsPrincipal(new ClaimsIdentity(
                    [new Claim("sub", actor.ToString())], "integration-test"))
            }
        };
        return controller;
    }

    private async Task SetTenantStatusAsync(string status)
    {
        await using var connection = new NpgsqlConnection(database.AdminConnectionString);
        await connection.ExecuteAsync(
            "UPDATE odca.tenants SET status=@status WHERE id=@tenant", new { status, tenant = TenantId });
    }

    private async Task SeedAsync()
    {
        const string sql = """
            DELETE FROM odca.member_roles WHERE tenant_id=@tenant;
            DELETE FROM odca.role_permissions WHERE role_id=@role;
            DELETE FROM odca.memberships WHERE tenant_id=@tenant;
            DELETE FROM odca.roles WHERE tenant_id=@tenant;
            DELETE FROM odca.tenants WHERE id=@tenant;
            DELETE FROM odca.sessions WHERE user_id IN (@reviewer,@reader,@blocked);
            DELETE FROM odca.users WHERE id IN (@reviewer,@reader,@blocked);

            INSERT INTO odca.users(id,email,email_normalized,login_normalized,display_name,password_hash,must_change_password,email_verified_at)
            VALUES
              (@reviewer,'reviewer-behavior@odca.local','REVIEWER-BEHAVIOR@ODCA.LOCAL','REVIEWER-BEHAVIOR@ODCA.LOCAL','Revisor sintético','not-used',false,now()),
              (@reader,'reader-behavior@odca.local','READER-BEHAVIOR@ODCA.LOCAL','READER-BEHAVIOR@ODCA.LOCAL','Leitor sintético','not-used',false,now()),
              (@blocked,'blocked-behavior@odca.local','BLOCKED-BEHAVIOR@ODCA.LOCAL','BLOCKED-BEHAVIOR@ODCA.LOCAL','Revisor bloqueado','not-used',false,now());
            INSERT INTO odca.tenants(id,business_code,display_name,status)
            VALUES(@tenant,'REVIEW-BEHAVIOR','Organização sintética de revisão','active');
            INSERT INTO odca.memberships(tenant_id,user_id,status)
            VALUES(@tenant,@reviewer,'active'),(@tenant,@reader,'active'),(@tenant,@blocked,'blocked');
            INSERT INTO odca.roles(id,scope_type,tenant_id,code,display_name,is_system)
            VALUES(@role,'tenant',@tenant,'reviewer-behavior','Revisor',false);
            INSERT INTO odca.role_permissions(role_id,permission_code)
            VALUES(@role,'tenant.reviews.decide'),(@role,'tenant.reviews.read');
            INSERT INTO odca.member_roles(tenant_id,user_id,role_id,assigned_by)
            VALUES(@tenant,@reviewer,@role,@reviewer),(@tenant,@blocked,@role,@reviewer);
            """;
        await using var connection = new NpgsqlConnection(database.AdminConnectionString);
        await connection.ExecuteAsync(sql, new
        {
            tenant = TenantId,
            reviewer = ReviewerId,
            reader = ReaderId,
            blocked = BlockedId,
            role = ReviewerRoleId
        });
    }

    private sealed class ConnectionContext
    {
        public string? Tenant { get; set; }
        public string? Actor { get; set; }
    }
}
