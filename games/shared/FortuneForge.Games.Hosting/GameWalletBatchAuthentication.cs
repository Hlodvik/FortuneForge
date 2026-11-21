using FortuneForge.Games.Abstractions.Wallet;
using Microsoft.AspNetCore.Http;

namespace FortuneForge.Games.Hosting;

public static class GameWalletBatchAuthentication
{
    public const int MaximumBodyBytes = 32 * 1024;
    public const string RoutePattern = "/internal/wallet/v1/games/{gameId}/batch-settlements";
    private const long HeartsBatchDebitLimitCents = 100_000;
    private const long HeartsBatchCreditLimitCents = 400_000;

    public static bool IsSupportedGameId(string gameId) => gameId is "texas-holdem" or "solitaire" or "hearts";

    public static bool AllowsEffects(string gameId, string actorAccountId, IReadOnlyList<GameWalletBatchEffect> effects) =>
        gameId switch
        {
            "texas-holdem" => true,
            "solitaire" => effects is [{ AccountId: var account }] && account == actorAccountId,
            "hearts" => AllowsHeartsEffects(actorAccountId, effects),
            _ => false
        };

    private static bool AllowsHeartsEffects(string actorAccountId, IReadOnlyList<GameWalletBatchEffect> effects) =>
        effects.Count is > 0 and <= 4 &&
        effects.All(effect => effect is { WagerCents: >= 0 and <= HeartsBatchDebitLimitCents,
            PayoutCents: >= 0 and <= HeartsBatchCreditLimitCents } &&
            (effect.AccountId == actorAccountId ||
             effect is { WagerCents: 0, PayoutCents: > 0, MinimumBalanceBeforeCents: null })) &&
        effects.Sum(effect => effect.PayoutCents) <= HeartsBatchCreditLimitCents;

    public static string Route(string gameId) => RoutePattern.Replace("{gameId}", gameId, StringComparison.Ordinal);

    public static string CreateAssertion(string gameId, string actorAccountId, ReadOnlySpan<byte> body, byte[] key,
        DateTimeOffset now) => GameWorkerAuthentication.CreateAssertion(Scope(gameId), actorAccountId, HttpMethods.Post,
            Route(gameId), body, key, now);

    public static Task<(string AccountId, string BodySha256)?> VerifyRequestAsync(HttpRequest request, string gameId,
        byte[] key, DateTimeOffset now, CancellationToken cancellationToken) => GameWorkerAuthentication.VerifySignedAccountAsync(
            request, GameWalletAuthentication.AssertionHeader, Scope(gameId), key, now, MaximumBodyBytes, cancellationToken);

    private static string Scope(string gameId) => $"wallet-batch:{gameId}";
}
