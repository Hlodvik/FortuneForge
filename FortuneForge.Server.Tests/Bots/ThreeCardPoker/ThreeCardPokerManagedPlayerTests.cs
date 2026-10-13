using FortuneForge.Games.ThreeCardPoker;
using FortuneForge.Server.Bots;
using FortuneForge.Server.Bots.ThreeCardPoker;
using Xunit;

namespace FortuneForge.Server.Tests.Bots.ThreeCardPoker;

public sealed class ThreeCardPokerManagedPlayerTests
{
    private static readonly DateTime Now = new(2026, 10, 13, 12, 0, 0, DateTimeKind.Utc);

    [Theory]
    [InlineData("Q|clubs", "6|hearts", "4|spades", true)]
    [InlineData("Q|clubs", "6|hearts", "3|spades", false)]
    [InlineData("Q|clubs", "7|hearts", "2|spades", true)]
    [InlineData("Q|clubs", "5|hearts", "4|spades", false)]
    [InlineData("K|clubs", "3|hearts", "2|spades", true)]
    [InlineData("J|clubs", "10|hearts", "8|spades", false)]
    [InlineData("2|clubs", "2|hearts", "4|spades", true)]
    [InlineData("J|clubs", "7|clubs", "2|clubs", true)]
    [InlineData("A|clubs", "2|hearts", "3|spades", true)]
    [InlineData("8|clubs", "7|hearts", "6|spades", true)]
    public void Strategy_plays_queen_six_four_or_better_and_made_hands(
        string first, string second, string third, bool play) =>
        Assert.Equal(play, ThreeCardPokerManagedActionPolicy.ShouldPlay([first, second, third]));

    [Fact]
    public void Profiles_created_for_other_games_and_existing_profiles_support_three_card_poker()
    {
        var profile = ManagedPlayerIdentityFactory.Create(ManagedPlayerGames.Blackjack, Now);
        Assert.Contains(ManagedPlayerGames.ThreeCardPoker, profile.SupportedGames);
        var existingGames = ManagedPlayerGames.SupportedWithCurrentAdapters([ManagedPlayerGames.TexasHoldem]);
        Assert.Contains(ManagedPlayerGames.TexasHoldem, existingGames);
        Assert.Contains(ManagedPlayerGames.ThreeCardPoker, existingGames);
    }

    [Fact]
    public void Supply_uses_only_unexcluded_reservations_for_this_table()
    {
        var first = ManagedPlayerIdentityFactory.Create(ManagedPlayerGames.ThreeCardPoker, Now);
        var second = ManagedPlayerIdentityFactory.Create(ManagedPlayerGames.ThreeCardPoker, Now);
        var supply = new ThreeCardPokerManagedPlayerSupply([
            new(first, "other-table"), new(second, "table")]);
        Assert.Equal(second, supply.Take("table", [], Now));
        Assert.Throws<ThreeCardPokerManagedPlayerSupplyRequiredException>(() =>
            supply.Take("table", [], Now));
        Assert.Throws<ThreeCardPokerManagedPlayerSupplyRequiredException>(() =>
            new ThreeCardPokerManagedPlayerSupply([new(second, "table")])
                .Take("table", [second.UserId], Now));
    }

    [Fact]
    public void Decision_is_legal_deterministic_and_independent_of_hidden_dealer_cards()
    {
        var player = Player();
        var table = new ThreeCardPokerTableState
        {
            TableId = "table", Players = [player], CreatedAtUtc = Now, UpdatedAtUtc = Now,
            RoundNumber = 1, RoundSeed = 42, Version = 8,
            DealerCards = ["A|hearts", "K|hearts", "Q|hearts"]
        };
        var decision = ThreeCardPokerManagedActionPolicy.Choose(table, player, ["play", "fold"]);
        table.DealerCards = ["J|hearts", "7|spades", "2|clubs"];
        Assert.Equal(decision, ThreeCardPokerManagedActionPolicy.Choose(table, player, ["play", "fold"]));
        Assert.Contains(decision, new[] { "play", "fold" });
        Assert.Equal("fold", ThreeCardPokerManagedActionPolicy.Choose(table, player, ["fold"]));
    }

    private static ThreeCardPokerTablePlayer Player() => new()
    {
        ActorId = "managed-player", PublicSeatId = "seat", DisplayName = "Player", Seat = 0,
        SessionId = "session", SessionStartedAtUtc = Now,
        Cards = ["Q|clubs", "6|hearts", "4|spades"],
        HostMetadata = ThreeCardPokerManagedSeat.Create(ManagedPlayerSkillLevels.Strong, 500, 600)
    };
}
