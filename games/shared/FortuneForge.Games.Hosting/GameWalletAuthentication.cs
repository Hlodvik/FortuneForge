using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Http;

namespace FortuneForge.Games.Hosting;

public static class GameWalletAuthentication
{
    public const string AssertionHeader = "X-FortuneForge-Wallet-Assertion";
    public const string RequestSha256Header = "X-FortuneForge-Wallet-Request-Sha256";
    public const string StateScopeHeader = "X-FortuneForge-Wallet-State-Scope";
    public const string SettlementRoutePattern = "/internal/wallet/v1/games/{gameId}/settlements";
    public const string AccountStateRoutePattern = "/internal/wallet/v1/games/{gameId}/state";
    public const int MaximumBodyBytes = 8 * 1024;
    public static readonly string EmptyBodySha256 = Convert.ToHexString(SHA256.HashData(ReadOnlySpan<byte>.Empty));

    public static readonly JsonSerializerOptions WireJson = new(JsonSerializerDefaults.Web)
    {
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        AllowDuplicateProperties = false,
        RespectRequiredConstructorParameters = true,
        PropertyNameCaseInsensitive = false,
        NumberHandling = JsonNumberHandling.Strict,
    };

    public static bool IsValidGameId(string? gameId) => gameId is { Length: > 0 and <= 64 } &&
        gameId.All(value => value is >= 'a' and <= 'z' or >= '0' and <= '9' or '-');

    public static string SettlementRoute(string gameId) => SettlementRoutePattern.Replace("{gameId}", gameId, StringComparison.Ordinal);

    public static string AccountStateRoute(string gameId) => AccountStateRoutePattern.Replace("{gameId}", gameId, StringComparison.Ordinal);

    public static string CreateSettlementAssertion(string gameId, string accountId, ReadOnlySpan<byte> body, byte[] key,
        DateTimeOffset now) => CreatePostAssertion(gameId, accountId, SettlementRoute(gameId), body, key, now);

    public static string CreatePostAssertion(string gameId, string accountId, string path, ReadOnlySpan<byte> body,
        byte[] key, DateTimeOffset now) => GameWorkerAuthentication.CreateAssertion(Scope(gameId), accountId,
            HttpMethods.Post, path, body, key, now);

    public static string CreateAccountStateAssertion(string gameId, string accountId, byte[] key, DateTimeOffset now) =>
        GameWorkerAuthentication.CreateAssertion(Scope(gameId), accountId, HttpMethods.Get, AccountStateRoute(gameId),
            ReadOnlySpan<byte>.Empty, key, now);

    public static string AccountStateScope(string gameId, string accountId) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes($"{AccountStateRoute(gameId)}\n{accountId}")));

    public static Task<(string AccountId, string BodySha256)?> VerifyRequestAsync(HttpRequest request, string gameId,
        byte[] key, DateTimeOffset now, CancellationToken cancellationToken) =>
        GameWorkerAuthentication.VerifySignedAccountAsync(request, AssertionHeader, Scope(gameId), key, now, MaximumBodyBytes,
            cancellationToken);

    private static string Scope(string gameId) => $"wallet:{gameId}";
}
