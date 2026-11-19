// Edited: 2026-11-18T18:00:00-06:00 | Computer: Vinnabok
using System.Collections.Frozen;
using FortuneForge.Games.Slots;

namespace FortuneForge.Games.ArcaneArchives;

public sealed record ArcanePotionBottle(long Id, string IngredientId, int Potency);

public sealed record ArcaneActivePotion(ArcanePotionBottle Bottle, int SpinsRemaining);

public sealed record ArcanePotionState(
    IReadOnlyList<string> Pot,
    IReadOnlyList<ArcanePotionBottle> Bottles,
    ArcaneActivePotion? Active,
    long NextBottleId);

public sealed record ArcanePotionCatalogEntry(string IngredientId, string Name, string Effect, int SecondaryPotency);

public sealed record ArcanePotionDescription(string IngredientId, string Name, string Effect, int Potency);

public sealed class ArcanePotionConflictException(string message) : Exception(message);

public static class ArcanePotions
{
    public const int Capacity = 8;
    public const int Majority = 5;
    public const int Duration = 3;
    public const int MaximumPotency = 18;
    private const int FrequencyScale = 4;

    private enum EffectKind
    {
        OghamValue,
        ConstellationFrequency,
        PremiumFrequency,
        WandFrequency,
        PremiumValue,
        ConstellationValue
    }

    private sealed record Rule(ArcanePotionCatalogEntry Entry, EffectKind Kind);
    private static readonly Rule[] Rules =
    [
        new(new(ArcaneTome.MandrakeRoot, "Mandrake Tonic", "Ogham payline value", 1), EffectKind.OghamValue),
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

    public static ArcanePotionBottle? ForSpin(ArcanePotionState? state, bool isFreeSpin) =>
        isFreeSpin ? null : state?.Active?.Bottle;

    public static ArcanePotionDescription Describe(ArcanePotionBottle bottle) => Describe(bottle.IngredientId, bottle.Potency);

    public static ArcanePotionDescription Describe(string ingredientId, int potency)
    {
        var rule = GetRule(ingredientId, potency);
        var effect = rule.Kind switch
        {
            EffectKind.OghamValue or EffectKind.PremiumValue or EffectKind.ConstellationValue =>
                $"{rule.Entry.Effect} +{PayoutBonusPercent(potency)}% (minimum 1 point per qualifying spin)",
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
            ?? throw new ArcanePotionConflictException("That potion is no longer on the shelf.");
        return state with
        {
            Active = new(bottle, Duration),
            Bottles = state.Bottles.Where(candidate => candidate.Id != bottleId).ToArray()
        };
    }

    public static ArcanePotionState CompleteSpin(
        ArcanePotionState before, IReadOnlyList<string> orderedUnits, bool isFreeSpin)
    {
        ValidateState(before);
        ArgumentNullException.ThrowIfNull(orderedUnits);
        var pot = before.Pot.ToList();
        var shelf = before.Bottles.ToList();
        var nextId = before.NextBottleId;
        foreach (var ingredient in orderedUnits)
        {
            if (!RulesByIngredient.ContainsKey(ingredient))
                throw new InvalidOperationException("The Arcane pot received an unknown ingredient.");
            pot.Add(ingredient);
            if (pot.Count != Capacity && pot.Count(id => id == ingredient) != Majority)
                continue;
            var (leader, potency) = ReadPot(pot);
            shelf.Add(new(nextId, leader, potency));
            nextId = checked(nextId + 1);
            pot.Clear();
        }

        var active = before.Active;
        if (!isFreeSpin && active is not null)
            active = active.SpinsRemaining == 1 ? null : active with { SpinsRemaining = active.SpinsRemaining - 1 };
        return new(pot.ToArray(), shelf.ToArray(), active, nextId);
    }

    public static void ValidateState(ArcanePotionState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        if (state.Pot is null || state.Bottles is null || state.NextBottleId < 1 ||
            state.Pot.Count >= Capacity || state.Pot.Any(id => id is null || !RulesByIngredient.ContainsKey(id)) ||
            state.Pot.GroupBy(id => id, StringComparer.Ordinal).Any(group => group.Count() >= Majority) ||
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
            first.Pot.SequenceEqual(second.Pot) && first.Bottles.SequenceEqual(second.Bottles);
    }

    public static SpinPayout BoostPaylines(SpinPayout payout, ArcanePotionBottle? potion)
    {
        var kind = PotionEffect(potion);
        var targets = kind switch
        {
            EffectKind.OghamValue => ArcaneArchivesEngine.OghamSymbols,
            EffectKind.PremiumValue => ArcaneArchivesEngine.PremiumSymbols,
            _ => null
        };
        if (potion is null || targets is null) return payout;

        var percent = PayoutBonusPercent(potion.Potency);
        long qualifyingPoints = 0;
        var paylines = payout.Paylines.Select(payline =>
        {
            var matches = payline.Matches.Select(match => targets.Contains(match.Match.SymbolId)
                ? match with { AmountPoints = checked(match.AmountPoints + NextPayoutBonus(match.AmountPoints, percent, ref qualifyingPoints)) }
                : match).ToArray();
            return payline with { AmountPoints = matches.Sum(match => match.AmountPoints), Matches = matches };
        }).ToArray();
        return payout with { TotalPoints = paylines.Sum(payline => payline.AmountPoints), Paylines = paylines };
    }

    internal static int ConstellationBonusPercent(ArcanePotionBottle? potion) =>
        potion is not null && PotionEffect(potion) == EffectKind.ConstellationValue
            ? PayoutBonusPercent(potion.Potency)
            : 0;

    // Round once on the qualifying total, and credit each match or cell with
    // only the increase since the previous one.
    internal static long NextPayoutBonus(long points, int percent, ref long qualifyingPoints)
    {
        if (points <= 0 || percent == 0) return 0;
        var alreadyAwarded = PayoutBonusPoints(qualifyingPoints, percent);
        qualifyingPoints = checked(qualifyingPoints + points);
        return PayoutBonusPoints(qualifyingPoints, percent) - alreadyAwarded;
    }

    internal static ReelSetDefinition BoostReelSet(ReelSetDefinition source, ArcanePotionBottle? potion)
    {
        if (potion is null) return source;
        IReadOnlyList<string> targets = PotionEffect(potion) switch
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

    private static bool CanReplace(IReadOnlyList<string> strip, int index, string target, int visibleRows)
    {
        if (!ArcaneArchivesEngine.OghamSymbols.Contains(strip[index])) return false;
        // Avoid adding a trigger beside the same trigger, which would increase
        // cluster size while producing fewer newly eligible four-row windows.
        return Enumerable.Range(1 - visibleRows, 2 * visibleRows - 1).All(offset => strip[(index + offset + strip.Count) % strip.Count] != target);
    }

    private static (string IngredientId, int Potency) ReadPot(IReadOnlyList<string> pot)
    {
        // The latest landing appears first among equally frequent ingredient groups.
        var winner = pot.Reverse().GroupBy(id => id, StringComparer.Ordinal).MaxBy(group => group.Count())!.Key;
        return (winner, pot.Where(id => id != winner).Sum(id => RulesByIngredient[id].Entry.SecondaryPotency));
    }

    private static bool ValidBottle(ArcanePotionBottle? bottle, long nextId) =>
        bottle is not null && bottle.Id > 0 && bottle.Id < nextId &&
        bottle.IngredientId is not null && RulesByIngredient.ContainsKey(bottle.IngredientId) && bottle.Potency is >= 0 and <= MaximumPotency;

    private static Rule GetRule(string ingredientId, int potency)
    {
        if (potency is < 0 or > MaximumPotency || string.IsNullOrWhiteSpace(ingredientId) ||
            !RulesByIngredient.TryGetValue(ingredientId, out var rule))
            throw new ArgumentException("The Arcane potion effect is invalid.");
        return rule;
    }

    private static EffectKind? PotionEffect(ArcanePotionBottle? potion) =>
        potion is null ? null : GetRule(potion.IngredientId, potion.Potency).Kind;

    private static int PayoutBonusPercent(int potency) => 2 + potency / 6;
    private static int FrequencyTickets(int potency) => 1 + potency / 9;
    private static decimal VisibleFrequencyBonus(int potency) =>
        100m * FrequencyTickets(potency) * ArcaneArchivesEngine.VisibleRows / (FrequencyScale * ArcaneArchivesEngine.BaseStripStops);

    private static long PayoutBonusPoints(long points, int percent) =>
        points <= 0 ? 0 : Math.Max(1L,
            checked((long)Math.Round(points * (percent / 100m), MidpointRounding.AwayFromZero)));
}
