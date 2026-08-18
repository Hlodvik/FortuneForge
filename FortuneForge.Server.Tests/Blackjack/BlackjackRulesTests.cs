using FortuneForge.Server.Cards.Blackjack;
using Xunit;

namespace FortuneForge.Server.Tests.Blackjack;

public sealed class BlackjackRulesTests
{
    [Fact]
    public void Score_UsesAcesAsOneOnlyWhenNeeded()
    {
        var soft = BlackjackRules.Score(["A|spades", "6|hearts"]);
        var hard = BlackjackRules.Score(["A|spades", "6|hearts", "10|clubs"]);

        Assert.Equal(17, soft.Score);
        Assert.True(soft.Soft);
        Assert.Equal(17, hard.Score);
        Assert.False(hard.Soft);
        Assert.False(hard.Bust);
    }

    [Fact]
    public void Deal_PlayerNatural_PaysReturnedStakePlusThreeToTwo()
    {
        var game = BlackjackRules.Deal(
            "game",
            "player",
            100,
            Deck("A|spades", "9|clubs", "K|hearts", "7|diamonds"),
            DateTime.UnixEpoch);

        Assert.Equal(BlackjackStatuses.Completed, game.Status);
        Assert.Equal(BlackjackOutcomes.PlayerBlackjack, game.Outcome);
        Assert.Equal(250, game.PayoutCents);
    }

    [Fact]
    public void Deal_BothNatural_IsPush()
    {
        var game = BlackjackRules.Deal(
            "game",
            "player",
            150,
            Deck("A|spades", "A|clubs", "K|hearts", "Q|diamonds"),
            DateTime.UnixEpoch);

        Assert.Equal(BlackjackOutcomes.Push, game.Outcome);
        Assert.Equal(150, game.PayoutCents);
    }

    [Fact]
    public void Stand_DealerStandsOnSoftSeventeen()
    {
        var game = BlackjackRules.Deal(
            "game",
            "player",
            100,
            Deck("10|spades", "A|clubs", "8|hearts", "6|diamonds", "K|clubs"),
            DateTime.UnixEpoch);

        var result = BlackjackRules.ApplyAction(game, BlackjackActions.Stand, DateTime.UnixEpoch.AddSeconds(1));

        Assert.Equal(4, result.NextCardIndex);
        Assert.Equal(BlackjackOutcomes.PlayerWin, result.Outcome);
        Assert.Equal(200, result.PayoutCents);
    }

    [Fact]
    public void Hit_WhenPlayerBusts_CompletesWithoutDrawingDealer()
    {
        var game = BlackjackRules.Deal(
            "game",
            "player",
            100,
            Deck("10|spades", "9|clubs", "9|hearts", "7|diamonds", "K|clubs"),
            DateTime.UnixEpoch);

        var result = BlackjackRules.ApplyAction(game, BlackjackActions.Hit, DateTime.UnixEpoch.AddSeconds(1));

        Assert.Equal(BlackjackOutcomes.PlayerBust, result.Outcome);
        Assert.Equal(0, result.PayoutCents);
        Assert.Equal(5, result.NextCardIndex);
        Assert.Equal(2, result.DealerCards.Count);
    }

    [Fact]
    public void Double_DrawsOnePlayerCardAndSettlesAtTwiceTheWager()
    {
        var game = BlackjackRules.Deal(
            "game",
            "player",
            50,
            Deck(
                "5|spades", "9|clubs", "6|hearts", "7|diamonds",
                "10|clubs", "6|clubs"),
            DateTime.UnixEpoch);

        var result = BlackjackRules.ApplyAction(game, BlackjackActions.Double, DateTime.UnixEpoch.AddSeconds(1));

        Assert.Equal(100, result.TotalWagerCents);
        Assert.Equal(BlackjackOutcomes.PlayerWin, result.Outcome);
        Assert.Equal(200, result.PayoutCents);
        Assert.Equal(3, result.PlayerCards.Count);
    }

    [Fact]
    public void Double_AfterHit_IsRejected()
    {
        var game = BlackjackRules.Deal(
            "game",
            "player",
            50,
            Deck("2|spades", "10|clubs", "3|hearts", "7|diamonds", "4|clubs"),
            DateTime.UnixEpoch);
        var hit = BlackjackRules.ApplyAction(game, BlackjackActions.Hit, DateTime.UnixEpoch.AddSeconds(1));

        Assert.Throws<BlackjackConflictException>(() =>
            BlackjackRules.ApplyAction(hit, BlackjackActions.Double, DateTime.UnixEpoch.AddSeconds(2)));
    }

    internal static IReadOnlyList<string> Deck(params string[] leadingCards)
    {
        var cards = new List<string>(leadingCards);
        foreach (var suit in new[] { "clubs", "diamonds", "hearts", "spades" })
        {
            foreach (var rank in new[] { "A", "2", "3", "4", "5", "6", "7", "8", "9", "10", "J", "Q", "K" })
            {
                var card = $"{rank}|{suit}";
                if (!cards.Contains(card, StringComparer.Ordinal))
                {
                    cards.Add(card);
                }
            }
        }
        return cards;
    }
}
