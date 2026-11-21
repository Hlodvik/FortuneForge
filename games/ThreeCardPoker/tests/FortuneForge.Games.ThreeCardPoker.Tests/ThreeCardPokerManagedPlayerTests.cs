using FortuneForge.Games.ThreeCardPoker;
using FortuneForge.Games.Abstractions.ManagedPlayers;
using FortuneForge.Server.Bots.ThreeCardPoker;
using Xunit;

namespace FortuneForge.Games.ThreeCardPoker.Tests;

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
