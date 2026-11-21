using System.Diagnostics.CodeAnalysis;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using FortuneForge.Games.Abstractions.Analytics;
using FortuneForge.Games.Abstractions.Auditing;
using FortuneForge.Games.Craps;
using FortuneForge.Games.Dice;
using FortuneForge.Games.Abstractions.Wallet;
using FortuneForge.Games.Firestore;
using Google.Cloud.Firestore;

namespace FortuneForge.Server.Games.Craps;

public sealed partial class FirestorePaidCrapsStore
{
    private static PaidCrapsExtra[] InitialExtras(IReadOnlyList<CrapsExtraBetRequest>? requests)
    {
        if (requests is null || requests.Count == 0) return [];
        if (requests.Count > 3) throw new CrapsPaidException("craps-invalid-bets", "Choose at most one of each side bet.", 400);
        var extras = new List<PaidCrapsExtra>(requests.Count);
        foreach (var request in requests)
        {
            if (request is null || string.IsNullOrWhiteSpace(request.Kind)) throw new CrapsPaidException("craps-invalid-bets", "Choose a valid side bet.", 400);
            var kind = request.Kind.Trim().ToLowerInvariant();
            if (kind is not ("field" or "any-seven" or "any-craps") || extras.Any(extra => extra.Kind == kind))
                throw new CrapsPaidException("craps-invalid-bets", "Choose at most one of each supported side bet.", 400);
            ValidateStake(request.Stake);
            extras.Add(new(kind, request.Stake, false, false, null));
        }
        return [..extras];
    }

    private static PaidCrapsExtra[] SettleExtras(IReadOnlyList<PaidCrapsExtra> extras, CrapsPassLineState state, int total) =>
        extras.Select(extra => extra.Resolved || extra.Kind == "odds" && state.Phase != CrapsPassLinePhase.Resolved
            ? extra : Settle(extra, state, total)).ToArray();

    private static PaidCrapsExtra Settle(PaidCrapsExtra bet, CrapsPassLineState state, int total)
    {
        var payout = bet.Kind switch
        {
            "field" when total is 2 or 12 => bet.Stake * 3m,
            "field" when total is 3 or 4 or 9 or 10 or 11 => bet.Stake * 2m,
            "any-seven" when total == 7 => bet.Stake * 5m,
            "any-craps" when total is 2 or 3 or 12 => bet.Stake * 8m,
            "odds" when state.LastOutcome?.Result is CrapsRollResult.PointHit => state.Point switch
            {
                4 or 10 => bet.Stake * 3m,
                5 or 9 => bet.Stake * 2.5m,
                6 or 8 => bet.Stake * 2.2m,
                _ => throw new CrapsRuleException("The point does not support odds."),
            },
            _ => 0m,
        };
        return bet with { Resolved = true, Won = payout > 0, TotalReturn = payout,
            ResolvedOnRoll = bet.WagerId is null ? null : state.Rolls.Length };
    }

    private static CrapsRoundResponse ToResponse(PaidCrapsRound round, long balance) => new(
        round.Id, round.State.Bet.Stake, round.State.Phase switch
        {
            CrapsPassLinePhase.ComeOut => "come-out", CrapsPassLinePhase.Point => "point", _ => "resolved"
        }, round.State.Point,
        round.State.Rolls.Select((dice, index) => new CrapsRollResponse(index + 1, dice.First.Value, dice.Second.Value, dice.Total,
            index == round.State.Rolls.Length - 1 && round.State.LastOutcome is { } outcome ? ResultName(outcome.Result) : null)).ToArray(),
        round.State.LastOutcome is { } last ? new CrapsOutcomeResponse(last.Dice.First.Value, last.Dice.Second.Value, last.Dice.Total,
            ResultName(last.Result), last.IsTerminal, last.TotalReturn) : null,
        round.Extras.Select(extra => new CrapsExtraBetResponse(extra.Kind, extra.Stake, extra.Resolved, extra.Won, extra.TotalReturn,
            extra.WagerId, extra.PlacedForRoll, extra.ResolvedOnRoll, extra.Cancelled)).ToArray(), balance,
        round.Revision, round.TotalWagerCents, round.TotalReturnCents);

    private static void ValidateRevision(PaidCrapsRound round, long? revision, int? rollNumber)
    {
        if (revision is null or < 0 || rollNumber is null or < 1)
            throw new CrapsPaidException("craps-revision-required", "Refresh the round before submitting an action.", 400);
        if (revision != round.Revision || rollNumber != round.State.Rolls.Length + 1)
            throw new CrapsPaidException("craps-round-stale", "The round changed in another tab. Refresh it before submitting a new action.", 409);
    }

    private static string ResultName(CrapsRollResult result) => result switch
    {
        CrapsRollResult.NaturalWin => "natural-win", CrapsRollResult.CrapsLoss => "craps-loss",
        CrapsRollResult.PointEstablished => "point-established", CrapsRollResult.PointHit => "point-hit",
        CrapsRollResult.SevenOut => "seven-out", CrapsRollResult.NoDecision => "no-decision",
        _ => throw new ArgumentOutOfRangeException(nameof(result)),
    };

    private static void ValidateStake(decimal stake)
    {
        if (stake is < MinimumStake or > MaximumStake || stake % MinimumStake != 0m)
            throw new CrapsPaidException("craps-invalid-wager", "Choose a wager from R1 through R100 in whole rand.", 400);
    }
    private static long Cents(decimal value) => checked((long)(value * 100m));
}
