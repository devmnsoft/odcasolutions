using Dapper;
using Npgsql;
using Odca.Application.Identity;

namespace Odca.Infrastructure.Database;

public sealed class DevelopmentSeeder(IPasswordService passwordService)
{
    public async Task SeedSuperAdministratorAsync(
        string adminConnectionString,
        string email,
        string password,
        CancellationToken cancellationToken = default)
    {
        var normalizedEmail = email.Trim().ToUpperInvariant();
        var template = new UserCredential(
            Guid.NewGuid(),
            email.Trim(),
            "Administrador da plataforma",
            string.Empty,
            1,
            true,
            true,
            null,
            false,
            null);
        var hash = passwordService.Hash(template, password);

        const string sql = """
            INSERT INTO odca.users
                (id, email, email_normalized, login_normalized, display_name, password_hash,
                 must_change_password, is_platform_administrator, email_verified_at)
            VALUES
                (@Id, @Email, @NormalizedEmail, @NormalizedEmail, @DisplayName, @Hash,
                 true, true, now())
            ON CONFLICT (email_normalized) DO NOTHING;
            """;

        await using var connection = new NpgsqlConnection(adminConnectionString);
        await connection.OpenAsync(cancellationToken);
        await connection.ExecuteAsync(new CommandDefinition(
            sql,
            new
            {
                template.Id,
                template.Email,
                NormalizedEmail = normalizedEmail,
                template.DisplayName,
                Hash = hash
            },
            cancellationToken: cancellationToken));
    }
}
