// Edited: 2026-11-18T18:00:51-06:00 | Computer: Vinnabok
// Edited: 2026-11-18T16:54:32-06:00 | Computer: Vinnabok
// Edited: 2026-11-18T16:45:56-06:00 | Computer: Vinnabok
// Edited: 2026-11-18T16:26:20-06:00 | Computer: Vinnabok
// Edited: 2026-11-18T14:45:02-06:00 | Computer: Vinnabok
using FortuneForge.Games.Slots;
using FortuneForge.Games.Slots.Runtime;
using static FortuneForge.Games.ArcaneArchives.ArcaneWandCollector;

namespace FortuneForge.Games.ArcaneArchives;

public sealed class ArcaneArchivesEngine
{
    public const string GameId = "arcane-archives-v1";
    public const string EngineVersion = "arcane-archives-paylines-v5";
    public const string PreviousWandEngineVersion = "arcane-archives-paylines-v2";
    public const string ReelSetId = "arcane-archives-payline-reels-v5";
    public const string TomeReelSetId = "arcane-archives-tome-reels-v5";
    public const string SymbolSetId = "arcane-archives-payline-symbols-v2";
    public const string PaytableId = "arcane-archives-paytable-v2";
    private const string RotatingWandEngineVersion = "arcane-archives-paylines-v4";
    private const string RotatingReelSetId = "arcane-archives-payline-reels-v4";
    private const string RotatingTomeReelSetId = "arcane-archives-tome-reels-v4";
    private const string SparseWandEngineVersion = "arcane-archives-paylines-v3";
    private const string SparseReelSetId = "arcane-archives-payline-reels-v3";
    private const string SparseTomeReelSetId = "arcane-archives-tome-reels-v3";
    private const string PreviousWandReelSetId = "arcane-archives-payline-reels-v2";
    private const string PreviousTomeReelSetId = "arcane-archives-tome-reels-v2";
    private const string PreviousReelSetId = "arcane-archives-payline-reels-v1";
    internal static IReadOnlyList<string> OghamSymbols { get; } = Array.AsReadOnly<string>(["2", "3", "4"]);
    internal static IReadOnlyList<string> PremiumSymbols { get; } = Array.AsReadOnly<string>(["5", "6"]);
    private static readonly string[] PaylineSymbols = [.. OghamSymbols, .. PremiumSymbols];

    public static SlotProfile Profile { get; } = new(GameId, 0, 0, 0, 0,
        UsesCollections: false, UsesEnergy: false, UsesDirectValueTokens: false,
        BaseReelSetId: ReelSetId,
        PaylinePatternIds: [1,2,3,4,5,6,7,8,9,10,18,19,20,21,22,23],
        PayoutMultiplier: 0.716m, PreserveEnergySymbolsWithoutMeter: true);

    private static readonly SlotProfile TomeProfile = Profile with { BaseReelSetId = TomeReelSetId };
    private static readonly FrozenSlotDefinitions Definitions = CreateDefinitions(Profile, false);
    private static readonly FrozenSlotDefinitions TomeDefinitions = CreateDefinitions(TomeProfile, true);
    private static readonly ReelSetDefinition[] AlternateReelSets = CreateAlternateReelSets(Definitions);
    private static readonly ReelSetDefinition[] AlternateTomeReelSets = CreateAlternateReelSets(TomeDefinitions);
    private static readonly IReadOnlyDictionary<string, ReelSetDefinition> CurrentReelSets =
        new[] { Definitions.ReelSet, TomeDefinitions.ReelSet }.Concat(AlternateReelSets).Concat(AlternateTomeReelSets)
            .ToDictionary(set => set.Id, StringComparer.Ordinal);
    private static readonly IReadOnlySet<string> CurrentTomeReelSetIds =
        AlternateTomeReelSets.Select(set => set.Id).Append(TomeReelSetId).ToHashSet(StringComparer.Ordinal);
    private static readonly ClassicSlotEngineOptions EngineOptions = new()
    {
        AllowFreeSpinCarryWager = true,
        UsesPatternBonus = false,
        FeaturePayoutCalculator = static (reels, wagerPoints, _) =>
        {
            var collection = Calculate(reels, wagerPoints);
            return new(0, 0, 0, collection is null ? [] : [CreatePayout(collection)]);
        }
    };

    private readonly ClassicSlotEngine engine;
    private readonly ClassicSlotEngine tomeEngine;

    public ArcaneArchivesEngine(IGameRandomSource random)
    {
        engine = new(Definitions, Profile, random, EngineVersion, EngineOptions, AlternateReelSets);
        tomeEngine = new(TomeDefinitions, TomeProfile, random, EngineVersion, EngineOptions, AlternateTomeReelSets);
    }

    // Existing free spins remain redeemable with the base reels and their carried wager.
    public ClassicSlotSpinOutcome Spin(long wagerPoints, string playerId,
        bool specialBoostApplied = false, long currentEnergyBalance = 0,
        string? featureMode = null, int? persistedPityMisses = null,
        bool isFreeSpin = false) => engine.Spin(wagerPoints, playerId,
            currentEnergyBalance: currentEnergyBalance,
            persistedPityMisses: persistedPityMisses, isFreeSpin: isFreeSpin);

    public ClassicSlotSpinOutcome SpinTome(long wagerPoints, string playerId,
        long currentEnergyBalance, int? persistedPityMisses, bool isFreeSpin, bool tomeActive, ArcanePotionBottle? potion = null)
    {
        var paidPotion = isFreeSpin ? null : potion;
        var outcome = (tomeActive ? tomeEngine : engine).Spin(wagerPoints, playerId,
            currentEnergyBalance: currentEnergyBalance,
            persistedPityMisses: persistedPityMisses, isFreeSpin: isFreeSpin,
            prepareReels: paidPotion is null ? null : reels => ArcanePotions.BoostReelSet(reels, paidPotion));
        if (paidPotion is null) return outcome;

        var payout = ArcanePotions.BoostPaylines(outcome.Payout, paidPotion);
        var collection = Calculate(outcome.Reels, wagerPoints, paidPotion);
        if (collection is not null && collection.Cells.Any(cell => cell.PotionBonusPoints > 0))
        {
            payout = new(checked(payout.TotalPoints + collection.Cells.Sum(cell => cell.PotionBonusPoints)),
                payout.Paylines.Select(line => line.PaylineId == PaylineId ? CreatePayout(collection) : line).ToArray());
        }
        return outcome with { Payout = payout };
    }

    internal static int VisibleRows => Definitions.Game.Layout.VisibleRows;
    internal static int BaseStripStops => Definitions.ReelSet.Reels[0].Count;

    public static bool SupportsPotionEngineVersion(string? version) => version == EngineVersion;

    public static bool PotionReelsMatch(ReelSetDefinition effective, ArcanePotionBottle? potion)
    {
        ArgumentNullException.ThrowIfNull(effective);
        if (!CurrentReelSets.TryGetValue(effective.Id, out var baseline)) return false;
        var expected = ArcanePotions.BoostReelSet(baseline, potion);
        return expected.SymbolSetId == effective.SymbolSetId && expected.Reels.Count == effective.Reels.Count &&
            expected.Reels.Zip(effective.Reels).All(pair => pair.First.SequenceEqual(pair.Second));
    }

    public static bool MatchesTomeState(string reelSetId, ArcaneTomeState state) =>
        (CurrentReelSets.ContainsKey(reelSetId) || IsPreviousRotatingLayout(reelSetId, RotatingReelSetId) ||
            IsPreviousRotatingLayout(reelSetId, RotatingTomeReelSetId) || reelSetId is SparseReelSetId or SparseTomeReelSetId
            or PreviousWandReelSetId or PreviousTomeReelSetId) &&
        IsBoostedReelSet(reelSetId) == (state.ActivePage is not null);

    public static bool IsBoostedReelSet(string reelSetId) =>
        reelSetId is SparseTomeReelSetId or PreviousTomeReelSetId ||
        IsPreviousRotatingLayout(reelSetId, RotatingTomeReelSetId) || CurrentTomeReelSetIds.Contains(reelSetId);

    public static bool IsPaylineModel(GameDefinition game) =>
        IsWandModel(game) || game.Id == GameId && game.Math.ReelSetId == PreviousReelSetId;

    public static bool IsWandEngineVersion(string? version) => version is EngineVersion or RotatingWandEngineVersion or SparseWandEngineVersion or PreviousWandEngineVersion;

    public static bool IsWandModel(GameDefinition game) => WandEngineVersion(game) is not null;

    public static string? WandEngineVersion(GameDefinition game) =>
        game.Id != GameId ? null : game.Math.ReelSetId switch
        {
            PreviousWandReelSetId or PreviousTomeReelSetId => PreviousWandEngineVersion,
            SparseReelSetId or SparseTomeReelSetId => SparseWandEngineVersion,
            var id when IsPreviousRotatingLayout(id, RotatingReelSetId) ||
                IsPreviousRotatingLayout(id, RotatingTomeReelSetId) => RotatingWandEngineVersion,
            var id when CurrentReelSets.ContainsKey(id) => EngineVersion,
            _ => null
        };

    private static bool IsPreviousRotatingLayout(string id, string prefix) =>
        id == prefix || (id.StartsWith(prefix + "-shift-", StringComparison.Ordinal) &&
            id.Length == prefix.Length + "-shift-".Length + 1 && id[^1] is >= '1' and <= '4');

    public static GameDefinition CreateGame() => Definitions.CreateGame();
    public static SymbolSetDefinition CreateSymbolSet() => Definitions.CreateSymbolSet();
    public static ReelSetDefinition CreateReelSet() => Definitions.CreateReelSet();
    public static ReelSetDefinition CreateTomeReelSet() => TomeDefinitions.CreateReelSet();
    public static PaytableDefinition CreatePaytable() => Definitions.CreatePaytable();
    public static ReelSetDefinition? FindReelSet(string id) =>
        CurrentReelSets.TryGetValue(id, out var reelSet) ? SlotDefinitionCloner.Clone(reelSet) : null;

    private static ReelSetDefinition[] CreateAlternateReelSets(FrozenSlotDefinitions definitions)
    {
        var source = ClassicSlotDefinitionFactory.CreateClassicReelSet().Reels;
        var baseReels = definitions.ReelSet;
        var count = baseReels.Reels.Count;
        var rows = definitions.Game.Layout.VisibleRows;
        // Rotate the positions that can produce an ingredient, while keeping the
        // wand, constellations and Grimoire on the same stops in every layout.
        return Enumerable.Range(1, count - 1).Select(offset => new ReelSetDefinition
        {
            Id = $"{baseReels.Id}-shift-{offset}",
            SymbolSetId = baseReels.SymbolSetId,
            Reels = baseReels.Reels.Select((strip, reel) =>
                MoveIngredientStops(strip, source[reel], reel, (count + reel - offset) % count, rows)).ToList()
        }).ToArray();
    }

    private static List<string> MoveIngredientStops(
        IReadOnlyList<string> strip, IReadOnlyList<string> source, int reel, int role, int visibleRows)
    {
        var shifted = strip.Select((symbol, stop) =>
        {
            if (!ArcaneTome.IngredientIds.Contains(symbol))
                return symbol;
            var original = source[stop];
            return PaylineSymbols.Contains(original) ? original : PaylineSymbols[(reel + stop) % PaylineSymbols.Length];
        }).ToList();

        var ingredientStops = GetIngredientStops(role, strip.Count, visibleRows);
        for (var index = 0; index < ingredientStops.Count; index++)
        {
            var stop = ingredientStops[index];
            if (role == 0 && IsSparseIngredientGap(reel, index))
                continue;
            if (!PaylineSymbols.Contains(shifted[stop]))
                throw new InvalidOperationException("An Arcane ingredient stop overlaps a special symbol.");
            shifted[stop] = ArcaneTome.IngredientIds[(index + reel) % ArcaneTome.IngredientIds.Count];
        }
        return shifted;
    }

    private static IReadOnlyList<int> GetIngredientStops(int role, int stripLength, int visibleRows) => role switch
    {
        0 => Enumerable.Range(0, stripLength / visibleRows).Select(stop => stop * visibleRows + 2).ToArray(),
        1 => [1, 8, 27, 33, 40, 49, 53],
        2 => [1, 47],
        _ => []
    };

    // The dense band has three four-row gaps. Adjust a gap where needed
    // to keep the physical reel's existing special stop outside that band.
    private static bool IsSparseIngredientGap(int reel, int index) => reel switch
    {
        1 => index is 3 or 9 or 13,
        2 => index is 4 or 8 or 13,
        3 => index is 3 or 8 or 14,
        _ => index is 3 or 8 or 13
    };

    private static FrozenSlotDefinitions CreateDefinitions(SlotProfile profile, bool suppressGrimoire)
    {
        var source = ClassicSlotDefinitionFactory.Create(profile);
        var game = source.CreateGame();
        var symbols = new SymbolSetDefinition
        {
            Id = SymbolSetId,
            Symbols = new[] { WandSymbolId, AetherSymbolId, FortuneSymbolId, LightningSymbolId, ArcaneTome.GrimoireSymbolId }
                .Concat(PaylineSymbols).Concat(ArcaneTome.IngredientIds)
                .Select(id => new SymbolDefinition { Id = id }).ToList()
        };
        var reels = source.CreateReelSet();
        var paytable = new PaytableDefinition
        {
            Id = PaytableId,
            SymbolSetId = symbols.Id,
            Rules = source.CreatePaytable().Rules.Where(rule => PaylineSymbols.Contains(rule.SymbolId)).ToList()
        };
        return new(new GameDefinition
        {
            Id = game.Id,
            Layout = game.Layout,
            Symbols = new GameSymbolRules
            {
                SymbolSetId = symbols.Id,
                WildSymbolId = string.Empty,
                PaylineSymbolIds = [.. PaylineSymbols]
            },
            Matching = game.Matching,
            Math = new GameMathDefinition
            {
                ReelSetId = reels.Id,
                PaytableId = paytable.Id,
                PayoutMultiplier = game.Math.PayoutMultiplier,
                PaylinePayoutSteps = game.Math.PaylinePayoutSteps,
                FiveMatchPityMissLimit = game.Math.FiveMatchPityMissLimit,
                Targets = new GameMathTargets()
            },
            Wagering = game.Wagering,
            Paylines = game.Paylines
        }, symbols, new ReelSetDefinition
        {
            Id = reels.Id,
            SymbolSetId = symbols.Id,
            Reels = reels.Reels.Select((strip, index) =>
            {
                var activeStrip = CreateReelStrip(strip, index, game.Layout.VisibleRows);
                if (suppressGrimoire)
                {
                    var replacement = PaylineSymbols[index];
                    activeStrip = activeStrip.Select(symbol =>
                        symbol == ArcaneTome.GrimoireSymbolId ? replacement : symbol).ToList();
                }
                return activeStrip;
            }).ToList()
        }, paytable);
    }

    // Stops on the frozen 64-position classic strips. Aether is most common,
    // followed by Lightning, then Fortune. This keeps the wand award occasional
    // at the full 2x, 4x and 10x wager multiples.
    private static IReadOnlyList<(int Stop, string Symbol)> GetConstellationStops(int reel) => reel switch
    {
        0 => [(7, AetherSymbolId), (31, LightningSymbolId), (32, FortuneSymbolId), (52, AetherSymbolId)],
        1 => [(38, LightningSymbolId), (43, AetherSymbolId)],
        2 => [(31, AetherSymbolId)],
        3 => [(29, AetherSymbolId)],
        4 => [(39, AetherSymbolId)],
        _ => []
    };

    private static List<string> CreateReelStrip(IReadOnlyList<string> source, int reel, int visibleRows)
    {
        var replacements = GetConstellationStops(reel).ToDictionary(value => value.Stop, value => value.Symbol);

        // Space ingredients so a visible window can land up to one per reel.
        // Gaps on reel 1 let a spin naturally finish with no ingredients.
        var ingredientStops = GetIngredientStops(reel, source.Count, visibleRows);
        for (var index = 0; index < ingredientStops.Count; index++)
        {
            var ingredient = ArcaneTome.IngredientIds[(index + reel) % ArcaneTome.IngredientIds.Count];
            if (reel == 0 && IsSparseIngredientGap(reel, index))
                replacements.Add(ingredientStops[index], PaylineSymbols[(index + reel) % PaylineSymbols.Length]);
            else
                replacements.Add(ingredientStops[index], ingredient);
        }

        var nextPayline = reel;
        return source.Select((symbol, stop) =>
        {
            if (replacements.TryGetValue(stop, out var replacement))
                return replacement;
            return symbol switch
            {
                LightningSymbolId when reel >= 2 => WandSymbolId,
                "7" or "FREE" or AetherSymbolId => PaylineSymbols[nextPayline++ % PaylineSymbols.Length],
                _ => symbol
            };
        }).ToList();
    }
}
