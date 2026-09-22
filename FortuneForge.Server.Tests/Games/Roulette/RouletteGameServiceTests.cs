using FortuneForge.Server.Games.Roulette;
using Xunit;

namespace FortuneForge.Server.Tests.Games.Roulette;

public sealed class RouletteGameServiceTests
{
    [Fact]
    public void Practice_round_is_private_and_uses_a_separate_free_balance()
    {
        var service = new RouletteGameService();
        var round = service.Start("player-a");

        Assert.Equal(1000m, round.Balance);
        Assert.Equal("open", round.Phase);
        Assert.Throws<RouletteAccessException>(() => service.Get("player-b", round.RoundId));
    }

    [Fact]
    public void A_valid_bet_can_be_settled_without_touching_an_account_balance()
    {
        var service = new RouletteGameService();
        var round = service.Start("player-a");
        var bet = service.PlaceBet("player-a", round.RoundId, new PlaceRouletteBetRequest("red", 10m, null, []));

        Assert.Equal(990m, bet.Balance);
        Assert.Single(bet.Bets);

        var settled = service.Spin("player-a", round.RoundId);
        Assert.Equal("settled", settled.Phase);
        Assert.NotNull(settled.WinningPocket);
        Assert.Single(settled.Settlements);
    }
}
