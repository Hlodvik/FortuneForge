using System.Numerics;
using System.Text.Json;
using FortuneForge.Games.Slots;

namespace FortuneForge.Games.CrookedRoad;

public sealed record CrookedRoadSymbolRule(string Id, int Weight, int[] Pays);
public sealed record CrookedRoadDestinationRule(string Id, int Cell, int Port);
public sealed record CrookedRoadMapRules(int EntryCell, int EntryPort, Dictionary<string, int> TilePorts,
    CrookedRoadDestinationRule[] Destinations);
public sealed record CrookedRoadRuleData(string ModelId, decimal PointValueInCents, CrookedRoadSymbolRule[] Symbols, int[][] Paylines,
    int PayoutScaleNumerator, int PayoutScaleDenominator, int PayoutUnits, long MinimumWagerPoints,
    long MaximumWagerPoints, long WagerIncrementPoints, int FundingSpins, int BonusExpectedMultiplier,
    int ScatterRequired, int FortLandingDenominator, int[] FortCoinValues, int[] FortCoinWeights, int[][] HillPrizes,
    CrookedRoadMapRules Road);

public static class CrookedRoadRules
{
    public const string GameId = "crooked-road-v1";
    public const string EngineVersion = "crooked-road-engine-v1";
    public const string SymbolSetId = "crooked-road-v1-symbols";
    public const string ReelSetId = "crooked-road-independent-reels-v1";
    public static decimal PointValueInCents => Data.PointValueInCents;
    public const int MaximumTickets = 16;
    public const int HandCapacity = 2;
    public const int QueueCapacity = 12;
    private static readonly JsonSerializerOptions Json = new() { PropertyNameCaseInsensitive = true };
    public static CrookedRoadRuleData Data { get; } = Load();
    public static IReadOnlyList<long> Wagers { get; } = Enumerable.Range(0,
        checked((int)((Data.MaximumWagerPoints - Data.MinimumWagerPoints) / Data.WagerIncrementPoints + 1)))
        .Select(index => Data.MinimumWagerPoints + index * Data.WagerIncrementPoints).ToArray();

    private static CrookedRoadRuleData Load()
    {
        using var stream = typeof(CrookedRoadRules).Assembly.GetManifestResourceStream(
            "FortuneForge.Games.CrookedRoad.Data.crooked-road-v1.json") ?? throw new InvalidOperationException("Missing Crooked Road rules.");
        var rules = JsonSerializer.Deserialize<CrookedRoadRuleData>(stream, Json) ?? throw new InvalidOperationException("Invalid Crooked Road rules.");
        if (rules.Symbols.Length != 9 || rules.Symbols.Select(symbol => symbol.Id).Distinct(StringComparer.Ordinal).Count() != 9 ||
            rules.Symbols.Any(symbol => symbol.Weight <= 0 || symbol.Pays.Length is not (0 or 3) || symbol.Pays.Any(value => value < 0)) ||
            rules.Symbols.Sum(symbol => symbol.Weight) != 100 || rules.Paylines.Length != 10 ||
            rules.Paylines.Any(line => line.Length != 5 || line.Any(row => row is < 0 or > 2)) ||
            rules.Paylines.Select(line => string.Join(',', line)).Distinct(StringComparer.Ordinal).Count() != 10 ||
            rules.PointValueInCents != 1 || rules.PayoutUnits <= 0 || rules.PayoutScaleNumerator <= 0 || rules.PayoutScaleDenominator <= 0 ||
            rules.MinimumWagerPoints <= 0 || rules.MaximumWagerPoints < rules.MinimumWagerPoints || rules.WagerIncrementPoints <= 0 ||
            rules.FundingSpins <= 0 || rules.BonusExpectedMultiplier <= 0 || rules.ScatterRequired is < 1 or > 15 ||
            rules.FortLandingDenominator < 2 || rules.FortCoinValues.Length != rules.FortCoinWeights.Length ||
            rules.FortCoinValues.Length == 0 || rules.FortCoinValues.Any(value => value <= 0) || rules.FortCoinWeights.Any(weight => weight <= 0) ||
            rules.HillPrizes.Length != 3 || rules.HillPrizes.Any(stage => stage.Length != 3 || stage.Any(value => value < 0)) ||
            rules.HillPrizes.Sum(stage => stage.Sum()) != rules.BonusExpectedMultiplier * 3)
            throw new InvalidOperationException("Invalid Crooked Road reel contract.");
        return rules;
    }

    public static void ValidateWager(long wager, bool funded = false)
    {
        if (wager < Data.MinimumWagerPoints || wager > Data.MaximumWagerPoints ||
            !funded && (wager - Data.MinimumWagerPoints) % Data.WagerIncrementPoints != 0)
            throw new ArgumentOutOfRangeException(nameof(wager), "Choose an available Crooked Road wager.");
    }

    public static string DrawSymbol(IGameRandomSource random)
    {
        var ticket = random.Next(100);
        foreach (var symbol in Data.Symbols)
        {
            ticket -= symbol.Weight;
            if (ticket < 0) return symbol.Id;
        }
        throw new InvalidOperationException("The random source returned an invalid symbol ticket.");
    }

    public static (int Units, string Symbol, int Length) LineScore(IReadOnlyList<string> line)
    {
        var best = (Units: 0, Symbol: string.Empty, Length: 0);
        foreach (var symbol in Data.Symbols.Where(symbol => symbol.Pays.Length == 3))
        {
            var length = 0;
            while (length < line.Count && (line[length] == symbol.Id || line[length] == "ACE")) length++;
            if (length >= 3 && symbol.Pays[length - 3] > best.Units) best = (symbol.Pays[length - 3], symbol.Id, length);
        }
        return best;
    }

    public static long FloorRatio(BigInteger numerator, BigInteger denominator)
    {
        if (numerator < 0 || denominator <= 0) throw new InvalidOperationException("Invalid award ratio.");
        return checked((long)(numerator / denominator));
    }

    public static SpinPayout Evaluate(IReadOnlyList<IReadOnlyList<string>> reels, long wager)
    {
        var payouts = new List<PaylinePayout>();
        for (var index = 0; index < Data.Paylines.Length; index++)
        {
            var route = Data.Paylines[index];
            var score = LineScore(route.Select((row, reel) => reels[reel][row]).ToArray());
            if (score.Units == 0) continue;
            var amount = FloorRatio((BigInteger)wager * score.Units * Data.PayoutScaleNumerator,
                Data.PayoutUnits * Data.PayoutScaleDenominator);
            if (amount == 0) continue;
            var positions = Enumerable.Range(0, score.Length).Select(reel => new GridPosition(reel, route[reel])).ToArray();
            var wilds = positions.Where(position => reels[position.Reel][position.Row] == "ACE").ToArray();
            payouts.Add(new(index + 1, amount, [new(new(index + 1, score.Symbol, score.Length, positions, wilds), score.Units, amount)]));
        }
        return new(payouts.Sum(line => line.AmountPoints), payouts);
    }
}
