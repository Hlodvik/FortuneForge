using FortuneForge.Games.Slots;

namespace FortuneForge.Games.Slots.Runtime;

/// <summary>
/// Immutable game-owned choices consumed by the neutral classic slot runtime.
/// The runtime deliberately contains no catalogue or game identifiers.
/// </summary>
public sealed record SlotProfile(
    string GameId,
    int ScatterRequiredSymbols,
    int ScatterAwardedSpins,
    int CollectionTarget,
    int CollectionAwardedSpins,
    bool UsesCollections = true,
    bool UsesEnergy = true,
    bool UsesDirectValueTokens = true,
    string? ScatterFeatureMode = null,
    string? CollectionFeatureMode = null,
    string? BaseReelSetId = null,
    decimal? TargetHitRate = null,
    IReadOnlyList<int>? PaylinePatternIds = null,
    decimal PayoutMultiplier = 1m,
    bool PreserveEnergySymbolsWithoutMeter = false)
{
    private static readonly string[] KnownModes = ["sync", "rows", "paw", "rand", "arcade"];

    public bool HasFeatureMode(string? value, string mode) =>
        IsFeatureMode(value) && value!.Split('-', StringSplitOptions.RemoveEmptyEntries |
            StringSplitOptions.TrimEntries).Contains(mode, StringComparer.Ordinal);

    public static bool IsFeatureMode(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return false;
        var modes = value.Split('-', StringSplitOptions.RemoveEmptyEntries |
            StringSplitOptions.TrimEntries);
        return modes.Length > 0 && modes.All(mode => KnownModes.Contains(mode, StringComparer.Ordinal));
    }

    internal GameFreeGamesDefinition? CreateFreeGames() => ScatterAwardedSpins <= 0 ? null : new()
    {
        SymbolId = "FREE",
        RequiredSymbols = ScatterRequiredSymbols,
        AwardedSpins = ScatterAwardedSpins,
        VisibleFrequencyDivisor = 2
    };
}
