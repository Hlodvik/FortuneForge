using FortuneForge.Server.Slots.Configuration;
using FortuneForge.Server.Slots.Games.GoldFoundry;
using FortuneForge.Server.Slots.Models;
using FortuneForge.Server.Slots.Reels;
using FortuneForge.Games.Slots.Runtime;
using ArcaneEngine = FortuneForge.Games.ArcaneArchives.ArcaneArchivesEngine;
using CandyEngine = FortuneForge.Games.CandyCarnival.CandyCarnivalEngine;
using CosmicEngine = FortuneForge.Games.CosmicFortune.CosmicFortuneEngine;
using DesertEngine = FortuneForge.Games.DesertTreasures.DesertTreasuresEngine;
using OlympusEngine = FortuneForge.Games.GodsOfOlympus.GodsOfOlympusEngine;
using HighNoonEngine = FortuneForge.Games.HighNoonFortune.HighNoonFortuneEngine;
using NordicEngine = FortuneForge.Games.NordicLegends.NordicLegendsEngine;
using OceanEngine = FortuneForge.Games.OceanOdyssey.OceanOdysseyEngine;
using PhantomEngine = FortuneForge.Games.PhantomManor.PhantomManorEngine;
using PiratesEngine = FortuneForge.Games.PiratesFortune.PiratesFortuneEngine;
using RainbowEngine = FortuneForge.Games.RainbowRealm.RainbowRealmEngine;
using ReelRichesEngine = FortuneForge.Games.ReelRiches.ReelRichesEngine;
using RobotEngine = FortuneForge.Games.RobotRevolution.RobotRevolutionEngine;
using RoyalEngine = FortuneForge.Games.RoyalDraw.RoyalDrawEngine;
using SamuraiEngine = FortuneForge.Games.SamuraiFortune.SamuraiFortuneEngine;
using WukongEngine = FortuneForge.Games.Wukong.WukongEngine;

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
    public const string WukongGameId = WukongEngine.GameId;
    public const string RainbowRealmGameId = RainbowEngine.GameId;
    public const string CosmicFortuneGameId = CosmicEngine.GameId;
    public const string HighNoonFortuneGameId = HighNoonEngine.GameId;
    public const string GodsOfOlympusGameId = OlympusEngine.GameId;
    public const string PiratesFortuneGameId = PiratesEngine.GameId;
    public const string RoyalDrawGameId = RoyalEngine.GameId;
    public const string SamuraiFortuneGameId = SamuraiEngine.GameId;
    public const string RobotRevolutionGameId = RobotEngine.GameId;
    public const string PhantomManorGameId = PhantomEngine.GameId;
    public const string OceanOdysseyGameId = OceanEngine.GameId;
    public const string DragonHoardGameId = MidnightVaultSlotDefinitions.GameId;
    public const string JadeDragonGameId = "empty-slots-v1";
    // Historical identifier retained for saved evidence and source compatibility.
    public const string JungleJackpotGameId = "jungle-jackpot-v1";
    public const string CandyCarnivalGameId = CandyEngine.GameId;
    public const string DesertTreasuresGameId = DesertEngine.GameId;
    public const string NeonNightsGameId = NeonArcadeSlotDefinitions.GameId;
    public const string NordicLegendsGameId = NordicEngine.GameId;
    public const string ReelRichesGameId = ReelRichesEngine.GameId;
    public const string ArcaneArchivesGameId = ArcaneEngine.GameId;

    private static readonly string[] Modes = ["sync", "rows", "paw", "rand", "arcade"];
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
            [WukongGameId] = FromPackage(WukongEngine.Profile),
            [RainbowRealmGameId] = FromPackage(RainbowEngine.Profile),
            [CosmicFortuneGameId] = FromPackage(CosmicEngine.Profile),
            [HighNoonFortuneGameId] = FromPackage(HighNoonEngine.Profile),
            [GodsOfOlympusGameId] = FromPackage(OlympusEngine.Profile),
            [PiratesFortuneGameId] = FromPackage(PiratesEngine.Profile),
            [RoyalDrawGameId] = FromPackage(RoyalEngine.Profile),
            [SamuraiFortuneGameId] = FromPackage(SamuraiEngine.Profile),
            [RobotRevolutionGameId] = FromPackage(RobotEngine.Profile),
            [PhantomManorGameId] = FromPackage(PhantomEngine.Profile),
            [OceanOdysseyGameId] = FromPackage(OceanEngine.Profile),
            [DragonHoardGameId] = Games.MidnightVault.MidnightVaultEngine.Profile,
            [JadeDragonGameId] = Games.JadeDragon.JadeDragonEngine.Profile,
            [CandyCarnivalGameId] = FromPackage(CandyEngine.Profile),
            [DesertTreasuresGameId] = FromPackage(DesertEngine.Profile),
            // Historical host/audit descriptor only; preserve its recorded
            // values. Current Neon bypasses these scatter/collection policies.
            [NeonNightsGameId] = new(NeonNightsGameId, 3, 0, 26, 0, false, false, false,
                BaseReelSetId: NeonArcadeSlotDefinitions.ReelSetId,
                PayoutMultiplier: NeonArcadeSlotDefinitions.PayoutMultiplier),
            [NordicLegendsGameId] = FromPackage(NordicEngine.Profile),
            [ReelRichesGameId] = FromPackage(ReelRichesEngine.Profile),
            [ArcaneArchivesGameId] = FromPackage(ArcaneEngine.Profile),
            [GoldFoundrySlotDefinitions.GameId] = Games.GoldFoundry.GoldFoundryEngine.Profile
        };

    public static bool TryGet(string gameId, out SlotSpecialRoundProfile profile) =>
        Profiles.TryGetValue(gameId, out profile!);

    public static IReadOnlyList<SlotSpecialRoundProfile> All { get; } = Profiles.Values.ToArray();

    private static SlotSpecialRoundProfile FromPackage(SlotProfile profile) => new(
        profile.GameId,
        profile.ScatterRequiredSymbols,
        profile.ScatterAwardedSpins,
        profile.CollectionTarget,
        profile.CollectionAwardedSpins,
        profile.UsesCollections,
        profile.UsesEnergy,
        profile.UsesDirectValueTokens,
        profile.ScatterFeatureMode,
        profile.CollectionFeatureMode,
        profile.BaseReelSetId,
        profile.TargetHitRate,
        profile.PaylinePatternIds,
        profile.PayoutMultiplier);

    public static bool IsFeatureMode(string? value)
    {
        if (value == FortuneForge.Games.JadeDragon.JadeDragonCollections.CollectionFeatureMode) return true;
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
        bool energyCompleted,
        Guid spinId = default)
    {
        if (profile.GameId == JadeDragonGameId)
            return Games.JadeDragon.JadeDragonEngine.SettleCollections(current, awarded, wagerPoints);
        if (!profile.UsesCollections)
        {
            return new SlotSpecialRoundProgress(0, null, []);
        }

        // Historic demo evidence keeps its frozen fifteen-gem profile and rule.
        if (profile.GameId == PiratesFortuneGameId &&
            profile.CollectionTarget == PiratesGemBonus.MaximumGems)
            return SettlePiratesDemo(profile, current, awarded, wagerPoints, spinId);

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

    private static SlotSpecialRoundProgress SettlePiratesDemo(
        SlotSpecialRoundProfile profile,
        IReadOnlyList<SlotSealCollection> current,
        IReadOnlyDictionary<string, int> awarded,
        long wagerPoints,
        Guid spinId)
    {
        var states = Modes.ToDictionary(mode => mode, mode => new RoundState(), StringComparer.Ordinal);
        foreach (var collection in current.Where(collection => states.ContainsKey(collection.SealId)))
            states[collection.SealId] = new RoundState(collection.Count, collection.AverageWagerPoints);

        var awardedByMode = Modes.ToDictionary(mode => mode, _ => 0, StringComparer.Ordinal);
        foreach (var (sealId, count) in awarded)
        {
            if (count > 0 && ModesBySeal.TryGetValue(sealId, out var mode))
                awardedByMode[mode] = checked(awardedByMode[mode] + count);
        }

        var freeSpins = 0;
        string? featureMode = null;
        foreach (var mode in Modes)
        {
            var state = states[mode];
            var progress = PiratesGemBonus.Advance(spinId, mode, state.Count,
                checked(state.AverageWagerPoints * state.Count), awardedByMode[mode], wagerPoints);
            if (progress.Awards.Count > 0)
            {
                freeSpins = checked(freeSpins +
                    progress.Awards.Count * profile.CollectionAwardedSpins);
                featureMode = PiratesGemBonus.AddFeatureMode(featureMode, mode);
            }
            var average = progress.Count == 0 ? 0 :
                (long)Math.Round(progress.WagerTotal / (decimal)progress.Count,
                    MidpointRounding.AwayFromZero);
            states[mode] = new RoundState(progress.Count, average);
        }

        return new SlotSpecialRoundProgress(freeSpins, featureMode,
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
