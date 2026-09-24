namespace Odca.Web.Services;

public enum ApiCallStatus
{
    Success,
    InvalidCredentials,
    Unauthorized,
    Forbidden,
    RateLimited,
    Unavailable,
    Timeout,
    InvalidRequest,
    NotFound,
    Conflict
}

public sealed record ApiCallResult<T>(
    ApiCallStatus Status,
    T? Value = default,
    string? ErrorTitle = null,
    string? ErrorDetail = null,
    IReadOnlyDictionary<string, string[]>? ValidationErrors = null,
    string? ErrorCode = null)
{
    public bool Succeeded => Status == ApiCallStatus.Success && Value is not null;

    public string UserMessage(string fallback) =>
        !string.IsNullOrWhiteSpace(ErrorDetail) ? ErrorDetail! :
        !string.IsNullOrWhiteSpace(ErrorTitle) ? ErrorTitle! :
        fallback;
}
