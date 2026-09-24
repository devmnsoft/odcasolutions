using Dapper;
using Npgsql;
using Odca.Application.Patients;
using Odca.Contracts.Patients;

namespace Odca.Infrastructure.Patients;

public sealed class NpgsqlPatientRepository(NpgsqlDataSource dataSource) : IPatientRepository
{
    public async Task<PatientPage?> ListAsync(Guid actorId, Guid tenantId, string? search, bool includeInactive, int page, int pageSize, CancellationToken ct)
    {
        await using var connection = await dataSource.OpenConnectionAsync(ct);
        await using var tx = await AuthorizedAsync(connection, actorId, tenantId, "tenant.patients.read", ct);
        if (tx is null) return null;
        var query = string.IsNullOrWhiteSpace(search) ? null : search.Trim();
        var args = new { tenantId, search = query, includeInactive, limit = pageSize, offset = (page - 1) * pageSize };
        var total = await connection.ExecuteScalarAsync<int>(new CommandDefinition("SELECT count(*)::int FROM odca.patients WHERE tenant_id=@tenantId AND (@includeInactive OR inactive_at IS NULL) AND (@search IS NULL OR full_name ILIKE '%'||@search||'%' OR preferred_name ILIKE '%'||@search||'%' OR identifier_normalized=@search)", args, tx, cancellationToken: ct));
        var rows = await connection.QueryAsync<PatientSummary>(new CommandDefinition("SELECT id AS Id,full_name AS FullName,preferred_name AS PreferredName,CASE WHEN identifier_normalized IS NULL THEN NULL ELSE repeat('*',greatest(length(identifier_normalized)-4,0))||right(identifier_normalized,4) END AS MaskedIdentifier,(inactive_at IS NULL) AS Active,row_version AS Version,updated_at AS UpdatedAt FROM odca.patients WHERE tenant_id=@tenantId AND (@includeInactive OR inactive_at IS NULL) AND (@search IS NULL OR full_name ILIKE '%'||@search||'%' OR preferred_name ILIKE '%'||@search||'%' OR identifier_normalized=@search) ORDER BY full_name,id LIMIT @limit OFFSET @offset", args, tx, cancellationToken: ct));
        await tx.CommitAsync(ct); return new PatientPage(rows.AsList(), page, pageSize, total);
    }

    public async Task<PatientDetails?> GetAsync(Guid actorId, Guid tenantId, Guid patientId, CancellationToken ct)
    {
        await using var connection = await dataSource.OpenConnectionAsync(ct); await using var tx = await AuthorizedAsync(connection, actorId, tenantId, "tenant.patients.read", ct); if (tx is null) return null;
        var row = await ReadAsync(connection, tx, tenantId, patientId, ct); await tx.CommitAsync(ct); return row;
    }

    public async Task<PatientMutation> CreateAsync(Guid actorId, Guid tenantId, SavePatientRequest request, CancellationToken ct)
    {
        await using var connection = await dataSource.OpenConnectionAsync(ct); await using var tx = await AuthorizedAsync(connection, actorId, tenantId, "tenant.patients.manage", ct); if (tx is null) return new(PatientMutationStatus.Forbidden);
        var id = Guid.NewGuid();
        try
        {
            await WriteAsync(connection, tx, actorId, tenantId, id, request, false, ct);
            var row = await ReadAsync(connection, tx, tenantId, id, ct); await tx.CommitAsync(ct); return new(PatientMutationStatus.Success, row);
        }
        catch (PostgresException e) when (e.SqlState == PostgresErrorCodes.UniqueViolation) { await tx.RollbackAsync(ct); return new(PatientMutationStatus.Duplicate); }
    }

    public async Task<PatientMutation> UpdateAsync(Guid actorId, Guid tenantId, Guid patientId, SavePatientRequest request, CancellationToken ct)
    {
        await using var connection = await dataSource.OpenConnectionAsync(ct); await using var tx = await AuthorizedAsync(connection, actorId, tenantId, "tenant.patients.manage", ct); if (tx is null) return new(PatientMutationStatus.Forbidden);
        try
        {
            var changed = await WriteAsync(connection, tx, actorId, tenantId, patientId, request, true, ct);
            if (changed == 0) { var exists = await connection.ExecuteScalarAsync<bool>(new CommandDefinition("SELECT EXISTS(SELECT 1 FROM odca.patients WHERE tenant_id=@tenantId AND id=@patientId)", new { tenantId, patientId }, tx, cancellationToken: ct)); await tx.RollbackAsync(ct); return new(exists ? PatientMutationStatus.Conflict : PatientMutationStatus.NotFound); }
            var row = await ReadAsync(connection, tx, tenantId, patientId, ct); await tx.CommitAsync(ct); return new(PatientMutationStatus.Success, row);
        }
        catch (PostgresException e) when (e.SqlState == PostgresErrorCodes.UniqueViolation) { await tx.RollbackAsync(ct); return new(PatientMutationStatus.Duplicate); }
    }

    public async Task<PatientMutationStatus> SetActiveAsync(Guid actorId, Guid tenantId, Guid patientId, bool active, long expectedVersion, CancellationToken ct)
    {
        await using var connection = await dataSource.OpenConnectionAsync(ct); await using var tx = await AuthorizedAsync(connection, actorId, tenantId, "tenant.patients.manage", ct); if (tx is null) return PatientMutationStatus.Forbidden;
        try
        {
            var changed = await connection.ExecuteAsync(new CommandDefinition("UPDATE odca.patients SET inactive_at=CASE WHEN @active THEN NULL ELSE now() END,inactivated_by=CASE WHEN @active THEN NULL ELSE @actorId END,row_version=row_version+1,updated_at=now() WHERE tenant_id=@tenantId AND id=@patientId AND row_version=@expectedVersion", new { actorId, tenantId, patientId, active, expectedVersion }, tx, cancellationToken: ct));
            if (changed == 0) { await tx.RollbackAsync(ct); return PatientMutationStatus.Conflict; }
            await AuditAsync(connection, tx, actorId, tenantId, patientId, active ? "patient.restored" : "patient.inactivated", ct); await tx.CommitAsync(ct); return PatientMutationStatus.Success;
        }
        catch (PostgresException e) when (e.SqlState == PostgresErrorCodes.UniqueViolation) { await tx.RollbackAsync(ct); return PatientMutationStatus.Duplicate; }
    }

    public async Task<PatientArchivePage?> ArchiveAsync(Guid actorId, Guid tenantId, Guid patientId, int page, int pageSize, CancellationToken ct)
    {
        await using var connection = await dataSource.OpenConnectionAsync(ct); await using var tx = await AuthorizedAsync(connection, actorId, tenantId, "tenant.patients.documents.read", ct); if (tx is null) return null;
        var args = new { tenantId, patientId, limit = pageSize, offset = (page - 1) * pageSize };
        var total = await connection.ExecuteScalarAsync<int>(new CommandDefinition("SELECT count(*)::int FROM odca.generated_contract_versions WHERE tenant_id=@tenantId AND patient_id=@patientId", args, tx, cancellationToken: ct));
        var rows = await connection.QueryAsync<PatientDocument>(new CommandDefinition("SELECT v.id AS Id,v.contract_id AS ContractId,c.title AS Title,t.contract_type AS Type,v.version_number AS Version,u.display_name AS Author,v.created_at AS CreatedAt,'generated' AS DocumentStatus,v.review_status AS ReviewStatus,'not_available' AS SignatureStatus FROM odca.generated_contract_versions v JOIN odca.contracts c ON (c.tenant_id,c.id)=(v.tenant_id,v.contract_id) JOIN odca.contract_templates t ON t.id=v.source_template_id JOIN odca.users u ON u.id=v.created_by WHERE v.tenant_id=@tenantId AND v.patient_id=@patientId ORDER BY v.created_at DESC,v.id LIMIT @limit OFFSET @offset", args, tx, cancellationToken: ct));
        await tx.CommitAsync(ct); return new PatientArchivePage(rows.AsList(), page, pageSize, total);
    }

    private static async Task<int> WriteAsync(NpgsqlConnection c, NpgsqlTransaction tx, Guid actor, Guid tenant, Guid id, SavePatientRequest r, bool update, CancellationToken ct)
    {
        var normalized = PatientRules.NormalizeIdentifier(r.Identifier?.Type, r.Identifier?.Value); var representativeId = r.Representative is null ? (Guid?)null : Guid.NewGuid();
        var sql = update
            ? "UPDATE odca.patients SET full_name=@fullName,preferred_name=@preferredName,birth_date=@birthDate,email=@email,phone=@phone,address=@address,identifier_type=@identifierType,identifier_value=@identifierValue,identifier_normalized=@normalized,representative_id=@representativeId,updated_by=@actor,row_version=row_version+1,updated_at=now() WHERE tenant_id=@tenant AND id=@id AND row_version=@expectedVersion"
            : "INSERT INTO odca.patients(id,tenant_id,full_name,preferred_name,birth_date,email,phone,address,identifier_type,identifier_value,identifier_normalized,representative_id,created_by,updated_by) VALUES(@id,@tenant,@fullName,@preferredName,@birthDate,@email,@phone,@address,@identifierType,@identifierValue,@normalized,@representativeId,@actor,@actor)";
        if (r.Representative is not null) await c.ExecuteAsync(new CommandDefinition("INSERT INTO odca.patient_representatives(id,tenant_id,full_name,identifier_type,identifier_value,relationship,created_by) VALUES(@representativeId,@tenant,@repName,@repType,@repValue,@relationship,@actor)", new { representativeId, tenant, repName=r.Representative.FullName.Trim(), repType=r.Representative.IdentifierType, repValue=r.Representative.IdentifierValue, r.Representative.Relationship, actor }, tx, cancellationToken: ct));
        var changed = await c.ExecuteAsync(new CommandDefinition(sql, new { id, tenant, fullName=r.FullName.Trim(), preferredName=Null(r.PreferredName), r.BirthDate, email=Null(r.Email), phone=Null(r.Phone), address=Null(r.Address), identifierType=r.Identifier?.Type.Trim().ToLowerInvariant(), identifierValue=r.Identifier?.Value.Trim(), normalized, representativeId, actor, r.ExpectedVersion }, tx, cancellationToken: ct));
        if (changed == 1) await AuditAsync(c, tx, actor, tenant, id, update ? "patient.updated" : "patient.created", ct); return changed;
    }
    private static string? Null(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    private static Task<int> AuditAsync(NpgsqlConnection c,NpgsqlTransaction tx,Guid actor,Guid tenant,Guid id,string action,CancellationToken ct)=>c.ExecuteAsync(new CommandDefinition("INSERT INTO odca.audit_events(scope_type,tenant_id,actor_user_id,action,entity_type,entity_id,result) VALUES('tenant',@tenant,@actor,@action,'patient',@id,'success')",new{tenant,actor,action,id},tx,cancellationToken:ct));
    private static async Task<PatientDetails?> ReadAsync(NpgsqlConnection c,NpgsqlTransaction tx,Guid tenant,Guid id,CancellationToken ct)
    {
        var p=await c.QuerySingleOrDefaultAsync<PatientRow>(new CommandDefinition("SELECT p.id AS Id,p.full_name AS FullName,p.preferred_name AS PreferredName,p.birth_date AS BirthDate,p.email AS Email,p.phone AS Phone,p.address AS Address,p.identifier_type AS IdentifierType,p.identifier_value AS IdentifierValue,r.full_name AS RepresentativeName,r.identifier_type AS RepresentativeIdentifierType,r.identifier_value AS RepresentativeIdentifierValue,r.relationship AS Relationship,(p.inactive_at IS NULL) AS Active,p.row_version AS Version,p.updated_at AS UpdatedAt FROM odca.patients p LEFT JOIN odca.patient_representatives r ON (r.tenant_id,r.id)=(p.tenant_id,p.representative_id) WHERE p.tenant_id=@tenant AND p.id=@id",new{tenant,id},tx,cancellationToken:ct));
        return p is null?null:new PatientDetails(p.Id,p.FullName,p.PreferredName,p.BirthDate,p.Email,p.Phone,p.Address,p.IdentifierType,p.IdentifierValue,p.RepresentativeName is null?null:new(p.RepresentativeName,p.RepresentativeIdentifierType,p.RepresentativeIdentifierValue,p.Relationship!),p.Active,p.Version,p.UpdatedAt);
    }
    private static async Task<NpgsqlTransaction?> AuthorizedAsync(NpgsqlConnection c,Guid actor,Guid tenant,string permission,CancellationToken ct){var tx=await c.BeginTransactionAsync(ct);var ok=await c.ExecuteScalarAsync<bool>(new CommandDefinition("SELECT odca.tenant_actor_has_permission(@actor,@tenant,@permission)",new{actor,tenant,permission},tx,cancellationToken:ct));if(!ok){await tx.RollbackAsync(ct);await tx.DisposeAsync();return null;}await c.ExecuteAsync(new CommandDefinition("SELECT set_config('odca.tenant_id',@tenant,true),set_config('odca.actor_id',@actor,true)",new{tenant=tenant.ToString(),actor=actor.ToString()},tx,cancellationToken:ct));return tx;}
    private sealed class PatientRow { public Guid Id{get;init;} public string FullName{get;init;}=""; public string? PreferredName{get;init;} public DateOnly? BirthDate{get;init;} public string? Email{get;init;} public string? Phone{get;init;} public string? Address{get;init;} public string? IdentifierType{get;init;} public string? IdentifierValue{get;init;} public string? RepresentativeName{get;init;} public string? RepresentativeIdentifierType{get;init;} public string? RepresentativeIdentifierValue{get;init;} public string? Relationship{get;init;} public bool Active{get;init;} public long Version{get;init;} public DateTimeOffset UpdatedAt{get;init;} }
}
