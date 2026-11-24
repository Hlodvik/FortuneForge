using System.Numerics;
using FortuneForge.Games.FrontierFortune;

internal sealed record ExactBaseReturn(BigInteger Numerator, BigInteger Denominator, long Sequences)
{
    public double Rate => (double)Numerator / (double)Denominator;
    public string Fraction
    {
        get
        {
            var divisor = BigInteger.GreatestCommonDivisor(Numerator, Denominator);
            return $"{Numerator / divisor}/{Denominator / divisor}";
        }
    }
}

internal static class EconomicsMath
{
    public static ExactBaseReturn EvaluateBase()
    {
        var rules = FrontierRules.Definition;
        var columns = new string[rules.Columns][];
        var numerator = BigInteger.Zero;
        long sequences = 0;
        void Enumerate(int column, long weight)
        {
            if (column == columns.Length)
            {
                numerator += (BigInteger)weight * FrontierFortuneEngine.Evaluate(columns, rules.MinWagerPoints).Sum(win => win.AwardPoints);
                sequences++;
                return;
            }
            foreach (var symbol in rules.Symbols)
            {
                columns[column] = Enumerable.Repeat(symbol.Id, rules.Rows).ToArray();
                Enumerate(column + 1, weight * symbol.Weight);
            }
        }
        // Identical rows make every native payline see the same independent five-symbol sequence.
        Enumerate(0, 1);
        return new(numerator, BigInteger.Pow(rules.Symbols.Sum(symbol => symbol.Weight), rules.Columns) * rules.MinWagerPoints, sequences);
    }

    public static BigInteger Choose(int n, int k)
    {
        var value = BigInteger.One;
        for (var index = 1; index <= k; index++) value = value * (n - index + 1) / index;
        return value;
    }
}
