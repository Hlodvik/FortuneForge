using FortuneForge.Games.Slots.Runtime;
using GodsOfOlympusTumbleSpinOutcome = FortuneForge.Games.GodsOfOlympus.GodsOfOlympusTumbleSpinOutcome;
using FortuneForge.Server.Slots.Bonuses;
using FortuneForge.Server.Slots.Models;
using FortuneForge.Server.Slots.Reels;
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

namespace FortuneForge.Server.Slots.Games;

/// <summary>
/// Host composition only. Every delegate crosses into one independently owned
/// game package; this registry contains no outcome policy.
/// </summary>
public sealed class StandaloneSlotPackageRegistry
{
    private delegate ClassicSlotSpinOutcome SpinDelegate(
        long wagerPoints,
        string playerId,
        bool specialBoostApplied,
        long currentEnergyBalance,
        string? featureMode,
        int? persistedPityMisses,
        bool isFreeSpin);

    private sealed record DefinitionBundle(
        Func<GameDefinition> Game,
        Func<SymbolSetDefinition> Symbols,
        Func<ReelSetDefinition> Reels,
        Func<PaytableDefinition> Paytable);

    private static readonly IReadOnlyDictionary<string, DefinitionBundle> Definitions =
        new Dictionary<string, DefinitionBundle>(StringComparer.Ordinal)
        {
            [WukongEngine.GameId] = new(WukongEngine.CreateGame, WukongEngine.CreateSymbolSet,
                WukongEngine.CreateReelSet, WukongEngine.CreatePaytable),
            [RainbowEngine.GameId] = new(RainbowEngine.CreateGame, RainbowEngine.CreateSymbolSet,
                RainbowEngine.CreateReelSet, RainbowEngine.CreatePaytable),
            [PiratesEngine.GameId] = new(PiratesEngine.CreateGame, PiratesEngine.CreateSymbolSet,
                PiratesEngine.CreateReelSet, PiratesEngine.CreatePaytable),
            [OlympusEngine.GameId] = new(OlympusEngine.CreateGame, OlympusEngine.CreateSymbolSet,
                OlympusEngine.CreateReelSet, OlympusEngine.CreatePaytable),
            [ReelRichesEngine.GameId] = new(ReelRichesEngine.CreateGame, ReelRichesEngine.CreateSymbolSet,
                ReelRichesEngine.CreateReelSet, ReelRichesEngine.CreatePaytable),
            [HighNoonEngine.GameId] = new(HighNoonEngine.CreateGame, HighNoonEngine.CreateSymbolSet,
                HighNoonEngine.CreateReelSet, HighNoonEngine.CreatePaytable),
            [RoyalEngine.GameId] = new(RoyalEngine.CreateGame, RoyalEngine.CreateSymbolSet,
                RoyalEngine.CreateReelSet, RoyalEngine.CreatePaytable),
            [ArcaneEngine.GameId] = new(ArcaneEngine.CreateGame, ArcaneEngine.CreateSymbolSet,
                ArcaneEngine.CreateReelSet, ArcaneEngine.CreatePaytable),
            [CosmicEngine.GameId] = new(CosmicEngine.CreateGame, CosmicEngine.CreateSymbolSet,
                CosmicEngine.CreateReelSet, CosmicEngine.CreatePaytable),
            [OceanEngine.GameId] = new(OceanEngine.CreateGame, OceanEngine.CreateSymbolSet,
                OceanEngine.CreateReelSet, OceanEngine.CreatePaytable),
            [SamuraiEngine.GameId] = new(SamuraiEngine.CreateGame, SamuraiEngine.CreateSymbolSet,
                SamuraiEngine.CreateReelSet, SamuraiEngine.CreatePaytable),
            [CandyEngine.GameId] = new(CandyEngine.CreateGame, CandyEngine.CreateSymbolSet,
                CandyEngine.CreateReelSet, CandyEngine.CreatePaytable),
            [PhantomEngine.GameId] = new(PhantomEngine.CreateGame, PhantomEngine.CreateSymbolSet,
                PhantomEngine.CreateReelSet, PhantomEngine.CreatePaytable),
            [NordicEngine.GameId] = new(NordicEngine.CreateGame, NordicEngine.CreateSymbolSet,
                NordicEngine.CreateReelSet, NordicEngine.CreatePaytable),
            [DesertEngine.GameId] = new(DesertEngine.CreateGame, DesertEngine.CreateSymbolSet,
                DesertEngine.CreateReelSet, DesertEngine.CreatePaytable),
            [RobotEngine.GameId] = new(RobotEngine.CreateGame, RobotEngine.CreateSymbolSet,
                RobotEngine.CreateReelSet, RobotEngine.CreatePaytable)
        };

    private readonly IReadOnlyDictionary<string, SpinDelegate> spinners;
    private readonly OlympusEngine olympusEngine;

    public StandaloneSlotPackageRegistry(IRandomIndexSource random)
    {
        olympusEngine = new OlympusEngine(random);
        spinners = new Dictionary<string, SpinDelegate>(StringComparer.Ordinal)
        {
            [WukongEngine.GameId] = new WukongEngine(random).Spin,
            [RainbowEngine.GameId] = new RainbowEngine(random).Spin,
            [PiratesEngine.GameId] = new PiratesEngine(random).Spin,
            [OlympusEngine.GameId] = olympusEngine.Spin,
            [ReelRichesEngine.GameId] = new ReelRichesEngine(random).Spin,
            [HighNoonEngine.GameId] = new HighNoonEngine(random).Spin,
            [RoyalEngine.GameId] = new RoyalEngine(random).Spin,
            [ArcaneEngine.GameId] = new ArcaneEngine(random).Spin,
            [CosmicEngine.GameId] = new CosmicEngine(random).Spin,
            [OceanEngine.GameId] = new OceanEngine(random).Spin,
            [SamuraiEngine.GameId] = new SamuraiEngine(random).Spin,
            [CandyEngine.GameId] = new CandyEngine(random).Spin,
            [PhantomEngine.GameId] = new PhantomEngine(random).Spin,
            [NordicEngine.GameId] = new NordicEngine(random).Spin,
            [DesertEngine.GameId] = new DesertEngine(random).Spin,
            [RobotEngine.GameId] = new RobotEngine(random).Spin
        };
    }

    public static bool Owns(string gameId) => Definitions.ContainsKey(gameId);

    public static GameDefinition? GetGame(string id) =>
        Definitions.TryGetValue(id, out var package) ? package.Game() : null;

    public static SymbolSetDefinition? GetSymbolSet(string id) => Definitions.Values
        .Select(package => package.Symbols()).FirstOrDefault(value => value.Id == id);

    public static ReelSetDefinition? GetReelSet(string id) => Definitions.Values
        .Select(package => package.Reels()).FirstOrDefault(value => value.Id == id);

    public static PaytableDefinition? GetPaytable(string id) => Definitions.Values
        .Select(package => package.Paytable()).FirstOrDefault(value => value.Id == id);

    public bool TrySpin(string gameId, long wagerPoints, string playerId,
        bool specialBoostApplied, long currentEnergyBalance, string? featureMode,
        int? persistedPityMisses, bool isFreeSpin, out SpinResult result)
    {
        if (!spinners.TryGetValue(gameId, out var spin))
        {
            result = null!;
            return false;
        }

        result = MapOutcome(spin(wagerPoints, playerId, specialBoostApplied,
            currentEnergyBalance, featureMode, persistedPityMisses, isFreeSpin));
        return true;
    }

    internal static SpinResult MapOutcome(ClassicSlotSpinOutcome outcome)
    {
        SlotSpecialRoundProfiles.TryGet(outcome.GameId, out var profile);
        var audit = outcome.Audit;
        return new SpinResult(outcome.SpinId, outcome.GameId, outcome.ReelSetId,
            outcome.SymbolSetId, outcome.PaytableId, outcome.WagerPoints,
            outcome.PointValueInCents, outcome.ReelStops, outcome.Reels,
            outcome.Payout, outcome.ConsecutiveFiveMisses,
            outcome.FiveMatchPityTriggered, outcome.FreeSpinsAwarded,
            outcome.SpecialPointsAwarded, outcome.EnergyAwarded,
            outcome.SpecialBoostApplied)
        {
            IsFreeSpin = outcome.IsFreeSpin,
            EngineAudit = new SlotSpinEngineAudit(audit.EngineVersion,
                audit.GeneratedAtUtc, audit.BaseGame, audit.EffectiveGame,
                audit.ReelSet, audit.SymbolSet, audit.Paytable, profile,
                audit.PityMissesBefore, audit.EnergyBalanceBefore,
                audit.FeatureMode, audit.GenerationAttempts,
                audit.ReelsBeforeFrequency, audit.ReelsAfterFrequency)
            {
                IsDerivedFreeSpin = outcome.IsFreeSpin && outcome.GameId == ArcaneEngine.GameId
            },
            MonkeyPawCount = outcome.PawCount,
            MoneyGrabPoints = outcome.CollectorPoints,
            BananaBonusPoints = outcome.PatternBonusPoints,
            LightningCollection = outcome.GameId == ArcaneEngine.GameId
                ? ArcaneLightningCollector.Calculate(outcome.Reels, outcome.WagerPoints)
                : null,
            SealsAwarded = outcome.CollectionsAwarded
        };
    }

    public SpinResult SpinOlympusTumble(string engineVersion, long wagerPoints,
        bool showcase, int pityMissesBefore, long energyBalanceBefore)
    {
        return MapTumble(olympusEngine.SpinTumble(wagerPoints, showcase, engineVersion), showcase,
            pityMissesBefore, energyBalanceBefore);
    }

    internal static SpinResult MapTumble(GodsOfOlympusTumbleSpinOutcome package, bool showcase,
        int pityMissesBefore, long energyBalanceBefore)
    {
        var engineVersion = package.EngineVersion;
        var wagerPoints = package.WagerPoints;
        var symbols = OlympusEngine.CreateSymbolSet();
        var reels = OlympusEngine.CreateReelSet();
        var paytable = OlympusEngine.CreatePaytable();
        var tumble = new OlympusTumbleOutcome(
            package.Outcome.InitialReels,
            package.Outcome.Steps.Select(step => new OlympusTumbleStep(
                step.Awards.Select(award => new OlympusTumbleAward(
                    award.Symbol, award.AmountPoints, award.Positions)).ToArray(),
                step.Cleared, step.Reels, step.DropRows, step.AmountPoints)).ToArray(),
            package.Outcome.TotalPoints);

        return new SpinResult(package.SpinId, package.GameId, engineVersion,
            symbols.Id, engineVersion, wagerPoints, package.PointValueInCents,
            new int[tumble.InitialReels.Count], tumble.InitialReels,
            new SpinPayout(tumble.TotalPoints, []), pityMissesBefore, false,
            0, 0, 0, false)
        {
            OlympusTumble = tumble,
            EngineAudit = new SlotSpinEngineAudit(engineVersion, DateTime.UtcNow,
                package.BaseGame, package.BaseGame, reels, symbols, paytable, null,
                pityMissesBefore, energyBalanceBefore, null, 1,
                tumble.InitialReels, tumble.InitialReels)
            {
                OlympusTumble = new OlympusTumbleEvidence(
                    showcase, package.DrawIndexes)
            }
        };
    }
}
