using FortuneForge.Server.Slots.Models;
using FortuneForge.Server.Slots.Reels;

namespace FortuneForge.Server.Slots.Bonuses;

public sealed record SlotSpecialRoundProfile(
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
    decimal PayoutMultiplier = 1m)
{
    public GameFreeGamesDefinition Configure(GameFreeGamesDefinition source) => new()
    {
        SymbolId = source.SymbolId,
        RequiredSymbols = ScatterRequiredSymbols,
        AwardedSpins = ScatterAwardedSpins,
        VisibleFrequencyDivisor = source.VisibleFrequencyDivisor
    };
}

public sealed record SlotSpecialRoundProgress(
    int FreeSpinsAwarded,
    string? FeatureMode,
    IReadOnlyList<SlotSealCollection> Collections);

public static class SlotSpecialRoundProfiles
{
    public const string ClassicGameId = "classic-demo-v1";
    public const string RainbowRealmGameId = "rainbow-realm-fruits-v1";
    public const string CosmicFortuneGameId = "cosmic-fortune-v1";
    public const string HighNoonFortuneGameId = "high-noon-fortune-v1";
    public const string GodsOfOlympusGameId = "gods-of-olympus-v1";
    public const string PiratesFortuneGameId = "pirates-fortune-v1";
    public const string RoyalDrawGameId = "royal-draw-v1";
    public const string SamuraiFortuneGameId = "samurai-fortune-v1";
    public const string RobotRevolutionGameId = "robot-revolution-v1";
    public const string PhantomManorGameId = "phantom-manor-v1";
    public const string OceanOdysseyGameId = "ocean-odyssey-v1";
    public const string DragonHoardGameId = "dragon-hoard-v1";
    public const string JungleJackpotGameId = "jungle-jackpot-v1";
    public const string CandyCarnivalGameId = "candy-carnival-v1";
    public const string DesertTreasuresGameId = "desert-treasures-v1";
    public const string NeonNightsGameId = "neon-nights-v1";
    public const string NordicLegendsGameId = "nordic-legends-v1";
    public const string ReelRichesGameId = "reel-riches-v1";
    public const string ArcaneArchivesGameId = "arcane-archives-v1";
    public const string DinoDominionGameId = "dino-dominion-v1";

    private static readonly string[] Modes = ["sync", "rows", "paw", "rand"];
    private static readonly IReadOnlyDictionary<string, string> ModesBySeal =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["SEAL_SYNC"] = "sync",
            ["SEAL_ROWS"] = "rows",
            ["SEAL_PAW"] = "paw",
            ["SEAL_RAND"] = "rand"
        };
    private static readonly IReadOnlyDictionary<string, SlotSpecialRoundProfile> Profiles =
        new Dictionary<string, SlotSpecialRoundProfile>(StringComparer.Ordinal)
        {
            [ClassicGameId] = new(ClassicGameId, 3, 5, 40, 10),
            [RainbowRealmGameId] = new(
                RainbowRealmGameId,
                3,
                5,
                60,
                8),
            [CosmicFortuneGameId] = new(
                CosmicFortuneGameId, 3, 6, 24, 7, true, true, false, null, "sync-rows",
                PayoutMultiplier: 0.856m),
            [HighNoonFortuneGameId] = new(
                HighNoonFortuneGameId, 3, 5, 20, 8, false, false, true, "sync",
                TargetHitRate: 0.32m,
                PayoutMultiplier: 1.105m),
            [GodsOfOlympusGameId] = new(GodsOfOlympusGameId, 4, 6, 28, 6, true, false, false, null, "paw-rand"),
            [PiratesFortuneGameId] = new(
                PiratesFortuneGameId,
                3,
                7,
                15,
                10,
                true,
                false,
                true,
                null,
                "paw",
                PiratesFortuneBaseReelSet.Id,
                0.32m,
                PayoutMultiplier: 0.724m),
            [RoyalDrawGameId] = new(
                RoyalDrawGameId, 3, 6, 24, 7, true, true, true, null, "rows-rand",
                PayoutMultiplier: 0.942m),
            [SamuraiFortuneGameId] = new(
                SamuraiFortuneGameId, 4, 6, 26, 7, true, false, false, null, "sync-paw",
                PayoutMultiplier: 1.057m),
            [RobotRevolutionGameId] = new(
                RobotRevolutionGameId, 3, 7, 24, 8, false, false, false, "rows",
                TargetHitRate: 0.32m,
                PayoutMultiplier: 1.126m),
            [PhantomManorGameId] = new(
                PhantomManorGameId, 3, 5, 18, 9, false, false, false, "paw",
                TargetHitRate: 0.32m,
                PayoutMultiplier: 1.139m),
            [OceanOdysseyGameId] = new(
                OceanOdysseyGameId, 4, 6, 28, 6, true, true, false, null, "sync-rand",
                PayoutMultiplier: 1.071m),
            [DragonHoardGameId] = new(
                DragonHoardGameId, 3, 6, 30, 7, true, false, false, null, "rows-paw",
                PayoutMultiplier: 1.076m),
            [JungleJackpotGameId] = new(
                JungleJackpotGameId, 3, 6, 22, 8, true, false, false, null, "sync-rows-paw",
                TargetHitRate: 0.32m,
                PayoutMultiplier: 0.866m),
            [CandyCarnivalGameId] = new(
                CandyCarnivalGameId, 4, 5, 20, 8, false, false, false, "rand",
                TargetHitRate: 0.32m,
                PayoutMultiplier: 1.179m),
            [DesertTreasuresGameId] = new(
                DesertTreasuresGameId, 3, 7, 24, 7, true, true, true, null, "rows",
                PayoutMultiplier: 0.977m),
            [NeonNightsGameId] = new(NeonNightsGameId, 3, 6, 26, 7, true, false, false, null, "sync"),
            [NordicLegendsGameId] = new(
                NordicLegendsGameId, 4, 6, 28, 6, false, false, true, "sync-paw",
                TargetHitRate: 0.32m,
                PayoutMultiplier: 1.158m),
            [ReelRichesGameId] = new(
                ReelRichesGameId, 3, 5, 40, 10,
                CollectionFeatureMode: "sync-rows-rand",
                TargetHitRate: 0.29m,
                PaylinePatternIds: [1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15, 16, 21, 22, 23]),
            [ArcaneArchivesGameId] = new(
                ArcaneArchivesGameId, 3, 5, 40, 10,
                CollectionFeatureMode: "sync-paw-rand",
                PaylinePatternIds: [1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 18, 19, 20, 21, 22, 23],
                PayoutMultiplier: 0.716m),
            [DinoDominionGameId] = new(
                DinoDominionGameId, 3, 5, 40, 10,
                CollectionFeatureMode: "rows-paw-rand",
                PaylinePatternIds: [1, 2, 3, 4, 5, 6, 16, 17, 18, 19, 20, 21, 22, 23],
                PayoutMultiplier: 0.607m)
        };

    public static bool TryGet(string gameId, out SlotSpecialRoundProfile profile) =>
        Profiles.TryGetValue(gameId, out profile!);

    public static IReadOnlyList<SlotSpecialRoundProfile> All { get; } = Profiles.Values.ToArray();

    public static bool IsFeatureMode(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return false;

        var modes = value.Split('-', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return modes.Length > 0 && modes.All(mode => Modes.Contains(mode, StringComparer.Ordinal));
    }

    public static bool HasFeatureMode(string? value, string mode) =>
        IsFeatureMode(value) && value!.Split('-', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Contains(mode, StringComparer.Ordinal);

    public static SlotSpecialRoundProgress SettleDemo(
        SlotSpecialRoundProfile profile,
        IReadOnlyList<SlotSealCollection> current,
        IReadOnlyDictionary<string, int> awarded,
        long wagerPoints,
        bool energyCompleted)
    {
        if (!profile.UsesCollections)
        {
            return new SlotSpecialRoundProgress(0, null, []);
        }

        var states = Modes.ToDictionary(mode => mode, mode => new RoundState(), StringComparer.Ordinal);
        foreach (var collection in current.Where(collection => states.ContainsKey(collection.SealId)))
        {
            states[collection.SealId] = new RoundState(collection.Count, collection.AverageWagerPoints);
        }

        foreach (var (sealId, count) in awarded)
        {
            if (count <= 0 || !ModesBySeal.TryGetValue(sealId, out var mode)) continue;
            states[mode] = states[mode].Add(count, wagerPoints);
        }

        if (energyCompleted && profile.UsesEnergy)
        {
            var mode = Modes.OrderBy(mode => states[mode].Count).First();
            states[mode] = states[mode].Add(Math.Max(1, profile.CollectionTarget - states[mode].Count), wagerPoints);
        }

        var freeSpins = 0;
        string? featureMode = null;
        foreach (var mode in Modes)
        {
            var state = states[mode];
            if (state.Count < profile.CollectionTarget) continue;

            freeSpins += profile.CollectionAwardedSpins;
            featureMode ??= profile.CollectionFeatureMode ?? mode;
            states[mode] = state with { Count = state.Count % profile.CollectionTarget };
        }

        return new SlotSpecialRoundProgress(
            freeSpins,
            featureMode,
            Modes.Select(mode => states[mode].ToCollection(mode, profile.CollectionTarget)).ToArray());
    }

    private sealed record RoundState(int Count = 0, long AverageWagerPoints = 0)
    {
        public RoundState Add(int count, long wagerPoints)
        {
            var nextCount = checked(Count + count);
            var total = checked(AverageWagerPoints * Count + wagerPoints * count);
            return new RoundState(nextCount, nextCount == 0 ? 0 : (long)Math.Round(total / (decimal)nextCount));
        }

        public SlotSealCollection ToCollection(string mode, int target) =>
            new(mode, Math.Min(Count, target), AverageWagerPoints, target);
    }
}
