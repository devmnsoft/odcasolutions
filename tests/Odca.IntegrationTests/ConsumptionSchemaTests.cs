namespace Odca.IntegrationTests;

public sealed class ConsumptionSchemaTests
{
    private static readonly string Sql = File.ReadAllText(FindSql());

    [Fact]
    public void ApprovalIsSerializedAndGrantIsIdempotent()
    {
        Assert.Contains("FROM odca.additional_storage_requests WHERE tenant_id=requested_tenant AND id=request_id FOR UPDATE", Sql, StringComparison.Ordinal);
        Assert.Contains("UNIQUE(tenant_id,request_id)", Sql, StringComparison.Ordinal);
        Assert.Contains("ON CONFLICT(tenant_id,request_id) DO NOTHING", Sql, StringComparison.Ordinal);
    }

    [Fact]
    public void RequestsAndMovementsHaveTenantIdempotencyAndImmutableSnapshots()
    {
        Assert.Contains("package_name varchar(120) NOT NULL", Sql, StringComparison.Ordinal);
        Assert.Contains("terms_snapshot varchar(2000) NOT NULL", Sql, StringComparison.Ordinal);
        Assert.Contains("UNIQUE(tenant_id,idempotency_key)", Sql, StringComparison.Ordinal);
        Assert.Contains("CREATE TABLE odca.resource_movements", Sql, StringComparison.Ordinal);
    }

    [Fact]
    public void StorageCapacityAndConsumableCreditsAreSeparateResources()
    {
        Assert.Contains("resource_type IN('storage_capacity','storage_usage','signature_credit','ocr_credit')", Sql, StringComparison.Ordinal);
        Assert.Contains("movement_type IN('grant','reserve','consume','release','expire','reversal','adjustment')", Sql, StringComparison.Ordinal);
    }

    private static string FindSql()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null)
        {
            var path = Path.Combine(current.FullName, "database", "odca.sql");
            if (File.Exists(path)) return path;
            current = current.Parent;
        }
        throw new FileNotFoundException("database/odca.sql");
    }
}
