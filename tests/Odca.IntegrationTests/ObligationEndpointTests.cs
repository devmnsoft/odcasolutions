using System.Text.Json;
using System.Text.Json.Serialization;
using Dapper;
using Microsoft.AspNetCore.Mvc;
using Npgsql;
using Odca.Api.Controllers;
using Odca.Contracts.Obligations;

namespace Odca.IntegrationTests;

/// <summary>
/// D-OC38 (Sprint 4/A-1): a central de obrigações do cliente ganha cadastro e ações; para isso
/// o BFF precisa (1) da lista com <c>evidenceRequired</c> visível e (2) de um read individual
/// da obrigação com autorização por dono/read_all, além do 409 com <c>currentVersion</c> para
/// preservar os valores do formulário após concorrência. Apenas dados sintéticos.
/// </summary>
public sealed class ObligationEndpointTests(DatabaseFixture database) : IClassFixture<DatabaseFixture>
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    [Fact]
    public async Task ListExposesEvidenceRequiredForClientForms()
    {
        var seed = await SeedAsync();
        var controller = CreateController(seed.Owner);

        var result = await controller.List(seed.Tenant, "mine", null, null, null, null, null, null, null, 1, 20, CancellationToken.None);
        var page = Assert.IsType<OkObjectResult>(result);
        var json = JsonSerializer.Serialize(page.Value, JsonOptions);

        Assert.Contains($"\"id\":\"{seed.Obligation:D}\"", json, StringComparison.Ordinal);
        Assert.Contains("\"evidenceRequired\":true", json, StringComparison.Ordinal);
    }

    [Fact]
    public async Task SingleGetHonorsOwnershipAndReadAllPermission()
    {
        var seed = await SeedAsync();

        var own = await CreateController(seed.Owner).Get(seed.Tenant, seed.Obligation, CancellationToken.None);
        var ownJson = JsonSerializer.Serialize(Assert.IsType<OkObjectResult>(own).Value, JsonOptions);
        Assert.Contains($"\"id\":\"{seed.Obligation:D}\"", ownJson, StringComparison.Ordinal);
        Assert.Contains("\"evidenceRequired\":true", ownJson, StringComparison.Ordinal);

        var all = await CreateController(seed.Reader).Get(seed.Tenant, seed.Obligation, CancellationToken.None);
        Assert.IsType<OkObjectResult>(all);

        var stranger = await CreateController(seed.Stranger).Get(seed.Tenant, seed.Obligation, CancellationToken.None);
        Assert.IsType<ForbidResult>(stranger);

        var missing = await CreateController(seed.Owner).Get(seed.Tenant, Guid.NewGuid(), CancellationToken.None);
        Assert.IsType<NotFoundResult>(missing);
    }

    [Fact]
    public async Task StaleVersionActionReturnsConflictWithCurrentVersionForBffPreservation()
    {
        var seed = await SeedAsync();
        var controller = CreateController(seed.Owner);

        var stale = await controller.Action(seed.Tenant, seed.Obligation, "start",
            new ObligationActionRequest(999, null, null, null, null, null, null), CancellationToken.None);
        var conflict = Assert.IsType<ConflictObjectResult>(stale);
        var json = JsonSerializer.Serialize(conflict.Value, JsonOptions);

        Assert.Contains("\"code\":\"obligation.version.conflict\"", json, StringComparison.Ordinal);
        Assert.Contains("\"currentVersion\":1", json, StringComparison.Ordinal);
    }

    private sealed record Seed(Guid Tenant, Guid Owner, Guid Reader, Guid Stranger, Guid Contract, Guid Obligation);

    private async Task<Seed> SeedAsync()
    {
        var tenant = Guid.NewGuid();
        var owner = Guid.NewGuid();
        var reader = Guid.NewGuid();
        var stranger = Guid.NewGuid();
        var roleManager = Guid.NewGuid();
        var roleReader = Guid.NewGuid();
        var contract = Guid.NewGuid();
        var obligation = Guid.NewGuid();

        await using var admin = new NpgsqlConnection(database.AdminConnectionString);
        await admin.OpenAsync();
        await admin.ExecuteAsync("""
            INSERT INTO odca.tenants(id, business_code, display_name, status, timezone)
            VALUES (@tenant, @businessCode, 'Organização das obrigações', 'active', 'America/Sao_Paulo');
            INSERT INTO odca.users(id, email, email_normalized, login_normalized, display_name, password_hash, is_platform_administrator)
            VALUES
                (@owner, @ownerEmail, @ownerEmailUpper, @ownerEmailUpper, 'Dona da obrigação', 'hash', false),
                (@reader, @readerEmail, @readerEmailUpper, @readerEmailUpper, 'Leitor geral', 'hash', false),
                (@stranger, @strangerEmail, @strangerEmailUpper, @strangerEmailUpper, 'Outro membro', 'hash', false);
            INSERT INTO odca.memberships(id, tenant_id, user_id, status)
            VALUES
                (@ownerMembership, @tenant, @owner, 'active'),
                (@readerMembership, @tenant, @reader, 'active'),
                (@strangerMembership, @tenant, @stranger, 'active');
            INSERT INTO odca.roles(id, scope_type, tenant_id, code, display_name, is_system)
            VALUES
                (@roleManager, 'tenant', @tenant, 'obl-endpoint-manager', 'Gestor de obrigações', true),
                (@roleReader, 'tenant', @tenant, 'obl-endpoint-reader', 'Leitor de obrigações', true);
            INSERT INTO odca.role_permissions(role_id, permission_code)
            SELECT @roleManager, code FROM odca.permissions
            WHERE code IN ('tenant.obligations.read','tenant.obligations.read_all','tenant.obligations.manage');
            INSERT INTO odca.role_permissions(role_id, permission_code)
            SELECT @roleReader, code FROM odca.permissions WHERE code='tenant.obligations.read';
            INSERT INTO odca.member_roles(tenant_id, user_id, role_id, assigned_by)
            VALUES (@tenant, @owner, @roleManager, @owner), (@tenant, @reader, @roleManager, @owner), (@tenant, @stranger, @roleReader, @owner);
            INSERT INTO odca.contracts(id, tenant_id, title, reference)
            VALUES (@contract, @tenant, 'Contrato sintético das obrigações', @reference);
            INSERT INTO odca.contract_obligations(id, tenant_id, contract_id, title, category, obligated_party, owner_id, due_date, priority, origin, evidence_required, created_by)
            VALUES (@obligation, @tenant, @contract, 'Obrigação sintética', 'other', 'Contratada', @owner, DATE '2026-12-01', 'normal', 'manual', true, @owner);
            INSERT INTO odca.obligation_events(tenant_id, obligation_id, actor_id, event_type, details)
            VALUES (@tenant, @obligation, @owner, 'created', '{"dueDate":"2026-12-01"}'::jsonb);
            """, new
        {
            tenant, owner, reader, stranger, roleManager, roleReader, contract, obligation,
            businessCode = tenant.ToString("N")[..20],
            reference = $"OBL-{tenant.ToString("N")[..8].ToUpperInvariant()}",
            ownerEmail = $"{tenant:N}-owner@obl.test", ownerEmailUpper = $"{tenant:N}-OWNER@OBL.TEST".ToUpperInvariant(),
            readerEmail = $"{tenant:N}-reader@obl.test", readerEmailUpper = $"{tenant:N}-READER@OBL.TEST".ToUpperInvariant(),
            strangerEmail = $"{tenant:N}-stranger@obl.test", strangerEmailUpper = $"{tenant:N}-STRANGER@OBL.TEST".ToUpperInvariant(),
            ownerMembership = Guid.NewGuid(), readerMembership = Guid.NewGuid(), strangerMembership = Guid.NewGuid(),
        });

        return new Seed(tenant, owner, reader, stranger, contract, obligation);
    }

    private ObligationsController CreateController(Guid actor) =>
        new(NpgsqlDataSource.Create(database.AppConnectionString)) { ControllerContext = new ControllerContext { HttpContext = TestControllerContexts.Create(actor) } };
}
