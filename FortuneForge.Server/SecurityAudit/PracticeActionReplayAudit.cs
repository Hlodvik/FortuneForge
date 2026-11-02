using System.Collections.Immutable;
using System.Text.Json;
using FortuneForge.Games.Abstractions.Auditing;
using FortuneForge.Games.DropMerge;
using FortuneForge.Games.TwentyFortyEight;

namespace FortuneForge.Server.SecurityAudit;

/// <summary>Second-pass puzzle transition replay over the complete saved instance journal.</summary>
internal static class PracticeActionReplayAudit
{
    internal static bool Supports(RoundAuditSummary summary) => (summary.GameId, summary.AuditorVersion, summary.EvidenceKind) switch
    {
        ("2048", PracticeOutcomeAudit.TwentyFortyEightVersion, "2048-private-state-v1") => true,
        ("drop-merge", PracticeOutcomeAudit.DropMergeVersion, "drop-merge-private-state-v1") => true,
        _ => false
    };

    internal static IReadOnlyList<RoundAuditFinding> Inspect(
        RoundAuditSummary summary, string? payloadJson, IReadOnlyList<string> eventPayloads)
    {
        if (!Supports(summary)) return [];
        if (payloadJson is null)
            return Error("practice-action-terminal-missing", "The completed puzzle has no private terminal state.");
        try
        {
            using var terminal = JsonDocument.Parse(payloadJson);
            var saved = terminal.RootElement;
            if (!Classification(summary, saved))
                return Error("practice-action-classification-mismatch", "The puzzle episode identity or participant summary differs from its terminal evidence.");
            var observations = new List<Observation>(eventPayloads.Count);
            foreach (var payload in eventPayloads)
            {
                using var document = JsonDocument.Parse(payload);
                var root = document.RootElement;
                var request = root.GetProperty("request");
                var requestJson = request.ValueKind == JsonValueKind.Object &&
                    request.TryGetProperty("requestJson", out var json) && json.ValueKind == JsonValueKind.String
                    ? json.GetString() : null;
                observations.Add(new(root.GetProperty("version").GetInt64(),
                    root.GetProperty("eventKind").GetString() ?? "",
                    root.GetProperty("occurredAtUtc").GetDateTime().ToUniversalTime(),
                    root.GetProperty("state").Clone(), requestJson));
            }
            observations.Sort((a, b) => a.Version.CompareTo(b.Version));
            if (observations.Count == 0 ||
                !JsonElement.DeepEquals(observations[^1].Payload, saved))
                return Error("practice-action-terminal-mismatch", "The terminal puzzle evidence is missing from, or differs from, its last engine observation.");
            var episode = saved.GetProperty("completionEpisode").GetInt32();
            for (var i = 0; i < observations.Count; i++)
            {
                var current = observations[i];
                if (current.Version != observations[0].Version + i ||
                    (i > 0 && current.OccurredAtUtc < observations[i - 1].OccurredAtUtc) ||
                    current.Kind != current.Payload.GetProperty("lastEvent").GetString() ||
                    current.Payload.GetProperty("completionEpisode").GetInt32() != episode ||
                    current.Payload.GetProperty("runId").GetGuid() != saved.GetProperty("runId").GetGuid() ||
                    current.Payload.GetProperty("id").GetGuid() != saved.GetProperty("id").GetGuid())
                    return Error("practice-action-journal-sequence-mismatch", "The puzzle observation stream has missing versions, reversed times, or changed episode identity.");
            }
            if (episode == 1 && (observations[0].Version != 0 || observations[0].Kind != "started"))
                return Error("practice-action-start-missing", "The first puzzle episode lacks its initial accepted start observation.");
            var findings = summary.GameId == "2048" ? Replay2048(observations, episode) : ReplayDropMerge(observations, episode);
            if (findings.Any(value => value.Severity == "error")) return findings;
            var results = findings.ToList();
            if (episode > 1)
                results.Add(new("practice-resumed-episode-prior-history-not-linked", "warning",
                    "This completion episode begins after a prior undo or continue; its earlier run history is not copied into this episode's journal."));
            results.Add(new("practice-in-memory-state-journal-non-atomic", "warning",
                "The server's in-memory puzzle state and acknowledged Firestore journal cannot commit atomically; a failed write may leave an accepted transition unrecorded."));
            return results;
        }
        catch (Exception exception) when (exception is JsonException or KeyNotFoundException or InvalidOperationException or
            ArgumentException or ArithmeticException or FormatException or OverflowException or IndexOutOfRangeException or NotSupportedException)
        {
            return Error("practice-action-evidence-invalid", "The puzzle terminal state or observation stream cannot be parsed for action replay.");
        }
    }

    private static bool Classification(RoundAuditSummary summary, JsonElement saved)
    {
        var run = saved.GetProperty("runId").GetGuid();
        var table = saved.GetProperty("id").GetGuid();
        var episode = saved.GetProperty("completionEpisode").GetInt32();
        var completed = saved.GetProperty("completedAtUtc");
        return summary.RoundId == $"{run:N}-episode-{episode}" && summary.TableId == table.ToString("N") &&
            summary.RoundNumber == episode && episode >= 1 && completed.ValueKind != JsonValueKind.Null &&
            summary.StartedAtUtc == saved.GetProperty("startedAtUtc").GetDateTime().ToUniversalTime() &&
            summary.CompletedAtUtc == completed.GetDateTime().ToUniversalTime() &&
            summary.Participants.Count == 1 && summary.Participants[0].IsAccountBacked &&
            summary.Participants[0].WagerCents == 0 && summary.Participants[0].PayoutCents == 0 &&
            summary.Participants[0].NetCents == 0;
    }

    private static IReadOnlyList<RoundAuditFinding> Replay2048(IReadOnlyList<Observation> observations, int episode)
    {
        var first = observations[0];
        var prior = Read2048(first.Payload);
        var history = new Stack<TwentyFortyEightState>();
        var commandArgumentsUnavailable = false;
        if (episode == 1 && (!Same2048(prior, Start2048(prior.Seed)) ||
            first.Payload.GetProperty("count").GetInt32() != 0 ||
            first.Payload.GetProperty("scoreGained").GetInt32() != 0))
            return Error("2048-initial-deal-mismatch", "The first board does not reproduce from its stored seed and rules.");
        for (var i = 1; i < observations.Count; i++)
        {
            var next = observations[i];
            var current = Read2048(next.Payload);
            var scoreGained = next.Payload.GetProperty("scoreGained").GetInt32();
            if (next.Kind == "undone")
            {
                if (history.Count == 0 || !Same2048(current, history.Pop()) || scoreGained != 0)
                    return Error("2048-undo-mismatch", "An undo does not restore the previous accepted board.");
            }
            else if (next.Kind == "continued")
            {
                if (prior.Phase != TwentyFortyEightPhase.Won ||
                    !Same2048(current, prior with { Phase = TwentyFortyEightPhase.Playing }) || scoreGained != 0)
                    return Error("2048-continue-mismatch", "A continue does not follow a winning board without changing its tiles or score.");
            }
            else
            {
                var requested = RequestedDirection(next.RequestJson);
                if (requested is null) commandArgumentsUnavailable = true;
                var directions = requested is { } direction
                    ? [direction] : Enum.GetValues<TwentyFortyEightDirection>();
                var legal = directions
                    .Select(direction => Move2048(prior, direction))
                    .Any(candidate => candidate.Kind == next.Kind && candidate.ScoreGained == scoreGained &&
                        Same2048(candidate.State, current));
                if (!legal) return Error("2048-move-mismatch", "A saved board is not the result of any legal direction from the previous board.");
                if (next.Kind != "no-move") history.Push(prior);
            }
            if (next.Payload.GetProperty("count").GetInt32() != history.Count && episode == 1)
                return Error("2048-history-count-mismatch", "The saved undo depth differs from the accepted move history.");
            prior = current;
        }
        if (prior.Phase is not (TwentyFortyEightPhase.Won or TwentyFortyEightPhase.Lost) || prior.Moves == 0)
            return Error("2048-terminal-action-mismatch", "The last puzzle observation does not end in a completed board.");
        return commandArgumentsUnavailable ? CommandArgumentsUnavailable() : [];
    }

    private static TwentyFortyEightState Read2048(JsonElement payload) =>
        JsonSerializer.Deserialize<TwentyFortyEightState>(payload.GetProperty("state"), RoundAuditCapture.Json) ??
        throw new JsonException("Missing 2048 state.");

    private static bool Same2048(TwentyFortyEightState a, TwentyFortyEightState b) =>
        a.Size == b.Size && a.Seed == b.Seed && a.RandomState == b.RandomState &&
        a.Tiles.SequenceEqual(b.Tiles) && a.Score == b.Score && a.Moves == b.Moves &&
        a.Phase == b.Phase && a.HighestTile == b.HighestTile;

    private static TwentyFortyEightState Start2048(uint seed)
    {
        var tiles = new int[16];
        var random = seed == 0 ? 0xA341316Cu : seed;
        Spawn2048(tiles, ref random);
        Spawn2048(tiles, ref random);
        return new(4, seed, random, tiles.ToImmutableArray(), 0, 0, TwentyFortyEightPhase.Playing, tiles.Max());
    }

    private static (TwentyFortyEightState State, string Kind, int ScoreGained) Move2048(
        TwentyFortyEightState prior, TwentyFortyEightDirection direction)
    {
        if (prior.Phase != TwentyFortyEightPhase.Playing || prior.Size != 4 || prior.Tiles.Length != 16)
            return (prior, "invalid", 0);
        var tiles = prior.Tiles.ToArray();
        var gained = 0;
        for (var line = 0; line < 4; line++)
        {
            var indexes = Enumerable.Range(0, 4).Select(offset => direction switch
            {
                TwentyFortyEightDirection.Left => line * 4 + offset,
                TwentyFortyEightDirection.Right => line * 4 + 3 - offset,
                TwentyFortyEightDirection.Up => offset * 4 + line,
                _ => (3 - offset) * 4 + line
            }).ToArray();
            var values = indexes.Select(index => tiles[index]).Where(value => value > 0).ToArray();
            var merged = new List<int>(4);
            for (var index = 0; index < values.Length; index++)
            {
                if (index + 1 < values.Length && values[index] == values[index + 1])
                {
                    var value = checked(values[index] * 2);
                    merged.Add(value); gained = checked(gained + value); index++;
                }
                else merged.Add(values[index]);
            }
            for (var index = 0; index < 4; index++) tiles[indexes[index]] = index < merged.Count ? merged[index] : 0;
        }
        if (tiles.SequenceEqual(prior.Tiles)) return (prior, "no-move", 0);
        var random = prior.RandomState;
        Spawn2048(tiles, ref random);
        var highest = tiles.Max();
        var phase = prior.HighestTile < 2048 && highest >= 2048 ? TwentyFortyEightPhase.Won :
            Has2048Move(tiles) ? TwentyFortyEightPhase.Playing : TwentyFortyEightPhase.Lost;
        var kind = phase == TwentyFortyEightPhase.Won ? "won" : phase == TwentyFortyEightPhase.Lost ? "lost" : "moved";
        return (prior with { RandomState = random, Tiles = tiles.ToImmutableArray(),
            Score = checked(prior.Score + gained), Moves = checked(prior.Moves + 1),
            Phase = phase, HighestTile = highest }, kind, gained);
    }

    private static bool Has2048Move(int[] tiles) => Enumerable.Range(0, 16).Any(index =>
        tiles[index] == 0 || index % 4 < 3 && tiles[index] == tiles[index + 1] ||
        index / 4 < 3 && tiles[index] == tiles[index + 4]);

    private static void Spawn2048(int[] tiles, ref uint random)
    {
        var spaces = Enumerable.Range(0, tiles.Length).Where(index => tiles[index] == 0).ToArray();
        if (spaces.Length == 0) return;
        var spot = (int)(NextRandom(ref random) % (uint)spaces.Length);
        tiles[spaces[spot]] = NextRandom(ref random) % 10 == 0 ? 4 : 2;
    }

    private static uint NextRandom(ref uint value)
    {
        unchecked { value ^= value << 13; value ^= value >> 17; value ^= value << 5; }
        return value;
    }

    private static IReadOnlyList<RoundAuditFinding> ReplayDropMerge(IReadOnlyList<Observation> observations, int episode)
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

    private static IReadOnlyList<RoundAuditFinding> Error(string code, string message) => [new(code, "error", message)];

    private static IReadOnlyList<RoundAuditFinding> CommandArgumentsUnavailable() =>
        [new("practice-command-arguments-unavailable", "warning",
            "One or more accepted puzzle actions lack a retained direction or column argument; the state transition replays, but the exact player input cannot be verified.")];

    private static TwentyFortyEightDirection? RequestedDirection(string? json)
    {
        if (json is null) return null;
        using var document = JsonDocument.Parse(json);
        if (!TryProperty(document.RootElement, "direction", out var value) || value.ValueKind != JsonValueKind.String)
            return null;
        return value.GetString()?.Trim().ToLowerInvariant() switch
        {
            "up" => TwentyFortyEightDirection.Up,
            "right" => TwentyFortyEightDirection.Right,
            "down" => TwentyFortyEightDirection.Down,
            "left" => TwentyFortyEightDirection.Left,
            _ => throw new JsonException("Invalid recorded puzzle direction.")
        };
    }

    private static int? RequestedColumn(string? json)
    {
        if (json is null) return null;
        using var document = JsonDocument.Parse(json);
        return TryProperty(document.RootElement, "column", out var value) && value.ValueKind == JsonValueKind.Number
            ? value.GetInt32() : null;
    }

    private static bool TryProperty(JsonElement root, string name, out JsonElement value)
    {
        foreach (var property in root.EnumerateObject())
            if (property.Name.Equals(name, StringComparison.OrdinalIgnoreCase))
            {
                value = property.Value;
                return true;
            }
        value = default;
        return false;
    }

    private sealed record Observation(long Version, string Kind, DateTime OccurredAtUtc, JsonElement Payload, string? RequestJson);
}
