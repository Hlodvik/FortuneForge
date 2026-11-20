// Edited: 2026-11-18T18:00:00-06:00 | Computer: Vinnabok
using System.Collections.Frozen;
using System.Text.Json.Serialization;
using FortuneForge.Games.Slots;

namespace FortuneForge.Games.ArcaneArchives;

public sealed record ArcanePotionBottle(long Id, string IngredientId, int Potency)
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public long? LockedWagerPoints { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? VariantId { get; init; }
}

public sealed record ArcaneActivePotion(ArcanePotionBottle Bottle, int SpinsRemaining);
public sealed record ArcanePotWager(long TotalPoints, int PaidUnits);

public sealed record ArcanePotionState(
    IReadOnlyList<string> Pot,
    IReadOnlyList<ArcanePotionBottle> Bottles,
    ArcaneActivePotion? Active,
    long NextBottleId)
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public ArcanePotWager? PotWager { get; init; }
}

public sealed record ArcanePotionCatalogEntry(string IngredientId, string Name, string Effect, int SecondaryPotency);

public sealed record ArcanePotionDescription(string IngredientId, string Name, string Effect, int Potency);

public sealed class ArcanePotionConflictException(string message) : Exception(message);
public sealed class ArcanePotionWagerLockedException(long lockedWagerPoints)
    : Exception("Use the locked wager while this Arcane potion is active.")
{
    public long LockedWagerPoints { get; } = lockedWagerPoints;
}

public static class ArcanePotions
{
    public const int Capacity = 8;
    public const int Majority = 5;
    public const int Duration = 3;
    public const int MaximumPotency = 18;
    public const string AstralConvergence = "astral-convergence";
    public const string RuneboundElixir = "runebound-elixir";
    private const int FrequencyScale = 4;
    private const int MandrakeMinimumBonusPoints = 1;
    private const int MandrakeMaximumBonusFloorPoints = 5;
    private const int AstralWandExtraStops = 4;
    private const int AstralConstellationExtraStops = 2;
    private const int AstralPayoutBonusPercent = 184;
    private const int RuneboundPremiumExtraStops = 5;
    private const int RuneboundLineBonusPercent = 1044;
    private static readonly Lazy<GameWageringDefinition> Wagering = new(() => ArcaneArchivesEngine.CreateGame().Wagering);

    private enum EffectKind
    {
        OghamValue,
        ConstellationFrequency,
        PremiumFrequency,
        WandFrequency,
        PremiumValue,
        ConstellationValue,
        Astral,
        Runebound
    }

    private sealed record Rule(ArcanePotionCatalogEntry Entry, EffectKind Kind);
    private static readonly Rule[] Rules =
    [
        new(new(ArcaneTome.MandrakeRoot, "Mandrake Tonic", "Scribe Mark payline value", 1), EffectKind.OghamValue),
        new(new(ArcaneTome.NightshadeBerries, "Nightshade Elixir", "Constellation frequency", 2), EffectKind.ConstellationFrequency),
        new(new(ArcaneTome.MooncapMushroom, "Mooncap Draught", "VII and pentagram frequency", 2), EffectKind.PremiumFrequency),
        new(new(ArcaneTome.PhoenixFeather, "Phoenix Infusion", "Wand frequency", 3), EffectKind.WandFrequency),
        new(new(ArcaneTome.WolfsbaneWine, "Wolfsbane Cordial", "VII and pentagram payline value", 1), EffectKind.PremiumValue),
        new(new(ArcaneTome.Quicksilver, "Quicksilver Essence", "Constellation value", 3), EffectKind.ConstellationValue)
    ];
    private static readonly FrozenDictionary<string, Rule> RulesByIngredient =
        Rules.ToFrozenDictionary(rule => rule.Entry.IngredientId, StringComparer.Ordinal);

    public static IReadOnlyList<ArcanePotionCatalogEntry> Catalog { get; } =
        Array.AsReadOnly(Rules.Select(rule => rule.Entry).ToArray());

    public static ArcanePotionState Empty { get; } = new([], [], null, 1);

    // The payout engine and historical outcome contract only need the potion effect.
    public static ArcanePotionBottle? ForSpin(ArcanePotionState? state, bool isFreeSpin) =>
        isFreeSpin || state?.Active is not { } active ? null : active.Bottle with { LockedWagerPoints = null };

    public static long? LockedWagerForSpin(ArcanePotionState? state, bool isFreeSpin) =>
        isFreeSpin || state?.Active is not { } active ? null : LockedWagerPoints(active.Bottle);

    public static long LockedWagerPoints(ArcanePotionBottle bottle) => RoundDownToLegalWager(bottle.LockedWagerPoints ?? 0);

    public static ArcanePotionDescription Describe(ArcanePotionBottle bottle) => PotionEffect(bottle) switch
    {
        EffectKind.Astral => new(bottle.IngredientId, "Astral Convergence",
            $"More Enchanted Wands and constellations. Wand-collected constellations pay +{RareBonusPercent(AstralPayoutBonusPercent, bottle.Potency)}%.",
            bottle.Potency),
        EffectKind.Runebound => new(bottle.IngredientId, "Runebound Elixir",
            $"More VIIs and pentagrams. All payline wins pay +{RareBonusPercent(RuneboundLineBonusPercent, bottle.Potency)}%.",
            bottle.Potency),
        _ => Describe(bottle.IngredientId, bottle.Potency)
    };

    public static ArcanePotionDescription Describe(string ingredientId, int potency)
    {
        var rule = GetRule(ingredientId, potency);
        var percent = PayoutBonusPercent(potency, strongerMandrake: rule.Kind == EffectKind.OghamValue);
        var effect = rule.Kind switch
        {
            EffectKind.OghamValue =>
                $"{rule.Entry.Effect} +{percent}% (minimum {MandrakeMinimumBonusPoints}–{MandrakeMaximumBonusFloorPoints} points per qualifying spin, based on wager)",
            EffectKind.PremiumValue or EffectKind.ConstellationValue =>
                $"{rule.Entry.Effect} +{percent}% (minimum 1 point per qualifying spin)",
            _ => FormattableString.Invariant(
                $"{rule.Entry.Effect} +{VisibleFrequencyBonus(potency):0.##} percentage points per boosted symbol on its reel before redraws")
        };
        return new(ingredientId, rule.Entry.Name, effect, potency);
    }

    public static ArcanePotionDescription? Preview(ArcanePotionState state)
    {
        ValidateState(state);
        if (state.Pot.Count == 0) return null;
        var (ingredientId, potency) = ReadPot(state.Pot);
        return Describe(ingredientId, potency);
    }

    public static ArcanePotionState Use(ArcanePotionState state, long bottleId)
    {
        ValidateState(state);
        if (state.Active is not null)
            throw new ArcanePotionConflictException("Wait for the active potion to finish.");
        var bottle = state.Bottles.FirstOrDefault(candidate => candidate.Id == bottleId)
            ?? throw new ArcanePotionConflictException("That potion is no longer available.");
        return state with
        {
            Active = new(bottle, Duration),
            Bottles = state.Bottles.Where(candidate => candidate.Id != bottleId).ToArray()
        };
    }

    public static ArcanePotionState CompleteSpin(
        ArcanePotionState before, IReadOnlyList<string> orderedUnits, bool isFreeSpin,
        long? wagerPoints = null, bool allowRareRecipes = true)
    {
        ValidateState(before);
        ArgumentNullException.ThrowIfNull(orderedUnits);
        if (wagerPoints is <= 0)
            throw new ArgumentOutOfRangeException(nameof(wagerPoints));
        if (wagerPoints is null && before.PotWager is not null)
            throw new InvalidOperationException("A tracked Arcane pot requires the admitted spin wager.");
        var pot = before.Pot.ToList();
        var shelf = before.Bottles.ToList();
        // Older snapshots have ingredients but no stakes. Credit those units at the minimum.
        var potWager = wagerPoints is null ? null : before.PotWager ??
            new ArcanePotWager(checked(pot.Count * RoundDownToLegalWager(0)), pot.Count);
        var paidWager = isFreeSpin ? 0 : wagerPoints.GetValueOrDefault();
        var nextId = before.NextBottleId;
        foreach (var ingredient in orderedUnits)
        {
            if (!RulesByIngredient.ContainsKey(ingredient))
                throw new InvalidOperationException("The Arcane pot received an unknown ingredient.");
            pot.Add(ingredient);
            if (paidWager > 0 && potWager is not null)
                potWager = new(checked(potWager.TotalPoints + paidWager), potWager.PaidUnits + 1);
            if (pot.Count != Capacity && pot.Count(id => id == ingredient) != Majority)
                continue;
            var variantId = allowRareRecipes ? FindRareRecipe(pot) : null;
            var (leader, potency) = ReadPot(pot, variantId is null ? null : ArcaneTome.MandrakeRoot);
            shelf.Add(new(nextId, leader, potency)
            {
                VariantId = variantId,
                LockedWagerPoints = potWager is null ? null :
                    RoundDownToLegalWager(potWager.PaidUnits == 0 ? 0 : potWager.TotalPoints / potWager.PaidUnits)
            });
            nextId = checked(nextId + 1);
            pot.Clear();
            if (potWager is not null) potWager = new(0, 0);
        }

        var active = before.Active;
        if (!isFreeSpin && active is not null)
            active = active.SpinsRemaining == 1 ? null : active with { SpinsRemaining = active.SpinsRemaining - 1 };
        return new(pot.ToArray(), shelf.ToArray(), active, nextId) { PotWager = pot.Count > 0 ? potWager : null };
    }

    public static void ValidateState(ArcanePotionState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        if (state.Pot is null || state.Bottles is null || state.NextBottleId < 1 ||
            state.Pot.Count >= Capacity || state.Pot.Any(id => id is null || !RulesByIngredient.ContainsKey(id)) ||
            state.Pot.GroupBy(id => id, StringComparer.Ordinal).Any(group => group.Count() >= Majority) ||
            state.PotWager is { } wager && (state.Pot.Count == 0 || wager.PaidUnits < 0 ||
                wager.PaidUnits > state.Pot.Count || wager.TotalPoints < wager.PaidUnits ||
                wager.PaidUnits == 0 && wager.TotalPoints != 0) ||
            state.Bottles.Any(bottle => !ValidBottle(bottle, state.NextBottleId)) ||
            state.Bottles.Select(bottle => bottle.Id).Distinct().Count() != state.Bottles.Count ||
            state.Active is { } active && (!ValidBottle(active.Bottle, state.NextBottleId) ||
                active.SpinsRemaining is < 1 or > Duration || state.Bottles.Any(bottle => bottle.Id == active.Bottle.Id)))
            throw new InvalidOperationException("The saved Arcane potion state is invalid.");
    }

    public static bool SameState(ArcanePotionState first, ArcanePotionState second)
    {
        ValidateState(first);
        ValidateState(second);
        return first.NextBottleId == second.NextBottleId && first.Active == second.Active &&
            first.PotWager == second.PotWager && first.Pot.SequenceEqual(second.Pot) && first.Bottles.SequenceEqual(second.Bottles);
    }

    private static long RoundDownToLegalWager(long points)
    {
        var rules = Wagering.Value;
        if (rules.AllowedWagerPoints.Count > 0)
            return rules.AllowedWagerPoints.Where(wager => wager <= points).DefaultIfEmpty(rules.AllowedWagerPoints.Min()).Max();

        var bounded = Math.Clamp(points, rules.MinimumWagerPoints, rules.MaximumWagerPoints ?? long.MaxValue);
        var step = rules.WagerIncrementPoints ?? 1;
        return rules.MinimumWagerPoints + (bounded - rules.MinimumWagerPoints) / step * step;
    }

    public static SpinPayout BoostPaylines(SpinPayout payout, ArcanePotionBottle? potion, long wagerPoints,
        bool useLegacyMandrake = false)
    {
        var kind = PotionEffect(potion);
        var targets = kind switch
        {
            EffectKind.OghamValue => ArcaneArchivesEngine.OghamSymbols,
            EffectKind.PremiumValue => ArcaneArchivesEngine.PremiumSymbols,
            EffectKind.Runebound => ArcaneArchivesEngine.OghamSymbols.Concat(ArcaneArchivesEngine.PremiumSymbols).ToArray(),
            _ => null
        };
        if (potion is null || targets is null) return payout;

        var strongerMandrake = kind == EffectKind.OghamValue && !useLegacyMandrake;
        var percent = kind == EffectKind.Runebound ? RareBonusPercent(RuneboundLineBonusPercent, potion.Potency) :
            PayoutBonusPercent(potion.Potency, strongerMandrake);
        var minimumPoints = strongerMandrake
            ? (int)Math.Clamp(wagerPoints / 2, MandrakeMinimumBonusPoints, MandrakeMaximumBonusFloorPoints)
            : 1;
        long qualifyingPoints = 0;
        var paylines = payout.Paylines.Select(payline =>
        {
            var matches = payline.Matches.Select(match => targets.Contains(match.Match.SymbolId)
                ? match with { AmountPoints = checked(match.AmountPoints + NextPayoutBonus(match.AmountPoints, percent, ref qualifyingPoints, minimumPoints)) }
                : match).ToArray();
            return payline with { AmountPoints = matches.Sum(match => match.AmountPoints), Matches = matches };
        }).ToArray();
        return payout with { TotalPoints = paylines.Sum(payline => payline.AmountPoints), Paylines = paylines };
    }

    internal static int ConstellationBonusPercent(ArcanePotionBottle? potion) =>
        PotionEffect(potion) switch
        {
            EffectKind.ConstellationValue => PayoutBonusPercent(potion!.Potency),
            EffectKind.Astral => RareBonusPercent(AstralPayoutBonusPercent, potion!.Potency),
            _ => 0
        };

    // Round once on the qualifying total, and credit each match or cell with
    // only the increase since the previous one.
    internal static long NextPayoutBonus(long points, int percent, ref long qualifyingPoints, int minimumPoints = 1)
    {
        if (points <= 0 || percent == 0) return 0;
        var alreadyAwarded = PayoutBonusPoints(qualifyingPoints, percent, minimumPoints);
        qualifyingPoints = checked(qualifyingPoints + points);
        return PayoutBonusPoints(qualifyingPoints, percent, minimumPoints) - alreadyAwarded;
    }

    internal static ReelSetDefinition BoostReelSet(ReelSetDefinition source, ArcanePotionBottle? potion)
    {
        if (potion is null) return source;
        var kind = PotionEffect(potion);
        if (kind is EffectKind.Astral or EffectKind.Runebound)
            return BoostRareReelSet(source, kind.Value);
        IReadOnlyList<string> targets = kind switch
        {
            EffectKind.WandFrequency => [ArcaneWandCollector.WandSymbolId],
            EffectKind.ConstellationFrequency =>
                [ArcaneWandCollector.AetherSymbolId, ArcaneWandCollector.LightningSymbolId, ArcaneWandCollector.FortuneSymbolId],
            EffectKind.PremiumFrequency => ArcaneArchivesEngine.PremiumSymbols,
            _ => []
        };
        if (targets.Count == 0) return source;

        var reels = source.Reels.Select(reel => reel.ToList()).ToList();
        for (var index = 0; index < targets.Count; index++)
        {
            // Each effect target gets a fraction of one new stop on a physical
            // reel. Keep all ingredient and special stops from the base model.
            var target = targets[index];
            var reelIndex = target == ArcaneWandCollector.WandSymbolId ? reels.Count - 1 : index;
            var strip = source.Reels[reelIndex];
            var stop = Enumerable.Range(0, strip.Count).First(candidate => CanReplace(strip, candidate, target, ArcaneArchivesEngine.VisibleRows));
            var expanded = Enumerable.Range(0, FrequencyScale).SelectMany(_ => strip).ToList();
            for (var ticket = 0; ticket < FrequencyTickets(potion.Potency); ticket++)
                expanded[ticket * strip.Count + stop] = target;
            reels[reelIndex] = expanded;
        }

        return new() { Id = source.Id, SymbolSetId = source.SymbolSetId, Reels = reels };
    }

    private static ReelSetDefinition BoostRareReelSet(ReelSetDefinition source, EffectKind kind)
    {
        (int Reel, string Symbol, int Stops)[] targets = kind == EffectKind.Astral
            ? [
                (4, ArcaneWandCollector.WandSymbolId, AstralWandExtraStops),
                (0, ArcaneWandCollector.AetherSymbolId, AstralConstellationExtraStops),
                (1, ArcaneWandCollector.LightningSymbolId, AstralConstellationExtraStops),
                (2, ArcaneWandCollector.FortuneSymbolId, AstralConstellationExtraStops)
            ]
            : [
                (0, ArcaneArchivesEngine.PremiumSymbols[0], RuneboundPremiumExtraStops),
                (1, ArcaneArchivesEngine.PremiumSymbols[1], RuneboundPremiumExtraStops)
            ];

        var reels = source.Reels.Select(reel => reel.ToList()).ToList();
        foreach (var (reel, symbol, stops) in targets)
        {
            var strip = reels[reel];
            var added = 0;
            for (var stop = 0; stop < strip.Count && added < stops; stop++)
            {
                if (!CanReplace(strip, stop, symbol, ArcaneArchivesEngine.VisibleRows,
                    avoidNearby: kind == EffectKind.Astral))
                    continue;
                strip[stop] = symbol;
                added++;
            }
            if (added != stops)
                throw new InvalidOperationException("An Arcane rare potion could not place its protected extra reel stops.");
        }
        return new() { Id = source.Id, SymbolSetId = source.SymbolSetId, Reels = reels };
    }

    private static bool CanReplace(IReadOnlyList<string> strip, int index, string target, int visibleRows, bool avoidNearby = true)
    {
        if (!ArcaneArchivesEngine.OghamSymbols.Contains(strip[index])) return false;
        // Avoid adding a trigger beside the same trigger, which would increase
        // cluster size while producing fewer newly eligible four-row windows.
        return !avoidNearby ||
            Enumerable.Range(1 - visibleRows, 2 * visibleRows - 1).All(offset => strip[(index + offset + strip.Count) % strip.Count] != target);
    }

    private static string? FindRareRecipe(IReadOnlyList<string> pot)
    {
        if (pot.Count != Capacity || !pot.Contains(ArcaneTome.MandrakeRoot) ||
            pot.GroupBy(id => id, StringComparer.Ordinal).Any(group => group.Count() != 2))
            return null;
        return pot.Contains(ArcaneTome.Quicksilver) ? AstralConvergence : RuneboundElixir;
    }

    private static (string IngredientId, int Potency) ReadPot(IReadOnlyList<string> pot, string? recipeLeader = null)
    {
        // The latest landing appears first among equally frequent ingredient groups.
        var winner = recipeLeader ?? pot.Reverse().GroupBy(id => id, StringComparer.Ordinal).MaxBy(group => group.Count())!.Key;
        return (winner, pot.Where(id => id != winner).Sum(id => RulesByIngredient[id].Entry.SecondaryPotency));
    }

    private static bool ValidBottle(ArcanePotionBottle? bottle, long nextId) =>
        bottle is not null && bottle.Id > 0 && bottle.Id < nextId &&
        bottle.IngredientId is not null && RulesByIngredient.ContainsKey(bottle.IngredientId) &&
        bottle.Potency is >= 0 and <= MaximumPotency && bottle.LockedWagerPoints is null or > 0 &&
        (bottle.VariantId is null || bottle.IngredientId == ArcaneTome.MandrakeRoot &&
            bottle.VariantId is AstralConvergence or RuneboundElixir);

    private static Rule GetRule(string ingredientId, int potency)
    {
        if (potency is < 0 or > MaximumPotency || string.IsNullOrWhiteSpace(ingredientId) ||
            !RulesByIngredient.TryGetValue(ingredientId, out var rule))
            throw new ArgumentException("The Arcane potion effect is invalid.");
        return rule;
    }

    private static EffectKind? PotionEffect(ArcanePotionBottle? potion)
    {
        if (potion is null) return null;
        var rule = GetRule(potion.IngredientId, potion.Potency);
        return potion.VariantId switch
        {
            null => rule.Kind,
            AstralConvergence when potion.IngredientId == ArcaneTome.MandrakeRoot => EffectKind.Astral,
            RuneboundElixir when potion.IngredientId == ArcaneTome.MandrakeRoot => EffectKind.Runebound,
            _ => throw new ArgumentException("The Arcane potion variant is invalid.")
        };
    }

    private static int RareBonusPercent(int basePercent, int potency) => basePercent + potency / 6;

    private static int PayoutBonusPercent(int potency, bool strongerMandrake = false) =>
        (strongerMandrake ? 20 : 2) + potency / 6;
    private static int FrequencyTickets(int potency) => 1 + potency / 9;
    private static decimal VisibleFrequencyBonus(int potency) =>
        100m * FrequencyTickets(potency) * ArcaneArchivesEngine.VisibleRows / (FrequencyScale * ArcaneArchivesEngine.BaseStripStops);

    private static long PayoutBonusPoints(long points, int percent, int minimumPoints) =>
        points <= 0 ? 0 : Math.Max(minimumPoints,
            checked((long)Math.Round(points * (percent / 100m), MidpointRounding.AwayFromZero)));
}
