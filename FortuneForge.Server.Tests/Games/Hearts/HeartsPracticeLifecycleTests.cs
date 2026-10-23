using FortuneForge.Games.Hearts;
using FortuneForge.Server.Accounts;
using FortuneForge.Server.Bots.Hearts;
using FortuneForge.Server.Games.Hearts;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace FortuneForge.Server.Tests.Games.Hearts;

public sealed class HeartsPracticeLifecycleTests
{
    [Fact]
    public async Task Free_leave_requires_owner_releases_only_its_assignment_and_is_idempotent()
    {
        var service = Create(out _, out var roster, out var onboarding);
        var game = await service.StartAsync("alice", new(1931, 100));
        await Assert.ThrowsAsync<HeartsAccessException>(() => service.LeaveAsync("bruno", game.MatchId));
        Assert.Empty(roster.Releases);
        Assert.Equal(game.MatchId, (await service.GetAsync("alice", game.MatchId)).MatchId);

        await Task.WhenAll(Enumerable.Range(0, 6).Select(_ => service.LeaveAsync("alice", game.MatchId)));
        await service.LeaveAsync("alice", game.MatchId);
        var released = Assert.Single(roster.Releases);
        Assert.Equal($"hearts:{game.MatchId:N}", released.Assignment);
        Assert.Equal(["east-profile", "south-profile", "west-profile"], released.Ids);
        Assert.Equal(0, service.RetainedSessionCount);
        Assert.Equal(0, onboarding.Completions);
        await Assert.ThrowsAsync<HeartsAccessException>(() => service.GetAsync("alice", game.MatchId));
        await Assert.ThrowsAsync<HeartsAccessException>(() => service.AdvanceAsync("alice", game.MatchId));
    }

    [Fact]
    public async Task Free_leave_cannot_cancel_a_learning_trial_and_unknown_paid_ids_do_not_touch_roster()
    {
        var service = Create(out _, out var roster, out var onboarding);
        var trial = await service.StartTrialAsync("alice");
        await Assert.ThrowsAsync<HeartsRuleException>(() => service.LeaveAsync("alice", trial.MatchId));
        await service.LeaveAsync("alice", Guid.NewGuid());
        Assert.Empty(roster.Releases);
        Assert.True((await service.GetAsync("alice", trial.MatchId)).IsTrial);
        await service.CancelTrialAsync("alice", trial.MatchId);
        Assert.Single(roster.Releases);
        Assert.Equal(0, onboarding.Completions);
    }

    [Fact]
    public async Task Failed_free_leave_stops_play_immediately_and_retries_cleanup_without_double_release()
    {
        var service = Create(out var clock, out var roster, out _);
        var game = await service.StartAsync("alice", new(1931, 100));
        await service.PassAsync("alice", game.MatchId, game.Hand.Take(3).Select(card => card.Code).ToArray());
        roster.ReleaseFailures = 1;
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.LeaveAsync("alice", game.MatchId));
        Assert.Equal(1, service.RetainedSessionCount);
        await Assert.ThrowsAsync<HeartsRuleException>(() => service.AdvanceAsync("alice", game.MatchId));
        Assert.False((await service.GetAsync("alice", game.MatchId)).OpponentsThinking);
        clock.Advance(TimeSpan.FromSeconds(2));
        await service.AdvanceDueManagedTurnsAsync();
        await service.LeaveAsync("alice", game.MatchId);
        Assert.Single(roster.Releases);
        Assert.Equal(2, roster.ReleaseAttempts);
        Assert.Equal(0, service.RetainedSessionCount);
    }

    [Fact]
    public async Task Worker_expires_abandoned_free_and_trial_tables_and_never_completes_an_abandoned_trial()
    {
        var service = Create(out var clock, out var roster, out var onboarding);
        var free = await service.StartAsync("alice", new(1931, 100));
        var trial = await service.StartTrialAsync("bruno");
        await service.PassAsync("alice", free.MatchId, free.Hand.Take(3).Select(card => card.Code).ToArray());
        clock.Advance(HeartsGameService.AbandonedSessionTimeout);
        await service.AdvanceDueManagedTurnsAsync();
        Assert.Equal(2, roster.Releases.Count);
        Assert.Equal(0, service.RetainedSessionCount);
        Assert.Equal(0, onboarding.Completions);
        await Assert.ThrowsAsync<HeartsAccessException>(() => service.GetAsync("alice", free.MatchId));
        await Assert.ThrowsAsync<HeartsAccessException>(() => service.GetAsync("bruno", trial.MatchId));
        Assert.Equal(0, roster.Heartbeats);
    }

    [Fact]
    public async Task Expiry_cleanup_failure_stays_inert_and_worker_retries_until_it_can_remove_the_session()
    {
        var service = Create(out var clock, out var roster, out _);
        var game = await service.StartAsync("alice", new(1931, 100));
        roster.ReleaseFailures = 1;
        clock.Advance(HeartsGameService.AbandonedSessionTimeout);
        await service.AdvanceDueManagedTurnsAsync();
        Assert.Equal(1, service.RetainedSessionCount);
        await Assert.ThrowsAsync<HeartsAccessException>(() => service.AdvanceAsync("alice", game.MatchId));
        await service.AdvanceDueManagedTurnsAsync();
        Assert.Equal(0, service.RetainedSessionCount);
        Assert.Single(roster.Releases);
        Assert.Equal(0, roster.Heartbeats);
    }

    [Fact]
    public async Task Expired_request_cannot_reanimate_profiles_even_if_worker_has_not_run_for_longer_than_the_lease()
    {
        var service = Create(out var clock, out var roster, out _);
        var game = await service.StartAsync("alice", new(1931, 100));
        clock.Advance(TimeSpan.FromMinutes(45));
        await Assert.ThrowsAsync<HeartsAccessException>(() => service.GetAsync("alice", game.MatchId));
        await Assert.ThrowsAsync<HeartsAccessException>(() => service.PassAsync("alice", game.MatchId, game.Hand.Take(3).Select(card => card.Code).ToArray()));
        Assert.Equal(0, roster.Heartbeats);
        await service.AdvanceDueManagedTurnsAsync();
        Assert.Single(roster.Releases);
        Assert.Equal(0, service.RetainedSessionCount);
    }

    [Fact]
    public async Task Active_human_requests_refresh_inactivity_and_successful_lease_renewals_allow_a_longer_game()
    {
        var service = Create(out var clock, out var roster, out _);
        var game = await service.StartAsync("alice", new(1931, 100));
        for (var index = 0; index < 4; index++)
        {
            clock.Advance(TimeSpan.FromMinutes(10));
            Assert.Equal(game.MatchId, (await service.GetAsync("alice", game.MatchId)).MatchId);
            await service.AdvanceDueManagedTurnsAsync();
        }
        Assert.Equal(4, roster.Heartbeats);
        Assert.Empty(roster.Releases);
        Assert.Equal(1, service.RetainedSessionCount);
        clock.Advance(HeartsGameService.AbandonedSessionTimeout);
        await service.AdvanceDueManagedTurnsAsync();
        Assert.Single(roster.Releases);
        Assert.Equal(0, service.RetainedSessionCount);
    }

    [Fact]
    public async Task Repeated_requests_cannot_extend_a_cohort_when_its_lease_heartbeats_keep_failing()
    {
        var service = Create(out var clock, out var roster, out _);
        var game = await service.StartAsync("alice", new(1931, 100));
        roster.HeartbeatFailures = 2;
        clock.Advance(TimeSpan.FromMinutes(10));
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.GetAsync("alice", game.MatchId));
        clock.Advance(TimeSpan.FromMinutes(10));
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.GetAsync("alice", game.MatchId));
        clock.Advance(TimeSpan.FromMinutes(6));
        await Assert.ThrowsAsync<HeartsAccessException>(() => service.GetAsync("alice", game.MatchId));
        Assert.Equal(2, roster.Heartbeats);
        await service.AdvanceDueManagedTurnsAsync();
        Assert.Single(roster.Releases);
        Assert.Equal(0, service.RetainedSessionCount);
    }

    [Fact]
    public async Task Starting_a_trial_does_not_resume_an_expired_trial_or_satisfy_onboarding()
    {
        var service = Create(out var clock, out var roster, out var onboarding);
        var first = await service.StartTrialAsync("alice");
        clock.Advance(TimeSpan.FromMinutes(31));
        var second = await service.StartTrialAsync("alice");
        Assert.NotEqual(first.MatchId, second.MatchId);
        Assert.True(second.IsTrial);
        Assert.Single(roster.Releases);
        Assert.Equal(2, roster.Reservations);
        Assert.Equal(0, onboarding.Completions);
        Assert.Equal(0, roster.Heartbeats);
        await service.AdvanceDueManagedTurnsAsync();
        Assert.Equal(1, service.RetainedSessionCount);
    }

    [Fact]
    public async Task A_finished_free_match_retries_failed_release_in_worker_then_expires_without_releasing_twice()
    {
        var service = Create(out var clock, out var roster, out _);
        var started = await service.StartAsync("alice", new(1931, 1));
        roster.ReleaseFailures = 1;
        var finished = await FinishHand(service, clock, started);
        Assert.NotNull(finished.Winner);
        Assert.Equal(1, roster.ReleaseAttempts);
        Assert.Empty(roster.Releases);
        await service.AdvanceDueManagedTurnsAsync();
        Assert.Single(roster.Releases);
        Assert.Equal(2, roster.ReleaseAttempts);
        clock.Advance(HeartsGameService.AbandonedSessionTimeout);
        await service.AdvanceDueManagedTurnsAsync();
        Assert.Equal(0, service.RetainedSessionCount);
        Assert.Equal(2, roster.ReleaseAttempts);
    }

    [Fact]
    public async Task Expired_completed_trial_retains_its_completion_retry_until_confirmed_then_purges_the_session()
    {
        var service = Create(out var clock, out var roster, out var onboarding);
        var started = await service.StartTrialAsync("alice");
        onboarding.CompletionFailures = 2;
        var finished = await FinishHand(service, clock, started);
        Assert.Equal(13, finished.CompletedTricks.Count);
        Assert.Equal(0, onboarding.Completions);
        Assert.Single(roster.Releases);
        clock.Advance(HeartsGameService.AbandonedSessionTimeout);
        await service.AdvanceDueManagedTurnsAsync();
        Assert.Equal(1, service.RetainedSessionCount);
        Assert.Equal(0, onboarding.Completions);
        await service.AdvanceDueManagedTurnsAsync();
        Assert.Equal(0, service.RetainedSessionCount);
        Assert.Equal(1, onboarding.Completions);
        Assert.Single(roster.Releases);
    }

    [Fact]
    public async Task Leave_waits_for_an_inflight_heartbeat_then_releases_without_recreating_a_released_lease()
    {
        var service = Create(out var clock, out var roster, out _);
        var game = await service.StartAsync("alice", new(1931, 100));
        var heartbeat = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        roster.HeartbeatWait = heartbeat.Task;
        clock.Advance(TimeSpan.FromMinutes(5));
        var read = service.GetAsync("alice", game.MatchId);
        Assert.Equal(1, roster.Heartbeats);
        var leave = service.LeaveAsync("alice", game.MatchId);
        Assert.False(leave.IsCompleted);
        Assert.Empty(roster.Releases);
        heartbeat.SetResult();
        await Task.WhenAll(read, leave);
        Assert.Single(roster.Releases);
        Assert.Equal(0, service.RetainedSessionCount);
    }

    [Fact]
    public async Task Leave_endpoint_is_feature_gated_and_requires_authentication_even_for_absent_tables()
    {
        var context = new DefaultHttpContext();
        var accounts = new AccountService(null!, null!, null!, new HttpContextAccessor { HttpContext = context });
        var service = Create(out _, out _, out _);
        var disabled = new HeartsController(service, accounts, new ConfigurationBuilder().Build())
            { ControllerContext = new ControllerContext { HttpContext = context } };
        Assert.Equal(503, Assert.IsType<ObjectResult>(await disabled.Leave(Guid.NewGuid(), default)).StatusCode);
        var enabled = new HeartsController(service, accounts, new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["Features:HeartsEnabled"] = "true" }).Build())
            { ControllerContext = new ControllerContext { HttpContext = context } };
        Assert.IsType<UnauthorizedObjectResult>(await enabled.Leave(Guid.NewGuid(), default));
    }

    private static HeartsGameService Create(out Clock clock, out Roster roster, out Onboarding onboarding)
    {
        clock = new(); roster = new(); onboarding = new();
        return new(roster, clock, onboarding);
    }

    private static async Task<HeartsMatchResponse> FinishHand(HeartsGameService service, Clock clock, HeartsMatchResponse state)
    {
        for (var action = 0; action < 120 && state.Phase != "complete"; action++)
        {
            if (state.Phase == "passing" && !state.SubmittedPasses.Contains(state.HumanSeat))
                state = await service.PassAsync("alice", state.MatchId, state.Hand.Take(3).Select(card => card.Code).ToArray());
            else if (state.Phase == "playing" && state.YourTurn)
                state = await service.PlayCardAsync("alice", state.MatchId, state.LegalCards[0].Code);
            else
            {
                clock.Advance(TimeSpan.FromSeconds(2));
                state = await service.AdvanceAsync("alice", state.MatchId);
            }
        }
        Assert.Equal("complete", state.Phase);
        return state;
    }

    private sealed class Clock : TimeProvider
    {
        private DateTimeOffset now = new(2026, 10, 22, 12, 0, 0, TimeSpan.Zero);
        public override DateTimeOffset GetUtcNow() => now;
        public void Advance(TimeSpan duration) => now += duration;
    }

    private sealed class Onboarding : IHeartsOnboardingStore
    {
        public int Completions { get; private set; }
        public int CompletionFailures { get; set; }
        public Task<HeartsOnboardingResponse> GetAsync(string userId, CancellationToken token) => Task.FromResult(new HeartsOnboardingResponse(true));
        public Task<HeartsOnboardingResponse> ConfirmExperienceAsync(string userId, DateTime now, CancellationToken token) => throw new InvalidOperationException("Leaving must not confirm experience.");
        public Task CompleteTrialAsync(string userId, Guid matchId, DateTime now, CancellationToken token)
        {
            if (CompletionFailures-- > 0) throw new InvalidOperationException("Completion store unavailable.");
            Completions++; return Task.CompletedTask;
        }
    }

    private sealed class Roster : IHeartsManagedPlayerRoster
    {
        public int Reservations { get; private set; }
        public int Heartbeats { get; private set; }
        public int HeartbeatFailures { get; set; }
        public Task? HeartbeatWait { get; set; }
        public int ReleaseFailures { get; set; }
        public int ReleaseAttempts { get; private set; }
        public List<(string Assignment, string[] Ids)> Releases { get; } = [];
        public Task<IReadOnlyList<HeartsManagedPlayer>> ReserveAsync(string assignment, int count, IReadOnlyCollection<string> excluded, DateTime now, CancellationToken token)
        {
            Reservations++;
            return Task.FromResult<IReadOnlyList<HeartsManagedPlayer>>([new("east-profile", "MossRiver", 3), new("south-profile", "CopperFinch", 3), new("west-profile", "NightWindow", 3)]);
        }
        public async Task HeartbeatAsync(string assignment, IReadOnlyCollection<string> ids, DateTime now, CancellationToken token)
        {
            Heartbeats++;
            if (HeartbeatFailures-- > 0) throw new InvalidOperationException("Heartbeat unavailable.");
            if (HeartbeatWait is { } wait) await wait.WaitAsync(token);
        }
        public Task ReleaseAsync(string assignment, IReadOnlyCollection<string> ids, DateTime now, CancellationToken token)
        {
            ReleaseAttempts++;
            if (ReleaseFailures-- > 0) throw new InvalidOperationException("Release unavailable.");
            Releases.Add((assignment, ids.ToArray()));
            return Task.CompletedTask;
        }
    }
}
