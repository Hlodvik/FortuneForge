using System.Numerics;

namespace FortuneForge.Games.Mines.Server;

/// <summary>Independent arithmetic/sequence audit. Does not call the production payout or transition functions.</summary>
public static class MinesOutcomeAudit
{
    public const string Version = "mines-outcome-v1";

    public static bool Verify(MinesStoredRound record, long actualWalletCreditCents)
    {
        try
        {
            var state = record.State;
            var mines = state.MinePositions.ToArray(); var reveals = state.RevealedTiles.ToArray();
            if (state.MineCount is < 1 or > 24 || mines.Length != state.MineCount || mines.Distinct().Count() != mines.Length ||
                mines.Any(tile => tile is < 0 or > 24) || reveals.Any(tile => tile is < 0 or > 24) ||
                reveals.Distinct().Count() != reveals.Length || reveals.Length == 0 || record.UpdatedAt < record.CreatedAt)
                return false;
            if (state.PayoutPolicyVersion is not (1 or 2) || state.HouseEdgeBasisPoints is not (100 or 1000) ||
                state.PayoutPolicyVersion == 2 && state.HouseEdgeBasisPoints != 1000 ||
                state.WagerCents < (state.PayoutPolicyVersion == 2 ? 100 : 10) ||
                state.WagerCents > (state.PayoutPolicyVersion == 2 ? 500 : 10000)) return false;
            var hits = reveals.Count(mines.Contains);
            var safe = reveals.Length - hits; var safeTotal = 25 - state.MineCount;
            var hit = mines.Contains(reveals[^1]);
            if (hits > 1 || hits == 1 && !hit || safe > safeTotal || hit && safe == safeTotal) return false;
            var expected = ExpectedReturn(state.WagerCents, state.MineCount, safe, hit, state.HouseEdgeBasisPoints, state.PayoutPolicyVersion);
            var expectedStatus = hit ? expected == 0 ? MinesRoundStatus.Lost : MinesRoundStatus.Consolation
                : safe == safeTotal ? MinesRoundStatus.Cleared : MinesRoundStatus.CashedOut;
            var expectedVersion = 1L + reveals.Length + (expectedStatus == MinesRoundStatus.CashedOut ? 1 : 0);
            return state.Status == expectedStatus && record.Version == expectedVersion && state.PayoutCents == expected
                && actualWalletCreditCents == expected;
        }
        catch (Exception exception) when (exception is ArgumentException or ArithmeticException or InvalidOperationException)
        { return false; }
    }

    // Survival is multiplied step by step, rather than using the game's combinations implementation.
    public static long ExpectedReturn(long wager, int mines, int safe, bool hit, int edge, int policy)
    {
        if (mines is < 1 or > 24 || safe < 0 || safe > 25 - mines || hit && safe == 25 - mines || wager < 0 ||
            edge is not (100 or 1000) || policy is not (1 or 2) || policy == 2 && edge != 1000)
            throw new ArgumentException("Unsupported audit payout terms.");
        var values = new Fraction[26 - mines];
        values[0] = new(10000 - edge, 10000);
        for (var step = 1; step < values.Length; step++)
            values[step] = values[step - 1] * new Fraction(26 - step, 26 - mines - step);
        if (policy == 2 && values[^1].GreaterThan(50))
        {
            var firstOver = Array.FindIndex(values, value => value.GreaterThan(50));
            var knee = firstOver - 1; var start = values[knee]; var tail = values.Length - 1 - knee;
            for (var step = firstOver; step < values.Length; step++)
                values[step] = start + (new Fraction(50, 1) - start) * new Fraction(step - knee, tail);
        }
        var payout = hit ? policy == 1 ? new Fraction(0, 1)
            : (values[safe] * new Fraction(25 - safe, 1) - values[safe + 1] * new Fraction(25 - mines - safe, 1))
                * new Fraction(1, mines)
            : values[safe];
        return checked((long)(wager * payout.Numerator / payout.Denominator));
    }

    private readonly record struct Fraction
    {
        public Fraction(BigInteger numerator, BigInteger denominator)
        {
            if (denominator <= 0) throw new ArgumentException("Invalid fraction.");
            var divisor = BigInteger.GreatestCommonDivisor(numerator, denominator);
            Numerator = numerator / divisor; Denominator = denominator / divisor;
        }
        public BigInteger Numerator { get; }
        public BigInteger Denominator { get; }
        public bool GreaterThan(int value) => Numerator > value * Denominator;
        public static Fraction operator +(Fraction a, Fraction b) => new(a.Numerator * b.Denominator + b.Numerator * a.Denominator, a.Denominator * b.Denominator);
        public static Fraction operator -(Fraction a, Fraction b) => new(a.Numerator * b.Denominator - b.Numerator * a.Denominator, a.Denominator * b.Denominator);
        public static Fraction operator *(Fraction a, Fraction b) => new(a.Numerator * b.Numerator, a.Denominator * b.Denominator);
    }
}
