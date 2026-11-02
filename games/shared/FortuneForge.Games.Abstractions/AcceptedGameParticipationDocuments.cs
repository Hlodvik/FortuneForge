using FortuneForge.Games.Abstractions.Auditing;

namespace FortuneForge.Games.Abstractions.Analytics;

/// <summary>Positive metadata to merge in the transaction that accepts genuine game participation.</summary>
public static class AcceptedGameParticipationDocuments
{
    public const string CaptureVersion = "canonical-game-commit-v1";

    private static readonly HashSet<string> CanonicalGames = new(StringComparer.Ordinal)
    {
        "blackjack", "texas-holdem", "solitaire", "free-solitaire", "video-poker", "baccarat",
        "dragon-tiger", "hi-lo", "andar-bahar", "mines", "casino-war", "hearts",
        "three-card-poker", "house-holdem", "keno", "sic-bo", "roulette", "craps", "liars-dice",
        "asteroids", "flappy", "2048", "drop-merge", "snake", "horse-flight",
        "wukong-journey-to-the-west", "rainbow-realm", "pirates-fortune", "gods-of-olympus",
        "reel-riches", "high-noon-fortune", "royal-draw", "arcane-archives", "cosmic-fortune",
        "dino-dominion", "neon-nights", "jungle-jackpot", "ocean-odyssey", "samurai-fortune",
        "candy-carnival", "phantom-manor", "nordic-legends", "desert-treasures", "robot-revolution",
        "dragon-hoard",
    };

    /// <summary>
    /// Merge Fields at catalogPlayerActivity/{DocOwnerKey}/games/{GameId} in the same accepted
    /// state or financial commit. The caller owns participation eligibility and runtime-ID mapping.
    /// This observed time is not a first-play date. Existing provenance and recency are untouched.
    /// </summary>
    public static (string DocOwnerKey, string GameId, Dictionary<string, object> Fields) Create(
        string userId, string canonicalGameId, DateTime acceptedAtUtc)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(userId);
        if (userId.Length > 512 || userId.Any(char.IsControl))
            throw new ArgumentException("A game participation witness requires a valid account identifier.", nameof(userId));
        if (!CanonicalGames.Contains(canonicalGameId))
            throw new ArgumentException("A game participation witness requires a canonical catalogue game.", nameof(canonicalGameId));
        if (acceptedAtUtc.Kind != DateTimeKind.Utc)
            throw new ArgumentException("A game participation witness requires a server UTC time.", nameof(acceptedAtUtc));

        return (GameSecurityEventDocuments.Hash(userId), canonicalGameId, new()
        {
            ["hasPlayed"] = true,
            ["experienceSchemaVersion"] = 1L,
            ["experienceCaptureVersion"] = CaptureVersion,
            ["experienceObservedAt"] = acceptedAtUtc,
        });
    }
}
