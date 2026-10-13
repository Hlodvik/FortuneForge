using FortuneForge.Games.ThreeCardPoker;

namespace FortuneForge.Games.Tests.ThreeCardPoker;

public sealed class ThreeCardPokerRulesTests
{
    [Fact]
    public void ModuleUsesTheCardRoomCreditQueueRoute()
    {
        Assert.Equal("three-card-poker", ThreeCardPokerModule.Descriptor.Id);
        Assert.Equal("/cards/three-card-poker", ThreeCardPokerModule.Descriptor.ClientRoute);
        Assert.Equal("/api/cards/three-card-poker/table", ThreeCardPokerModule.Descriptor.ApiBasePath);
    }

    [Theory]
    [InlineData("A|hearts,K|hearts,Q|hearts", ThreeCardPokerHandCategory.StraightFlush)]
    [InlineData("2|hearts,2|clubs,2|spades", ThreeCardPokerHandCategory.ThreeOfAKind)]
    [InlineData("A|hearts,K|clubs,Q|spades", ThreeCardPokerHandCategory.Straight)]
    [InlineData("A|hearts,8|hearts,2|hearts", ThreeCardPokerHandCategory.Flush)]
    [InlineData("4|hearts,4|clubs,2|spades", ThreeCardPokerHandCategory.Pair)]
    [InlineData("Q|hearts,6|clubs,4|spades", ThreeCardPokerHandCategory.HighCard)]
    public void DetectsEveryThreeCardCategory(string cards, ThreeCardPokerHandCategory expected) =>
        Assert.Equal(expected, ThreeCardPokerRules.Evaluate(cards.Split(',')).Category);

    [Fact]
    public void ThreeCardRankingPlacesTripsAboveStraightAndStraightAboveFlush()
    {
        var trips = Hand("2|hearts,2|clubs,2|spades");
        var straight = Hand("A|hearts,K|clubs,Q|spades");
        var flush = Hand("A|hearts,J|hearts,9|hearts");
        Assert.True(trips.CompareTo(straight) > 0);
        Assert.True(straight.CompareTo(flush) > 0);
    }

    [Fact]
    public void AceCanBeLowOnlyInAceTwoThreeAndSuitsNeverBreakTies()
    {
        Assert.True(Hand("4|hearts,3|clubs,2|spades").CompareTo(Hand("A|clubs,2|hearts,3|spades")) > 0);
        Assert.Equal(ThreeCardPokerHandCategory.HighCard, Hand("A|hearts,K|clubs,2|spades").Category);
        Assert.Equal(0, Hand("Q|hearts,6|clubs,4|spades").CompareTo(Hand("Q|spades,6|hearts,4|clubs")));
        Assert.True(Hand("A|hearts,K|clubs,Q|spades").CompareTo(Hand("K|spades,Q|hearts,J|clubs")) > 0);
    }

    [Theory]
    [InlineData("Q|hearts,6|clubs,4|spades", true)]
    [InlineData("Q|hearts,6|clubs,3|spades", false)]
    [InlineData("Q|hearts,7|clubs,2|spades", true)]
    [InlineData("K|hearts,3|clubs,2|spades", true)]
    [InlineData("J|hearts,9|clubs,2|spades", false)]
    [InlineData("2|hearts,2|clubs,4|spades", true)]
    public void QueenSixFourStrategyUsesAllKickers(string cards, bool expected) =>
        Assert.Equal(expected, ThreeCardPokerRules.ShouldPlay(cards.Split(',')));

    [Theory]
    [InlineData("Q|hearts,3|clubs,2|spades", true)]
    [InlineData("J|hearts,9|clubs,2|spades", false)]
    [InlineData("2|hearts,2|clubs,4|spades", true)]
    public void DealerRequiresQueenHighOrAnyMadeHand(string cards, bool expected) =>
        Assert.Equal(expected, ThreeCardPokerRules.DealerQualifies(Hand(cards)));

    [Fact]
    public void All22100HandsHaveTheExpectedCategoryDistribution()
    {
        var deck = ThreeCardPokerRules.CreateShuffledDeck();
        var counts = new Dictionary<ThreeCardPokerHandCategory, int>();
        for (var first = 0; first < deck.Count - 2; first++)
        for (var second = first + 1; second < deck.Count - 1; second++)
        for (var third = second + 1; third < deck.Count; third++)
        {
            var category = ThreeCardPokerRules.Evaluate([deck[first], deck[second], deck[third]]).Category;
            counts[category] = counts.GetValueOrDefault(category) + 1;
        }
        Assert.Equal(48, counts[ThreeCardPokerHandCategory.StraightFlush]);
        Assert.Equal(52, counts[ThreeCardPokerHandCategory.ThreeOfAKind]);
        Assert.Equal(720, counts[ThreeCardPokerHandCategory.Straight]);
        Assert.Equal(1096, counts[ThreeCardPokerHandCategory.Flush]);
        Assert.Equal(3744, counts[ThreeCardPokerHandCategory.Pair]);
        Assert.Equal(16440, counts[ThreeCardPokerHandCategory.HighCard]);
    }

    [Fact]
    public void MalformedOrRepeatedCardsAreRejected()
    {
        Assert.Throws<ArgumentException>(() => ThreeCardPokerRules.Evaluate(["A|spades", "A|spades", "2|clubs"]));
        Assert.Throws<ArgumentException>(() => ThreeCardPokerRules.Evaluate(["A|spades", "2|clubs"]));
        Assert.Throws<ArgumentException>(() => ThreeCardPokerRules.Evaluate(["X|spades", "2|clubs", "3|hearts"]));
    }

    private static ThreeCardPokerHandValue Hand(string cards) => ThreeCardPokerRules.Evaluate(cards.Split(','));
}
