using FortuneForge.Games.Cards;
using FortuneForge.Games.LiarsDice;

namespace FortuneForge.Games.Tests;

public sealed class LiarsDiceMatchEngineTests
{
    [Fact]
    public void ChallengeRemovesOneDieAndNextRoundStartsAfterTheLoser()
    {
        var state = LiarsDiceMatchEngine.Start(77, ["you", "bot-1"], dicePerPlayer: 2);
        var bid = LiarsDiceMatchEngine.Apply(
            state,
            new PlaceLiarsDiceBid("you", new LiarsDiceBid(1, new FortuneForge.Games.Dice.DieValue(6))));
        var challenge = LiarsDiceMatchEngine.Apply(bid.State, new ChallengeLiarsDiceBid("bot-1"));

        Assert.NotNull(challenge.Outcome);
        Assert.Equal(1, challenge.State.DiceCounts[challenge.Outcome!.LoserId]);
        var next = LiarsDiceMatchEngine.StartNextRound(challenge.State, 99);

        Assert.Equal(2, next.RoundNumber);
        Assert.Equal(2, next.Round.TurnOrder.Length);
        Assert.Equal(3, next.Round.Hands.Values.Sum(hand => hand.Length));
    }

    [Fact]
    public void DeterministicBotsCanPlayUntilOnePlayerRemains()
    {
        var allPlayers = new HashSet<string>(["you", "bot-1", "bot-2", "bot-3"], StringComparer.Ordinal);
        var bot = new LiarsDiceBotAgent();
        var options = new CardBotGameOptions();
        var state = LiarsDiceMatchEngine.Start(123, allPlayers.ToArray(), dicePerPlayer: 2);

        for (var action = 0; action < 256 && state.Winner is null; action++)
        {
            if (state.Round.Phase == LiarsDiceRoundPhase.Resolved)
            {
                state = LiarsDiceMatchEngine.StartNextRound(state, (uint)(500 + action));
                continue;
            }

            state = LiarsDiceMatchEngine.AdvanceBotTurn(
                state,
                allPlayers,
                bot,
                CardBotSkillLevels.Strong,
                456,
                action,
                options).State;
        }

        Assert.NotNull(state.Winner);
        Assert.Single(state.DiceCounts, item => item.Value > 0);
    }
}
