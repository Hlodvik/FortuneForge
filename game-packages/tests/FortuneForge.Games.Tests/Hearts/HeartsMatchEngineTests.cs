using FortuneForge.Games.Cards;
using FortuneForge.Games.Hearts;
using FortuneForge.Games.TrickTaking;

namespace FortuneForge.Games.Tests;

public sealed class HeartsMatchEngineTests
{
    [Fact]
    public void PassingMovesThreeCardsAndPreservesFourThirteenCardHands()
    {
        var state = HeartsMatchEngine.Start(77);
        var humanCards = state.Round.HandFor(PlayerSeat.North).Take(3).ToArray();

        state = HeartsMatchEngine.Apply(state, new PassHeartsCards(PlayerSeat.North, humanCards)).State;
        state = AdvanceOpponentsUntilNorth(state);

        Assert.Equal(HeartsPhase.Playing, state.Round.Phase);
        Assert.All(state.Round.Hands, hand => Assert.Equal(13, hand.Cards.Count));
        Assert.All(state.Round.Hands.SelectMany(hand => hand.Cards).GroupBy(card => card), group => Assert.Single(group));
        Assert.Equal(HeartsPassDirection.Left, state.PassDirection);
    }

    [Fact]
    public void FourParticipantsCanCompleteAStandardRound()
    {
        var state = HeartsMatchEngine.Start(123);

        state = HeartsMatchEngine.Apply(
            state,
            new PassHeartsCards(PlayerSeat.North, state.Round.HandFor(PlayerSeat.North).Take(3).ToArray())).State;
        state = AdvanceOpponentsUntilNorth(state);

        while (state.Round.Phase != HeartsPhase.Complete)
        {
            Assert.Equal(PlayerSeat.North, state.Round.Turn);
            var card = HeartsEngine.LegalCards(state.Round, PlayerSeat.North).First();
            state = HeartsMatchEngine.Apply(state, new PlayHeartsCard(PlayerSeat.North, card)).State;
            state = AdvanceOpponentsUntilNorth(state);
        }

        Assert.Equal(13, state.Round.CompletedTricks.Count);
        Assert.All(state.Round.Hands, hand => Assert.Empty(hand.Cards));
        Assert.Equal(26, state.Round.Scores.Sum(score => score.Points));
    }

    [Fact]
    public void MatchScoreEndsWhenAPlayerReachesTheTargetAndLowestScoreWins()
    {
        var score = new HeartsMatchScore(101, 12, 30, 25);

        Assert.Equal(PlayerSeat.East, score.WinningSeat(100));
    }

    private static HeartsMatchState AdvanceOpponentsUntilNorth(HeartsMatchState state)
    {
        var current = state;
        while (current.Round.Phase == HeartsPhase.Passing &&
               current.Round.SubmittedPasses.Count < TrickTakingRules.PlayerCount)
        {
            var seat = Enum.GetValues<PlayerSeat>()
                .First(candidate => !current.Round.SubmittedPasses.Any(pass => pass.Seat == candidate));
            current = HeartsMatchEngine.Apply(
                current,
                new PassHeartsCards(seat, current.Round.HandFor(seat).Take(3).ToArray())).State;
        }

        while (current.Round.Phase == HeartsPhase.Playing && current.Round.Turn != PlayerSeat.North)
        {
            var seat = current.Round.Turn;
            current = HeartsMatchEngine.Apply(
                current,
                new PlayHeartsCard(seat, HeartsEngine.LegalCards(current.Round, seat).First())).State;
        }
        return current;
    }
}
