using Odca.Application.Tenancy;

namespace Odca.Application.Contracts;

public sealed class CounterpartyService(ICounterpartyRepository repository)
{
    public Task<QueryAccess<TenantPage<CounterpartyRecord>>> ListAsync(
        Guid actorId,
        Guid tenantId,
        string? search,
        string? status,
        int? page,
        int? pageSize,
        CancellationToken cancellationToken)
    {
        var (p, s) = Pagination.Normalize(page, pageSize);
        return repository.ListAsync(actorId, tenantId, search, status, p, s, cancellationToken);
    }

    public Task<QueryAccess<CounterpartyRecord?>> GetAsync(
        Guid actorId,
        Guid tenantId,
        Guid id,
        CancellationToken cancellationToken)
        => repository.GetAsync(actorId, tenantId, id, cancellationToken);

    public Task<MutationResult<CounterpartyRecord>> CreateAsync(
        Guid actorId,
        Guid tenantId,
        CounterpartyWriteModel model,
        CancellationToken cancellationToken)
    {
        var validation = Validate(model);
        if (validation is not null)
        {
            return Task.FromResult(new MutationResult<CounterpartyRecord>(MutationStatus.ValidationFailed, ErrorCode: validation));
        }

        return repository.CreateAsync(actorId, tenantId, Normalize(model), cancellationToken);
    }

    public Task<MutationResult<CounterpartyRecord>> UpdateAsync(
        Guid actorId,
        Guid tenantId,
        Guid id,
        long version,
        CounterpartyWriteModel model,
        CancellationToken cancellationToken)
    {
        var validation = Validate(model);
        if (validation is not null)
        {
            return Task.FromResult(new MutationResult<CounterpartyRecord>(MutationStatus.ValidationFailed, ErrorCode: validation));
        }

        return repository.UpdateAsync(actorId, tenantId, id, version, Normalize(model), cancellationToken);
    }

    public Task<MutationResult> InactivateAsync(
        Guid actorId,
        Guid tenantId,
        Guid id,
        long version,
        CancellationToken cancellationToken)
        => repository.InactivateAsync(actorId, tenantId, id, version, cancellationToken);

    private static string? Validate(CounterpartyWriteModel model)
    {
        if (model.PersonType is not ("individual" or "organization"))
        {
            return "person_type_invalid";
        }

        if (string.IsNullOrWhiteSpace(model.LegalName) || model.LegalName.Trim().Length > 200)
        {
            return "legal_name_invalid";
        }

        if (string.IsNullOrWhiteSpace(model.DisplayName) || model.DisplayName.Trim().Length > 200)
        {
            return "display_name_invalid";
        }

        if (!(model.IsClient || model.IsSupplier || model.IsPartner || model.IsProvider))
        {
            return "roles_required";
        }

        var document = ContractDocumentRules.NormalizeDocument(model.DocumentType, model.DocumentRaw);
        return document.Ok ? null : document.Error;
    }

    private static CounterpartyWriteModel Normalize(CounterpartyWriteModel model)
    {
        var document = ContractDocumentRules.NormalizeDocument(model.DocumentType, model.DocumentRaw);
        return model with
        {
            PersonType = model.PersonType.Trim().ToLowerInvariant(),
            LegalName = model.LegalName.Trim(),
            DisplayName = model.DisplayName.Trim(),
            DocumentType = document.DocumentType,
            DocumentNormalized = document.Normalized,
            DocumentDisplay = document.Display,
            Email = string.IsNullOrWhiteSpace(model.Email) ? null : model.Email.Trim(),
            Phone = string.IsNullOrWhiteSpace(model.Phone) ? null : model.Phone.Trim(),
            AddressLine1 = NullIfWhite(model.AddressLine1),
            AddressLine2 = NullIfWhite(model.AddressLine2),
            AddressCity = NullIfWhite(model.AddressCity),
            AddressState = NullIfWhite(model.AddressState),
            AddressPostalCode = NullIfWhite(model.AddressPostalCode),
            AddressCountry = NullIfWhite(model.AddressCountry)
        };
    }

    private static string? NullIfWhite(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
