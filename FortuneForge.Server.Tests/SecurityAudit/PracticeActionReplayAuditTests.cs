using System.Text.Json.Nodes;
using FortuneForge.Games.Abstractions.Auditing;
using FortuneForge.Server.Games.DropMerge;
using FortuneForge.Server.Games.TwentyFortyEight;
using FortuneForge.Server.SecurityAudit;
using Xunit;

namespace FortuneForge.Server.Tests.SecurityAudit;

public sealed class PracticeActionReplayAuditTests
{
    [Theory]
    [InlineData("2048")]
    [InlineData("drop-merge")]
    public async Task Real_puzzle_run_replays_from_seed_through_each_command_and_detects_intermediate_change(string game)
    {
        var sink = new CaptureSink();
        if (game == "2048")
        {
            var service = new TwentyFortyEightGameService(securityEvents: sink);
            var run = await service.StartAuditedAsync("alice", 32);
            string[] directions = ["up", "right", "down", "left"];
            for (var step = 0; step < 20_000 && run.Phase == "playing"; step++)
                run = await service.MoveAuditedAsync("alice", run.GameId, directions[step % directions.Length]);
            Assert.NotEqual("playing", run.Phase);
        }
        else
        {
            var service = new DropMergeGameService(securityEvents: sink);
            var run = await service.StartAuditedAsync("alice", 41);
            for (var step = 0; step < 20_000 && run.Phase == "playing"; step++)
                run = await service.DropAuditedAsync("alice", run.GameId, step % run.Columns);
            Assert.Equal("lost", run.Phase);
        }
        var capture = Assert.Single(sink.Completions);
        var events = sink.Events.Where(item => (string)item.Fields["instanceId"] == capture.Summary.RoundId)
            .Select(item => (string)item.Fields["payloadJson"]).ToArray();
        Assert.True(events.Length > 2);

        var clean = PracticeActionReplayAudit.Inspect(capture.Summary, capture.PayloadJson, events);
        Assert.DoesNotContain(clean, finding => finding.Severity == "error");
        Assert.Contains(clean, finding => finding.Code == "practice-in-memory-state-journal-non-atomic");
        Assert.Contains(clean, finding => finding.Code == "practice-command-arguments-unavailable" &&
            finding.Severity == "warning");

        var withCommands = events.Select((payload, index) =>
        {
            if (index == 0) return payload;
            var envelope = JsonNode.Parse(payload)!;
            var command = game == "2048"
                ? new JsonObject { ["direction"] = new[] { "up", "right", "down", "left" }[(index - 1) % 4] }
                : new JsonObject { ["column"] = (index - 1) % 7 };
            envelope["request"] = new JsonObject { ["requestJson"] = command.ToJsonString() };
            return envelope.ToJsonString();
        }).ToArray();
        var complete = PracticeActionReplayAudit.Inspect(capture.Summary, capture.PayloadJson, withCommands);
        Assert.DoesNotContain(complete, finding => finding.Severity == "error" ||
            finding.Code == "practice-command-arguments-unavailable");

        var changed = events.ToArray();
        var item = JsonNode.Parse(changed[1])!;
        item["state"]!["state"]!["score"] = 9_999_999;
        changed[1] = item.ToJsonString();
        var findings = PracticeActionReplayAudit.Inspect(capture.Summary, capture.PayloadJson, changed);
        Assert.Contains(findings, finding => finding.Severity == "error");
    }

    [Theory]
    [InlineData("2048")]
    [InlineData("drop-merge")]
    public async Task Reopened_completion_episode_is_checked_from_its_first_snapshot_and_marked_partial(string game)
    {
        var sink = new CaptureSink();
        if (game == "2048")
        {
            var service = new TwentyFortyEightGameService(securityEvents: sink);
            var run = await service.StartAuditedAsync("alice", 32);
            string[] directions = ["up", "right", "down", "left"];
            var last = "";
            for (var step = 0; step < 20_000 && run.Phase == "playing"; step++)
            {
                last = directions[step % directions.Length];
                run = await service.MoveAuditedAsync("alice", run.GameId, last);
            }
            Assert.NotEqual("playing", run.Phase);
            run = await service.UndoAuditedAsync("alice", run.GameId);
            run = await service.MoveAuditedAsync("alice", run.GameId, last);
            Assert.NotEqual("playing", run.Phase);
        }
        else
        {
            var service = new DropMergeGameService(securityEvents: sink);
            var run = await service.StartAuditedAsync("alice", 41);
            var last = -1;
            for (var step = 0; step < 20_000 && run.Phase == "playing"; step++)
            {
                last = step % run.Columns;
                run = await service.DropAuditedAsync("alice", run.GameId, last);
            }
            Assert.Equal("lost", run.Phase);
            run = await service.UndoAuditedAsync("alice", run.GameId);
            run = await service.DropAuditedAsync("alice", run.GameId, last);
            Assert.Equal("lost", run.Phase);
        }
        Assert.Equal(2, sink.Completions.Count);
        var capture = sink.Completions[1];
        var events = sink.Events.Where(item => (string)item.Fields["instanceId"] == capture.Summary.RoundId)
            .Select(item => (string)item.Fields["payloadJson"]).ToArray();

        var findings = PracticeActionReplayAudit.Inspect(capture.Summary, capture.PayloadJson, events);
        Assert.DoesNotContain(findings, finding => finding.Severity == "error");
        Assert.Contains(findings, finding => finding.Code == "practice-resumed-episode-prior-history-not-linked");
    }

    private sealed class CaptureSink : IGameSecurityEventSink
    {
        public List<GameSecurityEventDocument> Events { get; } = [];
        public List<RoundAuditCapture> Completions { get; } = [];
        public Task WriteAsync(GameSecurityEventDocument observation, CancellationToken cancellationToken = default)
        {
            Events.Add(observation);
            return Task.CompletedTask;
        }

        public Task WriteCompletedAsync(GameSecurityEventDocument observation, RoundAuditCapture capture,
            CancellationToken cancellationToken = default)
        {
            Events.Add(observation);
            Completions.Add(capture);
            return Task.CompletedTask;
        }
    }
}
