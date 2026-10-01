namespace FortuneForge.Games.Hearts;

public static class HeartsMatchEngine
{
    public const int DefaultTargetScore = 100;

    public static HeartsMatchState Start(uint seed, int targetScore = DefaultTargetScore)
    {
        ValidateTargetScore(targetScore);
        return new HeartsMatchState(
            targetScore,
            1,
            HeartsPassDirection.Left,
            HeartsEngine.Start(seed, HeartsPassDirection.Left),
            new HeartsMatchScore(0, 0, 0, 0),
            null);
    }

    public static HeartsMatchTransition Apply(HeartsMatchState state, HeartsCommand command)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(command);
        if (state.Winner is not null)
            throw new HeartsRuleException("The Hearts match is already complete.");

        var transition = HeartsEngine.Apply(state.Round, command);
        var next = state with { Round = transition.State };
        var message = transition.Message;
        if (transition.State.Phase == HeartsPhase.Complete)
        {
            var score = state.Score.Add(transition.State.Scores);
            var winner = score.WinningSeat(state.TargetScore);
            next = next with { Score = score, Winner = winner };
            if (winner is { } winningSeat)
                message = $"{transition.Message} {winningSeat} wins the Hearts match with the lowest score.";
        }

        return new HeartsMatchTransition(next, transition.EventType, message);
    }

    public static HeartsMatchState StartNextRound(HeartsMatchState state, uint seed)
    {
        ArgumentNullException.ThrowIfNull(state);
        if (state.Winner is not null)
            throw new HeartsRuleException("The Hearts match is already complete.");
        if (state.Round.Phase != HeartsPhase.Complete)
            throw new HeartsRuleException("The current Hearts round must finish before starting another.");

        var direction = HeartsRules.NextPassDirection(state.PassDirection);
        return state with
        {
            RoundNumber = checked(state.RoundNumber + 1),
            PassDirection = direction,
            Round = HeartsEngine.Start(seed, direction),
        };
    }

    private static void ValidateTargetScore(int targetScore)
    {
        if (targetScore <= 0)
            throw new ArgumentOutOfRangeException(nameof(targetScore), "The Hearts target score must be positive.");
    }
}
