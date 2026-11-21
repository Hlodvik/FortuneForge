using System.Text.Json;
using System.Text.Json.Nodes;
using FortuneForge.Games.Abstractions.Auditing;
using Xunit;

namespace FortuneForge.Games.TwentyFortyEight.Tests;

public sealed class TwentyFortyEightReplayTests
{
    [Fact]
    public void RecordedRunReplaysAndLegacyMissingInputsAreReportedWithoutRejectingLegalBoards()
    {
        var observations = CompletedRun();
        Assert.Empty(TwentyFortyEightActionReplay.Inspect(observations, 1));
        var legacy = observations.Select(value => value with { RequestJson = null }).ToArray();
        var finding = Assert.Single(TwentyFortyEightActionReplay.Inspect(legacy, 1));
        Assert.Equal("practice-command-arguments-unavailable", finding.Code);
        Assert.Equal("warning", finding.Severity);
    }

    [Fact]
    public void ReplayRejectsAChangedTerminalScoreEvenWhenItsTilesAreUnchanged()
    {
        var observations = CompletedRun();
        var last = observations[^1];
        var payload = JsonNode.Parse(last.Payload.GetRawText())!;
        payload["state"]!["score"] = payload["state"]!["score"]!.GetValue<int>() + 4;
        observations[^1] = last with { Payload = JsonSerializer.SerializeToElement(payload) };
        Assert.Contains(TwentyFortyEightActionReplay.Inspect(observations, 1),
            finding => finding.Code == "2048-move-mismatch" && finding.Severity == "error");
    }

    [Fact]
    public void FullRunReplayPreservesUndoHistoryAcrossCompletionEpisodes()
    {
        var observations = CompletedRun(reopen: true);
        Assert.Equal(2, observations[^1].Payload.GetProperty("completionEpisode").GetInt32());
        Assert.Empty(TwentyFortyEightActionReplay.InspectCompleteRun(observations));
    }

    private static List<PuzzleActionObservation> CompletedRun(bool reopen = false)
    {
        var now = DateTime.UtcNow;
        var run = TwentyFortyEightSession.Start(Guid.NewGuid(), Guid.NewGuid(), "alice", TwentyFortyEightEngine.Start(32), now);
        var observations = new List<PuzzleActionObservation> { Observe(run, null) };
        var history = new Stack<TwentyFortyEightState>();
        var acceptedDirections = new List<TwentyFortyEightDirection>();
        var directions = new[] { TwentyFortyEightDirection.Up, TwentyFortyEightDirection.Right,
            TwentyFortyEightDirection.Down, TwentyFortyEightDirection.Left };
        for (var move = 0; move < 200 && !run.IsTerminal; move++)
        {
            var direction = directions[move % directions.Length];
            var before = run;
            run = run.Move(TwentyFortyEightEngine.Move(run.State, direction), now.AddMilliseconds(move + 1));
            if (run.UndoDepth > before.UndoDepth)
            {
                history.Push(before.State);
                acceptedDirections.Add(direction);
            }
            observations.Add(Observe(run, JsonSerializer.Serialize(new { direction = direction.ToString() })));
        }
        Assert.True(run.IsTerminal);
        if (reopen)
        {
            foreach (var _ in Enumerable.Range(0, 2))
            {
                run = run.Undo(history.Pop(), now.AddSeconds(1));
                observations.Add(Observe(run, null));
            }
            foreach (var direction in acceptedDirections.TakeLast(2))
            {
                run = run.Move(TwentyFortyEightEngine.Move(run.State, direction), now.AddSeconds(2));
                observations.Add(Observe(run, JsonSerializer.Serialize(new { direction = direction.ToString() })));
            }
            Assert.True(run.IsTerminal);
        }
        return observations;
    }

    private static PuzzleActionObservation Observe(TwentyFortyEightSession run, string? request)
    {
        var (observation, _) = TwentyFortyEightAudit.Capture(run, run.StartedAtUtc);
        using var document = JsonDocument.Parse((string)observation.Fields["payloadJson"]);
        return new(run.EventVersion, run.LastEvent, run.StartedAtUtc,
            document.RootElement.GetProperty("state").Clone(), request);
    }
}
