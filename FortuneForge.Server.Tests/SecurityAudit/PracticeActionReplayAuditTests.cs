using System.Text.Json.Nodes;
using FortuneForge.Games.Abstractions.Auditing;
using FortuneForge.Server.Games.DropMerge;
using FortuneForge.Server.Games.TwentyFortyEight;
using FortuneForge.Server.SecurityAudit;
using FortuneForge.Server.Tests.Blackjack.Table;
using Google.Cloud.Firestore;
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
        var sink = await ReopenedRunAsync(game);
        Assert.Equal(2, sink.Completions.Count);
        var capture = sink.Completions[1];
        var events = sink.Events.Where(item => (string)item.Fields["instanceId"] == capture.Summary.RoundId)
            .Select(item => (string)item.Fields["payloadJson"]).ToArray();

        var findings = PracticeActionReplayAudit.Inspect(capture.Summary, capture.PayloadJson, events);
        Assert.DoesNotContain(findings, finding => finding.Severity == "error");
        Assert.Contains(findings, finding => finding.Code == "practice-resumed-episode-prior-history-not-linked");

        var full = sink.Events.Select(item => (string)item.Fields["payloadJson"]).ToArray();
        var verified = PracticeActionReplayAudit.Inspect(capture.Summary, capture.PayloadJson, full, completeHistory: true);
        Assert.DoesNotContain(verified, finding => finding.Severity == "error" || finding.Code == "practice-resumed-episode-prior-history-not-linked");

        var boundary = sink.Events.FindIndex(item => (string)item.Fields["instanceId"] == capture.Summary.RoundId);
        var altered = JsonNode.Parse(full[boundary])!;
        altered["eventKind"] = "moved";
        altered["state"]!["lastEvent"] = "moved";
        full[boundary] = altered.ToJsonString();
        Assert.Contains(PracticeActionReplayAudit.Inspect(capture.Summary, capture.PayloadJson, full, completeHistory: true),
            finding => finding.Code == "practice-action-journal-sequence-mismatch");
    }

    [Fact]
    public async Task TerminalClassificationUsesFirestoreMicrosecondsWithoutChangingEvidence()
    {
        var sink = await ReopenedRunAsync("2048");
        var capture = sink.Completions[0];
        var expectedStart = AtFirestorePrecision(capture.Summary.StartedAtUtc);
        var expectedCompletion = AtFirestorePrecision(capture.Summary.CompletedAtUtc);
        var summary = capture.Summary with { StartedAtUtc = expectedStart, CompletedAtUtc = expectedCompletion };
        var terminal = JsonNode.Parse(capture.PayloadJson)!;
        terminal["startedAtUtc"] = expectedStart.AddTicks(7);
        terminal["completedAtUtc"] = expectedCompletion.AddTicks(9);
        var events = sink.Events.Where(item => (string)item.Fields["instanceId"] == summary.RoundId).Select(item =>
        {
            var observation = JsonNode.Parse((string)item.Fields["payloadJson"])!;
            observation["state"]!["startedAtUtc"] = expectedStart.AddTicks(7);
            if (observation["state"]!["completedAtUtc"] is not null)
                observation["state"]!["completedAtUtc"] = expectedCompletion.AddTicks(9);
            return observation.ToJsonString();
        }).ToArray();

        Assert.DoesNotContain(PracticeActionReplayAudit.Inspect(summary, terminal.ToJsonString(), events, completeHistory: true),
            finding => finding.Severity == "error");
        Assert.Contains(PracticeActionReplayAudit.Inspect(summary with { CompletedAtUtc = expectedCompletion.AddTicks(10) },
            terminal.ToJsonString(), events, completeHistory: true), finding => finding.Code == "practice-action-classification-mismatch");
    }

    internal static DateTime AtFirestorePrecision(DateTime time) => new(time.Ticks / 10 * 10, DateTimeKind.Utc);

    internal static async Task<CaptureSink> ReopenedRunAsync(string game)
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
        return sink;
    }

    internal sealed class CaptureSink : IGameSecurityEventSink
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

[Collection("Blackjack table Firestore emulator")]
public sealed class PuzzleRunJournalFirestoreTests(BlackjackTableFirestoreEmulatorFixture fixture)
{
    [Theory]
    [InlineData("2048")]
    [InlineData("drop-merge")]
    public async Task AuditLoadsPriorEpisodeManifestsAndDistinguishesMissingAndChangedHistory(string game)
    {
        var database = new FirestoreDbBuilder
        {
            ProjectId = $"puzzle-run-journal-{Guid.NewGuid():N}"[..30], Endpoint = fixture.Endpoint,
            ChannelCredentials = Grpc.Core.ChannelCredentials.Insecure,
            EmulatorDetection = Google.Api.Gax.EmulatorDetection.None
        }.Build();
        var run = await PracticeActionReplayAuditTests.ReopenedRunAsync(game);
        var terminal = run.Completions[1];
        var current = terminal.Summary with
        {
            StartedAtUtc = PracticeActionReplayAuditTests.AtFirestorePrecision(terminal.Summary.StartedAtUtc),
            CompletedAtUtc = PracticeActionReplayAuditTests.AtFirestorePrecision(terminal.Summary.CompletedAtUtc)
        };
        var eventCollection = database.Collection(GameSecurityEventDocuments.CollectionName);
        var manifestCollection = database.Collection(GameSecurityEventDocuments.ManifestCollectionName);
        var records = RoundAuditDocuments.Create(terminal);
        await database.Collection(RoundAuditDocuments.Summaries).Document(current.Id).CreateAsync(records.Summary);
        await database.Collection(RoundAuditDocuments.Evidence).Document(current.Id).CreateAsync(records.Evidence);

        foreach (var grouping in run.Events.GroupBy(observation => (string)observation.Fields["instanceId"]))
        {
            var ids = grouping.Select(observation => observation.Id).ToArray();
            await manifestCollection.Document(GameSecurityEventDocuments.ManifestId(game, grouping.Key)).CreateAsync(
                new Dictionary<string, object> { ["schemaVersion"] = 1L, ["gameId"] = game, ["instanceId"] = grouping.Key, ["eventIds"] = ids });
            foreach (var chunk in grouping.Chunk(250))
            {
                var batch = database.StartBatch();
                foreach (var observation in chunk) batch.Create(eventCollection.Document(observation.Id), observation.Fields);
                await batch.CommitAsync();
            }
        }

        var complete = await GameSecurityIntegrityAuditor.InspectAsync(database, current, default);
        Assert.DoesNotContain(complete.Findings, finding => finding.Severity == "error" ||
            finding.Code == "practice-resumed-episode-prior-history-not-linked");

        var first = run.Events[0];
        await eventCollection.Document(first.Id).UpdateAsync("payloadJson", "{}");
        var corrupt = await GameSecurityIntegrityAuditor.InspectAsync(database, current, default);
        Assert.Contains(corrupt.Findings, finding => finding.Code == "engine-journal-changed" && finding.Message.Contains("episode 1", StringComparison.Ordinal));

        var firstEpisodeId = run.Completions[0].Summary.RoundId;
        await manifestCollection.Document(GameSecurityEventDocuments.ManifestId(game, firstEpisodeId)).DeleteAsync();
        var missing = await GameSecurityIntegrityAuditor.InspectAsync(database, current, default);
        Assert.Contains(missing.Findings, finding => finding.Code == "engine-journal-manifest-missing" && finding.Message.Contains("episode 1", StringComparison.Ordinal));

        var predecessor = run.Completions[0];
        var retained = RoundAuditDocuments.Create(predecessor).Summary;
        retained["retentionState"] = "deleted";
        retained["evidencePresent"] = false;
        await database.Collection(RoundAuditDocuments.Summaries).Document(predecessor.Summary.Id).CreateAsync(retained);
        var expired = await GameSecurityIntegrityAuditor.InspectAsync(database, current, default);
        Assert.Contains(expired.Findings, finding => finding.Code == "practice-action-prior-episode-expired" && finding.Severity == "warning");
        Assert.DoesNotContain(expired.Findings, finding => finding.Severity == "error");
    }
}
