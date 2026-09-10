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
    Conflict
}

public sealed record ApiCallResult<T>(ApiCallStatus Status, T? Value = default)
{
    public bool Succeeded => Status == ApiCallStatus.Success && Value is not null;
}
