using FortuneForge.Games.Slots;
using FortuneForge.Games.Slots.Runtime;

namespace FortuneForge.Games.PharaohsAscent.RtpAnalysis;

/// <summary>Selected paid matches only: a diagnostic, never an alternative payout calculation.</summary>
internal sealed class PayoutContributions
{
    private readonly Dictionary<ContributionKey, ContributionTotals> totals = [];

    internal void Add(ClassicSlotSpinOutcome outcome, SpinPayout payout, bool energyApplied, decimal energyMultiplier, bool free)
    {
        if (outcome.Payout.Paylines.Count != payout.Paylines.Count)
            throw new InvalidOperationException("Energy changed the selected payout structure.");
        for (var lineIndex = 0; lineIndex < outcome.Payout.Paylines.Count; lineIndex++)
        {
            var originalLine = outcome.Payout.Paylines[lineIndex];
            var settledLine = payout.Paylines[lineIndex];
            if (originalLine.PaylineId != settledLine.PaylineId || originalLine.Matches.Count != settledLine.Matches.Count)
                throw new InvalidOperationException("Energy changed the selected payout structure.");
            for (var matchIndex = 0; matchIndex < originalLine.Matches.Count; matchIndex++)
            {
                var original = originalLine.Matches[matchIndex];
                var settled = settledLine.Matches[matchIndex];
                var scarab = originalLine.PaylineId >= PharaohsAscentScarabs.FirstPaylineId;
                var ruleLength = scarab ? (int?)null : outcome.Audit.Paytable.Rules
                    .Where(rule => rule.SymbolId == original.Match.SymbolId && rule.MatchLength <= original.Match.MatchLength)
                    .MaxBy(rule => rule.MatchLength)?.MatchLength;
                var key = new ContributionKey(scarab ? "scarab-trail" : "paytable", original.Match.SymbolId,
                    original.Match.MatchLength, ruleLength, original.Multiplier, outcome.Audit.BaseGame.Math.PayoutMultiplier);
                if (!totals.TryGetValue(key, out var row)) totals[key] = row = new();
                row.Add(free, outcome.WagerPoints, original.AmountPoints, settled.AmountPoints, energyApplied, energyMultiplier);
            }
        }
    }

    internal object[] Report(long paidWagerPoints, long paidBase, long earnedBase, long paidSettled, long earnedSettled)
    {
        if (totals.Values.Sum(row => row.PaidBasePoints) != paidBase || totals.Values.Sum(row => row.EarnedBasePoints) != earnedBase ||
            totals.Values.Sum(row => row.PaidSettledPoints) != paidSettled || totals.Values.Sum(row => row.EarnedSettledPoints) != earnedSettled)
            throw new InvalidOperationException("Per-symbol contribution totals do not reconcile to the settled reel returns.");
        return totals.OrderBy(pair => pair.Key.Kind, StringComparer.Ordinal).ThenBy(pair => pair.Key.SymbolId, StringComparer.Ordinal)
            .ThenBy(pair => pair.Key.MatchLength).ThenBy(pair => pair.Key.Multiplier).Select(pair => (object)new
            {
                pair.Key.Kind, pair.Key.SymbolId, pair.Key.MatchLength, pair.Key.PaytableRuleLength,
                pair.Key.Multiplier, pair.Key.PayoutScale,
                totals = pair.Value,
                paidRtp = pair.Value.PaidSettledPoints / (double)paidWagerPoints,
                earnedRtp = pair.Value.EarnedSettledPoints / (double)paidWagerPoints,
                totalRtp = (pair.Value.PaidSettledPoints + pair.Value.EarnedSettledPoints) / (double)paidWagerPoints,
                selectedMatchLinearRtpPerMultiplier = pair.Key.PayoutScale *
                    (pair.Value.PaidMatchedWagerPoints + pair.Value.EarnedMatchedWagerPoints +
                     pair.Value.EnergyBoostExposurePoints) / paidWagerPoints,
            }).ToArray();
    }

    private sealed record ContributionKey(string Kind, string SymbolId, int MatchLength, int? PaytableRuleLength,
        long Multiplier, decimal PayoutScale);

    private sealed class ContributionTotals
    {
        public long PaidMatches { get; private set; }
        public long EarnedMatches { get; private set; }
        public long PaidMatchedWagerPoints { get; private set; }
        public long EarnedMatchedWagerPoints { get; private set; }
        public long EnergyMatchedWagerPoints { get; private set; }
        public decimal EnergyBoostExposurePoints { get; private set; }
        public long PaidBasePoints { get; private set; }
        public long EarnedBasePoints { get; private set; }
        public long PaidSettledPoints { get; private set; }
        public long EarnedSettledPoints { get; private set; }
        public long EnergyExtraPoints => PaidSettledPoints + EarnedSettledPoints - PaidBasePoints - EarnedBasePoints;

        internal void Add(bool free, long wager, long original, long settled, bool energyApplied, decimal energyMultiplier)
        {
            if (free) { EarnedMatches++; EarnedMatchedWagerPoints += wager; EarnedBasePoints += original; EarnedSettledPoints += settled; }
            else { PaidMatches++; PaidMatchedWagerPoints += wager; PaidBasePoints += original; PaidSettledPoints += settled; }
            if (energyApplied)
            {
                EnergyMatchedWagerPoints += wager;
                EnergyBoostExposurePoints += wager * (energyMultiplier - 1m);
            }
        }
    }
}
