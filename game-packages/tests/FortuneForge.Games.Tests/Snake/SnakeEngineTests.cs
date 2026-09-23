using System.Collections.Immutable;
using FortuneForge.Games.Abstractions;
using FortuneForge.Games.Snake;

namespace FortuneForge.Games.Tests.Snake;

public sealed class SnakeEngineTests
{
    [Fact]
    public void Start_is_seeded_and_places_a_food_item_outside_the_snake()
    {
        var first = SnakeEngine.Start(42);
        var second = SnakeEngine.Start(42);

        Assert.Equal(first.RandomState, second.RandomState);
        Assert.Equal(first.Body.ToArray(), second.Body.ToArray());
        Assert.Equal(first.Food, second.Food);
        Assert.Equal(3, first.Length);
        Assert.NotNull(first.Food);
        Assert.DoesNotContain(first.Food!.Value, first.Body);
    }

    [Fact]
    public void Tick_moves_the_head_without_changing_length()
    {
        var state = State(
            body: [new SnakePoint(3, 3), new SnakePoint(2, 3), new SnakePoint(1, 3)],
            direction: SnakeDirection.Right,
            food: new SnakePoint(6, 6));

        var transition = SnakeEngine.Tick(state);

        Assert.Equal(SnakeEventType.Moved, transition.Event);
        Assert.Equal(new SnakePoint(4, 3), transition.State.Head);
        Assert.Equal(3, transition.State.Length);
        Assert.Equal(1, transition.State.Moves);
        Assert.Equal(0, transition.State.Score);
    }

    [Fact]
    public void Eating_food_grows_the_snake_and_awards_points()
    {
        var state = State(
            body: [new SnakePoint(3, 3), new SnakePoint(2, 3), new SnakePoint(1, 3)],
            direction: SnakeDirection.Right,
            food: new SnakePoint(4, 3));

        var transition = SnakeEngine.Tick(state);

        Assert.Equal(SnakeEventType.AteFood, transition.Event);
        Assert.Equal(10, transition.ScoreGained);
        Assert.Equal(10, transition.State.Score);
        Assert.Equal(10, transition.State.BestScore);
        Assert.Equal(4, transition.State.Length);
        Assert.Equal(new SnakePoint(4, 3), transition.State.Head);
        Assert.NotEqual(state.Food, transition.State.Food);
    }

    [Fact]
    public void Reversing_direction_is_ignored_when_the_snake_has_a_body()
    {
        var state = State(
            body: [new SnakePoint(3, 3), new SnakePoint(2, 3), new SnakePoint(1, 3)],
            direction: SnakeDirection.Right,
            food: new SnakePoint(6, 6));

        var transition = SnakeEngine.Turn(state, SnakeDirection.Left);

        Assert.Equal(SnakeEventType.NoOp, transition.Event);
        Assert.Equal(state, transition.State);
        Assert.Contains("reverse", transition.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Hitting_the_wall_loses_the_game()
    {
        var state = State(
            body: [new SnakePoint(0, 0), new SnakePoint(1, 0), new SnakePoint(2, 0)],
            direction: SnakeDirection.Up,
            food: new SnakePoint(5, 5));

        var transition = SnakeEngine.Tick(state);

        Assert.Equal(SnakeEventType.Lost, transition.Event);
        Assert.Equal(SnakePhase.Lost, transition.State.Phase);
        Assert.Equal(1, transition.State.Moves);
        Assert.Equal(state.Body, transition.State.Body);
    }

    [Fact]
    public void Hitting_the_body_loses_the_game()
    {
        var state = State(
            body: [new SnakePoint(3, 3), new SnakePoint(3, 2), new SnakePoint(2, 2)],
            direction: SnakeDirection.Up,
            food: new SnakePoint(6, 6));

        var transition = SnakeEngine.Tick(state);

        Assert.Equal(SnakeEventType.Lost, transition.Event);
        Assert.Equal(SnakePhase.Lost, transition.State.Phase);
    }

    [Fact]
    public void Descriptor_is_an_arcade_free_play_game()
    {
        var descriptor = SnakeModule.Descriptor;

        Assert.Equal("snake", descriptor.Id);
        Assert.Equal(GameCategory.Arcade, descriptor.Category);
        Assert.Equal(GameCapability.FreePlay, descriptor.Capabilities);
        descriptor.Validate();
    }

    private static SnakeState State(
        SnakePoint[] body,
        SnakeDirection direction,
        SnakePoint food) => new(
            8,
            8,
            7,
            123456789,
            body.ToImmutableArray(),
            direction,
            food,
            0,
            0,
            0,
            SnakePhase.Playing);
}
