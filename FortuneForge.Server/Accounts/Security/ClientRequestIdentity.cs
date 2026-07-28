using System.Net;
using System.Security.Cryptography;
using System.Text;

namespace FortuneForge.Server.Accounts.Security;

public static class ClientRequestIdentity
{
    public static string GetSessionPartitionKey(HttpContext context)
    {
        if (context.Request.Cookies.TryGetValue(AccountSessionCookie.Name, out var token) &&
            token.Length is >= 32 and <= 256)
        {
            var tokenHash = SHA256.HashData(Encoding.UTF8.GetBytes(token));
            return $"session:{Convert.ToHexStringLower(tokenHash.AsSpan(0, 16))}";
        }

        return GetIpPartitionKey(context);
    }

    public static string GetIpPartitionKey(HttpContext context) =>
        $"ip:{GetClientIpAddress(context) ?? "unknown"}";

    public static string? GetClientIpAddress(HttpContext context)
    {
        var forwardedAddresses = context.Request.Headers["X-Forwarded-For"]
            .ToString()
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(value => IPAddress.TryParse(value, out var address) ? address : null)
            .Where(address => address is not null)
            .Cast<IPAddress>()
            .ToArray();

        // Google load balancers append the verified client and load-balancer addresses.
        // Values before that pair are client supplied and must not be trusted.
        if (forwardedAddresses.Length >= 2)
        {
            return NormalizeIpAddress(forwardedAddresses[^2]);
        }

        return context.Connection.RemoteIpAddress is { } remoteAddress
            ? NormalizeIpAddress(remoteAddress)
            : null;
    }

    private static string NormalizeIpAddress(IPAddress address) =>
        address.IsIPv4MappedToIPv6 ? address.MapToIPv4().ToString() : address.ToString();
}
