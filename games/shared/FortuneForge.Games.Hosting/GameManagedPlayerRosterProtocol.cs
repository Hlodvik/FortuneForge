using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Http;

namespace FortuneForge.Games.Hosting;

public sealed record ManagedPlayerReserveRequest(string AssignmentId, int Count,
    IReadOnlyList<string> ExcludedProfileIds, DateTime NowUtc);

public sealed record ManagedPlayerLeaseRequest(string AssignmentId, IReadOnlyList<string> ProfileIds, DateTime NowUtc);

public static class GameManagedPlayerRosterProtocol
{
    public const string AssertionHeader = "X-FortuneForge-Managed-Player-Assertion";
    public const string RequestSha256Header = "X-FortuneForge-Managed-Player-Request-Sha256";
    public const string ReserveAction = "reserve";
    public const string HeartbeatAction = "heartbeat";
    public const string TryRenewAction = "try-renew";
    public const string ReleaseAction = "release";
    public const string RoutePattern = "/internal/managed-players/v1/games/{gameId}/{action}";
    public const int MaximumBodyBytes = 8 * 1024;
    public const int MaximumCount = 16;
    public const int MaximumExclusions = 64;

    public static readonly JsonSerializerOptions WireJson = new(JsonSerializerDefaults.Web)
    {
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        AllowDuplicateProperties = false,
        RespectRequiredConstructorParameters = true,
        PropertyNameCaseInsensitive = false
    };

    public static bool IsValidGameId(string? gameId) => gameId is { Length: > 0 and <= 64 } &&
        gameId.All(value => value is >= 'a' and <= 'z' or >= '0' and <= '9' or '-');

    public static bool IsValidAssignment(string? assignmentId) => assignmentId is { Length: > 0 and <= 100 } &&
        assignmentId.All(value => char.IsAsciiLetterOrDigit(value) || value is '-' or '_' or ':' or '.');

    public static bool AreValidProfileIds(IReadOnlyList<string>? ids, int maximum) => ids is not null && ids.Count <= maximum &&
        ids.All(value => value is { Length: > 0 and <= 128 } && value is not "." and not ".." &&
            !value.Contains('/') && !value.Any(char.IsControl));

    public static string Route(string gameId, string action) => RoutePattern.Replace("{gameId}", gameId, StringComparison.Ordinal)
        .Replace("{action}", action, StringComparison.Ordinal);

    public static string CreateAssertion(string gameId, string action, string assignmentId, ReadOnlySpan<byte> body, byte[] key,
        DateTimeOffset now) => GameWorkerAuthentication.CreateAssertion(Scope(gameId), assignmentId, HttpMethods.Post,
            Route(gameId, action), body, key, now);

    public static Task<(string AccountId, string BodySha256)?> VerifyRequestAsync(HttpRequest request, string gameId, byte[] key,
        DateTimeOffset now, CancellationToken cancellationToken) =>
        GameWorkerAuthentication.VerifySignedAccountAsync(request, AssertionHeader, Scope(gameId), key, now,
            MaximumBodyBytes, cancellationToken);

    private static string Scope(string gameId) => $"managed-players:{gameId}";
}
