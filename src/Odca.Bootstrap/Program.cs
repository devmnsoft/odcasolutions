using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Odca.Configuration;
using Dapper;
using Npgsql;
using Odca.Bootstrap;
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
                {
                    var postgresDevelopment = args.Contains("--postgres-development", StringComparer.OrdinalIgnoreCase);
                    Initialize(paths, postgresDevelopment);
                    Console.WriteLine("Configuração local criada ou preservada.");
                    Console.WriteLine(postgresDevelopment
                        ? "Conexão PostgreSQL de Development preservada; nenhuma operação foi executada no banco."
                        : "Use Docker ou execute configure-native para apontar ao PostgreSQL local.");
                    return 0;
                }
                case "diagnose":
                    return await DiagnoseAsync(paths, args.Contains("--connection", StringComparer.OrdinalIgnoreCase)) ? 0 : 1;
                case "repair":
                    Repair(paths, args.Contains("--replace-invalid-signing-key", StringComparer.OrdinalIgnoreCase));
                    return 0;
                case "configure-native":
                    await ConfigureNativeAsync(paths);
                    Console.WriteLine("PostgreSQL nativo configurado. Execute migrate para aplicar o schema.");
                    return 0;
                case "migrate":
                    await MigrateAsync(paths);
                    Console.WriteLine("Migração aplicada e seed de Development verificado.");
                    return 0;
                case "show-login":
                    await ShowLoginAsync(paths);
                    return 0;
                case "reset-password":
                    await ResetPasswordAsync(paths);
                    Console.WriteLine("Senha inicial redefinida. Execute show-login para recuperá-la localmente.");
                    return 0;
                case "provision-test-access":
                    await ProvisionTestAccessAsync(paths, args);
                    return 0;
                default:
                    Console.WriteLine("Uso: dotnet run --project src/Odca.Bootstrap -- <init [--postgres-development]|diagnose [--connection]|repair [--replace-invalid-signing-key]|configure-native|migrate|show-login|reset-password|provision-test-access>");
                    return command == "help" ? 0 : 2;
            }
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine($"Bootstrap falhou: {exception.Message}");
            return 1;
        }
    }

    private static async Task<bool> DiagnoseAsync(LocalPaths paths, bool testConnection)
    {
        Console.WriteLine($"Ambiente=Development; caminho={paths.RuntimeFile}");
        if (!File.Exists(paths.RuntimeFile))
        {
            Console.WriteLine("Arquivo não encontrado.");
            return false;
        }

        JsonObject root;
        try { root = ReadObject(paths.RuntimeFile); }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException or InvalidDataException)
        {
            Console.WriteLine($"Arquivo ilegível ou JSON inválido: {exception.Message}");
            return false;
        }

        var issues = RuntimeIssues(root);
        if (issues.Count != 0)
        {
            Console.WriteLine($"Propriedades ausentes/inválidas: {string.Join(", ", issues)}");
            return false;
        }

        Console.WriteLine("Configuração sintaticamente válida; conectividade não testada.");
        if (!testConnection) return true;

        var connectionString = root["ConnectionStrings"]?["Database"]!.GetValue<string>()!;
        var target = new NpgsqlConnectionStringBuilder(connectionString);
        Console.WriteLine($"Teste: ambiente=Development; caminho={paths.RuntimeFile}; host={target.Host}; porta={target.Port}; banco={target.Database}; schema=odca");
        try
        {
            await using var connection = new NpgsqlConnection(connectionString);
            await connection.OpenAsync();
            var schemaExists = await connection.ExecuteScalarAsync<bool>("SELECT to_regnamespace('odca') IS NOT NULL;");
            if (!schemaExists)
            {
                Console.WriteLine("Conexão aprovada; schema odca ausente.");
                return true;
            }
            var version = await connection.ExecuteScalarAsync<int?>("SELECT max(version) FROM odca.schema_migrations;");
            Console.WriteLine(version == 9
                ? "Conexão aprovada; schema odca compatível (versão 009)."
                : $"Conexão aprovada; schema odca incompatível (versão encontrada: {version?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "nenhuma"}; esperada: 009).");
        }
        catch (Exception exception) when (exception is NpgsqlException or TimeoutException)
        {
            Console.WriteLine($"Conexão recusada ou indisponível: {exception.Message}");
            throw;
        }
        return true;
    }

    private static void Repair(LocalPaths paths, bool replaceInvalidSigningKey)
    {
        Directory.CreateDirectory(paths.LocalDirectory);
        if (!File.Exists(paths.RuntimeFile))
        {
            throw new InvalidOperationException("Arquivo não encontrado. Execute init para criar uma configuração nova.");
        }

        var root = ReadObject(paths.RuntimeFile);
        var connections = root["ConnectionStrings"] as JsonObject ?? new JsonObject();
        root["ConnectionStrings"] = connections;
        if (string.IsNullOrWhiteSpace(connections["DatabaseAdmin"]?.GetValue<string>()))
        {
            var database = connections["Database"]?.GetValue<string>();
            if (!string.IsNullOrWhiteSpace(database)) connections["DatabaseAdmin"] = database;
        }

        var jwt = root["Jwt"] as JsonObject ?? new JsonObject();
        root["Jwt"] = jwt;
        SetMissing(jwt, "Issuer", "odca-api");
        SetMissing(jwt, "Audience", "odca-bff");
        if (jwt["AccessTokenMinutes"] is null) jwt["AccessTokenMinutes"] = 15;

        var signingKey = jwt["SigningKey"]?.GetValue<string>();
        if (string.IsNullOrWhiteSpace(signingKey))
        {
            jwt["SigningKey"] = GenerateSecret(48);
        }
        else if (Encoding.UTF8.GetByteCount(signingKey) < 32)
        {
            if (!replaceInvalidSigningKey)
            {
                throw new InvalidOperationException(
                    "Jwt:SigningKey existente tem tamanho insuficiente. Use --replace-invalid-signing-key explicitamente; a troca invalidará todos os tokens já emitidos.");
            }
            jwt["SigningKey"] = GenerateSecret(48);
            Console.WriteLine("Jwt:SigningKey substituída; tokens emitidos anteriormente foram invalidados.");
        }

        var dataProtection = root["DataProtection"] as JsonObject ?? new JsonObject();
        root["DataProtection"] = dataProtection;
        SetMissing(dataProtection, "KeysPath", Path.Combine(paths.LocalDirectory, "data-protection-keys"));
        SetMissing(dataProtection, "ApplicationName", "ODCA Solutions");

        var security = root["Security"] as JsonObject ?? new JsonObject();
        root["Security"] = security;
        if (security["MfaRequiredForSuperAdmin"] is null) security["MfaRequiredForSuperAdmin"] = false;
        if (security["AllowDevelopmentBootstrap"] is null) security["AllowDevelopmentBootstrap"] = true;

        var backup = paths.RuntimeFile + ".backup-" + DateTime.UtcNow.ToString("yyyyMMddHHmmssfff", System.Globalization.CultureInfo.InvariantCulture);
        File.Copy(paths.RuntimeFile, backup, overwrite: false);
        ProtectFile(backup);
        WriteJsonAtomic(paths.RuntimeFile, root);
        Console.WriteLine($"Backup protegido: {backup}");
        Console.WriteLine("Reparo concluído sem alterar conexão, credenciais, banco ou migrações.");
        if (!DiagnoseAsync(paths, false).GetAwaiter().GetResult())
        {
            throw new InvalidOperationException("O reparo terminou, mas a configuração ainda é inválida.");
        }
    }

    private static void SetMissing(JsonObject value, string name, string defaultValue)
    {
        if (value[name] is null || string.IsNullOrWhiteSpace(value[name]!.GetValue<string>())) value[name] = defaultValue;
    }

    private static List<string> RuntimeIssues(JsonObject root)
    {
        var issues = new List<string>();
        if (string.IsNullOrWhiteSpace(root["ConnectionStrings"]?["Database"]?.GetValue<string>())) issues.Add("ConnectionStrings:Database (ausente)");
        if (string.IsNullOrWhiteSpace(root["ConnectionStrings"]?["DatabaseAdmin"]?.GetValue<string>())) issues.Add("ConnectionStrings:DatabaseAdmin (ausente)");
        if (string.IsNullOrWhiteSpace(root["Jwt"]?["Issuer"]?.GetValue<string>())) issues.Add("Jwt:Issuer (ausente)");
        if (string.IsNullOrWhiteSpace(root["Jwt"]?["Audience"]?.GetValue<string>())) issues.Add("Jwt:Audience (ausente)");
        var accessTokenMinutes = root["Jwt"]?["AccessTokenMinutes"]?.GetValue<int>();
        if (accessTokenMinutes is < 5 or > 30 or null) issues.Add("Jwt:AccessTokenMinutes (ausente/inválido)");
        var key = root["Jwt"]?["SigningKey"]?.GetValue<string>();
        if (string.IsNullOrWhiteSpace(key)) issues.Add("Jwt:SigningKey (ausente)");
        else if (Encoding.UTF8.GetByteCount(key) < 32) issues.Add("Jwt:SigningKey (tamanho insuficiente)");
        if (string.IsNullOrWhiteSpace(root["DataProtection"]?["KeysPath"]?.GetValue<string>())) issues.Add("DataProtection:KeysPath (ausente)");
        if (string.IsNullOrWhiteSpace(root["DataProtection"]?["ApplicationName"]?.GetValue<string>())) issues.Add("DataProtection:ApplicationName (ausente)");
        return issues;
    }

    private static JsonObject ReadObject(string path) => JsonNode.Parse(File.ReadAllText(path)) as JsonObject
        ?? throw new InvalidDataException("A raiz da configuração deve ser um objeto JSON.");

    private static void WriteJsonAtomic(string path, JsonObject root)
    {
        var temporary = path + ".tmp-" + Guid.NewGuid().ToString("N");
        File.WriteAllText(temporary, root.ToJsonString(JsonOptions));
        ProtectFile(temporary);
        File.Move(temporary, path, overwrite: true);
        ProtectFile(path);
    }

    private static void ProtectFile(string path)
    {
        if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite);
    }

    private static async Task ConfigureNativeAsync(LocalPaths paths)
    {
        Initialize(paths);
        var adminConnection = Environment.GetEnvironmentVariable("ODCA_NATIVE_ADMIN_CONNECTION");
        if (string.IsNullOrWhiteSpace(adminConnection))
        {
            throw new InvalidOperationException(
                "Defina ODCA_NATIVE_ADMIN_CONNECTION somente nesta sessão com a conexão administrativa do banco ODCA.");
        }

        var adminBuilder = new NpgsqlConnectionStringBuilder(adminConnection) { IncludeErrorDetail = false };
        if (string.IsNullOrWhiteSpace(adminBuilder.Password))
        {
            adminBuilder.Password = Environment.GetEnvironmentVariable("ODCA_NATIVE_PASSWORD")
                ?? ReadSecret("Senha administrativa do PostgreSQL: ");
        }
        if (string.IsNullOrWhiteSpace(adminBuilder.Host) || string.IsNullOrWhiteSpace(adminBuilder.Database))
        {
            throw new InvalidOperationException("A conexão nativa precisa informar Host e Database.");
        }

        await using (var connection = new NpgsqlConnection(adminBuilder.ConnectionString))
        {
            await connection.OpenAsync();
            await using var command = new NpgsqlCommand("SHOW server_version_num;", connection);
            var version = Convert.ToInt32(await command.ExecuteScalarAsync(), System.Globalization.CultureInfo.InvariantCulture);
            if (version < 180000)
            {
                throw new InvalidOperationException("ODCA requer PostgreSQL 18 ou superior.");
            }
        }

        var applicationConnection = Environment.GetEnvironmentVariable("ODCA_NATIVE_APPLICATION_CONNECTION");
        if (string.IsNullOrWhiteSpace(applicationConnection))
        {
            throw new InvalidOperationException("Defina ODCA_NATIVE_APPLICATION_CONNECTION explicitamente; configure-native não troca a conexão da aplicação por outra role.");
        }
        var appBuilder = new NpgsqlConnectionStringBuilder(applicationConnection) { IncludeErrorDetail = false };
        if (string.IsNullOrWhiteSpace(appBuilder.Password)) appBuilder.Password = adminBuilder.Password;
        var root = ReadObject(paths.RuntimeFile);
        var connections = root["ConnectionStrings"] as JsonObject ?? new JsonObject();
        root["ConnectionStrings"] = connections;
        connections["DatabaseAdmin"] = adminBuilder.ConnectionString;
        connections["Database"] = appBuilder.ConnectionString;
        WriteJsonAtomic(paths.RuntimeFile, root);
    }

    private static string ReadSecret(string prompt)
    {
        if (Console.IsInputRedirected)
        {
            throw new InvalidOperationException(
                "A conexão não contém Password e o terminal não permite leitura segura interativa.");
        }

        Console.Write(prompt);
        var value = new StringBuilder();
        while (true)
        {
            var key = Console.ReadKey(intercept: true);
            if (key.Key == ConsoleKey.Enter)
            {
                Console.WriteLine();
                return value.Length > 0
                    ? value.ToString()
                    : throw new InvalidOperationException("A senha administrativa não foi informada.");
            }

            if (key.Key == ConsoleKey.Backspace)
            {
                if (value.Length > 0)
                {
                    value.Length--;
                }

                continue;
            }

            if (!char.IsControl(key.KeyChar))
            {
                value.Append(key.KeyChar);
            }
        }
    }

    private static void Initialize(LocalPaths paths, bool postgresDevelopment = false)
    {
        Directory.CreateDirectory(paths.LocalDirectory);
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(paths.RuntimeFile))!);

        // Preserve an existing local setup when moving the runtime file into the API project.
        var legacyRuntime = Path.Combine(paths.LocalDirectory, "development-runtime.json");
        if (!File.Exists(paths.RuntimeFile) && File.Exists(legacyRuntime) &&
            Environment.GetEnvironmentVariable(LocalRuntimeConfiguration.EnvironmentVariable) is null)
        {
            File.Copy(legacyRuntime, paths.RuntimeFile, overwrite: false);
            ProtectFile(paths.RuntimeFile);
        }

        if (!File.Exists(paths.CredentialsFile))
        {
            var credentials = new DevelopmentCredentials(
                DefaultEmail,
                GeneratePassword(),
                GenerateSecret(24),
                GenerateSecret(24));
            WriteJsonNew(paths.CredentialsFile, credentials);
        }

        var stored = ReadJson<DevelopmentCredentials>(paths.CredentialsFile);
        if (!File.Exists(paths.RuntimeFile))
        {
            var postgresPassword = postgresDevelopment
                ? Environment.GetEnvironmentVariable("ODCA_LOCAL_POSTGRES_PASSWORD")
                : null;
            if (postgresDevelopment && string.IsNullOrWhiteSpace(postgresPassword))
            {
                throw new InvalidOperationException(
                    "Defina ODCA_LOCAL_POSTGRES_PASSWORD somente durante o init --postgres-development. A senha não será registrada em logs.");
            }

            var adminConnection = postgresDevelopment
                ? BuildPostgresDevelopmentConnectionString(postgresPassword!)
                : BuildConnectionString("postgres", stored.PostgresAdminPassword);
            var applicationConnection = postgresDevelopment
                ? adminConnection
                : BuildConnectionString("odca_app_login", stored.ApplicationDatabasePassword);
            var runtime = new DevelopmentRuntime(
                new ConnectionStrings(adminConnection, applicationConnection),
                new JwtSettings("odca-api", "odca-bff", GenerateSecret(48), 15),
                new SecuritySettings(false, true),
                new DataProtectionSettings(Path.Combine(paths.LocalDirectory, "data-protection-keys"), "ODCA Solutions"));
            WriteJsonNew(paths.RuntimeFile, runtime);
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

    private static async Task ShowLoginAsync(LocalPaths paths)
    {
        EnsureInitialized(paths);
        var credentials = ReadJson<DevelopmentCredentials>(paths.CredentialsFile);
        var runtime = ReadJson<DevelopmentRuntime>(paths.RuntimeFile);
        await ShowCredentialAsync(runtime.ConnectionStrings.DatabaseAdmin, "Superadministrador",
            credentials.SuperAdministratorEmail, credentials.SuperAdministratorPassword);
        if (!string.IsNullOrWhiteSpace(credentials.ClientPassword))
        {
            await ShowCredentialAsync(runtime.ConnectionStrings.DatabaseAdmin, "Cliente de demonstração",
                TestAccessProvisioner.ClientEmail, credentials.ClientPassword);
        }
    }

    private static async Task ShowCredentialAsync(string connectionString, string label, string email, string password)
    {
        const string sql = """
            SELECT id AS Id, email AS Email, display_name AS DisplayName, password_hash AS PasswordHash,
                   security_version AS SecurityVersion, is_platform_administrator AS IsPlatformAdministrator,
                   must_change_password AS MustChangePassword, locked_until AS LockedUntil,
                   is_deleted AS IsDeleted, last_login_at AS LastLoginAt
              FROM odca.users WHERE email_normalized=@email;
            """;
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        var user = await connection.QuerySingleOrDefaultAsync<LoginVerification>(sql, new { email = email.Trim().ToUpperInvariant() });
        Console.WriteLine($"{label}: {email}");
        if (user is null)
        {
            Console.WriteLine("Senha local não exibida: a identidade não foi encontrada no banco configurado.");
            return;
        }

        var matches = new AspNetPasswordService().Verify(
            new UserCredential(user.Id, user.Email, user.DisplayName, user.PasswordHash, user.SecurityVersion,
                user.MustChangePassword, user.IsPlatformAdministrator, null, user.IsDeleted, ToUtc(user.LockedUntil)),
            user.PasswordHash, password);
        if (!matches)
        {
            Console.WriteLine("Senha local não exibida: o arquivo está desatualizado em relação ao hash persistido.");
            Console.WriteLine("Use reset-password ou provision-test-access --rotate-passwords explicitamente.");
            return;
        }

        Console.WriteLine($"Senha inicial local (conferida com o banco): {password}");
        Console.WriteLine($"Perfil: {(user.IsPlatformAdministrator ? "SuperAdministrador" : "Administrador da organização")}");
        Console.WriteLine($"Troca inicial obrigatória: {(user.MustChangePassword ? "sim" : "não")}");
    }

    private static async Task ProvisionTestAccessAsync(LocalPaths paths, string[] args)
    {
        EnsureInitialized(paths);
        var environmentIndex = Array.FindIndex(args, value => value.Equals("--environment", StringComparison.OrdinalIgnoreCase));
        var environment = environmentIndex >= 0 && environmentIndex + 1 < args.Length ? args[environmentIndex + 1] : null;
        if (!string.Equals(environment, "Development", StringComparison.Ordinal))
        {
            throw new InvalidOperationException("Informe --environment Development. A concessão de demonstração é recusada fora de Development.");
        }

        var rotate = args.Contains("--rotate-passwords", StringComparer.OrdinalIgnoreCase);
        var runtime = ReadJson<DevelopmentRuntime>(paths.RuntimeFile);
        if (!runtime.Security.AllowDevelopmentBootstrap)
        {
            throw new InvalidOperationException("Security:AllowDevelopmentBootstrap está desabilitado.");
        }

        var target = new NpgsqlConnectionStringBuilder(runtime.ConnectionStrings.DatabaseAdmin);
        if (!IsProvisionTargetAllowed(environment!, target.Database,
                args.Contains("--allow-postgres-development", StringComparer.OrdinalIgnoreCase)))
        {
            throw new InvalidOperationException("Provisionamento na base postgres exige --environment Development e --allow-postgres-development explícitos.");
        }

        Console.WriteLine($"Destino: host={target.Host}; porta={target.Port}; banco={target.Database}; ambiente=Development");
        var credentials = ReadJson<DevelopmentCredentials>(paths.CredentialsFile);
        var provisioner = new TestAccessProvisioner(new AspNetPasswordService());
        var result = await provisioner.ProvisionAsync(runtime.ConnectionStrings.DatabaseAdmin,
            credentials.SuperAdministratorEmail, credentials.SuperAdministratorPassword,
            credentials.ClientPassword, rotate, GeneratePassword);

        var updated = credentials with
        {
            SuperAdministratorPassword = result.AdministratorPassword,
            ClientPassword = result.ClientPassword
        };
        try
        {
            WriteJson(paths.CredentialsFile, updated);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            throw new InvalidOperationException(
                "O banco foi confirmado, mas o arquivo local de credenciais não pôde ser atualizado. " +
                "Não considere a recuperação local da senha concluída.", exception);
        }

        Console.WriteLine($"Persistência verificada: {(AllVerified(result.Verification) ? "sim" : "não")}");
        Console.WriteLine($"Senha do superadministrador confere: {(result.AdministratorPasswordMatches ? "sim" : "não (arquivo local desatualizado; use --rotate-passwords)")}");
        Console.WriteLine($"Senha do cliente confere: {(result.ClientPasswordMatches ? "sim" : "não (arquivo local ausente/desatualizado; use --rotate-passwords)")}");
        Console.WriteLine("Login HTTP aprovado: não executado por este comando.");
        Console.WriteLine("MFA: superadministrador deve concluir troca inicial e inscrição/desafio; configuração existente foi preservada.");
        Console.WriteLine("Execute show-login para exibir somente as senhas locais que conferem com o banco.");
    }

    private static bool AllVerified(TestAccessVerification value) => value.AdministratorPersisted &&
        value.ClientPersisted && value.MembershipActive && value.TenantAdministrator && value.BasicPlanActive;

    public static bool IsProvisionTargetAllowed(string environment, string? database, bool allowPostgresDevelopment) =>
        !string.Equals(database, "postgres", StringComparison.OrdinalIgnoreCase) ||
        (string.Equals(environment, "Development", StringComparison.Ordinal) && allowPostgresDevelopment);

    private static DateTimeOffset? ToUtc(DateTime? value) => value is null
        ? null
        : new DateTimeOffset(DateTime.SpecifyKind(value.Value, DateTimeKind.Utc));

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
            null,
            false,
            null);
        var hash = new AspNetPasswordService().Hash(template, newPassword);

        const string updateUserSql = """
            UPDATE odca.users
               SET password_hash = @hash,
                   must_change_password = true,
                   security_version = security_version + 1,
                   failed_login_count = 0,
                   locked_until = NULL,
                   updated_at = now()
             WHERE email_normalized = @email;
            """;
        const string revokeSessionsSql = """
            UPDATE odca.sessions
               SET revoked_at = COALESCE(revoked_at, now())
             WHERE user_id = (SELECT id FROM odca.users WHERE email_normalized = @email);
            """;
        await using var connection = new NpgsqlConnection(runtime.ConnectionStrings.DatabaseAdmin);
        await connection.OpenAsync();
        await using var transaction = await connection.BeginTransactionAsync();
        var parameters = new
        {
            hash,
            email = credentials.SuperAdministratorEmail.Trim().ToUpperInvariant()
        };
        var userAffected = await connection.ExecuteAsync(updateUserSql, parameters, transaction);
        if (userAffected != 1)
        {
            throw new InvalidOperationException("A conta superadministradora ainda não existe; execute migrate.");
        }

        await connection.ExecuteAsync(revokeSessionsSql, parameters, transaction);
        await transaction.CommitAsync();

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

    private static string BuildPostgresDevelopmentConnectionString(string password) =>
        new NpgsqlConnectionStringBuilder
        {
            Host = "localhost",
            Port = 5432,
            Database = "postgres",
            Username = "postgres",
            Password = password,
            Pooling = true,
            MaxPoolSize = 50,
            MinPoolSize = 0,
            Timeout = 30,
            CommandTimeout = 60,
            SearchPath = "odca",
            ApplicationName = "odca.api",
            IncludeErrorDetail = false
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
        while (current is not null && !File.Exists(Path.Combine(current.FullName, "Odca.sln")))
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

    private static void WriteJson<T>(string path, T value)
    {
        File.WriteAllText(path, JsonSerializer.Serialize(value, JsonOptions));
        if (!OperatingSystem.IsWindows())
        {
            File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite);
        }
    }

    private static void WriteJsonNew<T>(string path, T value)
    {
        var temporary = path + ".tmp-" + Guid.NewGuid().ToString("N");
        try
        {
            File.WriteAllText(temporary, JsonSerializer.Serialize(value, JsonOptions));
            ProtectFile(temporary);
            try
            {
                File.Move(temporary, path, overwrite: false);
            }
            catch (IOException) when (File.Exists(path))
            {
                // Another setup won the race. Preserve its complete file rather than replacing it.
                return;
            }
            ProtectFile(path);
        }
        finally
        {
            if (File.Exists(temporary)) File.Delete(temporary);
        }
    }

    private sealed record LocalPaths(
        string RepositoryRoot,
        string LocalDirectory,
        string RuntimeFile,
        string CredentialsFile,
        string EnvironmentFile)
    {
        public static LocalPaths For(string repositoryRoot)
        {
            var runtimeOverride = Environment.GetEnvironmentVariable(LocalRuntimeConfiguration.EnvironmentVariable);
            if (runtimeOverride is not null && string.IsNullOrWhiteSpace(runtimeOverride))
            {
                throw new InvalidOperationException($"{LocalRuntimeConfiguration.EnvironmentVariable} foi definida com um caminho vazio.");
            }
            var localDirectory = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "ODCA Solutions");
            var runtimeFile = Path.GetFullPath(runtimeOverride ?? LocalRuntimeConfiguration.GetDefaultPath(repositoryRoot));
            return new(
                repositoryRoot,
                localDirectory,
                runtimeFile,
                Path.Combine(localDirectory, "development-credentials.json"),
                Path.Combine(repositoryRoot, ".env.local"));
        }
    }

    private sealed record DevelopmentCredentials(
        string SuperAdministratorEmail,
        string SuperAdministratorPassword,
        string PostgresAdminPassword,
        string ApplicationDatabasePassword,
        string? ClientPassword = null);

    private sealed record LoginVerification(Guid Id, string Email, string DisplayName, string PasswordHash,
        int SecurityVersion, bool IsPlatformAdministrator, bool MustChangePassword,
        DateTime? LockedUntil, bool IsDeleted, DateTime? LastLoginAt);

    private sealed record DevelopmentRuntime(
        ConnectionStrings ConnectionStrings,
        JwtSettings Jwt,
        SecuritySettings Security,
        DataProtectionSettings? DataProtection = null);

    private sealed record ConnectionStrings(string DatabaseAdmin, string Database);

    private sealed record JwtSettings(string Issuer, string Audience, string SigningKey, int AccessTokenMinutes);

    private sealed record SecuritySettings(bool MfaRequiredForSuperAdmin, bool AllowDevelopmentBootstrap);

    private sealed record DataProtectionSettings(string KeysPath, string ApplicationName);
}
#pragma warning restore CA1050
