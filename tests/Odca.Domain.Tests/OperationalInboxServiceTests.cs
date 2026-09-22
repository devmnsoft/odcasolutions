using Odca.Application.Operations;
using Odca.Contracts.Operations;

namespace Odca.Domain.Tests;

public sealed class OperationalInboxServiceTests
{
    private static readonly Guid Tenant = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid Viewer = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly Guid Other = Guid.Parse("33333333-3333-3333-3333-333333333333");
    private static readonly Guid Contract = Guid.Parse("44444444-4444-4444-4444-444444444444");
    private static readonly DateOnly Today = new(2026, 9, 17);

    [Fact]
    public async Task OwnReaderDoesNotSeeSomeoneElsesObligation()
    {
        var service = new OperationalInboxService(new FakeInboxRepository(
        [
            Row(OperationalWorkKind.Obligation, Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaa1"), Other, Today)
        ]));

        var page = await service.QueryAsync(Tenant, Viewer, true, false, true, false, true, false, new OperationalInboxQuery(), default);

        Assert.Empty(page.Items);
        Assert.Equal(0, page.Total);
    }

    [Fact]
    public async Task TenantReaderSeesAllThreeKinds()
    {
        var service = new OperationalInboxService(new FakeInboxRepository(
        [
            Row(OperationalWorkKind.Obligation, Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaa1"), Other, Today),
            Row(OperationalWorkKind.Review, Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaa2"), Other, Today),
            Row(OperationalWorkKind.Renewal, Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaa3"), Other, Today.AddDays(20))
        ]));

        var page = await service.QueryAsync(Tenant, Viewer, true, true, true, true, true, true, new OperationalInboxQuery(), default);

        Assert.Equal(3, page.Total);
        Assert.Equal(2, page.DueToday);
    }

    [Fact]
    public async Task MissingSourcePermissionHidesEvenAnOwnedItem()
    {
        var service = new OperationalInboxService(new FakeInboxRepository(
        [
            Row(OperationalWorkKind.Obligation, Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaa1"), Viewer, Today),
            Row(OperationalWorkKind.Review, Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaa2"), Viewer, Today)
        ]));

        var page = await service.QueryAsync(
            Tenant, Viewer,
            canReadObligations: true, canReadTenantObligations: false,
            canReadReviews: false, canReadTenantReviews: false,
            canReadRenewals: false, canReadTenantRenewals: false,
            query: new OperationalInboxQuery(), cancellationToken: default);

        var item = Assert.Single(page.Items);
        Assert.Equal("Obligation", item.Kind);
    }

    [Fact]
    public async Task AgendaUsesTenantTodayAndFiltersByKind()
    {
        var repository = new FakeInboxRepository(
        [
            Row(OperationalWorkKind.Obligation, Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaa1"), Viewer, Today),
            Row(OperationalWorkKind.Review, Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaa2"), Viewer, Today)
        ]);
        var service = new MonthlyAgendaService(repository, repository);

        var page = await service.QueryAsync(
            Tenant, Viewer,
            canReadObligations: true, canReadTenantObligations: false,
            canReadReviews: true, canReadTenantReviews: false,
            canReadRenewals: false, canReadTenantRenewals: false,
            query: new MonthlyAgendaQuery(Today.Year, Today.Month, Kind: "Review"), cancellationToken: default);

        Assert.Equal(Today, page.Today);
        Assert.Equal("Review", Assert.Single(Assert.Single(page.Days).Items).Kind);
    }

    [Fact]
    public async Task KindFilterDropsOtherSources()
    {
        var service = new OperationalInboxService(new FakeInboxRepository(
        [
            Row(OperationalWorkKind.Obligation, Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaa1"), Viewer, Today),
            Row(OperationalWorkKind.Review, Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaa2"), Viewer, Today)
        ]));

        var page = await service.QueryAsync(Tenant, Viewer, true, true, true, true, true, true, new OperationalInboxQuery(Kind: "Review"), default);

        Assert.Single(page.Items);
        Assert.Equal("Review", page.Items[0].Kind);
    }

    [Fact]
    public async Task SourceConcurrencyVersionIsPreservedInTheResponse()
    {
        const long sourceVersion = 7;
        var service = new OperationalInboxService(new FakeInboxRepository(
        [
            Row(OperationalWorkKind.Review, Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaa2"), Viewer, Today, sourceVersion)
        ]));

        var page = await service.QueryAsync(Tenant, Viewer, true, true, true, true, true, true, new OperationalInboxQuery(), default);

        Assert.Equal(sourceVersion, Assert.Single(page.Items).Version);
    }

    [Fact]
    public void InvalidSourceConcurrencyVersionIsRejected() =>
        Assert.Throws<ArgumentOutOfRangeException>(() => OperationalInboxService.Map(
            Row(OperationalWorkKind.Obligation, Guid.NewGuid(), Viewer, Today, sourceVersion: 0),
            Today,
            Tenant));

    [Fact]
    public void FebruaryLeapYearWindowEndsOn29()
    {
        var (from, to) = MonthlyAgendaWindow.ForMonth(2024, 2);
        Assert.Equal(new DateOnly(2024, 2, 1), from);
        Assert.Equal(new DateOnly(2024, 2, 29), to);
    }

    [Fact]
    public void InvalidMonthIsRejected() =>
        Assert.Throws<ArgumentOutOfRangeException>(() => MonthlyAgendaWindow.ForMonth(2026, 13));

    private static OperationalInboxRow Row(
        OperationalWorkKind kind,
        Guid source,
        Guid owner,
        DateOnly due,
        long sourceVersion = 1) =>
        new(
            Kind: kind,
            SourceId: source,
            TenantId: Tenant,
            ContractId: Contract,
            ContractTitle: "Contrato",
            Title: kind.ToString(),
            OwnerId: owner,
            OwnerName: "Nome",
            DueOn: due,
            Status: "open",
            Version: sourceVersion);

    private sealed class FakeInboxRepository(IReadOnlyList<OperationalInboxRow> rows)
        : IOperationalInboxRepository, IMonthlyAgendaRepository
    {
        public Task<TenantCalendarContext> ReadCalendarAsync(Guid tenantId, CancellationToken cancellationToken) =>
            Task.FromResult(new TenantCalendarContext("America/Sao_Paulo", Today));

        public Task<IReadOnlyList<OperationalInboxRow>> ListCandidatesAsync(
            Guid tenantId, Guid viewerId, bool canReadObligations, bool canReadTenantObligations,
            bool canReadReviews, bool canReadTenantReviews, bool canReadRenewals, bool canReadTenantRenewals,
            Guid? contractId, Guid? ownerId, DateOnly today, DateOnly renewalWindowEnd,
            CancellationToken cancellationToken) => Task.FromResult(rows);

        public Task<IReadOnlyList<OperationalInboxRow>> ListWindowAsync(
            Guid tenantId, Guid viewerId, bool canReadObligations, bool canReadTenantObligations,
            bool canReadReviews, bool canReadTenantReviews, bool canReadRenewals, bool canReadTenantRenewals,
            Guid? ownerId, DateOnly windowStart, DateOnly windowEnd, CancellationToken cancellationToken) =>
            Task.FromResult(rows);
    }
}
