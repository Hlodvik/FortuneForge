using FortuneForge.Server.Slots.Models;

namespace FortuneForge.Server.Slots.Payouts;

public sealed class PayoutCalculator : IPayoutCalculator
{
    public SpinPayout Calculate(
        IReadOnlyList<PaylineEvaluation> evaluations,
        PaytableDefinition paytable,
        long wagerPoints)
    {
        if (wagerPoints <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(wagerPoints), "Wager points must be positive.");
        }

        var rules = paytable.Rules.ToDictionary(
            rule => (rule.SymbolId, rule.MatchLength),
            rule => rule.Multiplier);
        var payouts = new List<PaylinePayout>();

        foreach (var evaluation in evaluations)
        {
            var best = evaluation.Candidates
                .Select(candidate => PriceCandidate(evaluation.PaylineId, candidate, rules, wagerPoints))
                .Where(candidate => candidate.AmountPoints > 0)
                .OrderByDescending(candidate => candidate.AmountPoints)
                .ThenBy(candidate => CandidateKey(candidate), StringComparer.Ordinal)
                .FirstOrDefault();

            if (best is not null)
            {
                payouts.Add(best);
            }
        }

        return new SpinPayout(payouts.Sum(payout => payout.AmountPoints), payouts);
    }

    private static PaylinePayout PriceCandidate(
        int paylineId,
        MatchCandidate candidate,
        IReadOnlyDictionary<(string SymbolId, int MatchLength), long> rules,
        long wagerPoints)
    {
        var paidMatches = candidate.Matches
            .Select(match =>
            {
                var multiplier = rules.GetValueOrDefault((match.SymbolId, match.MatchLength));
                return new PaidMatch(match, multiplier, checked(wagerPoints * multiplier));
            })
            .Where(match => match.AmountPoints > 0)
            .ToArray();

        return new PaylinePayout(paylineId, paidMatches.Sum(match => match.AmountPoints), paidMatches);
    }

    private static string CandidateKey(PaylinePayout payout) => string.Join('|', payout.Matches.Select(match =>
        $"{match.Match.SymbolId}:{match.Match.MatchLength}:{match.Match.Positions[0].Reel}"));
}
