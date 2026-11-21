using System.Text.Json;
using System.Text.Json.Nodes;
using FortuneForge.Games.Abstractions.Auditing;
using Xunit;

namespace FortuneForge.Games.DropMerge.Tests;

public sealed class DropMergeReplayTests
{
    [Fact]
    public void RecordedRunReplaysItsQueueMergesAndLossWithLegacyInputCompatibility()
    {
        var observations = CompletedRun();
        Assert.Empty(DropMergeActionReplay.Inspect(observations, 1));
        var finding = Assert.Single(DropMergeActionReplay.Inspect(
            observations.Select(value => value with { RequestJson = null }).ToArray(), 1));
        Assert.Equal("practice-command-arguments-unavailable", finding.Code);
        Assert.Equal("warning", finding.Severity);
    }

    [Fact]
    public void ReplayRejectsChangedTileQueueAndChangedRecordedColumn()
    {
        var observations = CompletedRun();
        var last = observations[^1];
        var payload = JsonNode.Parse(last.Payload.GetRawText())!;
        payload["state"]!["nextTile"] = payload["state"]!["nextTile"]!.GetValue<int>() * 2;
        observations[^1] = last with { Payload = JsonSerializer.SerializeToElement(payload) };
        Assert.Contains(DropMergeActionReplay.Inspect(observations, 1),
            finding => finding.Code == "drop-merge-drop-mismatch" && finding.Severity == "error");
        observations[^1] = last with { RequestJson = "{\"column\":99}" };
        Assert.Contains(DropMergeActionReplay.Inspect(observations, 1),
            finding => finding.Code == "drop-merge-request-column-mismatch" && finding.Severity == "error");
    }

    [Fact]
    public void FullRunReplayPreservesQueueAndUndoHistoryAcrossCompletionEpisodes()
    {
        var observations = CompletedRun(reopen: true);
        Assert.Equal(2, observations[^1].Payload.GetProperty("completionEpisode").GetInt32());
        Assert.Empty(DropMergeActionReplay.InspectCompleteRun(observations));
    }

    private static List<PuzzleActionObservation> CompletedRun(bool reopen = false)
    {
        var now = DateTime.UtcNow;
        var run = DropMergeSession.Start(Guid.NewGuid(), Guid.NewGuid(), "alice", DropMergeEngine.Start(41), now);
        var observations = new List<PuzzleActionObservation> { Observe(run, null) };
        var history = new Stack<DropMergeState>();
        var acceptedColumns = new List<int>();
        for (var move = 0; move < 500 && run.State.Phase == DropMergePhase.Playing; move++)
        {
            var column = move % run.State.Columns;
            history.Push(run.State);
            acceptedColumns.Add(column);
            run = run.Drop(DropMergeEngine.Drop(run.State, column), now.AddMilliseconds(move + 1));
            observations.Add(Observe(run, JsonSerializer.Serialize(new { column })));
        }
        Assert.Equal(DropMergePhase.Lost, run.State.Phase);
        if (reopen)
        {
            foreach (var _ in Enumerable.Range(0, 2))
            {
                run = run.Undo(history.Pop());
                observations.Add(Observe(run, null));
            }
            foreach (var column in acceptedColumns.TakeLast(2))
            {
                run = run.Drop(DropMergeEngine.Drop(run.State, column), now.AddSeconds(2));
                observations.Add(Observe(run, JsonSerializer.Serialize(new { column })));
            }
            Assert.Equal(DropMergePhase.Lost, run.State.Phase);
        }
        return observations;
    }

    private static PuzzleActionObservation Observe(DropMergeSession run, string? request)
    {
        var (observation, _) = DropMergeAudit.Capture(run, run.StartedAtUtc);
        using var document = JsonDocument.Parse((string)observation.Fields["payloadJson"]);
        return new(run.EventVersion, run.LastEvent, run.StartedAtUtc,
            document.RootElement.GetProperty("state").Clone(), request);
    }
}
