using System.Security.Cryptography;
using System.Text;

namespace FortuneForge.Games.Abstractions.Analytics;

/// <summary>Authoritative completed human participation; amounts are gross Rand cents, never wallet deltas.</summary>
public sealed record GameAnalyticsSettlement(
    string GameId,
    string RoundId,
    string UserId,
    string Mode,
    bool IsHuman,
    DateTime CompletedAtUtc,
    DateTime? StartedAtUtc,
    long? WagerCents,
    long? PayoutCents,
    string SourceCollection,
    string SourceDocumentId);

public sealed record GameAnalyticsOutboxDocument(string Id, Dictionary<string, object> Fields);

/// <summary>
/// Framework-independent outbox schema. Firestore stores write Fields to
/// CollectionName/Id in the SAME transaction as settlement. DateTime values are
/// UTC and are natively serialized to Firestore timestamps by the host SDK.
/// </summary>
public static class GameAnalyticsOutbox
{
    public const string CollectionName = "gameAnalyticsSettlementOutbox";
    public static readonly IReadOnlySet<string> GameIds = new HashSet<string>(StringComparer.Ordinal)
    {
        "blackjack", "dragon-tiger", "mines", "texas-holdem", "solitaire", "free-solitaire", "three-card-poker", "house-holdem", "hearts",
        "andar-bahar", "hi-lo", "baccarat", "casino-war", "video-poker", "roulette", "craps", "keno", "sic-bo", "liars-dice", "2048", "drop-merge",
        "wukong-journey-to-the-west", "rainbow-realm", "pirates-fortune", "gods-of-olympus", "reel-riches", "high-noon-fortune",
        "royal-draw", "arcane-archives", "cosmic-fortune", "dino-dominion", "neon-nights", "jungle-jackpot", "ocean-odyssey",
        "samurai-fortune", "candy-carnival", "phantom-manor", "nordic-legends", "desert-treasures", "robot-revolution", "dragon-hoard",
    };

    public static GameAnalyticsOutboxDocument? Create(GameAnalyticsSettlement value)
    {
        if (!value.IsHuman) return null;
        if (!GameIds.Contains(value.GameId) || value.Mode is not ("paid" or "practice" or "free"))
            throw new ArgumentException("Analytics requires a canonical game and explicit paid, practice, or free mode.");
        if (string.IsNullOrWhiteSpace(value.RoundId) || value.RoundId.Length > 512 || string.IsNullOrWhiteSpace(value.UserId) || value.UserId.Length > 512)
            throw new ArgumentException("Analytics requires stable round and user identifiers.");
        if (value.CompletedAtUtc.Kind != DateTimeKind.Utc || value.StartedAtUtc is { Kind: not DateTimeKind.Utc } || value.StartedAtUtc > value.CompletedAtUtc)
            throw new ArgumentException("Analytics timestamps must be authoritative UTC dates in order.");
        if (value.WagerCents < 0 || value.PayoutCents < 0 || (value.WagerCents is null) != (value.PayoutCents is null))
            throw new ArgumentException("Both monetary totals must be known nonnegative cents or both unknown.");
        if (string.IsNullOrWhiteSpace(value.SourceCollection) || string.IsNullOrWhiteSpace(value.SourceDocumentId))
            throw new ArgumentException("Analytics requires the authoritative source reference.");
        var roundKey = Hash($"{value.GameId}\n{value.Mode}\n{value.RoundId}");
        return new(Hash($"{roundKey}\n{value.UserId}"), new Dictionary<string, object>
        {
            ["schemaVersion"] = 1L, ["pending"] = true, ["gameId"] = value.GameId, ["roundId"] = value.RoundId,
            ["roundKey"] = roundKey, ["userId"] = value.UserId, ["mode"] = value.Mode, ["isHuman"] = true,
            ["completedAt"] = value.CompletedAtUtc, ["startedAt"] = value.StartedAtUtc is { } start ? start : null!,
            ["wagerCents"] = value.WagerCents is { } wager ? wager : null!,
            ["payoutCents"] = value.PayoutCents is { } payout ? payout : null!,
            ["sourceCollection"] = value.SourceCollection, ["sourceDocumentId"] = value.SourceDocumentId,
        });
    }

    public static string Hash(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();
}
