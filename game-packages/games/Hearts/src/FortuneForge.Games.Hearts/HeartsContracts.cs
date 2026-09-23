using FortuneForge.Games.Cards;
using FortuneForge.Games.TrickTaking;

namespace FortuneForge.Games.Hearts;

public enum HeartsPhase
{
    Passing,
    Playing,
    Complete,
}

public enum HeartsPassDirection
{
    Left,
    Right,
    Across,
    Hold,
}

public enum HeartsEventType
{
    CardsPassed,
    CardPlayed,
    TrickCompleted,
    RoundCompleted,
}

public sealed record HeartsScore(PlayerSeat Seat, int Points);

public sealed record HeartsPass(PlayerSeat Seat, IReadOnlyList<PlayingCard> Cards);

public sealed record HeartsMatchScore(int North, int East, int South, int West)
{
    public int For(PlayerSeat seat) => seat switch
    {
        PlayerSeat.North => North,
        PlayerSeat.East => East,
        PlayerSeat.South => South,
        PlayerSeat.West => West,
        _ => throw new ArgumentOutOfRangeException(nameof(seat)),
    };

    public HeartsMatchScore Add(IReadOnlyList<HeartsScore> round)
    {
        ArgumentNullException.ThrowIfNull(round);
        return new HeartsMatchScore(
            checked(North + round.Single(score => score.Seat == PlayerSeat.North).Points),
            checked(East + round.Single(score => score.Seat == PlayerSeat.East).Points),
            checked(South + round.Single(score => score.Seat == PlayerSeat.South).Points),
            checked(West + round.Single(score => score.Seat == PlayerSeat.West).Points));
    }

    public PlayerSeat? WinningSeat(int targetScore)
    {
        if (targetScore <= 0)
            throw new ArgumentOutOfRangeException(nameof(targetScore), "The Hearts target score must be positive.");

        var scores = Enum.GetValues<PlayerSeat>()
            .Select(seat => (Seat: seat, Score: For(seat)))
            .ToArray();
        if (!scores.Any(item => item.Score >= targetScore))
            return null;
        return scores.OrderBy(item => item.Score).ThenBy(item => item.Seat).First().Seat;
    }
}

public sealed record HeartsState(
    uint Seed,
    PlayerSeat Turn,
    HeartsPhase Phase,
    IReadOnlyList<PlayerHand> Hands,
    TrickState CurrentTrick,
    IReadOnlyList<CompletedTrick> CompletedTricks,
    IReadOnlyList<HeartsScore> Scores,
    bool HeartsBroken,
    HeartsPassDirection PassDirection = HeartsPassDirection.Hold,
    IReadOnlyList<HeartsPass>? Passes = null)
{
    public IReadOnlyList<PlayingCard> HandFor(PlayerSeat seat) =>
        Hands.Single(hand => hand.Seat == seat).Cards;

    public IReadOnlyList<HeartsPass> SubmittedPasses => Passes ?? [];
}

public abstract record HeartsCommand(PlayerSeat Seat);

public sealed record PassHeartsCards(PlayerSeat Seat, IReadOnlyList<PlayingCard> Cards) : HeartsCommand(Seat);

public sealed record PlayHeartsCard(PlayerSeat Seat, PlayingCard Card) : HeartsCommand(Seat);

public sealed record HeartsTransition(
    HeartsState State,
    HeartsEventType EventType,
    string Message);

public sealed record HeartsMatchState(
    int TargetScore,
    int RoundNumber,
    HeartsPassDirection PassDirection,
    HeartsState Round,
    HeartsMatchScore Score,
    PlayerSeat? Winner)
{
    public HeartsPhase Phase => Round.Phase;

    public PlayerSeat Turn => Round.Turn;
}

public sealed record HeartsMatchTransition(
    HeartsMatchState State,
    HeartsEventType EventType,
    string Message);

public sealed class HeartsRuleException(string message) : InvalidOperationException(message);
