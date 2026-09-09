using Dapper;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Npgsql;
using Odca.Application.Identity;
using Odca.Infrastructure.Database;
using Odca.Infrastructure.Identity;

namespace Odca.IntegrationTests;

public sealed class DatabaseFixture : IAsyncLifetime
{
    public const string Email = "integration@odca.local";
    public const string InitialPassword = "Initial!Password2026";
    public const string ChangedPassword = "Changed!Password2026";
    private const string JwtKey = "test-only-key-with-more-than-thirty-two-bytes-2026";
    private const string TestMarker = "ODCA_INTEGRATION_TESTS";
    private const string TestRole = "odca_test_app_login";

    public string AdminConnectionString { get; private set; } = string.Empty;

    public string AppConnectionString { get; private set; } = string.Empty;

    public async Task InitializeAsync()
    {
        (AdminConnectionString, AppConnectionString) = LoadConnections();
        var sqlPath = FindFile("database", "odca.sql");
        var migrator = new DatabaseMigrator(sqlPath);
        await migrator.ApplyAsync(AdminConnectionString);
        await migrator.ApplyAsync(AdminConnectionString);
        var appPassword = new NpgsqlConnectionStringBuilder(AppConnectionString).Password
            ?? throw new InvalidOperationException("A conexão de teste da aplicação precisa de senha.");
        await DatabaseRoleProvisioner.ProvisionApplicationLoginAsync(AdminConnectionString, appPassword, TestRole);
        await ResetTestUserAsync();
    }

    public Task DisposeAsync() => Task.CompletedTask;

    public WebApplicationFactory<Program> CreateApi()
    {
        return new TestApiFactory(this);
    }

    public Task ResetAuthenticationScenarioAsync() => ResetTestUserAsync();

    public async Task<int> MigrationCountAsync()
    {
        await using var connection = new NpgsqlConnection(AdminConnectionString);
        return await connection.ExecuteScalarAsync<int>("SELECT count(*)::int FROM odca.schema_migrations WHERE version BETWEEN 1 AND 3;");
    }

    public async Task<bool> IsolationFixesArePresentAsync()
    {
        const string sql = """
            SELECT
                EXISTS (
                    SELECT 1 FROM pg_constraint
                     WHERE conrelid = 'odca.member_roles'::regclass
                       AND conname = 'member_roles_tenant_role_fk')
                AND (SELECT relrowsecurity FROM pg_class WHERE oid = 'odca.role_permissions'::regclass)
                AND (SELECT relrowsecurity FROM pg_class WHERE oid = 'odca.audit_events'::regclass);
            """;
        await using var connection = new NpgsqlConnection(AdminConnectionString);
        return await connection.ExecuteScalarAsync<bool>(sql);
    }

    public async Task<bool> PrivacyRequestExistsAsync(string protocol)
    {
        await using var connection = new NpgsqlConnection(AdminConnectionString);
        return await connection.ExecuteScalarAsync<bool>(
            "SELECT EXISTS (SELECT 1 FROM odca.privacy_requests WHERE public_protocol = @protocol);",
            new { protocol });
    }

    public async Task SeedTenantIsolationScenarioAsync()
    {
        const string sql = """
            INSERT INTO odca.tenants (id, business_code, display_name)
            VALUES
                ('50000000-0000-0000-0000-000000000001', 'TEST-A', 'Tenant A'),
                ('50000000-0000-0000-0000-000000000002', 'TEST-B', 'Tenant B')
            ON CONFLICT (id) DO UPDATE SET display_name = EXCLUDED.display_name;

            INSERT INTO odca.memberships (tenant_id, user_id, status)
            VALUES
                ('50000000-0000-0000-0000-000000000001', '20000000-0000-0000-0000-000000000001', 'active'),
                ('50000000-0000-0000-0000-000000000002', '20000000-0000-0000-0000-000000000001', 'active')
            ON CONFLICT (tenant_id, user_id) DO UPDATE SET status = 'active';

            INSERT INTO odca.permissions (code, description, delegable)
            VALUES ('tenant.test.read', 'Permissão sintética de teste de isolamento.', true)
            ON CONFLICT (code) DO NOTHING;

            INSERT INTO odca.roles (id, scope_type, tenant_id, code, display_name)
            VALUES
                ('60000000-0000-0000-0000-000000000001', 'tenant', '50000000-0000-0000-0000-000000000001', 'test-reader', 'Leitor A'),
                ('60000000-0000-0000-0000-000000000002', 'tenant', '50000000-0000-0000-0000-000000000002', 'test-reader', 'Leitor B')
            ON CONFLICT (id) DO NOTHING;

            INSERT INTO odca.role_permissions (role_id, permission_code)
            VALUES
                ('60000000-0000-0000-0000-000000000001', 'tenant.test.read'),
                ('60000000-0000-0000-0000-000000000002', 'tenant.test.read')
            ON CONFLICT DO NOTHING;

            INSERT INTO odca.audit_events (scope_type, tenant_id, action, entity_type, result)
            SELECT 'tenant', tenant_id, 'test.isolation', 'tenant', 'success'
              FROM (VALUES
                    ('50000000-0000-0000-0000-000000000001'::uuid),
                    ('50000000-0000-0000-0000-000000000002'::uuid)) AS source(tenant_id)
             WHERE NOT EXISTS
                (SELECT 1 FROM odca.audit_events existing
                  WHERE existing.tenant_id = source.tenant_id AND existing.action = 'test.isolation');
            """;
        await using var connection = new NpgsqlConnection(AdminConnectionString);
        await connection.ExecuteAsync(sql);
    }

    private async Task ResetTestUserAsync()
    {
        var user = new UserCredential(
            Guid.Parse("20000000-0000-0000-0000-000000000001"),
            Email,
            "Administrador de integração",
            string.Empty,
            1,
            true,
            true,
            false,
            null);
        var hash = new AspNetPasswordService().Hash(user, InitialPassword);
        const string sql = """
            INSERT INTO odca.users
                (id, email, email_normalized, login_normalized, display_name, password_hash,
                 must_change_password, is_platform_administrator, email_verified_at)
            VALUES (@Id, @Email, @NormalizedEmail, @NormalizedEmail, @DisplayName, @Hash,
                    true, true, now())
            ON CONFLICT (email_normalized) DO UPDATE
                SET password_hash = EXCLUDED.password_hash,
                    must_change_password = true,
                    is_platform_administrator = true,
                    security_version = odca.users.security_version + 1,
                    failed_login_count = 0,
                    locked_until = NULL,
                    is_deleted = false,
                    deleted_at = NULL,
                    deleted_by = NULL,
                    deletion_reason = NULL;
            UPDATE odca.sessions
               SET revoked_at = COALESCE(revoked_at, now())
             WHERE user_id = @Id;
            """;

        await using var connection = new NpgsqlConnection(AdminConnectionString);
        await connection.ExecuteAsync(sql, new
        {
            user.Id,
            user.Email,
            NormalizedEmail = Email.ToUpperInvariant(),
            user.DisplayName,
            Hash = hash
        });
    }

    private static (string Admin, string App) LoadConnections()
    {
        if (!string.Equals(
                Environment.GetEnvironmentVariable("ODCA_TEST_ENVIRONMENT"),
                TestMarker,
                StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"Defina ODCA_TEST_ENVIRONMENT={TestMarker} para autorizar somente o banco descartável de integração.");
        }

        var admin = Environment.GetEnvironmentVariable("ODCA_TEST_ADMIN_CONNECTION");
        var app = Environment.GetEnvironmentVariable("ODCA_TEST_APP_CONNECTION");
        if (string.IsNullOrWhiteSpace(admin) || string.IsNullOrWhiteSpace(app))
        {
            throw new InvalidOperationException(
                "Defina ODCA_TEST_ADMIN_CONNECTION e ODCA_TEST_APP_CONNECTION; testes nunca usam development-runtime.json.");
        }

        var adminBuilder = new NpgsqlConnectionStringBuilder(admin);
        var appBuilder = new NpgsqlConnectionStringBuilder(app);
        if (string.IsNullOrWhiteSpace(adminBuilder.Database) ||
            !adminBuilder.Database.StartsWith("odca_test", StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(appBuilder.Database, adminBuilder.Database, StringComparison.Ordinal) ||
            !string.Equals(appBuilder.Host, adminBuilder.Host, StringComparison.OrdinalIgnoreCase) ||
            appBuilder.Port != adminBuilder.Port ||
            !string.Equals(appBuilder.Username, TestRole, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"Configuração recusada: use banco odca_test* e a role exclusiva {TestRole} no mesmo servidor.");
        }

        return (adminBuilder.ConnectionString, appBuilder.ConnectionString);
    }

    private static string FindFile(params string[] segments)
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null)
        {
            var candidate = Path.Combine([current.FullName, .. segments]);
            if (File.Exists(candidate))
            {
                return candidate;
            }

            current = current.Parent;
        }

        throw new FileNotFoundException(string.Join(Path.DirectorySeparatorChar, segments));
    }

    private sealed class TestApiFactory(DatabaseFixture fixture) : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Testing");
            builder.ConfigureAppConfiguration((_, configuration) =>
            {
                configuration.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["ConnectionStrings:Database"] = fixture.AppConnectionString,
                    ["Jwt:Issuer"] = "odca-api-tests",
                    ["Jwt:Audience"] = "odca-api-tests",
                    ["Jwt:SigningKey"] = JwtKey,
                    ["Jwt:AccessTokenMinutes"] = "15",
                    ["Security:MfaRequiredForSuperAdmin"] = "true",
                    ["Security:AllowDevelopmentBootstrap"] = "false"
                });
            });
        }
    }
}
