using FortuneForge.Server.Slots.Models;

namespace FortuneForge.Server.Slots.Payouts;

public sealed class PayoutCalculator : IPayoutCalculator
{
    public SpinPayout Calculate(
        IReadOnlyList<PaylineEvaluation> evaluations,
        GameDefinition game,
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
        var pricedCandidates = new List<PaylinePayout>();

        foreach (var evaluation in evaluations)
        {
            var paylinePayoutStep = game.Math.PaylinePayoutSteps[evaluation.PaylineId - 1];
            var best = evaluation.Candidates
                .Select(candidate => PriceCandidate(
                    evaluation.PaylineId,
                    candidate,
                    rules,
                    wagerPoints,
                    game.Layout.ReelCount,
                    paylinePayoutStep))
                .Where(candidate => candidate.AmountPoints > 0)
                .OrderByDescending(candidate => candidate.AmountPoints)
                .ThenBy(candidate => CandidateKey(candidate), StringComparer.Ordinal)
                .FirstOrDefault();

            if (best is not null)
            {
                pricedCandidates.Add(best);
            }
        }

        var payouts = pricedCandidates
            .Where(payout => payout.Matches.Any(match => match.Match.MatchLength == game.Layout.ReelCount))
            .ToList();
        var claimedShortMatches = new HashSet<string>(StringComparer.Ordinal);

        foreach (var candidate in pricedCandidates
                     .Where(payout => payout.Matches.All(match =>
                         match.Match.MatchLength < game.Layout.ReelCount))
                     .OrderByDescending(payout => payout.Matches.Count)
                     .ThenByDescending(payout => payout.AmountPoints)
                     .ThenBy(payout => CandidateKey(payout), StringComparer.Ordinal))
        {
            var matchKeys = candidate.Matches
                .Select(match => MatchGeometryKey(match.Match))
                .ToArray();
            if (matchKeys.Any(claimedShortMatches.Contains))
            {
                continue;
            }

            payouts.Add(candidate);
            foreach (var matchKey in matchKeys)
            {
                claimedShortMatches.Add(matchKey);
            }
        }

        return new SpinPayout(payouts.Sum(payout => payout.AmountPoints), payouts);
    }

    private static PaylinePayout PriceCandidate(
        int paylineId,
        MatchCandidate candidate,
        IReadOnlyDictionary<(string SymbolId, int MatchLength), long> rules,
        long wagerPoints,
        int fullMatchLength,
        int paylinePayoutStep)
    {
        var paidMatches = candidate.Matches
            .Select(match =>
            {
                var baseMultiplier = rules.GetValueOrDefault((match.SymbolId, match.MatchLength));
                var multiplier = match.MatchLength == fullMatchLength && baseMultiplier > 0
                    ? checked(baseMultiplier + paylinePayoutStep)
                    : baseMultiplier;
                return new PaidMatch(match, multiplier, checked(wagerPoints * multiplier));
            })
            .Where(match => match.AmountPoints > 0)
            .ToArray();

        return new PaylinePayout(paylineId, paidMatches.Sum(match => match.AmountPoints), paidMatches);
    }

    private static string CandidateKey(PaylinePayout payout) => string.Join('|', payout.Matches.Select(match =>
        $"{match.Match.SymbolId}:{match.Match.MatchLength}:{match.Match.Positions[0].Reel}"));

    private static string MatchGeometryKey(SymbolMatch match) =>
        $"{match.MatchLength}:" + string.Join(',', match.Positions.Select(position =>
            $"{position.Reel}.{position.Row}"));
}
