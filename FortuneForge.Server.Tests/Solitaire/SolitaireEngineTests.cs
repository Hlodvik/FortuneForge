using FortuneForge.Server.Cards.Solitaire;
using System.Text.Json;
using Xunit;

namespace FortuneForge.Server.Tests.Solitaire;

public sealed class SolitaireEngineTests
{
    [Fact]
    public void CreateGame_MatchesFrozenClientMulberry32Deal()
    {
        var game = SolitaireEngine.CreateGame(1);

        Assert.Equal(1u, game.Seed);
        Assert.Equal(24, game.Stock.Count);
        Assert.Equal(
            ["diamonds-12", "spades-6", "diamonds-11", "clubs-11", "spades-5"],
            game.Stock.Take(5).Select(card => card.Id));
        Assert.Equal(["hearts-7"], game.Tableau[0].Select(card => card.Id));
        Assert.Equal(["clubs-1", "hearts-1"], game.Tableau[1].Select(card => card.Id));
        Assert.Equal(
            ["hearts-11", "clubs-2", "clubs-13", "diamonds-4", "diamonds-9", "clubs-8", "hearts-5"],
            game.Tableau[6].Select(card => card.Id));
        Assert.Equal(52, CardCount(game));
        Assert.All(game.Stock, card => Assert.False(card.FaceUp));
        Assert.All(game.Tableau, pile => Assert.True(pile[^1].FaceUp));
    }

    [Fact]
    public void Draw_IsDrawOneAndRecycleCostsOneHundredWithZeroFloor()
    {
        var game = State(
            stock: [Card("clubs", 2, false), Card("hearts", 3, false)],
            waste: [],
            score: 70) with { DrawCount = 1 };

        var first = SolitaireEngine.Apply(game, Command(SolitaireCommandTypes.Draw));
        var second = SolitaireEngine.Apply(first, Command(SolitaireCommandTypes.Draw));
        var recycled = SolitaireEngine.Apply(second, Command(SolitaireCommandTypes.Draw));

        Assert.Single(first.Waste);
        Assert.Equal("hearts-3", first.Waste[0].Id);
        Assert.True(first.Waste[0].FaceUp);
        Assert.Equal(0, recycled.Score);
        Assert.Equal(3, recycled.Moves);
        Assert.Equal(["clubs-2", "hearts-3"], recycled.Stock.Select(card => card.Id));
        Assert.All(recycled.Stock, card => Assert.False(card.FaceUp));
    }

    [Fact]
    public void EmptyFoundationsAreGenericAndOccupiedFoundationsRemainSameSuitAscending()
    {
        var game = State(waste: [Card("clubs", 1, true)]);
        var valid = Command(
            SolitaireCommandTypes.Move,
            from: new("waste", 0),
            startIndex: 0,
            to: new("foundation", 0));
        var anotherGenericFoundation = valid with { To = new SolitairePileReference("foundation", 1) };

        var moved = SolitaireEngine.Apply(game, valid);

        Assert.Equal(10, moved.Score);
        Assert.Equal("clubs-1", Assert.Single(moved.Foundations[0]).Id);
        Assert.Equal(
            "clubs-1",
            Assert.Single(SolitaireEngine.Apply(game, anotherGenericFoundation).Foundations[1]).Id);

        var occupied = State(
            waste: [Card("hearts", 2, true)],
            foundations: Piles([Card("clubs", 1, true)], count: 4));
        Assert.Throws<SolitaireIllegalMoveException>(() => SolitaireEngine.Apply(
            occupied,
            valid with { From = new("waste", 0), StartIndex = 0, To = new("foundation", 0) }));
    }

    [Fact]
    public void DrawThreeDrawsUpToThreeCards()
    {
        var game = State(
            stock: [Card("clubs", 2, false), Card("hearts", 3, false), Card("spades", 4, false)],
            waste: []) with { DrawCount = 3 };

        var drawn = SolitaireEngine.Apply(game, Command(SolitaireCommandTypes.Draw));

        Assert.Empty(drawn.Stock);
        Assert.Equal(["spades-4", "hearts-3", "clubs-2"], drawn.Waste.Select(card => card.Id));
    }

    [Fact]
    public void WasteToTableauAndAutomaticRevealUseFrozenScoreValues()
    {
        var wasteGame = State(
            waste: [Card("hearts", 12, true)],
            tableau: Piles(
                [Card("clubs", 13, true)]));
        var wasteMoved = SolitaireEngine.Apply(wasteGame, Command(
            SolitaireCommandTypes.Move,
            from: new("waste", 0),
            startIndex: 0,
            to: new("tableau", 0)));

        var revealGame = State(tableau: Piles(
            [Card("spades", 8, false), Card("clubs", 4, true)],
            [Card("hearts", 5, true)]));
        var revealMoved = SolitaireEngine.Apply(revealGame, Command(
            SolitaireCommandTypes.Move,
            from: new("tableau", 0),
            startIndex: 1,
            to: new("tableau", 1)));

        Assert.Equal(5, wasteMoved.Score);
        Assert.Equal(5, revealMoved.Score);
        Assert.True(revealMoved.Tableau[0][0].FaceUp);
    }

    [Fact]
    public void FoundationToTableauCostsFifteenAndScoreNeverGoesBelowZero()
    {
        var game = State(
            foundations: Piles([Card("hearts", 12, true)], count: 4),
            tableau: Piles([Card("clubs", 13, true)]),
            score: 10);

        var moved = SolitaireEngine.Apply(game, Command(
            SolitaireCommandTypes.Move,
            from: new("foundation", 0),
            startIndex: 0,
            to: new("tableau", 0)));

        Assert.Equal(0, moved.Score);
        Assert.Equal(1, moved.Moves);
    }

    [Fact]
    public void Flip_AllowsOnlyTopFaceDownTableauCard()
    {
        var game = State(tableau: Piles([Card("clubs", 7, false)]));

        var flipped = SolitaireEngine.Apply(game, Command(
            SolitaireCommandTypes.Flip,
            column: 0));

        Assert.True(flipped.Tableau[0][0].FaceUp);
        Assert.Equal(5, flipped.Score);
        Assert.Throws<SolitaireIllegalMoveException>(() => SolitaireEngine.Apply(
            flipped,
            Command(SolitaireCommandTypes.Flip, column: 0)));
    }

    [Fact]
    public void RankingUsesScoreThenElapsedThenMovesAndStableFallback()
    {
        var now = DateTime.UnixEpoch;
        var players = new[]
        {
            Player("c", score: 100, elapsed: 20_000, moves: 30, now),
            Player("b", score: 100, elapsed: 20_000, moves: 25, now),
            Player("a", score: 100, elapsed: 10_000, moves: 40, now),
            Player("d", score: 90, elapsed: 1_000, moves: 1, now)
        };

        var ranked = SolitaireCompetitionRules.Rank(players);

        Assert.Equal(["a", "b", "c", "d"], ranked.Select(player => player.UserId));
    }

    [Fact]
    public void GameState_RoundTripsThroughDurableJsonWithoutLosingCardsOrSeed()
    {
        var game = SolitaireEngine.CreateGame(uint.MaxValue);
        var json = JsonSerializer.Serialize(game, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        var restored = JsonSerializer.Deserialize<SolitaireGameState>(
            json,
            new JsonSerializerOptions(JsonSerializerDefaults.Web));

        Assert.NotNull(restored);
        Assert.Equal(game.Seed, restored.Seed);
        Assert.Equal(game.Stock.Select(card => card.Id), restored.Stock.Select(card => card.Id));
        Assert.Equal(
            game.Tableau.SelectMany(pile => pile).Select(card => (card.Id, card.FaceUp)),
            restored.Tableau.SelectMany(pile => pile).Select(card => (card.Id, card.FaceUp)));
        Assert.Equal(52, CardCount(restored));
    }

    private static SolitaireCommandRequest Command(
        string type,
        int version = 1,
        SolitairePileReference? from = null,
        int? startIndex = null,
        SolitairePileReference? to = null,
        int? column = null) =>
        new(type, version, from, startIndex, to, column);

    private static SolitaireGameState State(
        IReadOnlyList<SolitaireCard>? stock = null,
        IReadOnlyList<SolitaireCard>? waste = null,
        IReadOnlyList<IReadOnlyList<SolitaireCard>>? foundations = null,
        IReadOnlyList<IReadOnlyList<SolitaireCard>>? tableau = null,
        int score = 0,
        int moves = 0) => new(
        stock ?? [],
        waste ?? [],
        foundations ?? Piles(count: 4),
        tableau ?? Piles(count: 7),
        score,
        moves,
        1,
        "test");

    private static IReadOnlyList<IReadOnlyList<SolitaireCard>> Piles(
        IReadOnlyList<SolitaireCard>? first = null,
        IReadOnlyList<SolitaireCard>? second = null,
        int count = 7) => Enumerable.Range(0, count)
        .Select(index => index switch
        {
            0 => first ?? [],
            1 => second ?? [],
            _ => (IReadOnlyList<SolitaireCard>)Array.Empty<SolitaireCard>()
        })
        .ToArray();

    private static SolitaireCard Card(string suit, int rank, bool faceUp) =>
        new($"{suit}-{rank}", suit, rank, faceUp);

    private static int CardCount(SolitaireGameState game) =>
        game.Stock.Count + game.Waste.Count +
        game.Foundations.Sum(pile => pile.Count) +
        game.Tableau.Sum(pile => pile.Count);

    private static SolitairePlayerState Player(
        string userId,
        int score,
        long elapsed,
        int moves,
        DateTime completed) => new(
        "match",
        userId,
        userId,
        1,
        SolitairePlayerStatuses.Finished,
        State(score: score, moves: moves),
        2,
        elapsed,
        completed,
        0,
        false);
}
