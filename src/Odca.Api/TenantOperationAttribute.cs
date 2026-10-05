namespace Odca.Api;

[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, Inherited = true, AllowMultiple = false)]
public sealed class TenantOperationAttribute : Attribute
{
    public string? Feature { get; init; }

    public string? Permission { get; init; }
}
