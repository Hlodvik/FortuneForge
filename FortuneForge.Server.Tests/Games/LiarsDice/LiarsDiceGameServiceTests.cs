using FortuneForge.Server.Bots.LiarsDice;
using FortuneForge.Server.Games.LiarsDice;
using Xunit;

namespace FortuneForge.Server.Tests.Games.LiarsDice;

public sealed class LiarsDiceGameServiceTests
{
    [Fact]
    public async Task Started_match_is_private_and_uses_reserved_profiles()
    {
        var service = CreateService(out _, out var roster);
        var match = await service.StartAsync(
            "player-a", new StartLiarsDiceMatchRequest(77, 3));

        Assert.Equal("bidding", match.Phase);
        Assert.Equal(3, match.Hand.Count);
        Assert.Equal(
            ["MossRiver", "CopperFinch", "NightWindow"],
            match.Players.Where(player => !player.IsHuman).Select(player => player.DisplayName));
        Assert.Equal(1, roster.ReserveCalls);
        await Assert.ThrowsAsync<LiarsDiceAccessException>(() =>
            service.GetAsync("player-b", match.MatchId));
    }

    [Fact]
    public async Task Each_advance_executes_only_the_current_opponents_private_scheduled_turn()
    {
        var service = CreateService(out var clock, out _);
        var match = await service.StartAsync(
            "player-a", new StartLiarsDiceMatchRequest(98, 3));

        var afterBid = await service.BidAsync(
            "player-a", match.MatchId, new PlaceLiarsDiceBidRequest(1, 1));
        Assert.True(afterBid.OpponentsThinking);
        var firstOpponentId = afterBid.CurrentPlayerId;

        var tooEarly = await service.AdvanceAsync("player-a", match.MatchId);
        Assert.Equal(firstOpponentId, tooEarly.CurrentPlayerId);

        clock.Advance(TimeSpan.FromSeconds(2));
        var oneMove = await service.AdvanceAsync("player-a", match.MatchId);
        Assert.NotEqual(firstOpponentId, oneMove.CurrentPlayerId);
        Assert.True(oneMove.OpponentsThinking || oneMove.Phase == "resolved");
    }

    [Fact]
    public async Task Server_scheduler_advances_a_due_opponent_without_a_browser_advance_request()
    {
        var service = CreateService(out var clock, out _);
        var match = await service.StartAsync(
            "player-a", new StartLiarsDiceMatchRequest(98, 3));
        var waiting = await service.BidAsync(
            "player-a", match.MatchId, new PlaceLiarsDiceBidRequest(1, 1));
        var firstOpponentId = waiting.CurrentPlayerId;

        clock.Advance(TimeSpan.FromSeconds(2));
        await service.AdvanceDueManagedTurnsAsync();
        var advanced = await service.GetAsync("player-a", match.MatchId);

        Assert.NotEqual(firstOpponentId, advanced.CurrentPlayerId);
        Assert.True(advanced.OpponentsThinking || advanced.Phase == "resolved");
    }

    [Fact]
    public async Task Active_match_renews_managed_profile_leases_without_heartbeating_every_poll()
    {
        var service = CreateService(out var clock, out var roster);
        var match = await service.StartAsync(
            "player-a", new StartLiarsDiceMatchRequest(98, 3));

        clock.Advance(TimeSpan.FromMinutes(4));
        await service.GetAsync("player-a", match.MatchId);
        Assert.Equal(0, roster.HeartbeatCalls);

        clock.Advance(TimeSpan.FromMinutes(1));
        await service.GetAsync("player-a", match.MatchId);
        await service.GetAsync("player-a", match.MatchId);

        Assert.Equal(1, roster.HeartbeatCalls);
        Assert.Equal(
            ["managed-liars-a", "managed-liars-b", "managed-liars-c"],
            roster.LastHeartbeatProfileIds);
    }

    private static LiarsDiceGameService CreateService(
        out MutableTimeProvider clock,
        out RecordingRoster roster)
    {
        clock = new MutableTimeProvider();
        roster = new RecordingRoster();
        return new LiarsDiceGameService(roster, clock);
    }

    private sealed class RecordingRoster : ILiarsDiceManagedPlayerRoster
    {
        public int ReserveCalls { get; private set; }
        public int HeartbeatCalls { get; private set; }
        public IReadOnlyList<string> LastHeartbeatProfileIds { get; private set; } = [];

        public Task<IReadOnlyList<LiarsDiceManagedPlayer>> ReserveAsync(
            string assignmentId,
            int count,
            IReadOnlyCollection<string> excludedProfileIds,
            DateTime nowUtc,
            CancellationToken cancellationToken)
        {
            ReserveCalls++;
            Assert.Equal(3, count);
            Assert.Contains("player-a", excludedProfileIds);
            return Task.FromResult<IReadOnlyList<LiarsDiceManagedPlayer>>([
                new("managed-liars-a", "MossRiver", 2),
                new("managed-liars-b", "CopperFinch", 3),
                new("managed-liars-c", "NightWindow", 4),
            ]);
        }

        public Task HeartbeatAsync(
            string assignmentId,
            IReadOnlyCollection<string> profileIds,
            DateTime nowUtc,
            CancellationToken cancellationToken)
        {
            HeartbeatCalls++;
            LastHeartbeatProfileIds = profileIds.ToArray();
            return Task.CompletedTask;
        }

        public Task ReleaseAsync(
            string assignmentId,
            IReadOnlyCollection<string> profileIds,
            DateTime nowUtc,
            CancellationToken cancellationToken) => Task.CompletedTask;
    }

    private sealed class MutableTimeProvider : TimeProvider
    {
        private DateTimeOffset now = new(2026, 9, 30, 12, 0, 0, TimeSpan.Zero);
        public override DateTimeOffset GetUtcNow() => now;
        public void Advance(TimeSpan duration) => now += duration;
    }
}
