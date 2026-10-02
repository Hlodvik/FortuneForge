using FortuneForge.Games.Dice;

namespace FortuneForge.Games.Craps;

public static class CrapsEngine
{
    public static CrapsPassLineState StartPassLine(CrapsPassLineBet bet)
    {
        ArgumentNullException.ThrowIfNull(bet);
        if (string.IsNullOrWhiteSpace(bet.PlayerId))
            throw new ArgumentException("A player ID is required.", nameof(bet));
        if (bet.Stake <= 0m)
            throw new ArgumentOutOfRangeException(nameof(bet), "A pass-line stake must be positive.");

        return new CrapsPassLineState(bet, CrapsPassLinePhase.ComeOut, null, [], null);
    }

    public static CrapsTransition Roll(CrapsPassLineState state, DicePair dice)
    {
        ArgumentNullException.ThrowIfNull(state);
        dice.Validate();
        if (state.Phase == CrapsPassLinePhase.Resolved)
            throw new CrapsRuleException("The pass-line bet has already been resolved.");

        return state.Phase switch
        {
            CrapsPassLinePhase.ComeOut => ResolveComeOut(state, dice),
            CrapsPassLinePhase.Point => ResolvePoint(state, dice),
            _ => throw new CrapsRuleException("The pass-line state is invalid."),
        };
    }

    private static CrapsTransition ResolveComeOut(CrapsPassLineState state, DicePair dice)
    {
        if (dice.Total is 7 or 11)
            return Resolve(state, dice, CrapsRollResult.NaturalWin, state.Bet.Stake * 2m);
        if (dice.Total is 2 or 3 or 12)
            return Resolve(state, dice, CrapsRollResult.CrapsLoss, 0m);

        var outcome = new CrapsRollOutcome(dice, CrapsRollResult.PointEstablished, false, null);
        var next = state with
        {
            Phase = CrapsPassLinePhase.Point,
            Point = dice.Total,
            Rolls = state.Rolls.Add(dice),
            LastOutcome = outcome,
        };
        return new CrapsTransition(next, outcome);
    }

    private static CrapsTransition ResolvePoint(CrapsPassLineState state, DicePair dice)
    {
        if (dice.Total == state.Point)
            return Resolve(state, dice, CrapsRollResult.PointHit, state.Bet.Stake * 2m);
        if (dice.Total == 7)
            return Resolve(state, dice, CrapsRollResult.SevenOut, 0m);

        var outcome = new CrapsRollOutcome(dice, CrapsRollResult.NoDecision, false, null);
        var next = state with { Rolls = state.Rolls.Add(dice), LastOutcome = outcome };
        return new CrapsTransition(next, outcome);
    }

    private static CrapsTransition Resolve(
        CrapsPassLineState state,
        DicePair dice,
        CrapsRollResult result,
        decimal totalReturn)
    {
        var outcome = new CrapsRollOutcome(dice, result, true, totalReturn);
        var resolved = state with
        {
            Phase = CrapsPassLinePhase.Resolved,
            Rolls = state.Rolls.Add(dice),
            LastOutcome = outcome,
        };
        return new CrapsTransition(resolved, outcome);
    }
}
