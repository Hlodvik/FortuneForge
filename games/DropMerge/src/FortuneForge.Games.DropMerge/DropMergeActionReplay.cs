using System.Collections.Immutable;
using System.Text.Json;
using FortuneForge.Games.Abstractions.Auditing;
using static FortuneForge.Games.Abstractions.Auditing.PuzzleReplayEvidence;

namespace FortuneForge.Games.DropMerge;

/// <summary>Independently verifies recorded actions against the game rules.</summary>
public static class DropMergeActionReplay
{
    /// <summary>Replays a run from its original start across completion and undo episodes.</summary>
    public static IReadOnlyList<RoundAuditFinding> InspectCompleteRun(IReadOnlyList<PuzzleActionObservation> observations) =>
        Inspect(observations, 1);

    public static IReadOnlyList<RoundAuditFinding> Inspect(IReadOnlyList<PuzzleActionObservation> observations, int episode)
    {
        var first = observations[0];
        var prior = ReadDrop(first.Payload);
        var history = new Stack<DropMergeState>();
        var commandArgumentsUnavailable = false;
        if (episode == 1 && (!SameDrop(prior, StartDrop(prior.Seed)) ||
            first.Payload.GetProperty("historyCount").GetInt32() != 0 ||
            first.Payload.GetProperty("scoreGained").GetInt32() != 0 ||
            first.Payload.GetProperty("mergeCount").GetInt32() != 0))
            return Error("drop-merge-initial-deal-mismatch", "The first tile queue or board does not reproduce from its saved seed.");
        for (var i = 1; i < observations.Count; i++)
        {
            var next = observations[i];
            var current = ReadDrop(next.Payload);
            if (next.Kind == "undone")
            {
                if (history.Count == 0 || !SameDrop(current, history.Pop()) ||
                    next.Payload.GetProperty("scoreGained").GetInt32() != 0 ||
                    next.Payload.GetProperty("mergeCount").GetInt32() != 0)
                    return Error("drop-merge-undo-mismatch", "An undo does not restore the previous accepted board and queue.");
            }
            else
            {
                var column = next.Payload.GetProperty("dropColumn").GetInt32();
                var requested = RequestedColumn(next.RequestJson);
                if (requested is null) commandArgumentsUnavailable = true;
                if (requested is { } requestedColumn && requestedColumn != column)
                    return Error("drop-merge-request-column-mismatch", "The accepted drop column differs from the recorded command argument.");
                var expected = DropLocally(prior, column);
                var savedSteps = JsonSerializer.Deserialize<DropMergeMergeStep[]>(
                    next.Payload.GetProperty("mergeSteps"), RoundAuditCapture.Json) ?? throw new JsonException("Missing merge steps.");
                if (expected.Kind != next.Kind || !SameDrop(current, expected.State) ||
                    next.Payload.GetProperty("scoreGained").GetInt32() != expected.ScoreGained ||
                    next.Payload.GetProperty("mergeCount").GetInt32() != expected.MergeCount ||
                    next.Payload.GetProperty("dropRow").GetInt32() != expected.DropRow ||
                    next.Payload.GetProperty("removedTile").GetInt32() != expected.RemovedTile ||
                    next.Payload.GetProperty("introducedTile").GetInt32() != 0 ||
                    !SameSteps(savedSteps, expected.Steps))
                    return Error("drop-merge-drop-mismatch", "A saved drop, merge chain, tile queue or score is not the legal result of the previous board.");
                history.Push(prior);
            }
            if (next.Payload.GetProperty("historyCount").GetInt32() != history.Count && episode == 1)
                return Error("drop-merge-history-count-mismatch", "The saved undo depth differs from the accepted drop history.");
            prior = current;
        }
        if (prior.Phase != DropMergePhase.Lost || prior.Moves == 0)
            return Error("drop-merge-terminal-action-mismatch", "The last puzzle observation does not end in a lost board.");
        return commandArgumentsUnavailable ? CommandArgumentsUnavailable() : [];
    }

    private static DropMergeState ReadDrop(JsonElement payload) =>
        JsonSerializer.Deserialize<DropMergeState>(payload.GetProperty("state"), RoundAuditCapture.Json) ??
        throw new JsonException("Missing Drop Merge state.");

    private static bool SameDrop(DropMergeState a, DropMergeState b) =>
        a.Columns == b.Columns && a.Rows == b.Rows && a.Seed == b.Seed &&
        a.RandomState == b.RandomState && a.Tiles.SequenceEqual(b.Tiles) &&
        a.CurrentTile == b.CurrentTile && a.NextTile == b.NextTile && a.Score == b.Score &&
        a.Moves == b.Moves && a.Combo == b.Combo && a.BestCombo == b.BestCombo &&
        a.TotalMerges == b.TotalMerges && a.SmallestTileClearCount == b.SmallestTileClearCount &&
        a.SmallestQueuedTile == b.SmallestQueuedTile && a.NextBigTile == b.NextBigTile &&
        a.Phase == b.Phase && a.HighestTile == b.HighestTile;

    private static DropMergeState StartDrop(uint seed)
    {
        var random = seed == 0 ? 0xA341316Cu : seed;
        var first = DrawDropTile(ref random, 2);
        var second = DrawDropTile(ref random, 2);
        return new(7, 7, seed, random, new int[49].ToImmutableArray(), first, second,
            0, 0, 0, 0, 0, 0, 2, 1024, DropMergePhase.Playing, 0);
    }

    private static (DropMergeState State, string Kind, int ScoreGained, int MergeCount,
        int DropRow, int RemovedTile, IReadOnlyList<DropMergeMergeStep> Steps) DropLocally(DropMergeState prior, int column)
    {
        if (prior.Phase != DropMergePhase.Playing || prior.Columns != 7 || prior.Rows != 7 ||
            prior.Tiles.Length != 49 || column is < 0 or > 6)
            throw new JsonException("Invalid Drop Merge precondition or column.");
        var tiles = prior.Tiles.ToArray();
        var row = BottomEmpty(tiles, column);
        if (row < 0)
            return (prior with { Phase = DropMergePhase.Lost }, "lost", 0, 0, -1, 0, []);
        var landing = row * 7 + column;
        tiles[landing] = prior.CurrentTile;
        var preferred = landing;
        var score = 0;
        var mergedCount = 0;
        var highestMerged = 0;
        var steps = new List<DropMergeMergeStep>();
        while (FindMerge(tiles, preferred) is { } group)
        {
            var sources = group.Select(index => new DropMergeMergeTile(index / 7, index % 7, tiles[index])).ToImmutableArray();
            var value = tiles[group[0]];
            for (var member = 1; member < group.Count; member++) value = checked(value * 2);
            var anchor = group.Contains(preferred) ? preferred : group.OrderByDescending(index => index / 7)
                .ThenBy(index => index % 7).First();
            var anchorColumn = anchor % 7;
            foreach (var index in group) tiles[index] = 0;
            foreach (var affected in group.Select(index => index % 7).Distinct()) Gravity(tiles, affected);
            var mergedRow = BottomEmpty(tiles, anchorColumn);
            if (mergedRow < 0) throw new JsonException("No place for merged tile.");
            preferred = mergedRow * 7 + anchorColumn;
            tiles[preferred] = value;
            steps.Add(new(sources, new(mergedRow, anchorColumn, value), tiles.ToImmutableArray()));
            score = checked(score + value);
            mergedCount = checked(mergedCount + group.Count - 1);
            highestMerged = Math.Max(highestMerged, value);
        }
        var clearCount = prior.SmallestTileClearCount;
        var smallest = prior.SmallestQueuedTile;
        var nextBig = prior.NextBigTile;
        var removed = 0;
        if (highestMerged >= nextBig)
        {
            removed = smallest;
            var removedIndexes = Enumerable.Range(0, 49).Where(index => tiles[index] == smallest).ToArray();
            foreach (var index in removedIndexes) tiles[index] = 0;
            foreach (var affected in removedIndexes.Select(index => index % 7).Distinct()) Gravity(tiles, affected);
            clearCount = checked(clearCount + 1);
            smallest = checked(smallest * 2);
            nextBig = checked(nextBig * 2);
        }
        var random = prior.RandomState;
        var current = prior.NextTile >= smallest ? prior.NextTile : DrawDropTile(ref random, smallest);
        var next = DrawDropTile(ref random, smallest);
        var phase = tiles.Any(value => value == 0) ? DropMergePhase.Playing : DropMergePhase.Lost;
        var kind = phase == DropMergePhase.Lost ? "lost" : removed > 0 ? "big-tile-reached" : "dropped";
        var state = prior with
        {
            RandomState = random, Tiles = tiles.ToImmutableArray(), CurrentTile = current, NextTile = next,
            Score = checked(prior.Score + score), Moves = checked(prior.Moves + 1),
            Combo = mergedCount, BestCombo = Math.Max(prior.BestCombo, mergedCount),
            TotalMerges = checked(prior.TotalMerges + mergedCount), SmallestTileClearCount = clearCount,
            SmallestQueuedTile = smallest, NextBigTile = nextBig, Phase = phase, HighestTile = tiles.Max()
        };
        return (state, kind, score, mergedCount, row, removed, steps);
    }

    private static int DrawDropTile(ref uint random, int smallest)
    {
        ReadOnlySpan<int> weights = [18, 24, 24, 18, 16];
        var roll = (int)(NextRandom(ref random) % 100u);
        for (var rank = 0; rank < weights.Length; rank++)
        {
            if (roll < weights[rank]) return checked(smallest * (1 << rank));
            roll -= weights[rank];
        }
        throw new JsonException("Invalid Drop Merge spawn roll.");
    }

    private static List<int>? FindMerge(int[] tiles, int preferred)
    {
        if (preferred >= 0 && preferred < 49 && tiles[preferred] > 0)
        {
            var group = Connected(tiles, preferred);
            if (group.Count > 1) return group;
        }
        for (var index = 0; index < 49; index++)
            if (tiles[index] > 0 && Connected(tiles, index) is { Count: > 1 } group) return group;
        return null;
    }

    private static List<int> Connected(int[] tiles, int start)
    {
        var value = tiles[start];
        var visited = new bool[49];
        var queue = new Queue<int>();
        var group = new List<int>();
        visited[start] = true; queue.Enqueue(start);
        while (queue.Count > 0)
        {
            var index = queue.Dequeue();
            group.Add(index);
            var row = index / 7; var column = index % 7;
            Visit(row - 1, column); Visit(row + 1, column); Visit(row, column - 1); Visit(row, column + 1);
        }
        return group;

        void Visit(int row, int column)
        {
            if (row is < 0 or > 6 || column is < 0 or > 6) return;
            var index = row * 7 + column;
            if (visited[index] || tiles[index] != value) return;
            visited[index] = true; queue.Enqueue(index);
        }
    }

    private static int BottomEmpty(int[] tiles, int column)
    {
        for (var row = 6; row >= 0; row--) if (tiles[row * 7 + column] == 0) return row;
        return -1;
    }

    private static void Gravity(int[] tiles, int column)
    {
        var values = Enumerable.Range(0, 7).Select(row => tiles[row * 7 + column]).Where(value => value > 0).ToArray();
        for (var row = 0; row < 7; row++) tiles[row * 7 + column] = row >= 7 - values.Length ? values[row - (7 - values.Length)] : 0;
    }

    private static bool SameSteps(IReadOnlyList<DropMergeMergeStep> saved, IReadOnlyList<DropMergeMergeStep> expected)
    {
        if (saved.Count != expected.Count) return false;
        for (var step = 0; step < saved.Count; step++)
        {
            if (saved[step].Target != expected[step].Target ||
                !saved[step].TilesAfter.SequenceEqual(expected[step].TilesAfter) ||
                saved[step].Sources.Length != expected[step].Sources.Length)
                return false;
            var left = saved[step].Sources.OrderBy(item => (item.Row, item.Column)).ToArray();
            var right = expected[step].Sources.OrderBy(item => (item.Row, item.Column)).ToArray();
            if (!left.SequenceEqual(right)) return false;
        }
        return true;
    }

    private static int? RequestedColumn(string? json)
    {
        if (json is null) return null;
        using var document = JsonDocument.Parse(json);
        return TryProperty(document.RootElement, "column", out var value) && value.ValueKind == JsonValueKind.Number
            ? value.GetInt32() : null;
    }
}
