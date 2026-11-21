using System.Collections.Immutable;
using System.Text.Json;
using FortuneForge.Games.Abstractions.Auditing;
using static FortuneForge.Games.Abstractions.Auditing.PuzzleReplayEvidence;

namespace FortuneForge.Games.TwentyFortyEight;

/// <summary>Independently verifies recorded actions against the game rules.</summary>
public static class TwentyFortyEightActionReplay
{
    /// <summary>Replays a run from its original start across completion, undo, and continue episodes.</summary>
    public static IReadOnlyList<RoundAuditFinding> InspectCompleteRun(IReadOnlyList<PuzzleActionObservation> observations) =>
        Inspect(observations, 1);

    public static IReadOnlyList<RoundAuditFinding> Inspect(IReadOnlyList<PuzzleActionObservation> observations, int episode)
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
}
