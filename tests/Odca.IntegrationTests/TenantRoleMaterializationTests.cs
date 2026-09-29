using System.Security.Claims;
using Dapper;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Npgsql;
using Odca.Application.Tenancy;
using Odca.Infrastructure.Tenancy;

namespace Odca.IntegrationTests;

public sealed class TenantRoleMaterializationTests(DatabaseFixture database) : IClassFixture<DatabaseFixture>
{
    private static readonly Guid TenantA = Guid.Parse("75000000-0000-0000-0000-000000000001");
    private static readonly Guid TenantB = Guid.Parse("75000000-0000-0000-0000-000000000002");
    private static readonly Guid AdminUser = Guid.Parse("75000000-0000-0000-0000-000000000011");

    [Fact]
    public async Task ListRolesMaterializesEmptySingleMultipleSystemCustomAndEnforcesIsolation()
    {
        await using var adminConn = new NpgsqlConnection(database.AdminConnectionString);
        await adminConn.OpenAsync();

        // 1. Seed two tenants, users and roles
        await adminConn.ExecuteAsync("""
            INSERT INTO odca.tenants(id, business_code, display_name, status)
            VALUES (@TenantA, '75000000000100', 'Tenant A Materialization', 'active'),
                   (@TenantB, '75000000000200', 'Tenant B Materialization', 'active')
            ON CONFLICT (id) DO NOTHING;

            INSERT INTO odca.users(id, email, email_normalized, login_normalized, display_name, password_hash, is_platform_administrator, email_verified_at)
            VALUES (@AdminUser, 'admin.roles@odca.local', 'ADMIN.ROLES@ODCA.LOCAL', 'ADMIN.ROLES@ODCA.LOCAL', 'Admin Roles', 'hash', false, now())
            ON CONFLICT (id) DO NOTHING;

            INSERT INTO odca.memberships(tenant_id, user_id, status)
            VALUES (@TenantA, @AdminUser, 'active')
            ON CONFLICT (tenant_id, user_id) DO UPDATE SET status = 'active';

            -- Clean roles for TenantA & TenantB
            DELETE FROM odca.role_permissions WHERE role_id IN (SELECT id FROM odca.roles WHERE tenant_id IN (@TenantA, @TenantB));
            DELETE FROM odca.member_roles WHERE tenant_id IN (@TenantA, @TenantB);
            DELETE FROM odca.roles WHERE tenant_id IN (@TenantA, @TenantB);

            -- Ensure permissions exist
            INSERT INTO odca.permissions(code, description)
            VALUES ('tenant.team.read', 'Ler equipe'),
                   ('tenant.team.manage', 'Gerenciar equipe'),
                   ('tenant.patients.read', 'Ler pacientes')
            ON CONFLICT (code) DO NOTHING;

            -- Admin role for TenantA so AdminUser is authorized for tenant.team.read and tenant.team.manage
            INSERT INTO odca.roles(id, scope_type, tenant_id, code, display_name, is_system)
            VALUES ('75000000-0000-0000-0000-000000000020', 'tenant', @TenantA, 'admin-role', 'Admin Role', true);

            INSERT INTO odca.role_permissions(role_id, permission_code)
            VALUES ('75000000-0000-0000-0000-000000000020', 'tenant.team.read'),
                   ('75000000-0000-0000-0000-000000000020', 'tenant.team.manage');

            INSERT INTO odca.member_roles(tenant_id, user_id, role_id, assigned_by)
            VALUES (@TenantA, @AdminUser, '75000000-0000-0000-0000-000000000020', @AdminUser);

            -- Role 1: System role without permissions
            INSERT INTO odca.roles(id, scope_type, tenant_id, code, display_name, is_system)
            VALUES ('75000000-0000-0000-0000-000000000021', 'tenant', @TenantA, 'system-empty', 'A. Perfil Sistema Sem Permissão', true);

            -- Role 2: Custom role with 1 permission
            INSERT INTO odca.roles(id, scope_type, tenant_id, code, display_name, is_system)
            VALUES ('75000000-0000-0000-0000-000000000022', 'tenant', @TenantA, 'custom-single', 'B. Perfil Customizado Uma Permissão', false);

            INSERT INTO odca.role_permissions(role_id, permission_code)
            VALUES ('75000000-0000-0000-0000-000000000022', 'tenant.patients.read');

            -- Role 3: System role with multiple permissions
            INSERT INTO odca.roles(id, scope_type, tenant_id, code, display_name, is_system)
            VALUES ('75000000-0000-0000-0000-000000000023', 'tenant', @TenantA, 'system-multiple', 'C. Perfil Sistema Várias Permissões', true);

            INSERT INTO odca.role_permissions(role_id, permission_code)
            VALUES ('75000000-0000-0000-0000-000000000023', 'tenant.team.read'),
                   ('75000000-0000-0000-0000-000000000023', 'tenant.team.manage');

            -- Tenant B role (for isolation test)
            INSERT INTO odca.roles(id, scope_type, tenant_id, code, display_name, is_system)
            VALUES ('75000000-0000-0000-0000-000000000099', 'tenant', @TenantB, 'tenant-b-role', 'Perfil Tenant B', false);
            """, new { TenantA, TenantB, AdminUser });

        // 2. Query TenantA roles through NpgsqlTenantAdministrationRepository
        await using var dataSource = NpgsqlDataSource.Create(database.AppConnectionString);
        var keysDir = new DirectoryInfo(Path.Combine(Path.GetTempPath(), "odca_test_keys_" + Guid.NewGuid().ToString("N")));
        keysDir.Create();
        var dataProtectionProvider = Microsoft.AspNetCore.DataProtection.DataProtectionProvider.Create(keysDir);
        var repo = new NpgsqlTenantAdministrationRepository(dataSource, dataProtectionProvider);

        var resultA = await repo.ListRolesAsync(AdminUser, TenantA, default);
        Assert.Equal(QueryAccessStatus.Ok, resultA.Status);
        Assert.NotNull(resultA.Value);

        var rolesA = resultA.Value!;
        // 4 roles in TenantA: Admin Role, A (empty), B (single), C (multiple)
        Assert.Equal(4, rolesA.Count);

        // Role sem permissão (A)
        var roleEmpty = rolesA.Single(r => r.Id == Guid.Parse("75000000-0000-0000-0000-000000000021"));
        Assert.True(roleEmpty.IsSystem);
        Assert.NotNull(roleEmpty.Permissions);
        Assert.Empty(roleEmpty.Permissions);

        // Role com 1 permissão (B)
        var roleSingle = rolesA.Single(r => r.Id == Guid.Parse("75000000-0000-0000-0000-000000000022"));
        Assert.False(roleSingle.IsSystem);
        Assert.Single(roleSingle.Permissions);
        Assert.Equal("tenant.patients.read", roleSingle.Permissions[0]);

        // Role com várias permissões (C)
        var roleMultiple = rolesA.Single(r => r.Id == Guid.Parse("75000000-0000-0000-0000-000000000023"));
        Assert.True(roleMultiple.IsSystem);
        Assert.Equal(2, roleMultiple.Permissions.Length);
        Assert.Contains("tenant.team.read", roleMultiple.Permissions);
        Assert.Contains("tenant.team.manage", roleMultiple.Permissions);

        // 3. Test Isolation: Tenant A cannot see Tenant B's roles
        Assert.DoesNotContain(rolesA, r => r.Id == Guid.Parse("75000000-0000-0000-0000-000000000099"));

        // AdminUser from TenantA attempting to query TenantB returns Forbidden
        var resultB = await repo.ListRolesAsync(AdminUser, TenantB, default);
        Assert.Equal(QueryAccessStatus.Forbidden, resultB.Status);

        // 4. Test Create, Edit and Re-read role
        var newRole = await repo.CreateRoleAsync(AdminUser, TenantA, "D. Novo Perfil Operador", ["tenant.team.read"], default);
        Assert.NotNull(newRole);
        Assert.Equal("D. Novo Perfil Operador", newRole!.Name);
        Assert.False(newRole.IsSystem);
        Assert.Single(newRole.Permissions);

        // Re-read roles and verify new role is listed with its permissions materialized
        var reReadResult = await repo.ListRolesAsync(AdminUser, TenantA, default);
        Assert.Equal(5, reReadResult.Value!.Count);
        var readCreated = reReadResult.Value.Single(r => r.Id == newRole.Id);
        Assert.Equal("D. Novo Perfil Operador", readCreated.Name);
        Assert.Single(readCreated.Permissions);
        Assert.Equal("tenant.team.read", readCreated.Permissions[0]);

        // Update permissions
        var updateResult = await repo.UpdateRolePermissionsAsync(AdminUser, TenantA, newRole.Id, ["tenant.team.read", "tenant.team.manage"], default);
        Assert.Equal(UpdateRolePermissionsStatus.Updated, updateResult.Status);
        Assert.Equal(2, updateResult.Role!.Permissions.Length);

        // Re-read and confirm persistence
        var reReadAfterUpdate = await repo.ListRolesAsync(AdminUser, TenantA, default);
        var updatedRole = reReadAfterUpdate.Value!.Single(r => r.Id == newRole.Id);
        Assert.Equal(2, updatedRole.Permissions.Length);
        Assert.Contains("tenant.team.read", updatedRole.Permissions);
        Assert.Contains("tenant.team.manage", updatedRole.Permissions);
    }
}

