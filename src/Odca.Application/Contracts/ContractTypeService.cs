using Odca.Application.Tenancy;
using System.Text.RegularExpressions;

namespace Odca.Application.Contracts;

public sealed class ContractTypeService(IContractTypeRepository repository)
{
    private static readonly Regex CodePattern = new("^[a-z0-9][a-z0-9_-]{0,63}$", RegexOptions.Compiled | RegexOptions.CultureInvariant);

    public Task<QueryAccess<IReadOnlyList<ContractTypeRecord>>> ListAsync(
        Guid actorId,
        Guid tenantId,
        string? status,
        CancellationToken cancellationToken)
        => repository.ListAsync(actorId, tenantId, status, cancellationToken);

    public Task<QueryAccess<ContractTypeRecord?>> GetAsync(
        Guid actorId,
        Guid tenantId,
        Guid id,
        CancellationToken cancellationToken)
        => repository.GetAsync(actorId, tenantId, id, cancellationToken);

    public Task<MutationResult<ContractTypeRecord>> CreateAsync(
        Guid actorId,
        Guid tenantId,
        ContractTypeWriteModel model,
        CancellationToken cancellationToken)
    {
        var normalized = Normalize(model, out var error);
        if (error is not null)
        {
            return Task.FromResult(new MutationResult<ContractTypeRecord>(MutationStatus.ValidationFailed, ErrorCode: error));
        }

        return repository.CreateAsync(actorId, tenantId, normalized!, cancellationToken);
    }

    public Task<MutationResult<ContractTypeRecord>> UpdateAsync(
        Guid actorId,
        Guid tenantId,
        Guid id,
        long version,
        ContractTypeWriteModel model,
        CancellationToken cancellationToken)
    {
        var normalized = Normalize(model, out var error);
        if (error is not null)
        {
            return Task.FromResult(new MutationResult<ContractTypeRecord>(MutationStatus.ValidationFailed, ErrorCode: error));
        }

        return repository.UpdateAsync(actorId, tenantId, id, version, normalized!, cancellationToken);
    }

    public Task<MutationResult> SetStatusAsync(
        Guid actorId,
        Guid tenantId,
        Guid id,
        long version,
        string status,
        CancellationToken cancellationToken)
    {
        if (status is not ("active" or "inactive"))
        {
            return Task.FromResult(new MutationResult(MutationStatus.ValidationFailed, "status_invalid"));
        }

        return repository.SetStatusAsync(actorId, tenantId, id, version, status, cancellationToken);
    }

    private static ContractTypeWriteModel? Normalize(ContractTypeWriteModel model, out string? error)
    {
        error = null;
        var code = model.Code.Trim().ToLowerInvariant();
        if (!CodePattern.IsMatch(code))
        {
            error = "code_invalid";
            return null;
        }

        if (string.IsNullOrWhiteSpace(model.Name) || model.Name.Trim().Length > 160)
        {
            error = "name_invalid";
            return null;
        }

        return new ContractTypeWriteModel(
            code,
            model.Name.Trim(),
            string.IsNullOrWhiteSpace(model.Description) ? null : model.Description.Trim(),
            string.IsNullOrWhiteSpace(model.Guidance) ? null : model.Guidance.Trim());
    }
}
