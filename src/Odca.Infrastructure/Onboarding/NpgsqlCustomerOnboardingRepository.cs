using Dapper;
using Npgsql;
using Odca.Application.Onboarding;
using Odca.Application.Plans;

namespace Odca.Infrastructure.Onboarding;

public sealed class NpgsqlCustomerOnboardingRepository(NpgsqlDataSource dataSource) : ICustomerOnboardingRepository
{
    public async Task<CustomerRegistrationDraft?> CreateRegistrationAsync(
        CustomerRegistrationDraft draft,
        string idempotencyKey,
        string confirmationTokenHash,
        string? developmentConfirmationToken,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        // Serialize competing registrations for the same normalized contractor before
        // the unique index is reached, so callers receive a domain conflict rather
        // than an unhandled constraint exception.
        await connection.ExecuteAsync(new CommandDefinition(
            "SELECT pg_advisory_xact_lock(hashtextextended(@documentNormalized, 0));",
            new { documentNormalized = draft.DocumentNormalized },
            transaction,
            cancellationToken: cancellationToken));
        var belongsToAnotherRegistration = await connection.ExecuteScalarAsync<bool>(new CommandDefinition(
            """
            SELECT EXISTS
            (
                SELECT 1
                  FROM odca.customer_registration_requests
                 WHERE document_type = @documentType
                   AND document_normalized = @documentNormalized
                   AND status IN ('email_pending', 'confirmed')
                   AND idempotency_key <> @idempotencyKey
            );
            """,
            new
            {
                documentType = draft.DocumentType,
                documentNormalized = draft.DocumentNormalized,
                idempotencyKey
            },
            transaction,
            cancellationToken: cancellationToken));
        if (belongsToAnotherRegistration)
        {
            await transaction.RollbackAsync(cancellationToken);
            return null;
        }

        var inserted = await connection.QuerySingleOrDefaultAsync<RegistrationInsertRow>(new CommandDefinition(
            """
            INSERT INTO odca.customer_registration_requests
                (id, idempotency_key, user_id, tenant_id, plan_version_id, plan_code, plan_version,
                 document_type, document_normalized, responsible_name, email, email_normalized,
                 password_hash, marketing_consent, confirmation_token_hash, development_confirmation_token,
                 expires_at, created_at, updated_at, terms_version, terms_accepted_at,
                 privacy_notice_version, privacy_notice_acknowledged_at)
            VALUES
                (@Id, @idempotencyKey, @UserId, @TenantId, @PlanVersionId, @PlanCode, @PlanVersion,
                 @DocumentType, @DocumentNormalized, @ResponsibleName, @Email, @EmailNormalized,
                 @PasswordHash, @MarketingConsent, @confirmationTokenHash, @developmentConfirmationToken,
                 @ExpiresAt, @now, @now, 'pending-legal-approval', @now,
                 'pending-legal-approval', @now)
            ON CONFLICT (idempotency_key) DO UPDATE
                SET confirmation_token_hash = EXCLUDED.confirmation_token_hash,
                    development_confirmation_token = EXCLUDED.development_confirmation_token,
                    expires_at = EXCLUDED.expires_at,
                    updated_at = EXCLUDED.updated_at
                WHERE odca.customer_registration_requests.status = 'email_pending'
            RETURNING id AS "Id", user_id AS "UserId", tenant_id AS "TenantId",
                      development_confirmation_token AS "DevelopmentConfirmationToken";
            """,
            new
            {
                draft.Id,
                idempotencyKey,
                draft.UserId,
                draft.TenantId,
                draft.PlanVersionId,
                draft.PlanCode,
                draft.PlanVersion,
                draft.DocumentType,
                draft.DocumentNormalized,
                draft.ResponsibleName,
                draft.Email,
                draft.EmailNormalized,
                draft.PasswordHash,
                draft.MarketingConsent,
                confirmationTokenHash,
                developmentConfirmationToken,
                draft.ExpiresAt,
                now
            },
            transaction,
            cancellationToken: cancellationToken));

        if (inserted is null)
        {
            await transaction.RollbackAsync(cancellationToken);
            return null;
        }

        if (inserted.Id != draft.Id)
        {
            await connection.ExecuteAsync(new CommandDefinition(
                """
                INSERT INTO odca.customer_registration_outbox
                    (id, registration_id, message_type, destination, status, payload)
                VALUES
                    (gen_random_uuid(), @registrationId, 'email_confirmation', @destination,
                     CASE WHEN @token IS NULL THEN 'provider_required' ELSE 'local_development' END,
                     jsonb_build_object('registrationId', @registrationId, 'plan', @planCode));
                """,
                new
                {
                    registrationId = inserted.Id,
                    destination = draft.Email,
                    token = developmentConfirmationToken,
                    planCode = draft.PlanCode
                },
                transaction,
                cancellationToken: cancellationToken));
            await transaction.CommitAsync(cancellationToken);
            return draft with
            {
                Id = inserted.Id,
                UserId = inserted.UserId,
                TenantId = inserted.TenantId
            };
        }

        var userInserted = await connection.ExecuteAsync(new CommandDefinition(
            """
            INSERT INTO odca.users
                (id, email, email_normalized, login_normalized, display_name, password_hash,
                 must_change_password, is_platform_administrator)
            VALUES (@UserId, @Email, @EmailNormalized, @EmailNormalized, @ResponsibleName, @PasswordHash,
                    false, false)
            ON CONFLICT (email_normalized) DO NOTHING;
            """,
            draft,
            transaction,
            cancellationToken: cancellationToken));
        if (userInserted != 1)
        {
            await transaction.RollbackAsync(cancellationToken);
            return null;
        }

        await connection.ExecuteAsync(new CommandDefinition(
            "SELECT set_config('odca.tenant_id', @tenantId, true);",
            new { tenantId = draft.TenantId.ToString() },
            transaction,
            cancellationToken: cancellationToken));
        await connection.ExecuteAsync(new CommandDefinition(
            """
            INSERT INTO odca.tenants (id, business_code, display_name, status)
            VALUES (@TenantId, @BusinessCode, @OrganizationName, 'pending');

            INSERT INTO odca.memberships (tenant_id, user_id, status)
            VALUES (@TenantId, @UserId, 'invited');

            INSERT INTO odca.subscriptions
                (id, tenant_id, plan_version_id, commercial_state, status, created_by)
            VALUES (gen_random_uuid(), @TenantId, @PlanVersionId, 'email_pending', 'pending', @UserId);

            INSERT INTO odca.customer_registration_outbox
                (id, registration_id, message_type, destination, status, payload)
            VALUES
                (gen_random_uuid(), @Id, 'email_confirmation', @Email,
                 CASE WHEN @DevelopmentConfirmationToken IS NULL THEN 'provider_required' ELSE 'local_development' END,
                 jsonb_build_object('registrationId', @Id, 'plan', @PlanCode));
            """,
            new
            {
                draft.Id,
                draft.UserId,
                draft.TenantId,
                draft.PlanVersionId,
                draft.PlanCode,
                draft.Email,
                DevelopmentConfirmationToken = developmentConfirmationToken,
                BusinessCode = $"PEND-{draft.Id.ToString("N")[..12].ToUpperInvariant()}",
                OrganizationName = draft.DocumentType == "cpf"
                    ? draft.ResponsibleName
                    : $"Organização {draft.DocumentNormalized}"
            },
            transaction,
            cancellationToken: cancellationToken));

        await transaction.CommitAsync(cancellationToken);
        return draft;
    }

    public async Task<bool> ConfirmRegistrationAsync(
        Guid registrationId,
        string tokenHash,
        DateTimeOffset confirmedAt,
        CancellationToken cancellationToken)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        var registration = await connection.QuerySingleOrDefaultAsync<RegistrationConfirmRow>(new CommandDefinition(
            """
            UPDATE odca.customer_registration_requests
               SET status = 'confirmed',
                   confirmed_at = @confirmedAt,
                   confirmation_token_hash = NULL,
                   development_confirmation_token = NULL,
                   updated_at = @confirmedAt
             WHERE id = @registrationId
               AND status = 'email_pending'
               AND confirmation_token_hash = @tokenHash
               AND expires_at > @confirmedAt
            RETURNING user_id AS "UserId", tenant_id AS "TenantId";
            """,
            new { registrationId, tokenHash, confirmedAt },
            transaction,
            cancellationToken: cancellationToken));
        if (registration is null)
        {
            await transaction.RollbackAsync(cancellationToken);
            return false;
        }

        await connection.ExecuteAsync(new CommandDefinition(
            "SELECT set_config('odca.tenant_id', @tenantId, true);",
            new { tenantId = registration.TenantId.ToString() },
            transaction,
            cancellationToken: cancellationToken));
        await connection.ExecuteAsync(new CommandDefinition(
            """
            UPDATE odca.users
               SET email_verified_at = COALESCE(email_verified_at, @confirmedAt),
                   updated_at = @confirmedAt
             WHERE id = @UserId;

            UPDATE odca.memberships
               SET status = 'active',
                   updated_at = @confirmedAt
             WHERE tenant_id = @TenantId AND user_id = @UserId;

            INSERT INTO odca.roles(scope_type,tenant_id,code,display_name,is_system)
            VALUES('tenant',@TenantId,'tenant-administrator','Administrador da organização',true)
            ON CONFLICT DO NOTHING;

            INSERT INTO odca.role_permissions(role_id,permission_code)
            SELECT r.id,p.code FROM odca.roles r CROSS JOIN odca.permissions p
             WHERE r.tenant_id=@TenantId AND r.code='tenant-administrator' AND p.code LIKE 'tenant.%'
            ON CONFLICT DO NOTHING;

            INSERT INTO odca.member_roles(tenant_id,user_id,role_id,assigned_by)
            SELECT @TenantId,@UserId,r.id,@UserId FROM odca.roles r
             WHERE r.tenant_id=@TenantId AND r.code='tenant-administrator'
            ON CONFLICT DO NOTHING;

            UPDATE odca.subscriptions
               SET commercial_state = 'commercial_pending',
                   updated_at = @confirmedAt
             WHERE tenant_id = @TenantId AND commercial_state = 'email_pending';

            INSERT INTO odca.audit_events
                (scope_type, tenant_id, actor_user_id, action, entity_type, entity_id, occurred_at, result)
            VALUES
                ('tenant', @TenantId, @UserId, 'onboarding.email.confirmed', 'tenant', @TenantId, @confirmedAt, 'success');
            """,
            new
            {
                registration.UserId,
                registration.TenantId,
                confirmedAt
            },
            transaction,
            cancellationToken: cancellationToken));
        await transaction.CommitAsync(cancellationToken);
        return true;
    }

    public async Task<CustomerHome?> GetHomeAsync(Guid userId, Guid? tenantId, CancellationToken cancellationToken)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        await connection.ExecuteAsync(new CommandDefinition(
            "SELECT set_config('odca.user_id', @userId, true);",
            new { userId = userId.ToString() },
            transaction,
            cancellationToken: cancellationToken));
        var row = await connection.QuerySingleOrDefaultAsync<CustomerHomeRow>(new CommandDefinition(
            """
            SELECT tenant_id AS "TenantId",
                   organization_name AS "OrganizationName",
                   tenant_status AS "TenantStatus",
                   commercial_state AS "CommercialState",
                   plan_code AS "PlanCode",
                   plan_name AS "PlanName",
                   plan_version AS "PlanVersion",
                   active_seats AS "ActiveSeats",
                   storage_bytes AS "StorageBytes",
                   user_storage_bytes AS "UserStorageBytes",
                   file_bytes AS "FileBytes",
                   ocr_pages_monthly AS "OcrPagesMonthly",
                   signature_envelopes_monthly AS "SignatureEnvelopesMonthly"
              FROM odca.customer_home_snapshot(@userId, @tenantId);
            """,
            new { userId, tenantId },
            transaction,
            cancellationToken: cancellationToken));
        await transaction.CommitAsync(cancellationToken);
        if (row is null)
        {
            return null;
        }

        return new CustomerHome(
            row.TenantId,
            row.OrganizationName,
            row.TenantStatus,
            row.CommercialState,
            new PlanCatalogItem(
                row.PlanCode,
                row.PlanVersion,
                row.PlanName,
                row.ActiveSeats,
                row.StorageBytes,
                row.UserStorageBytes,
                row.FileBytes,
                row.OcrPagesMonthly,
                row.SignatureEnvelopesMonthly));
    }

    private sealed class RegistrationInsertRow
    {
        public Guid Id { get; init; }

        public Guid UserId { get; init; }

        public Guid TenantId { get; init; }

        public string? DevelopmentConfirmationToken { get; init; }
    }

    private sealed record RegistrationConfirmRow(Guid UserId, Guid TenantId);

    private sealed record CustomerHomeRow(
        Guid TenantId,
        string OrganizationName,
        string TenantStatus,
        string CommercialState,
        string PlanCode,
        string PlanName,
        int PlanVersion,
        int ActiveSeats,
        long StorageBytes,
        long UserStorageBytes,
        long FileBytes,
        int OcrPagesMonthly,
        int SignatureEnvelopesMonthly);
}
