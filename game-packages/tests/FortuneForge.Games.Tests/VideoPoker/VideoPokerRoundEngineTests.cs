using FortuneForge.Games.Cards;
using FortuneForge.Games.VideoPoker;

namespace FortuneForge.Games.Tests.VideoPoker;

public sealed class VideoPokerRoundEngineTests
{
    [Fact]
    public void DealUsesTheSuppliedOrderedDeckDeterministically()
    {
        var deck = StandardDeck.Create();

        var round = VideoPokerRoundEngine.Deal(deck, coinsWagered: 1);

        Assert.Equal(deck.Take(5), round.InitialDeal.Cards);
        Assert.Equal(deck.Skip(5), round.RemainingDeck);
        Assert.Equal(VideoPokerRoundStatus.AwaitingDraw, round.Status);
    }

    [Fact]
    public void DrawPreservesHeldCardsAndReplacesTheOthersInDeckOrder()
    {
        var deck = StandardDeck.Create();
        var round = VideoPokerRoundEngine.Deal(deck, coinsWagered: 1);

        var completed = VideoPokerRoundEngine.Draw(
            round,
            new VideoPokerHeldCardPositions([VideoPokerCardPosition.First, VideoPokerCardPosition.Third]));

        Assert.Equal(round.InitialDeal.Cards[0], completed.Result!.FinalHand.Cards[0]);
        Assert.Equal(round.InitialDeal.Cards[2], completed.Result.FinalHand.Cards[2]);
        Assert.Equal(deck[5], completed.Result.FinalHand.Cards[1]);
        Assert.Equal(deck[6], completed.Result.FinalHand.Cards[3]);
        Assert.Equal(deck[7], completed.Result.FinalHand.Cards[4]);
    }

    [Fact]
    public void DrawWithNoHeldCardsReplacesEveryCard()
    {
        var deck = StandardDeck.Create();
        var round = VideoPokerRoundEngine.Deal(deck, coinsWagered: 1);

        var completed = VideoPokerRoundEngine.Draw(round, new VideoPokerHeldCardPositions([]));

        Assert.Equal(deck.Skip(5).Take(5), completed.Result!.FinalHand.Cards);
        Assert.Equal(42, completed.RemainingDeck.Length);
    }

    [Fact]
    public void DrawWithAllHeldCardsKeepsTheInitialDealAndDeckPosition()
    {
        var deck = StandardDeck.Create();
        var round = VideoPokerRoundEngine.Deal(deck, coinsWagered: 1);

        var completed = VideoPokerRoundEngine.Draw(
            round,
            new VideoPokerHeldCardPositions(Enum.GetValues<VideoPokerCardPosition>()));

        Assert.Equal(round.InitialDeal.Cards.ToArray(), completed.Result!.FinalHand.Cards.ToArray());
        Assert.Equal(round.RemainingDeck.ToArray(), completed.RemainingDeck.ToArray());
    }

    [Fact]
    public void DrawProducesFiveUniqueFinalCards()
    {
        var round = VideoPokerRoundEngine.Deal(StandardDeck.Create(), coinsWagered: 1);

        var completed = VideoPokerRoundEngine.Draw(
            round,
            new VideoPokerHeldCardPositions([VideoPokerCardPosition.Second, VideoPokerCardPosition.Fourth]));

        Assert.Equal(VideoPokerDeal.CardCount, completed.Result!.FinalHand.Cards.Distinct().Count());
    }

    [Fact]
    public void DrawEvaluatesAndSettlesTheFinalHand()
    {
        PlayingCard[] royalFlush =
        [
            new PlayingCard(CardRank.Ten, CardSuit.Spades),
            new PlayingCard(CardRank.Jack, CardSuit.Spades),
            new PlayingCard(CardRank.Queen, CardSuit.Spades),
            new PlayingCard(CardRank.King, CardSuit.Spades),
            new PlayingCard(CardRank.Ace, CardSuit.Spades),
        ];
        PlayingCard[] deck = [.. royalFlush, .. StandardDeck.Create().Except(royalFlush)];
        var round = VideoPokerRoundEngine.Deal(deck, coinsWagered: 5);

        var completed = VideoPokerRoundEngine.Draw(
            round,
            new VideoPokerHeldCardPositions(Enum.GetValues<VideoPokerCardPosition>()));

        Assert.Equal(VideoPokerHandRank.RoyalFlush, completed.Result!.HandRank);
        Assert.Equal(4_000, completed.Result.PaytableOutcome.CreditsWon);
    }

    [Fact]
    public void DealRejectsAnInvalidWager()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            VideoPokerRoundEngine.Deal(StandardDeck.Create(), coinsWagered: 0));
    }

    [Fact]
    public void DrawRejectsDrawingTheSameRoundTwice()
    {
        var round = VideoPokerRoundEngine.Deal(StandardDeck.Create(), coinsWagered: 1);
        var completed = VideoPokerRoundEngine.Draw(round, new VideoPokerHeldCardPositions([]));

        Assert.Throws<InvalidOperationException>(() =>
            VideoPokerRoundEngine.Draw(completed, new VideoPokerHeldCardPositions([])));
    }
}
