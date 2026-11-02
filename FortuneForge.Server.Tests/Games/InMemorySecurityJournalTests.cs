using System.Text.Json;
using FortuneForge.Games.Abstractions.Auditing;
using FortuneForge.Server.Games.DropMerge;
using FortuneForge.Server.Games.Craps;
using FortuneForge.Server.Games.Roulette;
using FortuneForge.Server.Matchmaking;
using FortuneForge.Server.Games.TwentyFortyEight;
using FortuneForge.Server.SecurityAudit;
using Xunit;

namespace FortuneForge.Server.Tests.Games;

public sealed class InMemorySecurityJournalTests
{
    [Theory]
    [InlineData("2048")]
    [InlineData("drop-merge")]
    public async Task Accepted_commands_have_acknowledged_private_engine_observations(string game)
    {
        var sink = new RecordingGameSecuritySink();
        var now = DateTime.UtcNow;
        using var request = GameSecurityContext.Push(new GameRequestObservation(
            "request-1", "alice", "session-hash", "203.0.113.7", now, "POST", "/api/games/" + game,
            game, null, "command-1", null, "{}", "sanitized", "direct"));
        if (game == "2048")
        {
            var service = new TwentyFortyEightGameService(securityEvents: sink);
            var start = await service.StartAuditedAsync("alice", 73);
            await service.MoveAuditedAsync("alice", start.GameId, "left");
        }
        else
        {
            var service = new DropMergeGameService(securityEvents: sink);
            var start = await service.StartAuditedAsync("alice", 73);
            await service.DropAuditedAsync("alice", start.GameId, 0);
        }
        Assert.Equal(2, sink.Events.Count);
        Assert.Equal(new long[] { 0, 1 }, sink.Events.Select(item => Assert.IsType<long>(item.Fields["version"])));
        Assert.All(sink.Events, item =>
        {
            Assert.Equal(game, item.Fields["gameId"]);
            Assert.Equal("request-1", item.Fields["requestId"]);
            Assert.Equal(new[] { "alice" }, item.Fields["participantIds"]);
            Assert.InRange(Assert.IsType<DateTime>(item.Fields["occurredAtUtc"]), now, DateTime.UtcNow);
            using var payload = JsonDocument.Parse(Assert.IsType<string>(item.Fields["payloadJson"]));
            Assert.Equal("command-1", payload.RootElement.GetProperty("request").GetProperty("commandId").GetString());
            Assert.True(payload.RootElement.GetProperty("state").GetProperty("state").ValueKind == JsonValueKind.Object);
        });
    }

    [Fact]
    public async Task Failed_start_evidence_write_prevents_exposing_a_2048_session()
    {
        var sink = new RecordingGameSecuritySink { Fail = true };
        var service = new TwentyFortyEightGameService(securityEvents: sink);
        await Assert.ThrowsAsync<GameSecurityRecordingException>(() => service.StartAuditedAsync("alice", 7));
        Assert.Empty(sink.Events);
    }

    [Fact]
    public async Task Craps_records_the_wager_and_each_authoritative_dice_roll()
    {
        var sink = new RecordingGameSecuritySink();
        var service = new CrapsGameService(securityEvents: sink);
        var round = await service.StartAuditedAsync("alice", new(2));
        for (var roll = 0; roll < 1000 && round.Phase != "resolved"; roll++)
            round = await service.RollAuditedAsync("alice", round.RoundId);
        Assert.Equal("resolved", round.Phase);
        Assert.True(sink.Events.Count >= 2);
        Assert.Single(sink.Completions);
        Assert.Equal("passed", sink.Completions[0].Summary.AuditStatus);
        Assert.Equal(PracticeOutcomeAudit.CrapsVersion, sink.Completions[0].Summary.AuditorVersion);
        Assert.Equal("started", sink.Events[0].Fields["eventKind"]);
        Assert.All(sink.Events.Skip(1), item => Assert.Equal("rolled", item.Fields["eventKind"]));
        Assert.Equal(Enumerable.Range(0, sink.Events.Count).Select(number => (long)number),
            sink.Events.Select(item => Assert.IsType<long>(item.Fields["version"])));
        using var final = JsonDocument.Parse(Assert.IsType<string>(sink.Events[^1].Fields["payloadJson"]));
        Assert.True(final.RootElement.GetProperty("state").GetProperty("state").GetProperty("phase").ValueKind is JsonValueKind.Number or JsonValueKind.String);
        Assert.True(final.RootElement.GetProperty("state").GetProperty("state").GetProperty("rolls").GetArrayLength() >= 1);
    }

    [Fact]
    public async Task Roulette_completion_preserves_the_whole_shared_virtual_credit_round()
    {
        var sink = new RecordingGameSecuritySink();
        var service = new RouletteGameService(new MultiplayerMatchmaker(), securityEvents: sink);
        var alice = await service.StartAuditedAsync("alice");
        var bob = await service.StartAuditedAsync("bob");
        Assert.Equal(alice.RoundId, bob.RoundId);
        await service.PlaceBetAuditedAsync("alice", alice.RoundId, new PlaceRouletteBetRequest("red", 10m, null, []));
        await service.PlaceBetAuditedAsync("bob", bob.RoundId, new PlaceRouletteBetRequest("black", 5m, null, []));
        await service.SpinAuditedAsync("alice", alice.RoundId);
        var completion = Assert.Single(sink.Completions);
        Assert.Equal("passed", completion.Summary.AuditStatus);
        Assert.Equal(PracticeOutcomeAudit.RouletteVersion, completion.Summary.AuditorVersion);
        Assert.Equal(new[] { "alice", "bob" }, completion.Summary.Participants.Select(item => item.ActorId).Order(StringComparer.Ordinal));
        using var evidence = JsonDocument.Parse(completion.PayloadJson);
        Assert.Equal(2, evidence.RootElement.GetProperty("state").GetProperty("bets").GetArrayLength());
        Assert.Equal(5, sink.Events.Count);
    }

    [Theory]
    [InlineData("2048")]
    [InlineData("drop-merge")]
    public async Task Puzzle_terminal_receipt_is_passed_by_named_outcome_check(string game)
    {
        var sink = new RecordingGameSecuritySink();
        if (game == "2048")
        {
            var service = new TwentyFortyEightGameService(securityEvents: sink);
            var round = await service.StartAuditedAsync("alice", 32);
            var directions = new[] { "up", "right", "down", "left" };
            for (var move = 0; move < 20_000 && round.Phase == "playing"; move++)
                round = await service.MoveAuditedAsync("alice", round.GameId, directions[move % directions.Length]);
            Assert.NotEqual("playing", round.Phase);
        }
        else
        {
            var service = new DropMergeGameService(securityEvents: sink);
            var round = await service.StartAuditedAsync("alice", 41);
            for (var move = 0; move < 20_000 && round.Phase == "playing"; move++)
                round = await service.DropAuditedAsync("alice", round.GameId, move % round.Columns);
            Assert.Equal("lost", round.Phase);
        }
        var completion = Assert.Single(sink.Completions);
        Assert.Equal(game, completion.Summary.GameId);
        Assert.Equal("passed", completion.Summary.AuditStatus);
        Assert.DoesNotContain(PracticeRoundReplayAudit.Inspect(completion.Summary, completion.PayloadJson),
            finding => finding.Severity == "error");
    }

    [Fact]
    public async Task Undo_after_drop_merge_loss_opens_a_new_auditable_completion_episode()
    {
        var sink = new RecordingGameSecuritySink();
        var service = new DropMergeGameService(securityEvents: sink);
        var round = await service.StartAuditedAsync("alice", 41);
        var lastColumn = -1;
        for (var move = 0; move < 20_000 && round.Phase == "playing"; move++)
        {
            lastColumn = move % round.Columns;
            round = await service.DropAuditedAsync("alice", round.GameId, lastColumn);
        }
        Assert.Equal("lost", round.Phase);
        var first = Assert.Single(sink.Completions).Summary;
        round = await service.UndoAuditedAsync("alice", round.GameId);
        Assert.Equal("playing", round.Phase);
        round = await service.DropAuditedAsync("alice", round.GameId, lastColumn);
        Assert.Equal("lost", round.Phase);
        var second = sink.Completions[^1].Summary;
        Assert.NotEqual(first.Id, second.Id);
        Assert.NotEqual(first.RoundId, second.RoundId);
        Assert.Equal(1, first.RoundNumber);
        Assert.Equal(2, second.RoundNumber);
        Assert.Equal(second.RoundId, sink.Events[^1].Fields["instanceId"]);
        Assert.Equal(second.RoundId, sink.Events[^2].Fields["instanceId"]);
        Assert.Equal("passed", second.AuditStatus);
    }

    [Fact]
    public async Task Undo_after_2048_terminal_state_opens_a_new_auditable_completion_episode()
    {
        var sink = new RecordingGameSecuritySink();
        var service = new TwentyFortyEightGameService(securityEvents: sink);
        var round = await service.StartAuditedAsync("alice", 32);
        var directions = new[] { "up", "right", "down", "left" };
        var lastDirection = "";
        for (var move = 0; move < 20_000 && round.Phase == "playing"; move++)
        {
            lastDirection = directions[move % directions.Length];
            round = await service.MoveAuditedAsync("alice", round.GameId, lastDirection);
        }
        Assert.NotEqual("playing", round.Phase);
        var first = Assert.Single(sink.Completions).Summary;
        round = await service.UndoAuditedAsync("alice", round.GameId);
        Assert.Equal("playing", round.Phase);
        round = await service.MoveAuditedAsync("alice", round.GameId, lastDirection);
        Assert.NotEqual("playing", round.Phase);
        var second = sink.Completions[^1].Summary;
        Assert.NotEqual(first.Id, second.Id);
        Assert.NotEqual(first.RoundId, second.RoundId);
        Assert.Equal(1, first.RoundNumber);
        Assert.Equal(2, second.RoundNumber);
        Assert.Equal(second.RoundId, sink.Events[^1].Fields["instanceId"]);
        Assert.Equal(second.RoundId, sink.Events[^2].Fields["instanceId"]);
        Assert.Equal("passed", second.AuditStatus);
    }

    private sealed class RecordingGameSecuritySink : IGameSecurityEventSink
    {
        public List<GameSecurityEventDocument> Events { get; } = [];
        public List<RoundAuditCapture> Completions { get; } = [];
        public bool Fail { get; set; }
        public Task WriteAsync(GameSecurityEventDocument observation, CancellationToken cancellationToken = default)
        {
            if (Fail) throw new IOException("Unavailable");
            Events.Add(observation);
            return Task.CompletedTask;
        }
        public Task WriteCompletedAsync(GameSecurityEventDocument observation, RoundAuditCapture capture,
            CancellationToken cancellationToken = default)
        {
            if (Fail) throw new IOException("Unavailable");
            Events.Add(observation);
            Completions.Add(capture);
            return Task.CompletedTask;
        }
    }
}
