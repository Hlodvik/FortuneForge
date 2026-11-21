using FortuneForge.Games.Abstractions.Auditing;
using FortuneForge.Games.Craps;
using FortuneForge.Games.DropMerge;
using FortuneForge.Games.Roulette;
using FortuneForge.Games.TwentyFortyEight;
using FortuneForge.Server.Games.Roulette;

namespace FortuneForge.Server.SecurityAudit;

internal sealed record PracticeCrapsExtraBet(string Kind, decimal Stake, bool Resolved, bool Won, decimal? TotalReturn);

/// <summary>Independent terminal/outcome arithmetic for non-credit games; not an RNG or full command replay attestation.</summary>
internal static class PracticeOutcomeAudit
{
    internal const string TwentyFortyEightVersion = TwentyFortyEightAudit.TerminalAuditorVersion;
    internal const string DropMergeVersion = DropMergeAudit.TerminalAuditorVersion;
    internal const string CrapsVersion = "craps-outcome-v1";
    internal const string RouletteVersion = RouletteAudit.Version;

    internal static IReadOnlyList<RoundAuditFinding> Check(TwentyFortyEightState state) =>
        TwentyFortyEightAudit.CheckTerminal(state);

    internal static IReadOnlyList<RoundAuditFinding> Check(DropMergeState state, int dropColumn, int dropRow) =>
        DropMergeAudit.CheckTerminal(state, dropColumn, dropRow);

    internal static IReadOnlyList<RoundAuditFinding> Check(CrapsPassLineState state,
        IReadOnlyList<PracticeCrapsExtraBet> extras)
    {
        var rolls = state.Rolls;
        if (state.Phase != CrapsPassLinePhase.Resolved || rolls.IsDefaultOrEmpty || state.Bet.Stake <= 0 ||
            rolls.Any(dice => dice.First.Value is < 1 or > 6 || dice.Second.Value is < 1 or > 6))
            return Result("craps-outcome-mismatch", false, "The ordered dice, pass-line result or side-bet return disagrees with the rules.");
        var first = rolls[0].Total;
        var last = rolls[^1];
        CrapsRollResult expected;
        decimal returned;
        if (first is 7 or 11)
        {
            expected = CrapsRollResult.NaturalWin;
            returned = state.Bet.Stake * 2;
        }
        else if (first is 2 or 3 or 12)
        {
            expected = CrapsRollResult.CrapsLoss;
            returned = 0;
        }
        else if (rolls.Length >= 2 && first is 4 or 5 or 6 or 8 or 9 or 10 &&
                 rolls.Skip(1).Take(rolls.Length - 2).All(dice => dice.Total != first && dice.Total != 7) &&
                 (last.Total == 7 || last.Total == first))
        {
            expected = last.Total == first ? CrapsRollResult.PointHit : CrapsRollResult.SevenOut;
            returned = expected == CrapsRollResult.PointHit ? state.Bet.Stake * 2 : 0;
        }
        else return Result("craps-outcome-mismatch", false, "The ordered dice, pass-line result or side-bet return disagrees with the rules.");
        var point = expected is CrapsRollResult.PointHit or CrapsRollResult.SevenOut ? first : (int?)null;
        var valid = state.Point == point && state.LastOutcome is { IsTerminal: true } outcome &&
            outcome.Dice == last && outcome.Result == expected && outcome.TotalReturn == returned &&
            (point is not null || rolls.Length == 1) && extras.All(extra => ValidExtra(extra, first, point, expected));
        return Result("craps-outcome-mismatch", valid,
            "Ordered dice, pass-line return and every recorded virtual side bet match independent rule arithmetic.");
    }

    internal static IReadOnlyList<RoundAuditFinding> Check(RouletteRoundState state) => RouletteAudit.Check(state);

    private static bool ValidExtra(PracticeCrapsExtraBet bet, int first, int? point, CrapsRollResult result)
    {
        if (bet.Stake <= 0 || !bet.Resolved) return false;
        var expected = bet.Kind switch
        {
            "field" when first is 2 or 12 => bet.Stake * 3,
            "field" when first is 3 or 4 or 9 or 10 or 11 => bet.Stake * 2,
            "any-seven" when first == 7 => bet.Stake * 5,
            "any-craps" when first is 2 or 3 or 12 => bet.Stake * 8,
            "odds" when point is 4 or 10 && result == CrapsRollResult.PointHit => bet.Stake * 3,
            "odds" when point is 5 or 9 && result == CrapsRollResult.PointHit => bet.Stake * 2.5m,
            "odds" when point is 6 or 8 && result == CrapsRollResult.PointHit => bet.Stake * 2.2m,
            "field" or "any-seven" or "any-craps" or "odds" => 0m,
            _ => -1m
        };
        return expected >= 0 && bet.TotalReturn == expected && bet.Won == (expected > 0);
    }

    private static IReadOnlyList<RoundAuditFinding> Result(string code, bool valid, string success) => valid
        ? [new("audit-scope", "info", success + " Command history, RNG provenance and behavioral cheating remain outside this check.")]
        : [new(code, "error", "The recorded terminal state or settlement disagrees with an independent rule calculation.")];
}
