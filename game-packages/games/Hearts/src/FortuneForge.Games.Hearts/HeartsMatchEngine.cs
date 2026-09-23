using FortuneForge.Games.Cards;
using FortuneForge.Games.TrickTaking;

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

    public static HeartsMatchTransition AdvanceBotTurn(
        HeartsMatchState state,
        IReadOnlySet<PlayerSeat> botSeats,
        HeartsBotAgent bot,
        int skillLevel,
        ulong seed,
        int version,
        CardBotGameOptions options,
        PlayerSeat humanSeat = PlayerSeat.North)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(botSeats);
        ArgumentNullException.ThrowIfNull(bot);
        ArgumentNullException.ThrowIfNull(options);
        if (state.Winner is not null)
            throw new HeartsRuleException("The Hearts match is already complete.");

        var round = state.Round;
        if (round.Phase == HeartsPhase.Passing)
        {
            if (!round.SubmittedPasses.Any(pass => pass.Seat == humanSeat))
                throw new HeartsRuleException("The human player must submit a pass first.");
            var seat = Enum.GetValues<PlayerSeat>()
                .FirstOrDefault(candidate => botSeats.Contains(candidate) && !round.SubmittedPasses.Any(pass => pass.Seat == candidate));
            if (!botSeats.Contains(seat))
                throw new HeartsRuleException("All Hearts bot passes have already been submitted.");
            var observation = new HeartsBotObservation(seat, round.HandFor(seat), [], round.Phase, round.PassDirection, round.CurrentTrick, round.HeartsBroken, round.Scores);
            var decision = bot.ChoosePass(observation, skillLevel, seed, version, options);
            return Apply(state, new PassHeartsCards(seat, decision.Cards));
        }

        if (round.Phase != HeartsPhase.Playing || !botSeats.Contains(round.Turn))
            throw new HeartsRuleException("It is not a Hearts bot's turn.");

        var legalCards = HeartsEngine.LegalCards(round, round.Turn);
        var playObservation = new HeartsBotObservation(round.Turn, round.HandFor(round.Turn), legalCards, round.Phase, round.PassDirection, round.CurrentTrick, round.HeartsBroken, round.Scores);
        var card = bot.ChooseCard(playObservation, skillLevel, seed, version, options);
        return Apply(state, new PlayHeartsCard(round.Turn, card));
    }

    public static HeartsMatchState AdvanceBotsUntilHuman(
        HeartsMatchState state,
        IReadOnlySet<PlayerSeat> botSeats,
        HeartsBotAgent bot,
        int skillLevel,
        ulong seed,
        CardBotGameOptions options,
        PlayerSeat humanSeat = PlayerSeat.North,
        int maximumActions = 256)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(botSeats);
        ArgumentNullException.ThrowIfNull(bot);
        ArgumentNullException.ThrowIfNull(options);
        if (maximumActions <= 0)
            throw new ArgumentOutOfRangeException(nameof(maximumActions));

        var current = state;
        for (var action = 0; action < maximumActions; action++)
        {
            if (current.Winner is not null || current.Round.Phase == HeartsPhase.Complete)
                return current;
            if (current.Round.Phase == HeartsPhase.Passing)
            {
                if (!current.Round.SubmittedPasses.Any(pass => pass.Seat == humanSeat) ||
                    current.Round.SubmittedPasses.Count == TrickTakingRules.PlayerCount)
                    return current;
            }
            else if (!botSeats.Contains(current.Round.Turn))
            {
                return current;
            }

            current = AdvanceBotTurn(current, botSeats, bot, skillLevel, seed, action, options, humanSeat).State;
        }

        throw new HeartsRuleException("The Hearts bot turn budget was exhausted before reaching a human turn.");
    }

    private static void ValidateTargetScore(int targetScore)
    {
        if (targetScore <= 0)
            throw new ArgumentOutOfRangeException(nameof(targetScore), "The Hearts target score must be positive.");
    }
}
