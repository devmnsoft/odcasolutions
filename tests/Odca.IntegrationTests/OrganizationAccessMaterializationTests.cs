using Dapper;
using Microsoft.AspNetCore.DataProtection;
using Npgsql;
using Odca.Infrastructure.Tenancy;

namespace Odca.IntegrationTests;

public sealed class OrganizationAccessMaterializationTests(DatabaseFixture database) : IClassFixture<DatabaseFixture>
{
    private static readonly Guid UserId = Guid.Parse("72000000-0000-0000-0000-000000000001");
    private static readonly Guid TenantWithoutPermissions = Guid.Parse("72000000-0000-0000-0000-000000000010");
    private static readonly Guid TenantWithPermissions = Guid.Parse("72000000-0000-0000-0000-000000000020");
    private const long BigintVersion = (long)int.MaxValue + 73;

    [Fact]
    public async Task RealFunctionMapsTypedEmptyAndMultiplePermissionArraysAndBigintVersion()
    {
        await SeedAsync();
        await using var dataSource = NpgsqlDataSource.Create(database.AppConnectionString);
        var keys = Directory.CreateTempSubdirectory("odca-organization-access-keys-");
        try
        {
            var repository = new NpgsqlTenantAdministrationRepository(
                dataSource, DataProtectionProvider.Create(keys));

            var organizations = await repository.ListOrganizationsAsync(UserId, default);

            Assert.Equal(2, organizations.Count);
            var empty = Assert.Single(organizations, item => item.Id == TenantWithoutPermissions);
            Assert.Empty(empty.Permissions);
            Assert.Equal(1L, empty.Version);

            var populated = Assert.Single(organizations, item => item.Id == TenantWithPermissions);
            Assert.Equal(BigintVersion, populated.Version);
            Assert.Equal(["tenant.organization.manage", "tenant.team.read"],
                populated.Permissions.Order(StringComparer.Ordinal).ToArray());

            await using var connection = new NpgsqlConnection(database.AdminConnectionString);
            var databaseTypes = await connection.QuerySingleAsync<DatabaseTypes>(
                """
                SELECT pg_typeof(version)::text AS "VersionType",
                       pg_typeof(permissions)::text AS "PermissionsType"
                  FROM odca.user_organizations(@userId)
                 WHERE id=@tenantId;
                """, new { userId = UserId, tenantId = TenantWithPermissions });
            Assert.Equal("bigint", databaseTypes.VersionType);
            Assert.Equal("text[]", databaseTypes.PermissionsType);
        }
        finally
        {
            keys.Delete(recursive: true);
        }
    }

    private async Task SeedAsync()
    {
        const string sql = """
            DELETE FROM odca.member_roles WHERE tenant_id IN (@emptyTenant, @populatedTenant);
            DELETE FROM odca.role_permissions WHERE role_id=@roleId;
            DELETE FROM odca.memberships WHERE tenant_id IN (@emptyTenant, @populatedTenant);
            DELETE FROM odca.roles WHERE tenant_id IN (@emptyTenant, @populatedTenant);
            DELETE FROM odca.tenants WHERE id IN (@emptyTenant, @populatedTenant);
            DELETE FROM odca.sessions WHERE user_id=@userId;
            DELETE FROM odca.users WHERE id=@userId;

            INSERT INTO odca.users(id,email,email_normalized,login_normalized,display_name,password_hash,
                                   must_change_password,email_verified_at)
            VALUES (@userId,'organization-access@odca.local','ORGANIZATION-ACCESS@ODCA.LOCAL',
                    'ORGANIZATION-ACCESS@ODCA.LOCAL','Materialização de organizações','not-used',false,now());
            INSERT INTO odca.tenants(id,business_code,display_name,status,version)
            VALUES (@emptyTenant,'ACCESS-EMPTY','Acesso sem permissões','active',1),
                   (@populatedTenant,'ACCESS-MULTIPLE','Acesso com permissões','active',@bigintVersion);
            INSERT INTO odca.memberships(tenant_id,user_id,status)
            VALUES (@emptyTenant,@userId,'active'),(@populatedTenant,@userId,'active');
            INSERT INTO odca.roles(id,scope_type,tenant_id,code,display_name)
            VALUES (@roleId,'tenant',@populatedTenant,'access-test','Perfil de materialização');
            INSERT INTO odca.permissions(code,description,delegable)
            VALUES ('tenant.organization.manage','Gerenciar organização.',true),
                   ('tenant.team.read','Consultar equipe.',true)
            ON CONFLICT (code) DO NOTHING;
            INSERT INTO odca.role_permissions(role_id,permission_code)
            VALUES (@roleId,'tenant.organization.manage'),(@roleId,'tenant.team.read');
            INSERT INTO odca.member_roles(tenant_id,user_id,role_id,assigned_by)
            VALUES (@populatedTenant,@userId,@roleId,@userId);
            """;
        await using var connection = new NpgsqlConnection(database.AdminConnectionString);
        await connection.ExecuteAsync(sql, new
        {
            userId = UserId,
            emptyTenant = TenantWithoutPermissions,
            populatedTenant = TenantWithPermissions,
            roleId = Guid.Parse("72000000-0000-0000-0000-000000000021"),
            bigintVersion = BigintVersion
        });
    }

    private sealed class DatabaseTypes
    {
        public string VersionType { get; init; } = string.Empty;
        public string PermissionsType { get; init; } = string.Empty;
    }
}
