using FortuneForge.Games.Slots;

namespace FortuneForge.Games.FrontierFortune;

public sealed record FrontierCell(int Column, int Row);
public sealed record FrontierLineWin(int Line, string Symbol, int Count, long AwardPoints, FrontierCell[] Cells);
public sealed record FrontierRandomDraw(int MaximumExclusive, int Value);
public sealed record FrontierSpin(string[][] Reels, FrontierLineWin[] Wins, long TotalPoints, FrontierBonus? Bonus,
    FrontierRandomDraw[] Draws);

/// <summary>Frontier's independent cell draws, left-edge lines, and tactical bounty board.</summary>
public sealed class FrontierFortuneEngine(IGameRandomSource random)
{
    public const string GameId = "high-noon-fortune-v1";
    public const string EngineVersion = "frontier-fortune-engine-v1";
    public const string SymbolSetId = "frontier-fortune-symbols-v1";
    public const string ReelSetId = "frontier-fortune-independent-cells-v1";
    public const string PaytableId = "frontier-fortune-paytable-v1";

    public FrontierSpin Spin(long wagerPoints, string bonusId)
    {
        FrontierRules.ValidateWager(wagerPoints);
        var rules = FrontierRules.Definition;
        List<FrontierRandomDraw> draws = [];
        int Draw(int maximum)
        {
            var value = random.Next(maximum);
            if (value < 0 || value >= maximum) throw new InvalidOperationException("The random source returned an invalid draw.");
            draws.Add(new(maximum, value));
            return value;
        }
        var totalWeight = rules.Symbols.Sum(x => x.Weight);
        var reels = Enumerable.Range(0, rules.Columns).Select(_ => Enumerable.Range(0, rules.Rows).Select(_ =>
        {
            var value = Draw(totalWeight);
            foreach (var symbol in rules.Symbols)
            {
                value -= symbol.Weight;
                if (value < 0) return symbol.Id;
            }
            throw new InvalidOperationException("The symbol weights are invalid.");
        }).ToArray()).ToArray();
        var wins = Evaluate(reels, wagerPoints);
        FrontierBonus? bonus = null;
        if (reels.Sum(x => x.Count(y => y == rules.ScatterSymbol)) >= rules.ScatterTrigger)
        {
            var targets = rules.Bonus.Targets.SelectMany(x => Enumerable.Repeat(x.Kind, x.Count)).ToArray();
            for (var index = targets.Length - 1; index > 0; index--)
            {
                var other = Draw(index + 1);
                (targets[index], targets[other]) = (targets[other], targets[index]);
            }
            bonus = FrontierBounties.Create(bonusId, wagerPoints, targets);
        }
        return new(reels, wins, wins.Sum(x => x.AwardPoints), bonus, draws.ToArray());
    }

    public static FrontierLineWin[] Evaluate(string[][] reels, long wagerPoints)
    {
        FrontierRules.ValidateWager(wagerPoints);
        ValidateReels(reels);
        var rules = FrontierRules.Definition;
        List<FrontierLineWin> wins = [];
        foreach (var (path, index) in rules.Paylines.Select((path, index) => (path, index)))
        {
            FrontierLineWin? best = null;
            foreach (var symbol in rules.Symbols.Where(x => x.Payouts.Length == 3))
            {
                var count = 0;
                for (var column = 0; column < rules.Columns; column++)
                {
                    var cell = reels[column][path[column]];
                    if (cell != symbol.Id && cell != rules.WildSymbol) break;
                    count++;
                }
                if (count < 3) continue;
                var award = checked(wagerPoints * symbol.Payouts[count - 3] / rules.PayoutDivisor);
                if (best is null || award > best.AwardPoints)
                    best = new(index + 1, symbol.Id, count, award,
                        Enumerable.Range(0, count).Select(column => new FrontierCell(column, path[column])).ToArray());
            }
            if (best is not null) wins.Add(best);
        }
        return wins.ToArray();
    }

    public static void ValidateReels(string[][] reels)
    {
        var rules = FrontierRules.Definition;
        if (reels is not { Length: 5 } || reels.Any(x => x is not { Length: 3 } ||
            x.Any(y => !rules.Symbols.Any(z => z.Id == y)))) throw new ArgumentException("The saved reels are invalid.");
    }

    public static GameDefinition CreateGame() => new()
    {
        Id = GameId, Layout = new() { ReelCount = FrontierRules.Definition.Columns, VisibleRows = FrontierRules.Definition.Rows,
            PaylineCount = FrontierRules.Definition.Paylines.Length },
        Symbols = new() { SymbolSetId = SymbolSetId, WildSymbolId = FrontierRules.Definition.WildSymbol,
            PaylineSymbolIds = FrontierRules.Definition.Symbols.Where(x => x.Payouts.Length > 0).Select(x => x.Id).ToList(),
            NativeWildMatchLengths = [3, 4, 5], WildSubstitutionMatchLengths = [3, 4, 5] },
        Matching = new() { MinimumRunLength = 3, AllowMultipleRunsPerPayline = false },
        Math = new() { ReelSetId = ReelSetId, PaytableId = PaytableId, PayoutMultiplier = 1m / FrontierRules.Definition.PayoutDivisor, Targets = new() },
        Wagering = new() { PointValueInCents = FrontierRules.Definition.PointValueInCents, MinimumWagerPoints = FrontierRules.Definition.MinWagerPoints,
            MaximumWagerPoints = FrontierRules.Definition.MaxWagerPoints, WagerIncrementPoints = FrontierRules.Definition.StepPoints },
        Paylines = FrontierRules.Definition.Paylines.Select(x => x.ToList()).ToList()
    };
    public static SymbolSetDefinition CreateSymbolSet() => new() { Id = SymbolSetId,
        Symbols = FrontierRules.Definition.Symbols.Select(x => new SymbolDefinition { Id = x.Id }).ToList() };
    public static PaytableDefinition CreatePaytable() => new() { Id = PaytableId, SymbolSetId = SymbolSetId,
        Rules = FrontierRules.Definition.Symbols.SelectMany(x => x.Payouts.Select((value, index) =>
            new PayoutRule { SymbolId = x.Id, MatchLength = index + 3, Multiplier = value })).ToList() };
    public static ReelSetDefinition CreateReelSet() => new() { Id = ReelSetId, SymbolSetId = SymbolSetId,
        Reels = Enumerable.Range(0, FrontierRules.Definition.Columns).Select(_ => FrontierRules.Definition.Symbols
            .SelectMany(x => Enumerable.Repeat(x.Id, x.Weight)).ToList()).ToList() };
}
