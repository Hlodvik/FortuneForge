using System.Net;
using FortuneForge.Games.Hosting;

namespace FortuneForge.Games.GameCompute.Server;

internal static class GameComputeEndpoints
{
    internal static void MapComputeAction<TRequest, TOutcome>(
        this WebApplication app, string route, Func<TRequest, TOutcome> action,
        Func<Exception, bool>? isRuleViolation = null)
    {
        if (!route.StartsWith("/internal/v1/", StringComparison.Ordinal))
            throw new ArgumentException("Game compute actions require an internal route.", nameof(route));

        app.MapDemoAction(route, (TRequest request) =>
        {
            try
            {
                return action(request);
            }
            catch (Exception exception) when (isRuleViolation?.Invoke(exception) == true)
            {
                throw new ArgumentException(exception.Message, exception);
            }
        });
    }

    internal static bool IsLocalServiceCall(HttpContext context)
    {
        var host = context.Request.Host.Host;
        return context.Connection.RemoteIpAddress is { } remote && IsLoopback(remote) &&
            (string.Equals(host, "localhost", StringComparison.OrdinalIgnoreCase) ||
                IPAddress.TryParse(host.Trim('[', ']'), out var hostAddress) && IsLoopback(hostAddress)) &&
            !context.Request.Headers.ContainsKey("Origin") &&
            !context.Request.Headers.ContainsKey("Sec-Fetch-Site");
    }

    private static bool IsLoopback(IPAddress address) =>
        IPAddress.IsLoopback(address.IsIPv4MappedToIPv6 ? address.MapToIPv4() : address);
}
