using System.Net;
using System.Security.Cryptography;
using System.Text.Json;
using FortuneForge.Games.Abstractions.Auditing;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;

namespace FortuneForge.Games.Hosting;

/// <summary>Authenticates game requests forwarded by the account platform.</summary>
public static class GameWorkerAuthentication
{
    public const string AssertionHeader = "X-FortuneForge-Worker-Assertion";
    public static IReadOnlyList<string> AssertedHeaderNames { get; } =
        Array.AsReadOnly(new[] { "Idempotency-Key", "Origin", "X-FortuneForge-Game", "Sec-Fetch-Site" });
    private const int AssertionLifetimeSeconds = 60;
    private const int MaximumBodyBytes = 1024 * 1024;
    private static readonly object AccountKey = new();

    public static byte[] ReadSigningKey(string encodedKey)
    {
        var key = Convert.FromBase64String(encodedKey);
        if (key.Length < 32)
            throw new ArgumentException("A game worker signing key must contain at least 32 bytes.", nameof(encodedKey));
        return key;
    }

    public static string CreateAssertion(string gameId, string playerId, string method, string target,
        ReadOnlySpan<byte> body, byte[] key, DateTimeOffset now, GameRequestObservation? observation = null,
        IReadOnlyDictionary<string, string>? headers = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(playerId);
        if (observation is not null && observation.UserId != playerId)
            throw new ArgumentException("The request observation belongs to a different account.", nameof(observation));
        var claims = new WorkerAssertion(gameId, playerId, method, target, now.ToUnixTimeSeconds(),
            Convert.ToHexString(SHA256.HashData(body)), observation, headers);
        var payload = JsonSerializer.SerializeToUtf8Bytes(claims);
        return WebEncoders.Base64UrlEncode(payload) + "." +
            WebEncoders.Base64UrlEncode(HMACSHA256.HashData(key, payload));
    }

    public static void UseGameWorkerAuthentication(this WebApplication app, string gameId)
    {
        var key = app.Configuration["GameWorker:SigningKey"] is { Length: > 0 } encodedKey
            ? ReadSigningKey(encodedKey) : null;
        var allowLoopback = app.Environment.IsDevelopment() &&
            app.Configuration.GetValue<bool>("GameWorker:AllowDevelopmentLoopback");

        app.Use(async (context, next) =>
        {
            var request = context.Request;
            if (HttpMethods.IsGet(request.Method) && request.Path == "/healthz")
            {
                await next();
                return;
            }

            if (request.Headers.TryGetValue(AssertionHeader, out var assertions))
            {
                var claims = key is not null && assertions.Count == 1
                    ? ReadAssertion(assertions.ToString(), key, gameId, request, DateTimeOffset.UtcNow) : null;
                if (claims is not null && await BodyMatchesAsync(request, claims.BodySha256, context.RequestAborted))
                {
                    context.Items[AccountKey] = claims.PlayerId;
                    using var scope = claims.Observation is { } observation ? GameSecurityContext.Push(observation) : null;
                    await next();
                    return;
                }
            }
            else if (allowLoopback && context.Connection.RemoteIpAddress is { } remoteAddress &&
                IPAddress.IsLoopback(remoteAddress) && !request.Headers.ContainsKey("Origin") &&
                !request.Headers.ContainsKey("Sec-Fetch-Site") && IsLoopbackHost(request.Host.Host))
            {
                await next();
                return;
            }

            context.Response.StatusCode = StatusCodes.Status403Forbidden;
        });
    }

    public static string? GetAccountId(HttpContext context) => context.Items[AccountKey] as string;

    private static bool IsLoopbackHost(string host) =>
        host.Equals("localhost", StringComparison.OrdinalIgnoreCase) ||
        IPAddress.TryParse(host.Trim('[', ']'), out var address) && IPAddress.IsLoopback(address);

    private static WorkerAssertion? ReadAssertion(string assertion, byte[] key, string gameId,
        HttpRequest request, DateTimeOffset now)
    {
        if (assertion.Length > 24 * 1024) return null;
        var separator = assertion.IndexOf('.');
        if (separator <= 0 || separator == assertion.Length - 1) return null;

        try
        {
            var payload = WebEncoders.Base64UrlDecode(assertion[..separator]);
            var signature = WebEncoders.Base64UrlDecode(assertion[(separator + 1)..]);
            if (!CryptographicOperations.FixedTimeEquals(HMACSHA256.HashData(key, payload), signature))
                return null;

            var claims = JsonSerializer.Deserialize<WorkerAssertion>(payload);
            var target = request.PathBase.ToUriComponent() + request.Path.ToUriComponent() +
                request.QueryString.ToUriComponent();
            var current = now.ToUnixTimeSeconds();
            return claims is not null && claims.GameId == gameId && claims.Method == request.Method &&
                claims.Target == target && claims.IssuedAt >= current - AssertionLifetimeSeconds &&
                claims.IssuedAt <= current + 5 && !string.IsNullOrWhiteSpace(claims.PlayerId) &&
                claims.BodySha256 is { Length: 64 } &&
                (claims.Observation is null || claims.Observation.UserId == claims.PlayerId) &&
                AssertedHeaderNames.All(name => HeaderMatches(request, claims.Headers, name)) ? claims : null;
        }
        catch (FormatException)
        {
            return null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static bool HeaderMatches(HttpRequest request, IReadOnlyDictionary<string, string>? signed, string name)
    {
        var actual = request.Headers.TryGetValue(name, out var value) ? value.ToString() : null;
        return actual == (signed is not null && signed.TryGetValue(name, out var expected) ? expected : null);
    }

    private static async Task<bool> BodyMatchesAsync(HttpRequest request, string expected, CancellationToken cancellationToken)
    {
        if (request.ContentLength > MaximumBodyBytes) return false;
        request.EnableBuffering(bufferThreshold: 32 * 1024, bufferLimit: MaximumBodyBytes);
        try
        {
            var hash = await SHA256.HashDataAsync(request.Body, cancellationToken);
            request.Body.Position = 0;
            return string.Equals(Convert.ToHexString(hash), expected, StringComparison.Ordinal);
        }
        catch (IOException)
        {
            return false;
        }
    }

    private sealed record WorkerAssertion(string GameId, string PlayerId, string Method, string Target,
        long IssuedAt, string BodySha256, GameRequestObservation? Observation,
        IReadOnlyDictionary<string, string>? Headers);
}
