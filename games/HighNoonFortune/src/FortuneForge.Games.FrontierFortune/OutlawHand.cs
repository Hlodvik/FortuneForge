using System.Text.Json;
using FortuneForge.Games.Slots;

namespace FortuneForge.Games.FrontierFortune;

public sealed record OutlawAnteRules(int RequiredChips, int WantedForOneChip, int WantedForTwoChips);
public sealed record OutlawCardWeight(string Id, int Weight);
public sealed record OutlawHandRules(int Deals, OutlawCardWeight[] Symbols, Dictionary<string, int> Awards);
public sealed record OutlawRuleDefinition(string EngineVersion, string BaseMathVersion, OutlawAnteRules Ante, OutlawHandRules Hand);
public static class OutlawRules
{
    public const string EngineVersion = "frontier-fortune-engine-v2";
    public static OutlawRuleDefinition Definition { get; } = Load();
    private static OutlawRuleDefinition Load()
    {
        using var stream = typeof(OutlawRules).Assembly.GetManifestResourceStream("frontier-outlaw-rules.json")!;
        var rules = JsonSerializer.Deserialize<OutlawRuleDefinition>(stream, FrontierRules.Json)!;
        if (rules.EngineVersion != EngineVersion || rules.Ante.RequiredChips != 12 || rules.Hand.Deals != 3 ||
            !rules.Hand.Symbols.Select(x => x.Id).SequenceEqual(new[] { "J", "Q", "K", "A", "WILD" }) ||
            rules.Hand.Symbols.Any(x => x.Weight <= 0) || rules.Hand.Awards.Count != 7)
            throw new InvalidOperationException("Outlaw's Hand rules are invalid.");
        return rules;
    }
}

public sealed record OutlawCard(string? Symbol, bool Held);
public sealed record OutlawHandRound(string RoundId, long WagerPoints, int DealsRemaining, string Phase,
    OutlawCard[] Hand, long AwardPoints = 0, string? Category = null);
public sealed record OutlawHandAward(string Category, int Multiplier);

/// <summary>Ranks selected from each actual reel persist only when held; the final confirmed hand alone pays.</summary>
public static class OutlawHand
{
    public static OutlawHandRound Start(string id, long wager)
    {
        FrontierRules.ValidateWager(wager);
        return new(id, wager, OutlawRules.Definition.Hand.Deals - 1, "choose",
            Enumerable.Range(0, 5).Select(_ => new OutlawCard(null, false)).ToArray());
    }
    public static OutlawHandRound Deal(OutlawHandRound before, bool[] held)
    {
        Validate(before);
        if (before.Phase != "hold" || before.DealsRemaining <= 0 || held is not { Length: 5 })
            throw new ArgumentException("Choose your holds before the next deal.");
        return before with { DealsRemaining = before.DealsRemaining - 1, Phase = "choose",
            Hand = before.Hand.Select((card, index) => card with { Held = held[index] }).ToArray() };
    }
    public static OutlawHandRound Confirm(OutlawHandRound before, string[][] reels, int?[] selectedRows)
    {
        Validate(before);
        ValidateCardGrid(reels);
        if (before.Phase != "choose" || selectedRows is not { Length: 5 })
            throw new ArgumentException("Choose one card from each unheld reel.");
        var cards = before.Hand.Select((card, column) =>
        {
            if (card.Held)
            {
                if (selectedRows[column] is not null) throw new ArgumentException("Held cards cannot be replaced.");
                return card;
            }
            if (selectedRows[column] is not (>= 0 and < 3)) throw new ArgumentException("Select a visible row on every unheld reel.");
            return new OutlawCard(reels[column][selectedRows[column]!.Value], false);
        }).ToArray();
        if (before.DealsRemaining > 0) return before with { Hand = cards, Phase = "hold" };
        var award = Classify(cards.Select(x => x.Symbol!).ToArray());
        return before with { Hand = cards, Phase = "complete", Category = award.Category,
            AwardPoints = checked(before.WagerPoints * award.Multiplier) };
    }
    public static OutlawHandAward Classify(IReadOnlyList<string> hand)
    {
        if (hand.Count != 5 || hand.Any(x => !OutlawRules.Definition.Hand.Symbols.Any(y => y.Id == x)))
            throw new ArgumentException("An Outlaw hand needs five card ranks.");
        if (hand.All(x => x == "WILD")) return Award("grand");
        var wilds = hand.Count(x => x == "WILD");
        var counts = new[] { "J", "Q", "K", "A" }.Select(rank => hand.Count(x => x == rank)).ToArray();
        var best = new OutlawHandAward("none", 0);
        void Assign(int remaining, int start)
        {
            if (remaining == 0)
            {
                var groups = counts.Where(x => x > 0).OrderDescending().ToArray();
                var category = groups[0] switch
                {
                    5 => "five", 4 => "four", 3 when groups.Length == 2 => "full-house", 3 => "trips",
                    2 when groups.Count(x => x == 2) == 2 => "two-pair", 2 => "pair", _ => "none"
                };
                var candidate = category == "none" ? new OutlawHandAward("none", 0) : Award(category);
                if (candidate.Multiplier > best.Multiplier) best = candidate;
                return;
            }
            for (var rank = start; rank < counts.Length; rank++)
            { counts[rank]++; Assign(remaining - 1, rank); counts[rank]--; }
        }
        Assign(wilds, 0);
        return best;
    }
    public static void Validate(OutlawHandRound round)
    {
        FrontierRules.ValidateWager(round.WagerPoints);
        if (!Guid.TryParseExact(round.RoundId, "N", out var id) || id == Guid.Empty || round.DealsRemaining is < 0 or > 2 ||
            round.Phase is not ("choose" or "hold" or "complete") || round.Hand is not { Length: 5 } ||
            round.Hand.Any(x => x is null || x.Symbol is not null && !OutlawRules.Definition.Hand.Symbols.Any(y => y.Id == x.Symbol) || x.Held && x.Symbol is null) ||
            round.Phase != "choose" && round.Hand.Any(x => x.Symbol is null) || round.Phase == "hold" && round.DealsRemaining == 0)
            throw new ArgumentException("The saved Outlaw hand is invalid.");
        if (round.Phase == "complete")
        {
            var expected = Classify(round.Hand.Select(x => x.Symbol!).ToArray());
            if (round.DealsRemaining != 0 || round.Category != expected.Category || round.AwardPoints != round.WagerPoints * expected.Multiplier)
                throw new ArgumentException("The saved final hand award is invalid.");
        }
        else if (round.AwardPoints != 0 || round.Category is not null) throw new ArgumentException("An unfinished hand cannot have an award.");
    }
    public static void ValidateCardGrid(string[][] reels)
    {
        if (reels is not { Length: 5 } || reels.Any(x => x is not { Length: 3 } ||
            x.Any(id => !OutlawRules.Definition.Hand.Symbols.Any(y => y.Id == id))))
            throw new ArgumentException("The dealer grid is invalid.");
    }
    private static OutlawHandAward Award(string category) => new(category, OutlawRules.Definition.Hand.Awards[category]);
}

public sealed record OutlawDraw(string[][] Reels, FrontierRandomDraw[] Draws);
public static class OutlawReels
{
    public static OutlawDraw Draw(IGameRandomSource random, bool handOnly)
    {
        var symbols = handOnly ? OutlawRules.Definition.Hand.Symbols : FrontierRules.Definition.Symbols
            .Select(x => new OutlawCardWeight(x.Id, x.Weight)).ToArray();
        var total = symbols.Sum(x => x.Weight);
        List<FrontierRandomDraw> draws = [];
        var reels = Enumerable.Range(0, 5).Select(_ => Enumerable.Range(0, 3).Select(_ =>
        {
            var value = random.Next(total);
            if (value < 0 || value >= total) throw new InvalidOperationException("Invalid random draw.");
            draws.Add(new(total, value));
            foreach (var symbol in symbols) { value -= symbol.Weight; if (value < 0) return symbol.Id; }
            throw new InvalidOperationException("Invalid card weights.");
        }).ToArray()).ToArray();
        return new(reels, draws.ToArray());
    }
}
