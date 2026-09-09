using Dapper;
using Npgsql;
using Odca.Application.Privacy;

namespace Odca.Infrastructure.Privacy;

public sealed class NpgsqlPrivacyRequestRepository(NpgsqlDataSource dataSource) : IPrivacyRequestRepository
{
    public async Task InsertAsync(PrivacyRequestSubmission submission, CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT odca.submit_privacy_request(
                @Id,
                @Protocol::char(24),
                @Email,
                @RequestType,
                @Details::varchar(2000));
            """;
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await connection.ExecuteAsync(new CommandDefinition(
            sql,
            submission,
            cancellationToken: cancellationToken));
    }
}
