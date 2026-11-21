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
    public const string EngineVersion = "arcane-archives-paylines-v8";
    public const string PreviousBalancedPotionEngineVersion = "arcane-archives-paylines-v7";
    public const string PreviousExactPaylineEngineVersion = "arcane-archives-paylines-v6";
    public const string PreviousPotionEngineVersion = "arcane-archives-paylines-v5";
    public const string PreviousWandEngineVersion = "arcane-archives-paylines-v2";
    public const string ReelSetId = "arcane-archives-payline-reels-v8";
    public const string TomeReelSetId = "arcane-archives-tome-reels-v8";
    public const string SymbolSetId = "arcane-archives-payline-symbols-v2";
    public const string PaytableId = "arcane-archives-paytable-v3";
    private const string PreviousPaytableId = "arcane-archives-paytable-v2";
    private const string PreviousBalancedPotionReelSetId = "arcane-archives-payline-reels-v7";
    private const string PreviousBalancedPotionTomeReelSetId = "arcane-archives-tome-reels-v7";
    private const string PreviousExactPaylineReelSetId = "arcane-archives-payline-reels-v6";
    private const string PreviousExactPaylineTomeReelSetId = "arcane-archives-tome-reels-v6";
    private const string PreviousPotionReelSetId = "arcane-archives-payline-reels-v5";
    private const string PreviousPotionTomeReelSetId = "arcane-archives-tome-reels-v5";
    private const decimal CurrentPayoutMultiplier = 0.357m;
    private const decimal PreviousBalancedPotionPayoutMultiplier = 0.357m;
    private const decimal PreviousExactPaylinePayoutMultiplier = 0.459m;
    private const decimal PreviousPotionPayoutMultiplier = 0.716m;
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
        PayoutMultiplier: CurrentPayoutMultiplier, PreserveEnergySymbolsWithoutMeter: true);

    private static readonly ArcaneSpinModel CurrentModel = CreateModel(
        EngineVersion, Profile, TomeReelSetId, SlotPaylineMatchMode.ExactStraightThreeAndFullPayline, PaytableId,
        supportsRarePotions: true);
    private static readonly ArcaneSpinModel[] PotionModels =
    [
        CurrentModel,
        CreateModel(PreviousBalancedPotionEngineVersion,
            Profile with { BaseReelSetId = PreviousBalancedPotionReelSetId, PayoutMultiplier = PreviousBalancedPotionPayoutMultiplier },
            PreviousBalancedPotionTomeReelSetId, SlotPaylineMatchMode.ExactStraightThreeAndFullPayline, PaytableId),
        CreateModel(PreviousExactPaylineEngineVersion,
            Profile with { BaseReelSetId = PreviousExactPaylineReelSetId, PayoutMultiplier = PreviousExactPaylinePayoutMultiplier },
            PreviousExactPaylineTomeReelSetId, SlotPaylineMatchMode.ExactStraightThreeAndFullPayline, PreviousPaytableId),
        CreateModel(PreviousPotionEngineVersion,
            Profile with { BaseReelSetId = PreviousPotionReelSetId, PayoutMultiplier = PreviousPotionPayoutMultiplier },
            PreviousPotionTomeReelSetId, SlotPaylineMatchMode.Classic, PreviousPaytableId)
    ];
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
    private readonly string engineVersion;

    public ArcaneArchivesEngine(IGameRandomSource random) : this(random, EngineVersion) { }

    internal ArcaneArchivesEngine(IGameRandomSource random, string engineVersion)
    {
        var model = FindPotionModel(engineVersion)
            ?? throw new ArgumentException("Unknown Arcane potion engine version.", nameof(engineVersion));
        this.engineVersion = engineVersion;
        engine = new(model.Definitions, model.Profile, random, engineVersion, EngineOptions, model.AlternateReelSets);
        tomeEngine = new(model.TomeDefinitions, model.TomeProfile, random, engineVersion, EngineOptions, model.AlternateTomeReelSets);
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
        if (paidPotion?.VariantId is not null && !SupportsRarePotions(engineVersion))
            throw new ArgumentException("Rare potions require the current Arcane engine.", nameof(potion));
        var outcome = (tomeActive ? tomeEngine : engine).Spin(wagerPoints, playerId,
            currentEnergyBalance: currentEnergyBalance,
            persistedPityMisses: persistedPityMisses, isFreeSpin: isFreeSpin,
            prepareReels: paidPotion is null ? null : reels => ArcanePotions.BoostReelSet(reels, paidPotion));
        if (paidPotion is null) return outcome;

        var payout = ArcanePotions.BoostPaylines(outcome.Payout, paidPotion, wagerPoints,
            useLegacyMandrake: UsesLegacyMandrake(engineVersion));
        var collection = Calculate(outcome.Reels, wagerPoints, paidPotion);
        if (collection is not null && collection.Cells.Any(cell => cell.PotionBonusPoints > 0))
        {
            payout = new(checked(payout.TotalPoints + collection.Cells.Sum(cell => cell.PotionBonusPoints)),
                payout.Paylines.Select(line => line.PaylineId == PaylineId ? CreatePayout(collection) : line).ToArray());
        }
        return outcome with { Payout = payout };
    }

    internal static int VisibleRows => CurrentModel.Definitions.Game.Layout.VisibleRows;
    internal static int BaseStripStops => CurrentModel.Definitions.ReelSet.Reels[0].Count;

    public static bool SupportsPotionEngineVersion(string? version) => FindPotionModel(version) is not null;

    public static bool SupportsRarePotions(string? version) => FindPotionModel(version)?.SupportsRarePotions == true;

    public static bool UsesLegacyMandrake(string? version) => version == PreviousPotionEngineVersion;

    internal static int ReplayLayoutIndex(string version, string reelSetId, bool tomeActive)
    {
        var model = FindPotionModel(version) ?? throw new InvalidOperationException("The saved Arcane engine version is unavailable.");
        var definitions = tomeActive ? model.TomeDefinitions : model.Definitions;
        if (definitions.ReelSet.Id == reelSetId) return 0;

        var alternatives = tomeActive ? model.AlternateTomeReelSets : model.AlternateReelSets;
        var index = Array.FindIndex(alternatives, reels => reels.Id == reelSetId);
        return index >= 0 ? index + 1 : throw new InvalidOperationException("The Arcane reel layout differs from the admitted Tome.");
    }

    public static bool PotionReelsMatch(ReelSetDefinition effective, ArcanePotionBottle? potion)
    {
        ArgumentNullException.ThrowIfNull(effective);
        if (FindPotionModelByReelSet(effective.Id) is not { } model ||
            potion?.VariantId is not null && !model.SupportsRarePotions)
            return false;
        var expected = ArcanePotions.BoostReelSet(model.ReelSets[effective.Id], potion);
        return expected.SymbolSetId == effective.SymbolSetId && expected.Reels.Count == effective.Reels.Count &&
            expected.Reels.Zip(effective.Reels).All(pair => pair.First.SequenceEqual(pair.Second));
    }

    public static bool MatchesTomeState(string reelSetId, ArcaneTomeState state) =>
        (GetPotionReelSet(reelSetId) is not null || IsPreviousRotatingLayout(reelSetId, RotatingReelSetId) ||
            IsPreviousRotatingLayout(reelSetId, RotatingTomeReelSetId) || reelSetId is SparseReelSetId or SparseTomeReelSetId
            or PreviousWandReelSetId or PreviousTomeReelSetId) &&
        IsBoostedReelSet(reelSetId) == (state.ActivePage is not null);

    public static bool IsBoostedReelSet(string reelSetId) =>
        reelSetId is SparseTomeReelSetId or PreviousTomeReelSetId ||
        IsPreviousRotatingLayout(reelSetId, RotatingTomeReelSetId) ||
        FindPotionModelByReelSet(reelSetId)?.TomeReelSetIds.Contains(reelSetId) == true;

    public static bool IsPaylineModel(GameDefinition game) =>
        IsWandModel(game) || game.Id == GameId && game.Math.ReelSetId == PreviousReelSetId;

    public static bool IsWandEngineVersion(string? version) => SupportsPotionEngineVersion(version) ||
        version is RotatingWandEngineVersion or SparseWandEngineVersion or PreviousWandEngineVersion;

    public static bool HasExpectedPaylineMath(string? version, GameDefinition game)
    {
        var model = FindPotionModel(version);
        return model is null
            ? game.Matching.PaylineMatchMode == SlotPaylineMatchMode.Classic
            : game.Matching.PaylineMatchMode == model.Definitions.Game.Matching.PaylineMatchMode &&
              game.Math.PaytableId == model.Definitions.Paytable.Id &&
              game.Math.PayoutMultiplier == model.Profile.PayoutMultiplier;
    }

    public static bool HasExpectedPotionPaytable(string version, PaytableDefinition paytable) =>
        FindPotionModel(version)?.Definitions.Paytable is { } expected &&
        expected.Id == paytable.Id && expected.SymbolSetId == paytable.SymbolSetId &&
        expected.Rules.Select(rule => (rule.SymbolId, rule.MatchLength, rule.Multiplier))
            .SequenceEqual(paytable.Rules.Select(rule => (rule.SymbolId, rule.MatchLength, rule.Multiplier)));

    public static bool IsWandModel(GameDefinition game) => WandEngineVersion(game) is not null;

    public static string? WandEngineVersion(GameDefinition game) =>
        game.Id != GameId ? null : game.Math.ReelSetId switch
        {
            PreviousWandReelSetId or PreviousTomeReelSetId => PreviousWandEngineVersion,
            SparseReelSetId or SparseTomeReelSetId => SparseWandEngineVersion,
            var id when IsPreviousRotatingLayout(id, RotatingReelSetId) ||
                IsPreviousRotatingLayout(id, RotatingTomeReelSetId) => RotatingWandEngineVersion,
            var id => FindPotionModelByReelSet(id)?.EngineVersion
        };

    private static bool IsPreviousRotatingLayout(string id, string prefix) =>
        id == prefix || (id.StartsWith(prefix + "-shift-", StringComparison.Ordinal) &&
            id.Length == prefix.Length + "-shift-".Length + 1 && id[^1] is >= '1' and <= '4');

    public static GameDefinition CreateGame() => CurrentModel.Definitions.CreateGame();
    public static SymbolSetDefinition CreateSymbolSet() => CurrentModel.Definitions.CreateSymbolSet();
    public static ReelSetDefinition CreateReelSet() => CurrentModel.Definitions.CreateReelSet();
    public static ReelSetDefinition CreateTomeReelSet() => CurrentModel.TomeDefinitions.CreateReelSet();
    public static PaytableDefinition CreatePaytable() => CurrentModel.Definitions.CreatePaytable();

    public static PaytableDefinition? FindPaytable(string id) =>
        PotionModels.FirstOrDefault(model => model.Definitions.Paytable.Id == id)?.Definitions.CreatePaytable();
    public static ReelSetDefinition? FindReelSet(string id) =>
        GetPotionReelSet(id) is { } reelSet ? SlotDefinitionCloner.Clone(reelSet) : null;

    private static ArcaneSpinModel? FindPotionModel(string? version) =>
        PotionModels.FirstOrDefault(model => model.EngineVersion == version);

    private static ArcaneSpinModel? FindPotionModelByReelSet(string id) =>
        PotionModels.FirstOrDefault(model => model.ReelSets.ContainsKey(id));

    private static ReelSetDefinition? GetPotionReelSet(string id) =>
        FindPotionModelByReelSet(id)?.ReelSets[id];

    private sealed record ArcaneSpinModel(
        string EngineVersion, SlotProfile Profile, FrozenSlotDefinitions Definitions, ReelSetDefinition[] AlternateReelSets,
        SlotProfile TomeProfile, FrozenSlotDefinitions TomeDefinitions, ReelSetDefinition[] AlternateTomeReelSets,
        IReadOnlyDictionary<string, ReelSetDefinition> ReelSets, IReadOnlySet<string> TomeReelSetIds, bool SupportsRarePotions);

    private static ArcaneSpinModel CreateModel(
        string engineVersion, SlotProfile profile, string tomeReelSetId, SlotPaylineMatchMode matchMode, string paytableId,
        bool supportsRarePotions = false)
    {
        var tomeProfile = profile with { BaseReelSetId = tomeReelSetId };
        var definitions = CreateDefinitions(profile, false, matchMode, paytableId);
        var tomeDefinitions = CreateDefinitions(tomeProfile, true, matchMode, paytableId);
        var alternateReelSets = CreateAlternateReelSets(definitions);
        var alternateTomeReelSets = CreateAlternateReelSets(tomeDefinitions);
        var reelSets = new[] { definitions.ReelSet, tomeDefinitions.ReelSet }
            .Concat(alternateReelSets).Concat(alternateTomeReelSets)
            .ToDictionary(set => set.Id, StringComparer.Ordinal);
        var tomeReelSetIds = alternateTomeReelSets.Select(set => set.Id)
            .Append(tomeReelSetId).ToHashSet(StringComparer.Ordinal);
        return new(engineVersion, profile, definitions, alternateReelSets, tomeProfile, tomeDefinitions, alternateTomeReelSets,
            reelSets, tomeReelSetIds, supportsRarePotions);
    }

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

    private static FrozenSlotDefinitions CreateDefinitions(
        SlotProfile profile, bool suppressGrimoire, SlotPaylineMatchMode matchMode, string paytableId)
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
            Id = paytableId,
            SymbolSetId = symbols.Id,
            Rules = source.CreatePaytable().Rules.Where(rule => PaylineSymbols.Contains(rule.SymbolId))
                .Select(rule => paytableId == PaytableId && rule.SymbolId == "4" && rule.MatchLength == 3
                    ? new PayoutRule { SymbolId = rule.SymbolId, MatchLength = rule.MatchLength, Multiplier = 2 }
                    : rule)
                .ToList()
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
            Matching = new GameMatchingRules
            {
                MinimumRunLength = game.Matching.MinimumRunLength,
                AllowMultipleRunsPerPayline = game.Matching.AllowMultipleRunsPerPayline,
                PaylineMatchMode = matchMode
            },
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
