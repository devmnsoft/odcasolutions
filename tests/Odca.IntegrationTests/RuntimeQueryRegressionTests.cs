namespace Odca.IntegrationTests;

public sealed class RuntimeQueryRegressionTests
{
    [Theory]
    [InlineData("OperationalInboxRepository.cs")]
    [InlineData("ContractSheetRepository.cs")]
    public void OperationalProjectionsReadTheCanonicalBigintConcurrencyVersion(string file)
    {
        var source = ReadSource("Odca.Infrastructure", "Operations", file);

        Assert.Contains("row_version::bigint AS Version", source, StringComparison.OrdinalIgnoreCase);
    }

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
    public void RenewalListProjectionSelectsNullAiSuggestionColumnForRecordConstructor()
    {
        var source = ReadSource("Odca.Api", "Controllers", "RenewalCenterController.cs");

        // O record RenewalListItem tem construtor único com AiSuggestion como último parâmetro;
        // a projeção da lista precisa declarar a coluna explícita (colunas == parâmetros, 1:1).
        Assert.Contains("NULL::text AS AiSuggestion FROM projected", source, StringComparison.Ordinal);
        Assert.DoesNotContain("Kind FROM projected", source, StringComparison.Ordinal);
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
        Assert.Contains("public DateTime? LastActivity { get; set; }", source, StringComparison.Ordinal);
        Assert.Contains("COALESCE(\"PlanName\", 'Sem plano') AS \"PlanName\"", source, StringComparison.Ordinal);
        Assert.Contains("COALESCE(\"SubscriptionStatus\", 'sem_assinatura') AS \"SubscriptionStatus\"", source, StringComparison.Ordinal);
        Assert.Contains("COALESCE(\"ActiveUsers\", 0)::int AS \"ActiveUsers\"", source, StringComparison.Ordinal);
        Assert.Contains("COALESCE(\"UsedBytes\", 0)::bigint AS \"UsedBytes\"", source, StringComparison.Ordinal);
        Assert.Contains("\"LastActivity\" AS \"LastActivity\"", source, StringComparison.Ordinal);
        Assert.DoesNotContain("COALESCE(\"LastActivity\", NOW())", source, StringComparison.Ordinal);
        Assert.Contains("row.PendingRequests, ToOffset(row.LastActivity)", source, StringComparison.Ordinal);
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

    [Fact]
    public void ContractImportListSeparatesSqlFragmentsTypesNullableRequesterAndUsesStableOrder()
    {
        var source = ReadSource("Odca.Api", "Controllers", "ContractImportsController.cs");

        Assert.Contains("+ Environment.NewLine\n            + filter\n            + Environment.NewLine", source, StringComparison.Ordinal);
        Assert.Contains("CAST(@requester AS uuid) IS NULL", source, StringComparison.Ordinal);
        Assert.Contains("i.requested_by=CAST(@requester AS uuid)", source, StringComparison.Ordinal);
        Assert.Contains("ORDER BY i.created_at DESC, i.id DESC", source, StringComparison.Ordinal);
    }

    [Fact]
    public void ReviewQueueUsesCanonicalAuthorizationFunctionWithActorTenantPermissionOrder()
    {
        var source = ReadSource("Odca.Api", "Controllers", "ReviewRequestsController.cs");

        Assert.DoesNotContain("odca.has_tenant_permission", source, StringComparison.Ordinal);
        Assert.Contains(
            "odca.tenant_actor_has_permission(@actor,@tenant,@permission)",
            source,
            StringComparison.Ordinal);
    }

    [Fact]
    public void DocumentWorkerScanClaimProjectionMatchesVersionItemSecondaryConstructor()
    {
        var source = ReadSource("Odca.Worker", "DocumentWorker.cs");

        // S22/D-OC56: a projecao do claim de scan tem 4 colunas e o record VersionItem tem 6
        // parametros desde a S19; os alias devem ancorar o construtor secundario nome por nome,
        // caso contrario o Dapper falha ao materializar a primeira linha de qualquer varredura.
        Assert.Contains("id AS Id,tenant_id AS TenantId,storage_key AS StorageKey,detected_type AS DetectedType FROM odca.claim_document_scan()", source, StringComparison.Ordinal);
        Assert.Contains("public VersionItem(Guid Id, Guid TenantId, string StorageKey, string DetectedType)", source, StringComparison.Ordinal);
    }

    [Fact]
    public void RenewalAgendaWindowCutsByNoticeDateNotContractEnd()
    {
        var source = ReadSource("Odca.Infrastructure", "Operations", "OperationalInboxRepository.cs");

        // S25/D-OC59: com aviso de 30 dias o vencimento cai no mes seguinte ao aviso; cortar a
        // janela pelo end_date escondia a renovacao da agenda exatamente quando o aviso venceu.
        // O corte deve seguir a data do aviso (due_on) materializada no SELECT.
        Assert.DoesNotContain("AND c.end_date <= @renewalWindowEnd", source, StringComparison.Ordinal);
        Assert.Contains("END, c.end_date) <= @renewalWindowEnd", source, StringComparison.Ordinal);
    }

    [Fact]
    public void ReviewSubmissionNormalizesDueAtToUtcBeforePersisting()
    {
        var source = ReadSource("Odca.Api", "Controllers", "ContractStudioController.cs");

        // S25/D-OC59: dueAt com offset nao-UTC derrubava o Submit de revisao com
        // ArgumentException do Npgsql (timestamp with time zone exige offset 0).
        Assert.Contains("request.DueAt?.ToUniversalTime()", source, StringComparison.Ordinal);
        Assert.DoesNotContain("@dueAt=request.DueAt", source, StringComparison.Ordinal);
    }

    private static string ReadSource(params string[] path)
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null)
        {
            var candidate = Path.Combine([current.FullName, "src", .. path]);
            // Normaliza para LF: o guard compara substrings multi-linha e o checkout pode ser
            // CRLF (Windows com autocrlf) ou LF (Linux/CI) dependendo do ambiente (D-OC58).
            if (File.Exists(candidate)) return File.ReadAllText(candidate).Replace("\r\n", "\n");
            current = current.Parent;
        }

        throw new FileNotFoundException(Path.Combine(path));
    }
}
