using FortuneForge.Games.LiarsDice;

namespace FortuneForge.Games.Tests;

public sealed class LiarsDiceMatchEngineTests
{
    [Fact]
    public void ChallengeRemovesOneDieAndNextRoundStartsAfterTheLoser()
    {
        var state = LiarsDiceMatchEngine.Start(77, ["you", "opponent-a"], dicePerPlayer: 2);
        var bid = LiarsDiceMatchEngine.Apply(
            state,
            new PlaceLiarsDiceBid("you", new LiarsDiceBid(1, new FortuneForge.Games.Dice.DieValue(6))));
        var challenge = LiarsDiceMatchEngine.Apply(bid.State, new ChallengeLiarsDiceBid("opponent-a"));

        Assert.NotNull(challenge.Outcome);
        Assert.Equal(1, challenge.State.DiceCounts[challenge.Outcome!.LoserId]);
        var next = LiarsDiceMatchEngine.StartNextRound(challenge.State, 99);

        Assert.Equal(2, next.RoundNumber);
        Assert.Equal(2, next.Round.TurnOrder.Length);
        Assert.Equal(3, next.Round.Hands.Values.Sum(hand => hand.Length));
    }

    [Fact]
    public void LegalParticipantCommandsCanPlayUntilOnePlayerRemains()
    {
        var allPlayers = new[] { "you", "opponent-a", "opponent-b", "opponent-c" };
        var state = LiarsDiceMatchEngine.Start(123, allPlayers.ToArray(), dicePerPlayer: 2);

        for (var action = 0; action < 256 && state.Winner is null; action++)
        {
            if (state.Round.Phase == LiarsDiceRoundPhase.Resolved)
            {
                state = LiarsDiceMatchEngine.StartNextRound(state, (uint)(500 + action));
                continue;
            }

            var player = state.CurrentPlayerId;
            LiarsDiceCommand command = state.Round.CurrentBid is null
                ? new PlaceLiarsDiceBid(player, new LiarsDiceBid(1, new FortuneForge.Games.Dice.DieValue(1)))
                : new ChallengeLiarsDiceBid(player);
            state = LiarsDiceMatchEngine.Apply(state, command).State;
        }

        Assert.NotNull(state.Winner);
        Assert.Single(state.DiceCounts, item => item.Value > 0);
    }
}
