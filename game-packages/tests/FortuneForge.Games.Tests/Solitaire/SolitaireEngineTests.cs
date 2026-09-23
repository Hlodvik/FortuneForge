using System.Text.Json;
using FortuneForge.Games.Solitaire;

namespace FortuneForge.Games.Tests;

public sealed class SolitaireEngineTests
{
    [Fact]
    public void DealPreservesTheExistingFrozenMulberry32Sequence()
    {
        var game = SolitaireEngine.CreateGame(1);

        Assert.Equal(24, game.Stock.Count);
        Assert.Equal(
            ["diamonds-12", "spades-6", "diamonds-11", "clubs-11", "spades-5"],
            game.Stock.Take(5).Select(card => card.Id));
        Assert.Equal(["hearts-7"], game.Tableau[0].Select(card => card.Id));
        Assert.Equal(52, CardCount(game));
    }

    [Fact]
    public void DrawThreeTurnsOverThreeVisibleCards()
    {
        var game = State(
            stock: [Card("clubs", 2, false), Card("hearts", 3, false), Card("spades", 4, false)])
            with
        { DrawCount = 3 };

        var drawn = SolitaireEngine.Apply(game, Command(SolitaireCommandTypes.Draw));

        Assert.Empty(drawn.Stock);
        Assert.Equal(["spades-4", "hearts-3", "clubs-2"], drawn.Waste.Select(card => card.Id));
        Assert.All(drawn.Waste, card => Assert.True(card.FaceUp));
    }

    [Fact]
    public void FoundationsAreGenericUntilAFoundationHasASuit()
    {
        var game = State(waste: [Card("clubs", 1, true)]);
        var command = Command(
            SolitaireCommandTypes.Move,
            from: new("waste", 0),
            startIndex: 0,
            to: new("foundation", 2));

        var moved = SolitaireEngine.Apply(game, command);

        Assert.Equal("clubs-1", Assert.Single(moved.Foundations[2]).Id);
        Assert.Equal(10, moved.Score);
    }

    [Fact]
    public void IllegalTableauMoveDoesNotMutateTheState()
    {
        var game = State(
            tableau: Piles([Card("clubs", 8, true)], [Card("spades", 3, true)]));

        Assert.Throws<SolitaireIllegalMoveException>(() => SolitaireEngine.Apply(game, Command(
            SolitaireCommandTypes.Move,
            from: new("tableau", 0),
            startIndex: 0,
            to: new("tableau", 1))));
        Assert.Equal("clubs-8", Assert.Single(game.Tableau[0]).Id);
    }

    [Fact]
    public void StateRoundTripsForDurableStorage()
    {
        var game = SolitaireEngine.CreateGame(uint.MaxValue);
        var json = JsonSerializer.Serialize(game, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        var restored = JsonSerializer.Deserialize<SolitaireGameState>(json, new JsonSerializerOptions(JsonSerializerDefaults.Web));

        Assert.NotNull(restored);
        Assert.Equal(game.Seed, restored.Seed);
        Assert.Equal(52, CardCount(restored));
    }

    private static SolitaireCommandRequest Command(
        string type,
        SolitairePileReference? from = null,
        int? startIndex = null,
        SolitairePileReference? to = null) => new(type, 1, from, startIndex, to, null);

    private static SolitaireGameState State(
        IReadOnlyList<SolitaireCard>? stock = null,
        IReadOnlyList<SolitaireCard>? waste = null,
        IReadOnlyList<IReadOnlyList<SolitaireCard>>? tableau = null) => new(
        stock ?? [],
        waste ?? [],
        Piles(count: 4),
        tableau ?? Piles(count: 7),
        0,
        0,
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
            _ => (IReadOnlyList<SolitaireCard>)Array.Empty<SolitaireCard>(),
        })
        .ToArray();

    private static SolitaireCard Card(string suit, int rank, bool faceUp) =>
        new($"{suit}-{rank}", suit, rank, faceUp);

    private static int CardCount(SolitaireGameState game) =>
        game.Stock.Count + game.Waste.Count + game.Foundations.Sum(pile => pile.Count) + game.Tableau.Sum(pile => pile.Count);
}
