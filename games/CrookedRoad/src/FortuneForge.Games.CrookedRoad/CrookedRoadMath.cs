using System.Numerics;

namespace FortuneForge.Games.CrookedRoad;

public readonly record struct CrookedRoadFraction
{
    public BigInteger Numerator { get; }
    public BigInteger Denominator { get; }
    public CrookedRoadFraction(BigInteger numerator, BigInteger denominator)
    {
        if (denominator <= 0) throw new ArgumentOutOfRangeException(nameof(denominator));
        var divisor = BigInteger.GreatestCommonDivisor(numerator, denominator);
        Numerator = numerator / divisor;
        Denominator = denominator / divisor;
    }
    public static CrookedRoadFraction operator +(CrookedRoadFraction left, CrookedRoadFraction right) =>
        new(left.Numerator * right.Denominator + right.Numerator * left.Denominator, left.Denominator * right.Denominator);
    public static CrookedRoadFraction operator *(CrookedRoadFraction left, CrookedRoadFraction right) =>
        new(left.Numerator * right.Numerator, left.Denominator * right.Denominator);
    public double Value => (double)Numerator / (double)Denominator;
}

/// <summary>Exact finite-model expectations. No observed or fitted RTP constant controls live awards.</summary>
public static class CrookedRoadMath
{
    public const long LineDenominator = 10_000_000_000L;
    private static readonly Lazy<long[]> WellExpectations = new(() => Enumerable.Range(0, 3).Select(origin =>
        Enumerable.Range(0, 3).Sum(radius => CrookedRoadRules.Data.Paylines.Sum(route => WeightedLineScore(
            route.Select((row, reel) => Math.Abs(reel - 2) + Math.Abs(row - origin) <= radius).ToArray())))).ToArray());
    private static readonly Lazy<CrookedRoadFraction> FortExpectation = new(ComputeFortExpectation);
    public static long WellExpectedScoreNumerator(int origin) => WellExpectations.Value[origin];
    public static CrookedRoadFraction FortExpectedScore => FortExpectation.Value;

    public static long WeightedLineScore(IReadOnlyList<bool> wild)
    {
        var line = new string[5];
        long total = 0;
        void Visit(int position, long weight)
        {
            if (position == 5) { total = checked(total + CrookedRoadRules.LineScore(line).Units * weight); return; }
            if (wild[position]) { line[position] = "ACE"; Visit(position + 1, checked(weight * 100)); return; }
            foreach (var symbol in CrookedRoadRules.Data.Symbols)
            {
                line[position] = symbol.Id;
                Visit(position + 1, checked(weight * symbol.Weight));
            }
        }
        Visit(0, 1);
        return total;
    }

    public static CrookedRoadFraction BaseReturn
    {
        get
        {
            var rules = CrookedRoadRules.Data;
            return new((BigInteger)WeightedLineScore(new bool[5]) * rules.Paylines.Length * rules.PayoutScaleNumerator,
                (BigInteger)LineDenominator * rules.PayoutUnits * rules.PayoutScaleDenominator);
        }
    }

    public static CrookedRoadFraction ScatterProbability
    {
        get
        {
            var weight = CrookedRoadRules.Data.Symbols.Single(symbol => symbol.Id == "FREE").Weight;
            BigInteger numerator = 0;
            for (var count = CrookedRoadRules.Data.ScatterRequired; count <= 15; count++)
                numerator += Choose(15, count) * BigInteger.Pow(weight, count) * BigInteger.Pow(100 - weight, 15 - count);
            return new(numerator, BigInteger.Pow(100, 15));
        }
    }

    public static CrookedRoadFraction FullReturnUpperBound => BaseReturn +
        new CrookedRoadFraction(CrookedRoadRules.Data.BonusExpectedMultiplier, CrookedRoadRules.Data.FundingSpins) +
        ScatterProbability * new CrookedRoadFraction(CrookedRoadRules.Data.BonusExpectedMultiplier, 1);

    public static long WellAward(long wager, long score, int origin) => CrookedRoadRules.FloorRatio(
        (BigInteger)wager * score * CrookedRoadRules.Data.BonusExpectedMultiplier * LineDenominator,
        WellExpectedScoreNumerator(origin));
    public static long FortAward(long wager, long score) => CrookedRoadRules.FloorRatio(
        (BigInteger)wager * score * CrookedRoadRules.Data.BonusExpectedMultiplier * FortExpectedScore.Denominator,
        FortExpectedScore.Numerator);

    private static CrookedRoadFraction ComputeFortExpectation()
    {
        var rules = CrookedRoadRules.Data;
        var meanCoin = new CrookedRoadFraction(rules.FortCoinValues.Zip(rules.FortCoinWeights).Sum(pair => pair.First * pair.Second), rules.FortCoinWeights.Sum());
        var memo = new Dictionary<(int, int), CrookedRoadFraction>();
        CrookedRoadFraction Visit(int held, int misses)
        {
            if (held == 9 || misses == 0) return new(0, 1);
            if (memo.TryGetValue((held, misses), out var saved)) return saved;
            var empty = 9 - held;
            var denominator = BigInteger.Pow(rules.FortLandingDenominator, empty);
            var result = new CrookedRoadFraction(0, 1);
            for (var landed = 0; landed <= empty; landed++)
            {
                var probability = new CrookedRoadFraction(Choose(empty, landed) *
                    BigInteger.Pow(rules.FortLandingDenominator - 1, empty - landed), denominator);
                var continuation = landed == 0 ? Visit(held, misses - 1) :
                    meanCoin * new CrookedRoadFraction(landed, 1) + Visit(held + landed, 3);
                result += probability * continuation;
            }
            memo[(held, misses)] = result;
            return result;
        }
        return Visit(0, 3);
    }

    private static BigInteger Choose(int total, int count)
    {
        BigInteger result = 1;
        for (var index = 1; index <= count; index++) result = result * (total - index + 1) / index;
        return result;
    }
}
