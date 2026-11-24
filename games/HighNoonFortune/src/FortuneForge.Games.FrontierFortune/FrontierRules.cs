using System.Text.Json;

namespace FortuneForge.Games.FrontierFortune;

public sealed record FrontierSymbolRule(string Id, int Weight, int[] Payouts);
public sealed record FrontierTargetRule(string Kind, int Count, int HitPoints, int RewardMultiplier);
public sealed record FrontierBonusRules(int StartingShots, int MaximumShots, int SupplyShots, FrontierTargetRule[] Targets);
public sealed record FrontierRuleDefinition(string EngineVersion, string GameId, int Columns, int Rows,
    int PointValueInCents, long MinWagerPoints, long MaxWagerPoints, long StepPoints, int PayoutDivisor,
    string WildSymbol, string ScatterSymbol, int ScatterTrigger, FrontierSymbolRule[] Symbols,
    int[][] Paylines, FrontierBonusRules Bonus);

public static class FrontierRules
{
    public static FrontierRuleDefinition Definition { get; } = Load();
    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private static FrontierRuleDefinition Load()
    {
        using var source = typeof(FrontierRules).Assembly.GetManifestResourceStream("frontier-rules.json")
            ?? throw new InvalidOperationException("Frontier rules are missing.");
        var rules = JsonSerializer.Deserialize<FrontierRuleDefinition>(source, new JsonSerializerOptions(JsonSerializerDefaults.Web))
            ?? throw new InvalidOperationException("Frontier rules are invalid.");
        if (rules.GameId != FrontierFortuneEngine.GameId || rules.EngineVersion != FrontierFortuneEngine.EngineVersion ||
            rules.Columns != 5 || rules.Rows != 3 || rules.Symbols.Select(x => x.Id).Distinct().Count() != rules.Symbols.Length ||
            rules.Symbols.Any(x => x.Weight <= 0 || x.Payouts.Length is not (0 or 3)) ||
            rules.Paylines.Length != 20 || rules.Paylines.Any(x => x.Length != rules.Columns || x.Any(y => y < 0 || y >= rules.Rows)) ||
            rules.Bonus.Targets.Sum(x => x.Count) != rules.Rows * rules.Columns || rules.PayoutDivisor != rules.StepPoints)
            throw new InvalidOperationException("Frontier rules failed their structural checks.");
        return rules;
    }

    public static void ValidateWager(long wagerPoints)
    {
        var rules = Definition;
        if (wagerPoints < rules.MinWagerPoints || wagerPoints > rules.MaxWagerPoints || wagerPoints % rules.StepPoints != 0)
            throw new ArgumentException($"Choose a wager from {rules.MinWagerPoints} to {rules.MaxWagerPoints} points in steps of {rules.StepPoints}.", nameof(wagerPoints));
    }
}
