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
        state = HeartsMatchEngine.AdvanceBotsUntilHuman(
            state,
            new HashSet<PlayerSeat> { PlayerSeat.East, PlayerSeat.South, PlayerSeat.West },
            new HeartsBotAgent(),
            CardBotSkillLevels.Strong,
            91,
            new CardBotGameOptions());

        Assert.Equal(HeartsPhase.Playing, state.Round.Phase);
        Assert.All(state.Round.Hands, hand => Assert.Equal(13, hand.Cards.Count));
        Assert.All(state.Round.Hands.SelectMany(hand => hand.Cards).GroupBy(card => card), group => Assert.Single(group));
        Assert.Equal(HeartsPassDirection.Left, state.PassDirection);
    }

    [Fact]
    public void AllBotsAndHumanCanCompleteAStandardRound()
    {
        var botSeats = new HashSet<PlayerSeat> { PlayerSeat.East, PlayerSeat.South, PlayerSeat.West };
        var bot = new HeartsBotAgent();
        var options = new CardBotGameOptions();
        var state = HeartsMatchEngine.Start(123);

        state = HeartsMatchEngine.Apply(
            state,
            new PassHeartsCards(PlayerSeat.North, state.Round.HandFor(PlayerSeat.North).Take(3).ToArray())).State;
        state = HeartsMatchEngine.AdvanceBotsUntilHuman(state, botSeats, bot, CardBotSkillLevels.Strong, 321, options);

        while (state.Round.Phase != HeartsPhase.Complete)
        {
            Assert.Equal(PlayerSeat.North, state.Round.Turn);
            var card = HeartsEngine.LegalCards(state.Round, PlayerSeat.North).First();
            state = HeartsMatchEngine.Apply(state, new PlayHeartsCard(PlayerSeat.North, card)).State;
            state = HeartsMatchEngine.AdvanceBotsUntilHuman(state, botSeats, bot, CardBotSkillLevels.Strong, 321, options);
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
}
