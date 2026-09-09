using System.Security.Cryptography;
using System.Text.Json;
using Dapper;
using Npgsql;
using Odca.Application.Identity;
using Odca.Infrastructure.Database;
using Odca.Infrastructure.Identity;

return await BootstrapProgram.RunAsync(args);

#pragma warning disable CA1050 // Console entry point type is intentionally global.
public static class BootstrapProgram
{
    private const string DefaultEmail = "admin@odca.local";
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public static async Task<int> RunAsync(string[] args)
    {
        try
        {
            var command = args.FirstOrDefault()?.ToLowerInvariant() ?? "help";
            var repositoryRoot = FindRepositoryRoot();
            var paths = LocalPaths.For(repositoryRoot);
            switch (command)
            {
                case "init":
                    Initialize(paths);
                    Console.WriteLine("Configuração local criada ou preservada.");
                    Console.WriteLine("Próximo passo: docker compose --env-file .env.local up -d");
                    return 0;
                case "migrate":
                    await MigrateAsync(paths);
                    Console.WriteLine("Migração aplicada e seed de Development verificado.");
                    return 0;
                case "show-login":
                    ShowLogin(paths);
                    return 0;
                case "reset-password":
                    await ResetPasswordAsync(paths);
                    Console.WriteLine("Senha inicial redefinida. Execute show-login para recuperá-la localmente.");
                    return 0;
                default:
                    Console.WriteLine("Uso: dotnet run --project src/Odca.Bootstrap -- <init|migrate|show-login|reset-password>");
                    return command == "help" ? 0 : 2;
            }
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine($"Bootstrap falhou: {exception.Message}");
            return 1;
        }
    }

    private static void Initialize(LocalPaths paths)
    {
        Directory.CreateDirectory(paths.LocalDirectory);

        if (!File.Exists(paths.CredentialsFile))
        {
            var credentials = new DevelopmentCredentials(
                DefaultEmail,
                GeneratePassword(),
                GenerateSecret(24),
                GenerateSecret(24));
            WriteJson(paths.CredentialsFile, credentials);
        }

        var stored = ReadJson<DevelopmentCredentials>(paths.CredentialsFile);
        if (!File.Exists(paths.RuntimeFile))
        {
            var runtime = new DevelopmentRuntime(
                new ConnectionStrings(
                    BuildConnectionString("postgres", stored.PostgresAdminPassword),
                    BuildConnectionString("odca_app_login", stored.ApplicationDatabasePassword)),
                new JwtSettings("odca-api", "odca-bff", GenerateSecret(48), 15),
                new SecuritySettings(false, true));
            WriteJson(paths.RuntimeFile, runtime);
        }

        if (!File.Exists(paths.EnvironmentFile))
        {
            File.WriteAllText(
                paths.EnvironmentFile,
                $"POSTGRES_DB=odca{Environment.NewLine}POSTGRES_USER=postgres{Environment.NewLine}POSTGRES_PASSWORD={stored.PostgresAdminPassword}{Environment.NewLine}");
        }
    }

    private static async Task MigrateAsync(LocalPaths paths)
    {
        EnsureInitialized(paths);
        var runtime = ReadJson<DevelopmentRuntime>(paths.RuntimeFile);
        var credentials = ReadJson<DevelopmentCredentials>(paths.CredentialsFile);
        var sqlPath = Path.Combine(paths.RepositoryRoot, "database", "odca.sql");

        await new DatabaseMigrator(sqlPath).ApplyAsync(runtime.ConnectionStrings.DatabaseAdmin);
        await DatabaseRoleProvisioner.ProvisionApplicationLoginAsync(
            runtime.ConnectionStrings.DatabaseAdmin,
            credentials.ApplicationDatabasePassword);
        await new DevelopmentSeeder(new AspNetPasswordService()).SeedSuperAdministratorAsync(
            runtime.ConnectionStrings.DatabaseAdmin,
            credentials.SuperAdministratorEmail,
            credentials.SuperAdministratorPassword);
    }

    private static void ShowLogin(LocalPaths paths)
    {
        EnsureInitialized(paths);
        var credentials = ReadJson<DevelopmentCredentials>(paths.CredentialsFile);
        Console.WriteLine($"Login: {credentials.SuperAdministratorEmail}");
        Console.WriteLine($"Senha inicial: {credentials.SuperAdministratorPassword}");
        Console.WriteLine("A aplicação exigirá a troca no primeiro acesso.");
    }

    private static async Task ResetPasswordAsync(LocalPaths paths)
    {
        EnsureInitialized(paths);
        var runtime = ReadJson<DevelopmentRuntime>(paths.RuntimeFile);
        var credentials = ReadJson<DevelopmentCredentials>(paths.CredentialsFile);
        var newPassword = GeneratePassword();
        var template = new UserCredential(
            Guid.Empty,
            credentials.SuperAdministratorEmail,
            "Administrador da plataforma",
            string.Empty,
            1,
            true,
            true,
            false,
            null);
        var hash = new AspNetPasswordService().Hash(template, newPassword);

        const string sql = """
            UPDATE odca.users
               SET password_hash = @hash,
                   must_change_password = true,
                   security_version = security_version + 1,
                   failed_login_count = 0,
                   locked_until = NULL,
                   updated_at = now()
             WHERE email_normalized = @email;
            UPDATE odca.sessions
               SET revoked_at = COALESCE(revoked_at, now())
             WHERE user_id = (SELECT id FROM odca.users WHERE email_normalized = @email);
            """;
        await using var connection = new NpgsqlConnection(runtime.ConnectionStrings.DatabaseAdmin);
        await connection.OpenAsync();
        var affected = await connection.ExecuteAsync(sql, new
        {
            hash,
            email = credentials.SuperAdministratorEmail.Trim().ToUpperInvariant()
        });
        if (affected == 0)
        {
            throw new InvalidOperationException("A conta superadministradora ainda não existe; execute migrate.");
        }

        WriteJson(paths.CredentialsFile, credentials with { SuperAdministratorPassword = newPassword });
    }

    private static string BuildConnectionString(string username, string password) =>
        new NpgsqlConnectionStringBuilder
        {
            Host = "localhost",
            Port = 55432,
            Database = "odca",
            Username = username,
            Password = password,
            IncludeErrorDetail = false,
            Pooling = true,
            Timeout = 5,
            CommandTimeout = 30
        }.ConnectionString;

    private static string GenerateSecret(int bytes) => Convert.ToBase64String(RandomNumberGenerator.GetBytes(bytes));

    private static string GeneratePassword()
    {
        const string lower = "abcdefghijkmnopqrstuvwxyz";
        const string upper = "ABCDEFGHJKLMNPQRSTUVWXYZ";
        const string digits = "23456789";
        const string symbols = "!@#$%*-_+";
        var characters = new List<char>
        {
            lower[RandomNumberGenerator.GetInt32(lower.Length)],
            upper[RandomNumberGenerator.GetInt32(upper.Length)],
            digits[RandomNumberGenerator.GetInt32(digits.Length)],
            symbols[RandomNumberGenerator.GetInt32(symbols.Length)]
        };
        var all = lower + upper + digits + symbols;
        while (characters.Count < 20)
        {
            characters.Add(all[RandomNumberGenerator.GetInt32(all.Length)]);
        }

        for (var index = characters.Count - 1; index > 0; index--)
        {
            var swap = RandomNumberGenerator.GetInt32(index + 1);
            (characters[index], characters[swap]) = (characters[swap], characters[index]);
        }

        return new string([.. characters]);
    }

    private static string FindRepositoryRoot()
    {
        var current = new DirectoryInfo(Directory.GetCurrentDirectory());
        while (current is not null && !File.Exists(Path.Combine(current.FullName, "Odca.slnx")))
        {
            current = current.Parent;
        }

        return current?.FullName
            ?? throw new DirectoryNotFoundException("Execute o bootstrap dentro do repositório ODCA Solutions.");
    }

    private static void EnsureInitialized(LocalPaths paths)
    {
        if (!File.Exists(paths.RuntimeFile) || !File.Exists(paths.CredentialsFile))
        {
            throw new InvalidOperationException("Execute o comando init primeiro.");
        }
    }

    private static T ReadJson<T>(string path) =>
        JsonSerializer.Deserialize<T>(File.ReadAllText(path), JsonOptions)
        ?? throw new InvalidDataException($"Configuração local inválida: {path}");

    private static void WriteJson<T>(string path, T value) =>
        File.WriteAllText(path, JsonSerializer.Serialize(value, JsonOptions));

    private sealed record LocalPaths(
        string RepositoryRoot,
        string LocalDirectory,
        string RuntimeFile,
        string CredentialsFile,
        string EnvironmentFile)
    {
        public static LocalPaths For(string repositoryRoot)
        {
            var localDirectory = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "ODCA Solutions");
            return new(
                repositoryRoot,
                localDirectory,
                Path.Combine(localDirectory, "development-runtime.json"),
                Path.Combine(localDirectory, "development-credentials.json"),
                Path.Combine(repositoryRoot, ".env.local"));
        }
    }

    private sealed record DevelopmentCredentials(
        string SuperAdministratorEmail,
        string SuperAdministratorPassword,
        string PostgresAdminPassword,
        string ApplicationDatabasePassword);

    private sealed record DevelopmentRuntime(
        ConnectionStrings ConnectionStrings,
        JwtSettings Jwt,
        SecuritySettings Security);

    private sealed record ConnectionStrings(string DatabaseAdmin, string Database);

    private sealed record JwtSettings(string Issuer, string Audience, string SigningKey, int AccessTokenMinutes);

    private sealed record SecuritySettings(bool MfaRequiredForSuperAdmin, bool AllowDevelopmentBootstrap);
}
#pragma warning restore CA1050
