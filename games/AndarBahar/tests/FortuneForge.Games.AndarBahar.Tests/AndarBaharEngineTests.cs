using System.Numerics;
using FortuneForge.Games.Cards;

namespace FortuneForge.Games.AndarBahar.Tests;

public sealed class AndarBaharEngineTests
{
    private static readonly PlayingCard DefaultJoker = new(CardRank.Seven, CardSuit.Hearts);

    [Fact]
    public void EveryPossiblePositionOfTheThreeMatchesProducesTheExactSideOddsAndReturns()
    {
        long andars = 0, bahars = 0;
        long[] counts = new long[50];
        // Conditional on any fixed Joker, the three remaining rank matches occupy a uniformly
        // random 3-subset of the 51 post-Joker positions. These are all 20,825 such subsets.
        for (var first = 1; first <= 49; first++)
        for (var second = first + 1; second <= 50; second++)
        for (var third = second + 1; third <= 51; third++)
        {
            var round = AndarBaharRules.Deal(DeckWithMatches(DefaultJoker, first, second, third));
            Assert.Equal(first, round.CardsDealt);
            Assert.Equal(first % 2 == 1 ? AndarBaharSide.Andar : AndarBaharSide.Bahar, round.Winner);
            Assert.Equal(DefaultJoker.Rank, round.WinningCard.Rank);
            Assert.All(round.Deals.SkipLast(1), deal => Assert.NotEqual(DefaultJoker.Rank, deal.Card.Rank));
            counts[first]++;
            if (round.Winner == AndarBaharSide.Andar) andars++; else bahars++;
        }

        Assert.Equal(20_825, andars + bahars); // choose(51,3)
        Assert.Equal(10_725, andars);
        Assert.Equal(10_100, bahars);
        Assert.Equal(429, andars / 25);
        Assert.Equal(404, bahars / 25);
        Assert.Equal(833, (andars + bahars) / 25);
        Assert.Equal(AndarBaharRules.AndarProbabilityNumerator, andars / 25);
        Assert.Equal(AndarBaharRules.BaharProbabilityNumerator, bahars / 25);
        Assert.Equal(AndarBaharRules.ProbabilityDenominator, (andars + bahars) / 25);

        // A first match at k leaves both other matches in 51-k positions: choose(51-k,2).
        for (var k = 1; k <= 49; k++) Assert.Equal((51L - k) * (50L - k) / 2, counts[k]);

        // Exact aggregate returns on a 10-cent wager on each possible layout, no floating point.
        Assert.Equal(203_775, andars * 19);
        Assert.Equal(202_000, bahars * 20);
        Assert.Equal(4_475, (andars + bahars) * 10 - andars * 19);
        Assert.Equal(6_250, (andars + bahars) * 10 - bahars * 20);
        Assert.Equal(179m / 8_330m, 4_475m / 208_250m); // Andar house edge ~2.14885954%
        Assert.Equal(25m / 833m, 6_250m / 208_250m); // Bahar house edge ~3.00120048%
    }

    [Fact]
    public void AllFiftyTwoPossibleJokersAndAllFortyNineFirstMatchPositionsObeyTheSameRules()
    {
        foreach (var joker in StandardDeck.Create())
        for (var first = 1; first <= 49; first++)
        {
            var deck = DeckWithMatches(joker, first, 50, 51);
            var round = AndarBaharRules.Deal(deck);
            Assert.Equal(joker, round.Joker);
            Assert.Equal(first, round.CardsDealt);
            Assert.Equal(first % 2 == 1 ? AndarBaharSide.Andar : AndarBaharSide.Bahar, round.Winner);
            Assert.Equal(deck.Skip(1).Take(first), round.Deals.Select(deal => deal.Card));
            Assert.Equal(Enumerable.Range(1, first), round.Deals.Select(deal => deal.Number));
            foreach (var deal in round.Deals)
                Assert.Equal(deal.Number % 2 == 1 ? AndarBaharSide.Andar : AndarBaharSide.Bahar, deal.Side);
        }
    }

    [Fact]
    public void ImmediateMatchEndsOnAndarAndIncludesNeitherOtherRankMatchNorTheHiddenTail()
    {
        var round = AndarBaharRules.Deal(DeckWithMatches(DefaultJoker, 1, 2, 3));
        Assert.Single(round.Deals);
        Assert.Equal(AndarBaharSide.Andar, round.Winner);
        Assert.NotEqual(round.Joker.Suit, round.WinningCard.Suit);
    }

    [Fact]
    public void LastPossibleMatchIsDealFortyNineOnAndarAndThereIsNoTie()
    {
        var round = AndarBaharRules.Deal(DeckWithMatches(DefaultJoker, 49, 50, 51));
        Assert.Equal(49, round.CardsDealt);
        Assert.Equal(25, round.Deals.Count(deal => deal.Side == AndarBaharSide.Andar));
        Assert.Equal(24, round.Deals.Count(deal => deal.Side == AndarBaharSide.Bahar));
        Assert.Equal(AndarBaharSide.Andar, round.Winner);
        Assert.Equal(new[] { "Andar", "Bahar" }, Enum.GetNames<AndarBaharSide>());
    }

    [Fact]
    public void InputAndPublicDealsCannotMutateAnEvaluatedRound()
    {
        var deck = DeckWithMatches(DefaultJoker, 4, 8, 12);
        var round = AndarBaharRules.Deal(deck);
        var originalJoker = round.Joker;
        var originalDeals = round.Deals.ToArray();
        Array.Reverse(deck);
        Assert.Equal(originalJoker, round.Joker);
        Assert.Equal(originalDeals, round.Deals);
        var list = Assert.IsAssignableFrom<IList<AndarBaharDeal>>(round.Deals);
        Assert.Throws<NotSupportedException>(() => list[0] = originalDeals[^1]);
        Assert.Empty(typeof(AndarBaharRound).GetConstructors());
    }

    [Fact]
    public void DealOrderIsTopCardFirstAndChangesTheWinnerWhenTheFirstMatchMoves()
    {
        var deck = DeckWithMatches(DefaultJoker, 1, 50, 51);
        var andarRound = AndarBaharRules.Deal(deck);
        (deck[1], deck[2]) = (deck[2], deck[1]);
        var baharRound = AndarBaharRules.Deal(deck);
        Assert.Equal(AndarBaharSide.Andar, andarRound.Winner);
        Assert.Equal(AndarBaharSide.Bahar, baharRound.Winner);
        Assert.Equal(andarRound.WinningCard, baharRound.WinningCard);
    }

    [Fact]
    public void RejectsNullWrongSizedDuplicateAndInvalidCardsIncludingHiddenTail()
    {
        Assert.Throws<ArgumentNullException>(() => AndarBaharRules.Deal(null!));
        foreach (var size in new[] { 0, 1, 2, 49, 51, 53, 104 })
            Assert.Throws<ArgumentException>(() => AndarBaharRules.Deal(Enumerable.Repeat(DefaultJoker, size).ToArray()));

        foreach (var index in new[] { 0, 1, 51 })
        {
            var deck = DeckWithMatches(DefaultJoker, 1, 2, 3);
            deck[index] = deck[index == 0 ? 51 : 0];
            Assert.Throws<ArgumentException>(() => AndarBaharRules.Deal(deck));
        }
        foreach (var invalid in new[]
        {
            new PlayingCard((CardRank)0, CardSuit.Clubs),
            new PlayingCard((CardRank)14, CardSuit.Clubs),
            new PlayingCard(CardRank.Ace, (CardSuit)(-1)),
            new PlayingCard(CardRank.Ace, (CardSuit)4),
        })
        {
            var deck = DeckWithMatches(DefaultJoker, 1, 2, 3);
            deck[^1] = invalid;
            Assert.Throws<ArgumentException>(() => AndarBaharRules.Deal(deck));
        }
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(50)]
    [InlineData(int.MaxValue)]
    public void RejectsOutOfRangeDealNumbers(int number) =>
        Assert.Throws<ArgumentOutOfRangeException>(() => AndarBaharRules.SideForDeal(number));

    [Theory]
    [InlineData(1, AndarBaharSide.Andar)]
    [InlineData(2, AndarBaharSide.Bahar)]
    [InlineData(48, AndarBaharSide.Bahar)]
    [InlineData(49, AndarBaharSide.Andar)]
    public void FixedStartingSideDoesNotDependOnTheJokerColor(int number, AndarBaharSide expected) =>
        Assert.Equal(expected, AndarBaharRules.SideForDeal(number));

    [Theory]
    [InlineData(1, AndarBaharSide.Andar, 10, 19, 9)]
    [InlineData(1, AndarBaharSide.Andar, 100, 190, 90)]
    [InlineData(1, AndarBaharSide.Andar, 250, 475, 225)]
    [InlineData(1, AndarBaharSide.Andar, 10000, 19000, 9000)]
    [InlineData(2, AndarBaharSide.Bahar, 10, 20, 10)]
    [InlineData(2, AndarBaharSide.Bahar, 250, 500, 250)]
    [InlineData(2, AndarBaharSide.Bahar, 10000, 20000, 10000)]
    public void WinningSettlementIncludesReturnedStakeAndExactProfit(
        int first, AndarBaharSide side, long stake, long expectedReturn, long expectedProfit)
    {
        var round = AndarBaharRules.Deal(DeckWithMatches(DefaultJoker, first, 50, 51));
        var result = AndarBaharRules.Settle(side, stake, round);
        Assert.Equal(side, result.Side);
        Assert.Equal(stake, result.StakeCents);
        Assert.Equal(AndarBaharDisposition.Win, result.Disposition);
        Assert.Equal(expectedReturn, result.TotalReturnCents);
        Assert.Equal(expectedProfit, result.ProfitCents);
    }

    [Fact]
    public void LossesReturnZeroAndNoWagerIsReportedAsNotPlaced()
    {
        foreach (var first in new[] { 1, 2 })
        {
            var round = AndarBaharRules.Deal(DeckWithMatches(DefaultJoker, first, 50, 51));
            foreach (var side in Enum.GetValues<AndarBaharSide>())
            {
                var zero = AndarBaharRules.Settle(side, 0, round);
                Assert.Equal(AndarBaharDisposition.NotPlaced, zero.Disposition);
                Assert.Equal(0, zero.TotalReturnCents);
                Assert.Equal(0, zero.ProfitCents);
                if (side == round.Winner) continue;
                var loss = AndarBaharRules.Settle(side, 100, round);
                Assert.Equal(AndarBaharDisposition.Loss, loss.Disposition);
                Assert.Equal(0, loss.TotalReturnCents);
                Assert.Equal(-100, loss.ProfitCents);
            }
        }
    }

    [Theory]
    [InlineData(long.MinValue)]
    [InlineData(-10)]
    [InlineData(-1)]
    [InlineData(1)]
    [InlineData(5)]
    [InlineData(9)]
    [InlineData(11)]
    [InlineData(99)]
    [InlineData(long.MaxValue)]
    public void StakesMustBeNonnegativeExactTenCentIncrements(long invalid)
    {
        var round = AndarBaharRules.Deal(DeckWithMatches(DefaultJoker, 1, 50, 51));
        foreach (var side in Enum.GetValues<AndarBaharSide>())
            Assert.Throws<ArgumentOutOfRangeException>(() => AndarBaharRules.Settle(side, invalid, round));
        Assert.Throws<ArgumentOutOfRangeException>(() => AndarBaharRules.ValidateWagers(new(invalid, 0)));
        Assert.Throws<ArgumentOutOfRangeException>(() => AndarBaharRules.ValidateWagers(new(0, invalid)));
    }

    [Fact]
    public void WagerSettlementIsIndependentlyCorrectForBothSidesAndChecksTotalOverflow()
    {
        var wagers = new AndarBaharWagers(100, 250);
        Assert.Equal(350, wagers.TotalCents);
        Assert.Equal(100, wagers.For(AndarBaharSide.Andar));
        Assert.Equal(250, wagers.For(AndarBaharSide.Bahar));
        foreach (var first in new[] { 1, 2 })
        {
            var round = AndarBaharRules.Deal(DeckWithMatches(DefaultJoker, first, 50, 51));
            var settlements = AndarBaharRules.Settle(wagers, round);
            Assert.Equal(2, settlements.Count);
            Assert.Single(settlements, settlement => settlement.Disposition == AndarBaharDisposition.Win);
            Assert.Single(settlements, settlement => settlement.Disposition == AndarBaharDisposition.Loss);
            Assert.Equal(first == 1 ? 190 : 500, settlements.Sum(settlement => settlement.TotalReturnCents));
            Assert.Equal(first == 1 ? -160 : 150, settlements.Sum(settlement => settlement.ProfitCents));
        }
        var overflow = new AndarBaharWagers(long.MaxValue - 7, 10);
        Assert.Throws<OverflowException>(() => overflow.TotalCents);
        Assert.Throws<OverflowException>(() => AndarBaharRules.ValidateWagers(overflow));
    }

    [Fact]
    public void WinningPayoutChecksOverflowButAcceptsEveryRepresentableExactReturn()
    {
        foreach (var side in Enum.GetValues<AndarBaharSide>())
        {
            var round = AndarBaharRules.Deal(DeckWithMatches(DefaultJoker, side == AndarBaharSide.Andar ? 1 : 2, 50, 51));
            var returnPerTen = side == AndarBaharSide.Andar ? 19 : 20;
            var largestStake = long.MaxValue / returnPerTen * 10;
            var result = AndarBaharRules.Settle(side, largestStake, round);
            Assert.Equal((long)((BigInteger)largestStake * returnPerTen / 10), result.TotalReturnCents);
            Assert.Throws<OverflowException>(() => AndarBaharRules.Settle(side, largestStake + 10, round));
        }
    }

    [Fact]
    public void InvalidSidesAndNullRoundsAreRejectedEvenForZeroStake()
    {
        var round = AndarBaharRules.Deal(DeckWithMatches(DefaultJoker, 1, 50, 51));
        foreach (var invalid in new[] { (AndarBaharSide)(-1), (AndarBaharSide)2 })
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => AndarBaharRules.Settle(invalid, 0, round));
            Assert.Throws<ArgumentOutOfRangeException>(() => new AndarBaharWagers(0, 0).For(invalid));
        }
        Assert.Throws<ArgumentNullException>(() => AndarBaharRules.Settle(AndarBaharSide.Andar, 0, null!));
        Assert.Throws<ArgumentNullException>(() => AndarBaharRules.Settle(new AndarBaharWagers(0, 0), null!));
    }

    [Fact]
    public void PackageDescriptorUsesEstablishedRoutesAndCapabilities()
    {
        AndarBaharModule.Descriptor.Validate();
        Assert.Equal("andar-bahar", AndarBaharModule.Descriptor.Id);
        Assert.Equal("/games/andar-bahar", AndarBaharModule.Descriptor.ClientRoute);
        Assert.Equal("/api/games/andar-bahar", AndarBaharModule.Descriptor.ApiBasePath);
    }

    private static PlayingCard[] DeckWithMatches(PlayingCard joker, int first, int second, int third)
    {
        var standard = StandardDeck.Create();
        using var matches = standard.Where(card => card.Rank == joker.Rank && card != joker).GetEnumerator();
        using var others = standard.Where(card => card.Rank != joker.Rank).GetEnumerator();
        var result = new PlayingCard[52];
        result[0] = joker;
        for (var position = 1; position <= 51; position++)
        {
            var source = position == first || position == second || position == third ? matches : others;
            Assert.True(source.MoveNext());
            result[position] = source.Current;
        }
        return result;
    }
}
