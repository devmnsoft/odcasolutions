using Dapper;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Npgsql;
using Odca.Application.Contracts;

namespace Odca.Api;

/// <summary>
/// Bloco A (decisão D-OC1): mantém o catálogo oficial publicado como linhas
/// globais (owner_tenant_id NULL, scope='global') sob gestão da plataforma.
/// O cliente consulta modelos publicados e cria minutas/copias próprias; ele
/// não carrega modelos. Idempotente e auto-corretivo a cada inicialização da
/// API: modelos ausentes do catálogo global são repostos com conteúdo do código
/// (mesma fonte de OfficialContractTemplates usada pela instalação por tenant),
/// preservando cópias particulares já existentes.
/// </summary>
public sealed partial class OfficialCatalogSeedingService(
    NpgsqlDataSource dataSource,
    ILogger<OfficialCatalogSeedingService> logger) : IHostedService
{
    // Autor estável da plataforma (TestAccessProvisioner.AdministratorId).
    private static readonly Guid PlatformAuthorId = Guid.Parse("10000000-0000-4000-8000-000000000001");

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        for (var attempt = 1; attempt <= 5; attempt++)
        {
            try
            {
                await SeedAsync(cancellationToken);
                return;
            }
            catch (Exception exception) when (attempt < 5)
            {
                LogCatalogRetry(logger, exception, attempt);
                try
                {
                    await Task.Delay(TimeSpan.FromSeconds(2), cancellationToken);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
            }
        }

        LogCatalogNotCompleted(logger);
    }

    private async Task SeedAsync(CancellationToken cancellationToken)
    {
        OfficialContractTemplates.EnsureValid();
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        var authorExists = await connection.ExecuteScalarAsync<bool>(new CommandDefinition(
            "SELECT EXISTS(SELECT 1 FROM odca.users WHERE id=@id)",
            new { id = PlatformAuthorId },
            cancellationToken: cancellationToken));
        if (!authorExists)
        {
            LogAuthorMissing(logger);
            return;
        }

        await using var tx = await connection.BeginTransactionAsync(cancellationToken);
        var seeded = 0;
        foreach (var template in OfficialContractTemplates.All)
        {
            var exists = await connection.ExecuteScalarAsync<bool>(new CommandDefinition(
                """
                SELECT EXISTS(
                    SELECT 1 FROM odca.contract_templates t
                     WHERE t.official_key = @key AND t.owner_tenant_id IS NULL
                       AND t.scope = 'global' AND t.status <> 'archived')
                """,
                new { key = template.Key },
                tx,
                cancellationToken: cancellationToken));
            if (exists) continue;

            var id = Guid.NewGuid();
            var versionId = Guid.NewGuid();
            var fields = OfficialContractTemplates.SerializeFields(template.Fields);
            await connection.ExecuteAsync(new CommandDefinition("""
                INSERT INTO odca.contract_templates(id,owner_tenant_id,name,description,contract_type,scope,status,author_id,published_at,official_key,official_revision,document_purpose)
                VALUES(@id,NULL,@name,@description,@contractType,'global','published',@author,now(),@key,1,@purpose);
                INSERT INTO odca.contract_template_versions(id,template_id,version_number,content,fields,created_by,published_at)
                VALUES(@versionId,@id,1,@content::jsonb,@fields::jsonb,@author,now());
                INSERT INTO odca.audit_events(scope_type,tenant_id,actor_user_id,action,entity_type,entity_id,result,metadata)
                VALUES('platform',NULL,@author,'template.official_catalog_seeded','contract_template',@id,'success',jsonb_build_object('key',@key));
                """,
                new
                {
                    id,
                    author = PlatformAuthorId,
                    name = template.Name,
                    template.Description,
                    template.ContractType,
                    versionId,
                    content = template.Content,
                    fields,
                    key = template.Key,
                    purpose = OfficialContractTemplates.PurposeFor(template)
                },
                tx,
                cancellationToken: cancellationToken));
            seeded++;
        }

        await tx.CommitAsync(cancellationToken);
        if (seeded > 0)
        {
            LogCatalogSeeded(logger, seeded);
        }
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    // EventIds 2201–2204 reserved for the official catalog seeder.
    [LoggerMessage(EventId = 2201, Level = LogLevel.Warning, Message = "Catálogo oficial: banco indisponível na tentativa {Attempt}; nova tentativa em 2 s.", SkipEnabledCheck = true)]
    private static partial void LogCatalogRetry(ILogger logger, Exception exception, int attempt);

    [LoggerMessage(EventId = 2202, Level = LogLevel.Warning, Message = "Catálogo oficial: seeding não concluído nesta inicialização; será repetido no próximo start.")]
    private static partial void LogCatalogNotCompleted(ILogger logger);

    [LoggerMessage(EventId = 2203, Level = LogLevel.Warning, Message = "Catálogo oficial: autor de plataforma não localizado; seeding adiado.")]
    private static partial void LogAuthorMissing(ILogger logger);

    [LoggerMessage(EventId = 2204, Level = LogLevel.Information, Message = "Catálogo oficial: {Count} modelo(s) global(is) publicado(s).")]
    private static partial void LogCatalogSeeded(ILogger logger, int count);
}
