namespace Odca.IntegrationTests;

public sealed class RuntimeQueryRegressionTests
{
    [Fact]
    public void SavedViewsUseProviderCompatibleMutableRowAndQuotedAliases()
    {
        var source = ReadSource("Odca.Api", "Controllers", "SavedViewsController.cs");

        Assert.Contains("private sealed class Row", source, StringComparison.Ordinal);
        Assert.Contains("public DateTime CreatedAt { get; set; }", source, StringComparison.Ordinal);
        Assert.Contains("created_at AS \"CreatedAt\"", source, StringComparison.Ordinal);
        Assert.Contains("filters::text AS \"Filters\"", source, StringComparison.Ordinal);
        Assert.Contains("row_version AS \"Version\"", source, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("@contractId::uuid")]
    [InlineData("@ownerId::uuid")]
    [InlineData("@category::text")]
    [InlineData("@status::text")]
    [InlineData("@from::date")]
    [InlineData("@to::date")]
    [InlineData("@search::text")]
    public void ObligationOptionalParametersHaveExplicitPostgresTypes(string parameter)
    {
        var source = ReadSource("Odca.Api", "Controllers", "ObligationsController.cs");

        Assert.Contains(parameter, source, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("@from::date")]
    [InlineData("@to::date")]
    [InlineData("@ownerId::uuid")]
    [InlineData("@contractType::text")]
    [InlineData("@counterparty::text")]
    [InlineData("@status::text")]
    public void RenewalOptionalParametersHaveExplicitPostgresTypes(string parameter)
    {
        var source = ReadSource("Odca.Api", "Controllers", "RenewalCenterController.cs");

        Assert.Contains(parameter, source, StringComparison.Ordinal);
    }

    [Fact]
    public void ConsumptionHistoryMapsNullableReasonAndProviderTimestampBeforePublicContract()
    {
        var source = ReadSource("Odca.Infrastructure", "Consumption", "NpgsqlConsumptionRepository.cs");

        Assert.Contains("QueryAsync<ConsumptionEventRow>", source, StringComparison.Ordinal);
        Assert.Contains("public string? Reason{get;init;}", source, StringComparison.Ordinal);
        Assert.Contains("public DateTime OccurredAt{get;init;}", source, StringComparison.Ordinal);
        Assert.Contains("m.occurred_at AS \"OccurredAt\"", source, StringComparison.Ordinal);
        Assert.Contains("new DateTimeOffset(row.OccurredAt)", source, StringComparison.Ordinal);
    }

    [Fact]
    public void PlatformDashboardMapsProviderTimestampAndNullSafeAliasedAuditFields()
    {
        var source = ReadSource("Odca.Infrastructure", "Dashboard", "NpgsqlPlatformDashboardRepository.cs");

        Assert.Contains("ReadAsync<PlatformAuditEventRow>", source, StringComparison.Ordinal);
        Assert.Contains("public DateTime OccurredAt { get; set; }", source, StringComparison.Ordinal);
        Assert.Contains("occurred_at AS \"OccurredAt\"", source, StringComparison.Ordinal);
        Assert.Contains("COALESCE(result, 'unknown') AS \"Result\"", source, StringComparison.Ordinal);
        Assert.Contains("COALESCE(actor_name, 'Sistema') AS \"ActorName\"", source, StringComparison.Ordinal);
        Assert.Contains("COALESCE(tenant_name, 'Plataforma') AS \"TenantName\"", source, StringComparison.Ordinal);
        Assert.Contains("new DateTimeOffset(row.OccurredAt)", source, StringComparison.Ordinal);
    }

    [Fact]
    public void PlatformCustomersUseExactAliasesCastsDefaultsAndProviderTimestampRow()
    {
        var source = ReadSource("Odca.Infrastructure", "Consumption", "NpgsqlConsumptionRepository.cs");

        Assert.Contains("QueryAsync<PlatformCustomerRow>", source, StringComparison.Ordinal);
        Assert.Contains("public DateTime LastActivity { get; set; }", source, StringComparison.Ordinal);
        Assert.Contains("COALESCE(\"PlanName\", 'Sem plano') AS \"PlanName\"", source, StringComparison.Ordinal);
        Assert.Contains("COALESCE(\"SubscriptionStatus\", 'sem_assinatura') AS \"SubscriptionStatus\"", source, StringComparison.Ordinal);
        Assert.Contains("COALESCE(\"ActiveUsers\", 0)::int AS \"ActiveUsers\"", source, StringComparison.Ordinal);
        Assert.Contains("COALESCE(\"UsedBytes\", 0)::bigint AS \"UsedBytes\"", source, StringComparison.Ordinal);
        Assert.Contains("COALESCE(\"LastActivity\", NOW()) AS \"LastActivity\"", source, StringComparison.Ordinal);
        Assert.Contains("@search::text", source, StringComparison.Ordinal);
    }

    [Fact]
    public void ConsumptionSummaryAndStorageRequestsUseProviderRowsAndExactAliases()
    {
        var source = ReadSource("Odca.Infrastructure", "Consumption", "NpgsqlConsumptionRepository.cs");

        Assert.Contains("QuerySingleOrDefaultAsync<SummaryRow>", source, StringComparison.Ordinal);
        Assert.Contains("public DateTime? PeriodStart{get;set;}", source, StringComparison.Ordinal);
        Assert.Contains("s.created_at AS \"PeriodStart\"", source, StringComparison.Ordinal);
        Assert.Contains("AS \"ActiveUsers\"", source, StringComparison.Ordinal);
        Assert.Contains("QueryAsync<AdditionalStorageRequestRow>", source, StringComparison.Ordinal);
        Assert.Contains("QuerySingleOrDefaultAsync<AdditionalStorageRequestRow>", source, StringComparison.Ordinal);
        Assert.Contains("public DateTime RequestedAt{get;set;}", source, StringComparison.Ordinal);
        Assert.Contains("r.requested_at AS \"RequestedAt\"", source, StringComparison.Ordinal);
        Assert.Contains("ToOffset(row.DecidedAt)", source, StringComparison.Ordinal);
    }

    [Fact]
    public void DashboardActiveUsersHasExplicitIntegerCastAndAlias()
    {
        var source = ReadSource("Odca.Infrastructure", "Dashboard", "NpgsqlPlatformDashboardRepository.cs");

        Assert.Contains("active_users::int AS \"ActiveUsers\"", source, StringComparison.Ordinal);
    }

    private static string ReadSource(params string[] path)
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null)
        {
            var candidate = Path.Combine([current.FullName, "src", .. path]);
            if (File.Exists(candidate)) return File.ReadAllText(candidate);
            current = current.Parent;
        }

        throw new FileNotFoundException(Path.Combine(path));
    }
}
