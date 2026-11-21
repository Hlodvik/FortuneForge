using FortuneForge.Server.Bots.Hearts;
using FortuneForge.Server.Games.Hearts;
using Xunit;

namespace FortuneForge.Server.Tests.Games.Hearts;

public sealed class HeartsGameServiceTests
{
    [Fact]
    public async Task Started_match_is_private_and_uses_reserved_profile_names()
    {
        var service = CreateService(out _, out var roster);
        var match = await service.StartAsync(
            "player-a", new StartHeartsMatchRequest(1931, 50, "relaxed"));

        Assert.Equal(50, match.TargetScore);
        Assert.Equal("relaxed", match.Difficulty);
        Assert.Equal("passing", match.Phase);
        Assert.Equal(13, match.Hand.Count);
        Assert.Equal(
            ["MossRiver", "CopperFinch", "NightWindow"],
            match.Players.Where(player => player.Seat != "north")
                .Select(player => player.DisplayName));
        Assert.Equal(1, roster.ReserveCalls);
        await Assert.ThrowsAsync<HeartsAccessException>(() =>
            service.GetAsync("player-b", match.MatchId));
    }

    [Fact]
    public async Task Opponents_advance_one_private_scheduled_turn_at_a_time()
    {
        var service = CreateService(out var clock, out _);
        var started = await service.StartAsync(
            "player-a", new StartHeartsMatchRequest(1942, 50));
        var next = await service.PassAsync(
            "player-a", started.MatchId,
            started.Hand.Take(3).Select(card => card.Code).ToArray());

        Assert.True(next.OpponentsThinking);
        Assert.Single(next.SubmittedPasses);
        var tooEarly = await service.AdvanceAsync("player-a", started.MatchId);
        Assert.Single(tooEarly.SubmittedPasses);

        clock.Advance(TimeSpan.FromSeconds(2));
        var oneMove = await service.AdvanceAsync("player-a", started.MatchId);
        Assert.Equal(2, oneMove.SubmittedPasses.Count);
        Assert.True(oneMove.OpponentsThinking);

        for (var action = 0; oneMove.OpponentsThinking && action < 24; action++)
        {
            clock.Advance(TimeSpan.FromSeconds(2));
            oneMove = await service.AdvanceAsync("player-a", started.MatchId);
        }

        Assert.Equal("playing", oneMove.Phase);
        Assert.True(oneMove.YourTurn);
        Assert.NotEmpty(oneMove.LegalCards);
    }

    [Fact]
    public async Task Server_scheduler_advances_a_due_opponent_without_a_browser_advance_request()
    {
        var service = CreateService(out var clock, out _);
        var started = await service.StartAsync(
            "player-a", new StartHeartsMatchRequest(1942, 50));
        var waiting = await service.PassAsync(
            "player-a", started.MatchId,
            started.Hand.Take(3).Select(card => card.Code).ToArray());
        Assert.Single(waiting.SubmittedPasses);

        clock.Advance(TimeSpan.FromSeconds(2));
        await service.AdvanceDueManagedTurnsAsync();
        var advanced = await service.GetAsync("player-a", started.MatchId);

        Assert.Equal(2, advanced.SubmittedPasses.Count);
        Assert.True(advanced.OpponentsThinking);
    }

    [Fact]
    public async Task Started_match_rejects_an_unknown_difficulty_before_reserving_profiles()
    {
        var service = CreateService(out _, out var roster);

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() =>
            service.StartAsync("player-a", new StartHeartsMatchRequest(1931, 50, "impossible")));
        Assert.Equal(0, roster.ReserveCalls);
    }

    [Fact]
    public async Task Active_match_renews_managed_profile_leases_without_heartbeating_every_poll()
    {
        var service = CreateService(out var clock, out var roster);
        var match = await service.StartAsync(
            "player-a", new StartHeartsMatchRequest(1931, 50));

        clock.Advance(TimeSpan.FromMinutes(4));
        await service.GetAsync("player-a", match.MatchId);
        Assert.Equal(0, roster.HeartbeatCalls);

        clock.Advance(TimeSpan.FromMinutes(1));
        await service.GetAsync("player-a", match.MatchId);
        await service.GetAsync("player-a", match.MatchId);

        Assert.Equal(1, roster.HeartbeatCalls);
        Assert.Equal(
            ["managed-hearts-a", "managed-hearts-b", "managed-hearts-c"],
            roster.LastHeartbeatProfileIds);
    }

    private static HeartsGameService CreateService(
        out MutableTimeProvider clock,
        out RecordingRoster roster)
    {
        clock = new MutableTimeProvider();
        roster = new RecordingRoster();
        return new HeartsGameService(roster, clock);
    }

    private sealed class RecordingRoster : IHeartsManagedPlayerRoster
    {
        public int ReserveCalls { get; private set; }
        public int HeartbeatCalls { get; private set; }
        public IReadOnlyList<string> LastHeartbeatProfileIds { get; private set; } = [];

        public Task<IReadOnlyList<HeartsManagedPlayer>> ReserveAsync(
            string assignmentId,
            int count,
            IReadOnlyCollection<string> excludedProfileIds,
            DateTime nowUtc,
            CancellationToken cancellationToken)
        {
            ReserveCalls++;
            Assert.Equal(3, count);
            Assert.Contains("player-a", excludedProfileIds);
            return Task.FromResult<IReadOnlyList<HeartsManagedPlayer>>([
                new("managed-hearts-a", "MossRiver", 2),
                new("managed-hearts-b", "CopperFinch", 3),
                new("managed-hearts-c", "NightWindow", 4),
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
