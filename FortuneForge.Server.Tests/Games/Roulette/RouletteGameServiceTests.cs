using FortuneForge.Server.Games.Roulette;
using Xunit;

namespace FortuneForge.Server.Tests.Games.Roulette;

public sealed class RouletteGameServiceTests
{
    [Fact]
    public void Players_join_the_oldest_open_table_and_keep_separate_balances()
    {
        var service = new RouletteGameService();
        var first = service.Start("player-a", "Alice");
        var second = service.Start("player-b", "Bob");

        Assert.Equal(first.RoundId, second.RoundId);
        Assert.Equal(1000m, first.Balance);
        Assert.Equal(1000m, second.Balance);
        Assert.Equal(2, second.Players.Count);
        Assert.Single(second.Players, player => player.IsCurrentPlayer && player.DisplayName == "Bob");
    }

    [Fact]
    public void A_full_table_causes_the_next_player_to_open_a_new_table()
    {
        var service = new RouletteGameService();
        var first = service.Start("player-1", "Player 1");
        for (var index = 2; index <= 8; index++)
        {
            var joined = service.Start($"player-{index}", $"Player {index}");
            Assert.Equal(first.RoundId, joined.RoundId);
        }

        var ninth = service.Start("player-9", "Player 9");

        Assert.NotEqual(first.RoundId, ninth.RoundId);
        Assert.Single(ninth.Players);
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

    [Fact]
    public void Bets_and_settlements_are_projected_only_to_their_owner()
    {
        var service = new RouletteGameService();
        var first = service.Start("player-a", "Alice");
        _ = service.Start("player-b", "Bob");
        _ = service.PlaceBet("player-a", first.RoundId, new PlaceRouletteBetRequest("red", 10m, null, []));
        var secondView = service.Get("player-b", first.RoundId);

        Assert.Empty(secondView.Bets);
        Assert.Equal(1000m, secondView.Balance);

        _ = service.PlaceBet("player-b", first.RoundId, new PlaceRouletteBetRequest("black", 5m, null, []));
        _ = service.Spin("player-a", first.RoundId);

        Assert.Single(service.Get("player-a", first.RoundId).Settlements);
        Assert.Single(service.Get("player-b", first.RoundId).Settlements);
    }

    [Fact]
    public void A_settled_table_advances_in_place_for_its_existing_players()
    {
        var service = new RouletteGameService();
        var opened = service.Start("player-a", "Alice");
        _ = service.PlaceBet("player-a", opened.RoundId, new PlaceRouletteBetRequest("red", 10m, null, []));
        _ = service.Spin("player-a", opened.RoundId);

        var next = service.Start("player-a", "Alice");

        Assert.Equal(opened.RoundId, next.RoundId);
        Assert.Equal(2, next.RoundNumber);
        Assert.Equal("open", next.Phase);
        Assert.Empty(next.Bets);
    }
}
