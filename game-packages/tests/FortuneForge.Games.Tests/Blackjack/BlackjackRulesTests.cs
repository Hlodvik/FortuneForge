using FortuneForge.Games.Blackjack;

namespace FortuneForge.Games.Tests;

public sealed class BlackjackRulesTests
{
    [Fact]
    public void ScoreUsesAcesAsOneOnlyWhenNeeded()
    {
        var soft = BlackjackRules.Score(["A|spades", "6|hearts"]);
        var hard = BlackjackRules.Score(["A|spades", "6|hearts", "10|clubs"]);

        Assert.Equal(17, soft.Score);
        Assert.True(soft.Soft);
        Assert.Equal(17, hard.Score);
        Assert.False(hard.Soft);
    }

    [Fact]
    public void PlayerNaturalReturnsStakePlusThreeToTwo()
    {
        var game = BlackjackRules.Deal(
            "game",
            "player",
            100,
            Deck("A|spades", "9|clubs", "K|hearts", "7|diamonds"),
            DateTime.UnixEpoch);

        Assert.Equal(BlackjackOutcomes.PlayerBlackjack, game.Outcome);
        Assert.Equal(250, game.PayoutCents);
    }

    [Fact]
    public void DealerStandsOnSoftSeventeen()
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
    }

    [Fact]
    public void DoubleAfterHitIsRejected()
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

    private static IReadOnlyList<string> Deck(params string[] leadingCards)
    {
        var cards = new List<string>(leadingCards);
        foreach (var suit in new[] { "clubs", "diamonds", "hearts", "spades" })
            foreach (var rank in new[] { "A", "2", "3", "4", "5", "6", "7", "8", "9", "10", "J", "Q", "K" })
            {
                var card = $"{rank}|{suit}";
                if (!cards.Contains(card, StringComparer.Ordinal)) cards.Add(card);
            }
        return cards;
    }
}
