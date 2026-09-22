using FortuneForge.Server.Games.Craps;
using Xunit;

namespace FortuneForge.Server.Tests.Games.Craps;

public sealed class CrapsGameServiceTests
{
    [Fact]
    public void Pass_line_round_is_private_and_opens_at_its_selected_stake()
    {
        var service = new CrapsGameService();
        var round = service.Start("player-a", new StartCrapsRoundRequest(10m));

        Assert.Equal("come-out", round.Phase);
        Assert.Equal(10m, round.Stake);
        Assert.Throws<CrapsAccessException>(() => service.Get("player-b", round.RoundId));
    }

    [Fact]
    public void Roll_records_a_valid_dice_outcome()
    {
        var service = new CrapsGameService();
        var round = service.Start("player-a", new StartCrapsRoundRequest(10m));
        var afterRoll = service.Roll("player-a", round.RoundId);

        var roll = Assert.Single(afterRoll.Rolls);
        Assert.InRange(roll.First, 1, 6);
        Assert.InRange(roll.Second, 1, 6);
        Assert.NotNull(afterRoll.LastOutcome);
    }
}
