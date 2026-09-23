using FortuneForge.Games.Abstractions;
using FortuneForge.Games.Cards;
using FortuneForge.Games.Hearts;
using FortuneForge.Games.TrickTaking;

namespace FortuneForge.Games.Tests;

public sealed class HeartsEngineTests
{
    [Fact]
    public void TwoOfClubsOwnerOpensTheRound()
    {
        var state = HeartsEngine.Start(99);

        Assert.Contains(new PlayingCard(CardRank.Two, CardSuit.Clubs), state.HandFor(state.Turn));
        Assert.Equal(state.Turn, state.CurrentTrick.Leader);
    }

    [Fact]
    public void FirstPlayMustBeTwoOfClubs()
    {
        var state = State(
            PlayerSeat.North,
            [Card(CardRank.Two, CardSuit.Clubs), Card(CardRank.Three, CardSuit.Clubs)],
            [],
            [],
            []);

        var exception = Assert.Throws<HeartsRuleException>(() => HeartsEngine.Apply(
            state,
            new PlayHeartsCard(PlayerSeat.North, Card(CardRank.Three, CardSuit.Clubs))));

        Assert.Contains("two of clubs", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void SafeDiscardIsRequiredInsteadOfAFirstTrickPointCard()
    {
        var plays = new[] { new TrickPlay(PlayerSeat.North, Card(CardRank.Two, CardSuit.Clubs)) };
        var state = State(
            PlayerSeat.East,
            [],
            [Card(CardRank.Queen, CardSuit.Spades), Card(CardRank.Three, CardSuit.Diamonds)],
            [],
            [],
            plays);

        var exception = Assert.Throws<HeartsRuleException>(() => HeartsEngine.Apply(
            state,
            new PlayHeartsCard(PlayerSeat.East, Card(CardRank.Queen, CardSuit.Spades))));

        Assert.Contains("point card", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void TrickWinnerReceivesHeartsAndQueenPoints()
    {
        TrickPlay[] plays =
        [
            new(PlayerSeat.North, Card(CardRank.Ten, CardSuit.Clubs)),
            new(PlayerSeat.East, Card(CardRank.Queen, CardSuit.Spades)),
            new(PlayerSeat.South, Card(CardRank.Four, CardSuit.Hearts)),
        ];
        var state = State(
            PlayerSeat.West,
            [],
            [],
            [],
            [Card(CardRank.Ace, CardSuit.Clubs)],
            plays,
            completedTrickCount: 1);

        var result = HeartsEngine.Apply(
            state,
            new PlayHeartsCard(PlayerSeat.West, Card(CardRank.Ace, CardSuit.Clubs)));

        Assert.Equal(HeartsEventType.TrickCompleted, result.EventType);
        Assert.Equal(PlayerSeat.West, result.State.Turn);
        Assert.Equal(14, result.State.Scores.Single(score => score.Seat == PlayerSeat.West).Points);
    }

    [Fact]
    public void FinalPointCompletesAShootingTheMoonRound()
    {
        var scores = Enum.GetValues<PlayerSeat>()
            .Select(seat => new HeartsScore(seat, seat == PlayerSeat.North ? 25 : 0))
            .ToArray();
        var state = new HeartsState(
            1,
            PlayerSeat.West,
            HeartsPhase.Playing,
            Hands([], [], [], [Card(CardRank.Two, CardSuit.Hearts)]),
            new TrickState(PlayerSeat.North,
            [
                new(PlayerSeat.North, Card(CardRank.Ace, CardSuit.Clubs)),
                new(PlayerSeat.East, Card(CardRank.King, CardSuit.Clubs)),
                new(PlayerSeat.South, Card(CardRank.Queen, CardSuit.Clubs)),
            ]),
            Enumerable.Range(1, 12)
                .Select(number => new CompletedTrick(number, PlayerSeat.North, PlayerSeat.North, []))
                .ToArray(),
            scores,
            true);

        var result = HeartsEngine.Apply(
            state,
            new PlayHeartsCard(PlayerSeat.West, Card(CardRank.Two, CardSuit.Hearts)));

        Assert.Equal(HeartsPhase.Complete, result.State.Phase);
        Assert.Equal(0, result.State.Scores.Single(score => score.Seat == PlayerSeat.North).Points);
        Assert.All(
            result.State.Scores.Where(score => score.Seat != PlayerSeat.North),
            score => Assert.Equal(26, score.Points));
    }

    [Fact]
    public void DescriptorIdentifiesThePreviewShellContract()
    {
        Assert.Equal("Hearts", HeartsModule.Descriptor.DisplayName);
        Assert.Equal("0.1.1", HeartsModule.Descriptor.PackageVersion);
        Assert.Equal("/cards/hearts", HeartsModule.Descriptor.ClientRoute);
        Assert.Equal("/api/games/hearts", HeartsModule.Descriptor.ApiBasePath);
        Assert.False(HeartsModule.Descriptor.Capabilities.HasFlag(GameCapability.Credits));
    }

    private static HeartsState State(
        PlayerSeat turn,
        IReadOnlyList<PlayingCard> north,
        IReadOnlyList<PlayingCard> east,
        IReadOnlyList<PlayingCard> south,
        IReadOnlyList<PlayingCard> west,
        IReadOnlyList<TrickPlay>? plays = null,
        int completedTrickCount = 0) => new(
        1,
        turn,
        HeartsPhase.Playing,
        Hands(north, east, south, west),
        new TrickState(PlayerSeat.North, plays ?? []),
        Enumerable.Range(1, completedTrickCount)
            .Select(number => new CompletedTrick(number, PlayerSeat.North, PlayerSeat.North, []))
            .ToArray(),
        Enum.GetValues<PlayerSeat>().Select(seat => new HeartsScore(seat, 0)).ToArray(),
        false);

    private static IReadOnlyList<PlayerHand> Hands(params IReadOnlyList<PlayingCard>[] hands) =>
        Enum.GetValues<PlayerSeat>().Select((seat, index) => new PlayerHand(seat, hands[index])).ToArray();

    private static PlayingCard Card(CardRank rank, CardSuit suit) => new(rank, suit);
}
