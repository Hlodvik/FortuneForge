using FortuneForge.Games.Abstractions.Analytics;
using FortuneForge.Server.Activity;
using FortuneForge.Server.Games.Craps;
using FortuneForge.Server.Games.DropMerge;
using FortuneForge.Server.Games.Roulette;
using FortuneForge.Server.Games.TwentyFortyEight;
using FortuneForge.Server.Matchmaking;
using Xunit;

namespace FortuneForge.Server.Tests.Games;

internal sealed class RecordingSettlementSink : IGameAnalyticsSink
{
    public List<GameAnalyticsSettlement> Events { get; } = [];
    public List<GameAnalyticsSettlement> Attempts { get; } = [];
    public int FailuresRemaining { get; set; }
    public Task RecordAsync(GameAnalyticsSettlement settlement, CancellationToken ct) { Events.Add(settlement); return Task.CompletedTask; }
    public bool TryEnqueuePractice(GameAnalyticsSettlement settlement)
    {
        Attempts.Add(settlement);
        if (FailuresRemaining > 0) { FailuresRemaining--; return false; }
        Events.Add(settlement); return true;
    }
}

public sealed class GameCompletionAnalyticsTests
{
    [Theory]
    [InlineData("craps", false)]
    [InlineData("2048", false)]
    [InlineData("drop-merge", false)]
    [InlineData("2048", true)]
    [InlineData("drop-merge", true)]
    public async Task Queue_retry_keeps_the_original_terminal_timestamp(string game, bool retryBeforeReset)
    {
        var sink = new RecordingSettlementSink { FailuresRemaining = 1 };
        Action retry;
        if (game == "craps")
        {
            var service = new CrapsGameService(sink);
            var round = service.Start("alice", new(2));
            for (var move = 0; move < 1000 && round.Phase != "resolved"; move++) round = service.Roll("alice", round.RoundId);
            Assert.Equal("resolved", round.Phase);
            retry = () => service.Get("alice", round.RoundId);
        }
        else if (game == "2048")
        {
            var service = new TwentyFortyEightGameService(sink);
            var round = service.Start("alice", 32); var directions = new[] { "up", "right", "down", "left" };
            for (var move = 0; move < 20000 && round.Phase == "playing"; move++) round = service.Move("alice", round.GameId, directions[move % 4]);
            Assert.NotEqual("playing", round.Phase);
            retry = retryBeforeReset
                ? () => { service.Reset("alice", round.GameId, 32); Assert.Equal("playing", service.Get("alice", round.GameId).Phase); }
                : () => Assert.Equal(round, service.Get("alice", round.GameId));
        }
        else
        {
            var service = new DropMergeGameService(sink);
            var round = service.Start("alice", 41);
            for (var move = 0; move < 20000 && round.Phase == "playing"; move++) round = service.Drop("alice", round.GameId, move % round.Columns);
            Assert.Equal("lost", round.Phase);
            retry = retryBeforeReset
                ? () => { service.Reset("alice", round.GameId, 41); Assert.Equal("playing", service.Get("alice", round.GameId).Phase); }
                : () => Assert.Equal(round, service.Get("alice", round.GameId));
        }
        var terminalAttempt = Assert.Single(sink.Attempts);
        Assert.Empty(sink.Events);
        await Task.Delay(20);
        retry(); retry();
        var recorded = Assert.Single(sink.Events);
        Assert.Equal(terminalAttempt.CompletedAtUtc, recorded.CompletedAtUtc);
        Assert.Equal(terminalAttempt.RoundId, recorded.RoundId);
        Assert.Equal(2, sink.Attempts.Count);
    }

    [Fact]
    public void Craps_records_only_a_completed_practice_round_once_and_never_its_read_or_start()
    {
        var sink = new RecordingSettlementSink(); var service = new CrapsGameService(sink);
        var round = service.Start("alice", new(2));
        service.Get("alice", round.RoundId); Assert.Empty(sink.Events);
        for (var roll = 0; roll < 1000 && round.Phase != "resolved"; roll++) round = service.Roll("alice", round.RoundId);
        Assert.Equal("resolved", round.Phase);
        service.Get("alice", round.RoundId); service.Get("alice", round.RoundId);
        var recorded = Assert.Single(sink.Events);
        Assert.Equal("craps", recorded.GameId); Assert.Equal("practice", recorded.Mode);
        Assert.Equal("alice", recorded.UserId); Assert.Equal(0, recorded.WagerCents); Assert.Equal(0, recorded.PayoutCents);
        Assert.NotNull(GameAnalyticsOutbox.Create(recorded));
    }

    [Fact]
    public void Roulette_records_all_actual_human_bettors_excludes_removed_bets_and_uses_new_round_identity()
    {
        var sink = new RecordingSettlementSink(); var service = new RouletteGameService(new MultiplayerMatchmaker(), analytics: sink);
        var round = service.Start("alice"); service.Start("bob"); service.Start("carol");
        service.PlaceBet("alice", round.RoundId, new("red", 2, null, []));
        service.PlaceBet("bob", round.RoundId, new("black", 2, null, []));
        service.PlaceBet("carol", round.RoundId, new("red", 2, null, [])); service.ClearBets("carol", round.RoundId);
        Assert.Empty(sink.Events);
        service.Spin("alice", round.RoundId); service.Get("bob", round.RoundId);
        Assert.Equal(["alice", "bob"], sink.Events.Select(value => value.UserId).Order().ToArray());
        Assert.All(sink.Events, value => Assert.Equal("practice", value.Mode));
        service.Start("alice"); service.PlaceBet("alice", round.RoundId, new("red", 2, null, [])); service.Spin("alice", round.RoundId);
        Assert.Equal(3, sink.Events.Count);
        Assert.NotEqual(sink.Events[0].RoundId, sink.Events[2].RoundId);
    }

    [Fact]
    public void Finished_2048_run_records_once_even_after_undo_and_replay_and_reset_uses_new_identity()
    {
        var sink = new RecordingSettlementSink(); var service = new TwentyFortyEightGameService(sink);
        var round = service.Start("alice", 32); var directions = new[] { "up", "right", "down", "left" };
        Assert.Empty(sink.Events);
        for (var move = 0; move < 20000 && round.Phase == "playing"; move++) round = service.Move("alice", round.GameId, directions[move % 4]);
        Assert.NotEqual("playing", round.Phase); Assert.Single(sink.Events);
        service.Undo("alice", round.GameId);
        for (var move = 0; move < 100; move++) { round = service.Move("alice", round.GameId, directions[move % 4]); if (round.Phase != "playing") break; }
        Assert.Single(sink.Events);
        round = service.Reset("alice", round.GameId, 32);
        for (var move = 0; move < 20000 && round.Phase == "playing"; move++) round = service.Move("alice", round.GameId, directions[move % 4]);
        Assert.Equal(2, sink.Events.Count); Assert.NotEqual(sink.Events[0].RoundId, sink.Events[1].RoundId);
        Assert.All(sink.Events, value => { Assert.Equal("free", value.Mode); Assert.Equal(0, value.WagerCents); });
    }

    [Fact]
    public void Finished_drop_merge_run_records_once_and_new_run_does_not_record_until_it_finishes()
    {
        var sink = new RecordingSettlementSink(); var service = new DropMergeGameService(sink);
        var round = service.Start("alice", 41); Assert.Empty(sink.Events);
        for (var move = 0; move < 20000 && round.Phase == "playing"; move++) round = service.Drop("alice", round.GameId, move % round.Columns);
        Assert.Equal("lost", round.Phase);
        var recorded = Assert.Single(sink.Events); Assert.Equal("drop-merge", recorded.GameId); Assert.Equal("free", recorded.Mode);
        service.Reset("alice", round.GameId, 41); Assert.Single(sink.Events);
    }
}
