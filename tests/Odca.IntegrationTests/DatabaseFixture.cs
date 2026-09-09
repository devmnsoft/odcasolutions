using System.Text.Json;
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
        await DatabaseRoleProvisioner.ProvisionApplicationLoginAsync(AdminConnectionString, appPassword);
        await ResetTestUserAsync();
    }

    public Task DisposeAsync() => Task.CompletedTask;

    public WebApplicationFactory<Program> CreateApi()
    {
        Environment.SetEnvironmentVariable("ConnectionStrings__Database", AppConnectionString);
        Environment.SetEnvironmentVariable("Jwt__Issuer", "odca-api-tests");
        Environment.SetEnvironmentVariable("Jwt__Audience", "odca-api-tests");
        Environment.SetEnvironmentVariable("Jwt__SigningKey", JwtKey);
        Environment.SetEnvironmentVariable("Jwt__AccessTokenMinutes", "15");
        Environment.SetEnvironmentVariable("Security__MfaRequiredForSuperAdmin", "true");
        Environment.SetEnvironmentVariable("Security__AllowDevelopmentBootstrap", "false");
        return new TestApiFactory(this);
    }

    public async Task<int> MigrationCountAsync()
    {
        await using var connection = new NpgsqlConnection(AdminConnectionString);
        return await connection.ExecuteScalarAsync<int>("SELECT count(*)::int FROM odca.schema_migrations WHERE version IN (1, 2);");
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
        var admin = Environment.GetEnvironmentVariable("ODCA_TEST_ADMIN_CONNECTION");
        var app = Environment.GetEnvironmentVariable("ODCA_TEST_APP_CONNECTION");
        if (!string.IsNullOrWhiteSpace(admin) && !string.IsNullOrWhiteSpace(app))
        {
            return (admin, app);
        }

        var runtimePath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "ODCA Solutions",
            "development-runtime.json");
        if (!File.Exists(runtimePath))
        {
            throw new InvalidOperationException(
                "Banco de teste não configurado. Execute o bootstrap local ou defina ODCA_TEST_ADMIN_CONNECTION e ODCA_TEST_APP_CONNECTION.");
        }

        using var document = JsonDocument.Parse(File.ReadAllText(runtimePath));
        var connections = document.RootElement.GetProperty("ConnectionStrings");
        return (
            connections.GetProperty("DatabaseAdmin").GetString()!,
            connections.GetProperty("Database").GetString()!);
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
