using System.Collections.Immutable;
using FortuneForge.Games.Abstractions;
using FortuneForge.Games.TwentyFortyEight;

namespace FortuneForge.Games.Tests.TwentyFortyEight;

public sealed class TwentyFortyEightEngineTests
{
    [Fact]
    public void Start_is_seeded_and_places_two_tiles()
    {
        var first = TwentyFortyEightEngine.Start(42);
        var second = TwentyFortyEightEngine.Start(42);

        Assert.Equal(first.Size, second.Size);
        Assert.Equal(first.RandomState, second.RandomState);
        Assert.Equal(first.Tiles.ToArray(), second.Tiles.ToArray());
        Assert.Equal(2, first.Tiles.Count(tile => tile != 0));
        Assert.Equal(14, first.EmptyTileCount);
        Assert.Equal(TwentyFortyEightPhase.Playing, first.Phase);
    }

    [Fact]
    public void Left_move_merges_each_pair_once_and_spawns_a_tile()
    {
        var state = State([2, 2, 2, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0]);

        var transition = TwentyFortyEightEngine.Move(state, TwentyFortyEightDirection.Left);

        Assert.Equal(TwentyFortyEightEventType.Moved, transition.Event);
        Assert.Equal(4, transition.ScoreGained);
        Assert.Equal([4, 2, 0, 0], transition.State.Tiles.Take(4).ToArray());
        Assert.Equal(1, transition.State.Moves);
        Assert.Equal(3, transition.State.Tiles.Count(tile => tile != 0));
    }

    [Fact]
    public void No_move_does_not_change_score_board_or_random_state()
    {
        var state = State([2, 4, 8, 16, 32, 64, 128, 256, 512, 1024, 2, 4, 8, 16, 32, 64]);

        var transition = TwentyFortyEightEngine.Move(state, TwentyFortyEightDirection.Left);

        Assert.Equal(TwentyFortyEightEventType.NoMove, transition.Event);
        Assert.Equal(state, transition.State);
        Assert.Equal(0, transition.ScoreGained);
    }

    [Fact]
    public void Reaching_2048_wins_the_game()
    {
        var state = State([1024, 1024, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0]);

        var transition = TwentyFortyEightEngine.Move(state, TwentyFortyEightDirection.Left);

        Assert.Equal(TwentyFortyEightEventType.Won, transition.Event);
        Assert.Equal(TwentyFortyEightPhase.Won, transition.State.Phase);
        Assert.Equal(2048, transition.State.HighestTile);
        Assert.Equal(2048, transition.State.Score);
    }

    [Fact]
    public void A_full_board_without_pairs_has_no_legal_move()
    {
        var state = State([2, 4, 8, 16, 32, 64, 128, 256, 512, 1024, 2048, 4096, 8192, 16384, 32768, 65536]);

        Assert.False(TwentyFortyEightEngine.CanMove(state));
        Assert.Equal(TwentyFortyEightEventType.NoMove, TwentyFortyEightEngine.Move(state, TwentyFortyEightDirection.Up).Event);
    }

    [Fact]
    public void Descriptor_is_an_arcade_free_play_game_with_history()
    {
        var descriptor = TwentyFortyEightModule.Descriptor;

        Assert.Equal("2048", descriptor.Id);
        Assert.Equal(GameCategory.Arcade, descriptor.Category);
        Assert.Equal(GameCapability.FreePlay | GameCapability.History, descriptor.Capabilities);
        descriptor.Validate();
    }

    private static TwentyFortyEightState State(int[] tiles) => new(
        4,
        7,
        123456789,
        tiles.ToImmutableArray(),
        0,
        0,
        TwentyFortyEightPhase.Playing,
        tiles.Max());
}
