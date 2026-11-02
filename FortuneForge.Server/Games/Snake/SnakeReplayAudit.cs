using System.Text.Json;
using FortuneForge.Games.Abstractions.Auditing;
using FortuneForge.Games.Snake;

namespace FortuneForge.Server.Games.Snake;

/// <summary>Deterministic replay of the private Snake input trace from its saved start seed.</summary>
internal static class SnakeReplayAudit
{
    internal const string Version = "snake-engine-replay-v1";
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    internal static IReadOnlyList<RoundAuditFinding> Inspect(RoundAuditSummary summary, string payloadJson)
    {
        if (summary.GameId != "snake") return [];
        try
        {
            using var json = JsonDocument.Parse(payloadJson);
            var root = json.RootElement;
            var state = root.GetProperty("state").Deserialize<SnakeState>(Json)
                ?? throw new JsonException("Snake state missing.");
            var actions = root.GetProperty("actions").Deserialize<string[]>(Json)
                ?? throw new JsonException("Snake action trace missing.");
            var initialBest = root.GetProperty("initialBestScore").GetInt32();
            var abandoned = root.GetProperty("abandoned").GetBoolean();
            if (state.Width != state.Height || state.Width is not (16 or 20 or 24) ||
                root.GetProperty("scoreClassification").GetString() !=
                    FirestoreSnakeGameService.ScoreClassification(state.Width))
                return [new("snake-score-classification-invalid", "error",
                    "The Snake board or score class does not match a supported hosted format.")];
            if (root.GetProperty("runId").GetGuid().ToString("N") != summary.RoundId ||
                root.GetProperty("gameId").GetGuid().ToString("N") != summary.TableId)
                return [new("snake-instance-mismatch", "error", "The Snake private run identifies another instance or game table.")];
            return Check(state, initialBest, actions, abandoned);
        }
        catch (Exception exception) when (exception is JsonException or KeyNotFoundException or InvalidOperationException or FormatException or ArgumentException)
        {
            return [new("snake-evidence-invalid", "error", "The private Snake state or action trace could not be replayed.")];
        }
    }

    internal static IReadOnlyList<RoundAuditFinding> Check(SnakeState recorded, int initialBest,
        IReadOnlyList<string> actions, bool abandoned)
    {
        if (actions.Count > 10_001)
            return [new("snake-action-limit", "error", "The Snake action trace exceeded the supported audit limit.")];
        try
        {
            var replay = SnakeEngine.Start(recorded.Seed, recorded.Width, recorded.Height, initialBest);
            for (var index = 0; index < actions.Count; index++)
            {
                var action = actions[index];
                if (action == "tick") replay = SnakeEngine.Tick(replay).State;
                else if (action == "abandon" && abandoned && index == actions.Count - 1) continue;
                else if (action.StartsWith("turn:", StringComparison.Ordinal) &&
                    Enum.TryParse<SnakeDirection>(action.AsSpan(5), true, out var direction) && Enum.IsDefined(direction))
                    replay = SnakeEngine.Turn(replay, direction).State;
                else return [new("snake-action-invalid", "error", "The Snake action trace contains an unsupported input.")];
            }
            if (abandoned && recorded.Phase != SnakePhase.Playing ||
                !abandoned && recorded.Phase == SnakePhase.Playing)
                return [new("snake-terminal-phase-invalid", "error", "The saved Snake completion has an invalid phase.")];
            if (replay.Width != recorded.Width || replay.Height != recorded.Height || replay.Seed != recorded.Seed ||
                replay.RandomState != recorded.RandomState || !replay.Body.SequenceEqual(recorded.Body) ||
                replay.Direction != recorded.Direction || replay.Food != recorded.Food ||
                replay.Score != recorded.Score || replay.BestScore != recorded.BestScore ||
                replay.Moves != recorded.Moves || replay.Phase != recorded.Phase)
                return [new("snake-replay-state-mismatch", "error", "The saved Snake result differs from deterministic replay of its accepted actions.")];
            return abandoned
                ? [new("snake-run-abandoned", "warning", "The player reset an unfinished Snake run; its action trace and last state are retained.")]
                : [];
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException or OverflowException)
        {
            return [new("snake-replay-failed", "error", "The Snake engine rejected the saved action sequence.")];
        }
    }
}
