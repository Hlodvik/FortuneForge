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
        Assert.Equal(1000, round.TotalWagerCents);
        Assert.Equal(0, round.TotalReturnCents);
        Assert.Equal(0, round.Revision);
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
        Assert.Equal(1, afterRoll.Revision);
        Assert.Equal(1000, afterRoll.TotalWagerCents);
        Assert.Equal((long)((afterRoll.LastOutcome.TotalReturn ?? 0m) * 100m), afterRoll.TotalReturnCents);
    }

    [Fact]
    public void Common_one_roll_bets_are_accepted_and_settled_with_the_come_out()
    {
        var service = new CrapsGameService();
        var round = service.Start("player-a", new StartCrapsRoundRequest(10m,
            [new CrapsExtraBetRequest("field", 5m), new CrapsExtraBetRequest("any-seven", 2m)]));

        Assert.Equal(2, round.ExtraBets.Count);
        Assert.Equal(1700, round.TotalWagerCents);
        var afterRoll = service.Roll("player-a", round.RoundId);
        Assert.All(afterRoll.ExtraBets, bet => Assert.True(bet.Resolved));
        Assert.Equal(1700, afterRoll.TotalWagerCents);
        Assert.Equal((long)(((afterRoll.LastOutcome!.TotalReturn ?? 0m) + afterRoll.ExtraBets.Sum(bet => bet.TotalReturn ?? 0m)) * 100m), afterRoll.TotalReturnCents);
    }
}
