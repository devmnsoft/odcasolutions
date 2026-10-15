using System.Xml.Linq;
using Dapper;
using Microsoft.AspNetCore.DataProtection.Repositories;
using Npgsql;

namespace Odca.Infrastructure.Distributed;

/// <summary>
/// D-OC31 A.4: repositório de chaves de Data Protection sobre PostgreSQL
/// (<c>odca.data_protection_keys</c>, v054). As instâncias de API/BFF/worker
/// compartilham as mesmas chaves sem depender de filesystem compartilhado;
/// registrar via <c>PersistKeysToXmlRepository</c> (ver DistributedWiring).
/// </summary>
public sealed class PostgresKeysRepository(NpgsqlDataSource dataSource) : IXmlRepository
{
    public IReadOnlyCollection<XElement> GetAllElements()
    {
        using var connection = dataSource.OpenConnection();
        var payloads = connection.Query<string>(
            "SELECT xml_payload FROM odca.data_protection_keys WHERE deleted_utc IS NULL ORDER BY created");
        return payloads
            .Select(payload => XDocument.Parse(payload).Root!)
            .ToList();
    }

    public void StoreElement(XElement element, string friendlyName)
    {
        using var connection = dataSource.OpenConnection();
        connection.Execute(
            """
            INSERT INTO odca.data_protection_keys(xml_payload, friendly_name)
            VALUES(@xml, @friendlyName)
            """,
            new { xml = element.ToString(SaveOptions.DisableFormatting), friendlyName = string.IsNullOrWhiteSpace(friendlyName) ? "postgres-shared-keys" : friendlyName });
    }
}
