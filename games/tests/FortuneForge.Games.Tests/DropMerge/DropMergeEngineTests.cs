using System.Collections.Immutable;
using FortuneForge.Games.Abstractions;
using FortuneForge.Games.DropMerge;

namespace FortuneForge.Games.Tests.DropMerge;

public sealed class DropMergeEngineTests
{
    [Fact]
    public void Start_creates_an_empty_seven_by_seven_board_with_a_seeded_queue()
    {
        var first = DropMergeEngine.Start(42);
        var second = DropMergeEngine.Start(42);

        Assert.Equal(7, first.Columns);
        Assert.Equal(7, first.Rows);
        Assert.Equal(first.Tiles.ToArray(), second.Tiles.ToArray());
        Assert.Equal(first.CurrentTile, second.CurrentTile);
        Assert.Equal(first.NextTile, second.NextTile);
        Assert.Equal(49, first.EmptyTileCount);
        Assert.Contains(first.CurrentTile, new[] { 2, 4, 8, 16, 32 });
        Assert.Contains(first.NextTile, new[] { 2, 4, 8, 16, 32 });
        Assert.Equal(new[] { 18, 24, 24, 18, 16 }, DropMergeEngine.SpawnRankWeights);
        Assert.Equal(DropMergeEngine.StartingDropTimerMilliseconds, first.DropTimerMilliseconds);
        Assert.Equal(DropMergePhase.Playing, first.Phase);
    }

    [Fact]
    public void Dropping_a_matching_tile_merges_in_the_selected_column()
    {
        var tiles = new int[49];
        tiles[42] = 2;
        var state = State(tiles, currentTile: 2, nextTile: 4);

        var transition = DropMergeEngine.Drop(state, 0);

        Assert.Equal(DropMergeEventType.Dropped, transition.Event);
        Assert.Equal(1, transition.MergeCount);
        Assert.Equal(4, transition.ScoreGained);
        Assert.Equal(4, transition.State.Tiles[42]);
        Assert.Single(transition.MergeSteps);
        Assert.Equal(new DropMergeMergeTile(6, 0, 4), transition.MergeSteps[0].Target);
        Assert.Equal(4, transition.MergeSteps[0].TilesAfter[42]);
        Assert.Equal(1, transition.State.Moves);
        Assert.Equal(48, transition.State.EmptyTileCount);
    }

    [Fact]
    public void Dropping_into_two_adjacent_matching_tiles_merges_the_whole_group()
    {
        var tiles = new int[49];
        tiles[42] = 2;
        tiles[44] = 2;
        var state = State(tiles, currentTile: 2, nextTile: 4);

        var transition = DropMergeEngine.Drop(state, 1);

        Assert.Equal(2, transition.MergeCount);
        Assert.Equal(8, transition.ScoreGained);
        Assert.Equal(8, transition.State.Tiles[43]);
        Assert.DoesNotContain(2, transition.State.Tiles);
        Assert.Equal(new DropMergeMergeTile(6, 1, 8), transition.MergeSteps[0].Target);
        Assert.Contains(new DropMergeMergeTile(6, 1, 2), transition.MergeSteps[0].Sources);
    }

    [Fact]
    public void Dropping_into_a_four_tile_matching_cluster_creates_a_sixteen()
    {
        var tiles = new int[49];
        tiles[36] = 2;
        tiles[38] = 2;
        tiles[44] = 2;
        var state = State(tiles, currentTile: 2, nextTile: 4);

        var transition = DropMergeEngine.Drop(state, 2);

        Assert.Equal(3, transition.MergeCount);
        Assert.Equal(16, transition.ScoreGained);
        Assert.Equal(16, transition.State.Tiles[44]);
        Assert.DoesNotContain(2, transition.State.Tiles);
    }

    [Fact]
    public void Chain_merges_keep_the_intermediate_board_before_the_next_merge()
    {
        var tiles = new int[49];
        tiles[42] = 2;
        tiles[43] = 4;
        var state = State(tiles, currentTile: 2, nextTile: 4);

        var transition = DropMergeEngine.Drop(state, 0);

        Assert.Equal(2, transition.MergeSteps.Length);
        Assert.Equal(4, transition.MergeSteps[0].TilesAfter[42]);
        Assert.Equal(4, transition.MergeSteps[0].TilesAfter[43]);
        Assert.Equal(8, transition.MergeSteps[1].TilesAfter[42]);
        Assert.Equal(0, transition.MergeSteps[1].TilesAfter[43]);
    }

    [Fact]
    public void Making_a_1024_tile_retires_the_smallest_tier_and_shifts_future_tiles_upward()
    {
        var tiles = new int[49];
        tiles[42] = 512;
        tiles[43] = 2;
        tiles[44] = 2;
        tiles[48] = 2;
        var state = State(tiles, currentTile: 512, nextTile: 4);

        var transition = DropMergeEngine.Drop(state, 0);

        Assert.Equal(DropMergeEventType.BigTileReached, transition.Event);
        Assert.Equal(2, transition.RemovedTile);
        Assert.Equal(0, transition.IntroducedTile);
        Assert.Equal(1, transition.State.SmallestTileClearCount);
        Assert.Equal(4, transition.State.SmallestQueuedTile);
        Assert.Equal(2048, transition.State.NextBigTile);
        Assert.Equal(9_750, transition.State.DropTimerMilliseconds);
        Assert.Contains(1024, transition.State.Tiles);
        Assert.DoesNotContain(2048, transition.State.Tiles);
        Assert.DoesNotContain(2, transition.State.Tiles);
        Assert.DoesNotContain(2, new[] { transition.State.CurrentTile, transition.State.NextTile });
    }

    [Fact]
    public void Dropping_into_a_full_column_ends_the_run()
    {
        var tiles = new int[49];
        for (var row = 0; row < 7; row++)
            tiles[row * 7] = 2;
        var state = State(tiles, currentTile: 4, nextTile: 2);

        var transition = DropMergeEngine.Drop(state, 0);

        Assert.Equal(DropMergeEventType.Lost, transition.Event);
        Assert.Equal(DropMergePhase.Lost, transition.State.Phase);
        Assert.Equal(state.Tiles, transition.State.Tiles);
        Assert.Contains("full", transition.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Falling_animation_gets_faster_only_in_small_steps()
    {
        var slow = State(new int[49], 2, 4);
        var quicker = slow with { Moves = DropMergeEngine.MovesPerTempoLevel * 2 };

        Assert.Equal(1, slow.TempoLevel);
        Assert.Equal(3, quicker.TempoLevel);
        Assert.True(quicker.DropDurationMilliseconds < slow.DropDurationMilliseconds);
        Assert.True(quicker.DropDurationMilliseconds >= DropMergeEngine.MinimumDropDurationMilliseconds);
        Assert.Equal(slow.DropTimerMilliseconds, quicker.DropTimerMilliseconds);
    }

    [Fact]
    public void Auto_drop_timer_reduces_by_two_point_five_percent_per_smallest_tile_clear()
    {
        var start = State(new int[49], 2, 4);
        var firstClear = start with { SmallestTileClearCount = 1 };
        var secondClear = start with { SmallestTileClearCount = 2 };

        Assert.Equal(10_000, start.DropTimerMilliseconds);
        Assert.Equal(9_750, firstClear.DropTimerMilliseconds);
        Assert.Equal(9_506, secondClear.DropTimerMilliseconds);
    }

    [Fact]
    public void Descriptor_is_an_arcade_free_play_game_with_history()
    {
        var descriptor = DropMergeModule.Descriptor;

        Assert.Equal("drop-merge", descriptor.Id);
        Assert.Equal(GameCategory.Arcade, descriptor.Category);
        Assert.Equal(GameCapability.FreePlay | GameCapability.History, descriptor.Capabilities);
        descriptor.Validate();
    }

    private static DropMergeState State(int[] tiles, int currentTile, int nextTile) => new(
        7,
        7,
        7,
        123456789,
        tiles.ToImmutableArray(),
        currentTile,
        nextTile,
        0,
        0,
        0,
        0,
        0,
        0,
        DropMergeEngine.SmallestSpawnTile,
        DropMergeEngine.FirstBigTile,
        DropMergePhase.Playing,
        tiles.Max());
}
