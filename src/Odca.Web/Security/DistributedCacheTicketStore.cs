using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Caching.Distributed;

namespace Odca.Web.Security;

public sealed class DistributedCacheTicketStore(
    IDistributedCache cache,
    IDataProtectionProvider dataProtectionProvider) : ITicketStore
{
    private const string KeyPrefix = "odca:bff:ticket:";
    private readonly IDataProtector protector = dataProtectionProvider.CreateProtector(
        "ODCA Solutions",
        "BFF authentication tickets",
        "v1");

    public async Task<string> StoreAsync(AuthenticationTicket ticket)
    {
        var key = $"{KeyPrefix}{Guid.NewGuid():N}";
        await WriteAsync(key, ticket);
        return key;
    }

    public Task RenewAsync(string key, AuthenticationTicket ticket) => WriteAsync(key, ticket);

    public async Task<AuthenticationTicket?> RetrieveAsync(string key)
    {
        if (!IsValidKey(key))
        {
            return null;
        }

        var protectedTicket = await cache.GetAsync(key);
        if (protectedTicket is null)
        {
            return null;
        }

        try
        {
            return TicketSerializer.Default.Deserialize(protector.Unprotect(protectedTicket));
        }
        catch (System.Security.Cryptography.CryptographicException)
        {
            await cache.RemoveAsync(key);
            return null;
        }
    }

    public Task RemoveAsync(string key) => IsValidKey(key) ? cache.RemoveAsync(key) : Task.CompletedTask;

    private Task WriteAsync(string key, AuthenticationTicket ticket)
    {
        var expiresAt = ticket.Properties.ExpiresUtc ?? DateTimeOffset.UtcNow.AddMinutes(15);
        var serialized = TicketSerializer.Default.Serialize(ticket);
        return cache.SetAsync(
            key,
            protector.Protect(serialized),
            new DistributedCacheEntryOptions { AbsoluteExpiration = expiresAt });
    }

    private static bool IsValidKey(string key) =>
        key.StartsWith(KeyPrefix, StringComparison.Ordinal) && key.Length == KeyPrefix.Length + 32;
}
