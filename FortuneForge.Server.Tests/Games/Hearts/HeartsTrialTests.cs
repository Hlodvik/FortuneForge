using System.Collections.Concurrent;
using FortuneForge.Games.Hearts;
using FortuneForge.Server.Bots.Hearts;
using FortuneForge.Server.Games.Hearts;
using Xunit;

namespace FortuneForge.Server.Tests.Games.Hearts;

public sealed class HeartsTrialTests
{
    [Fact]
    public async Task Starting_and_abandoning_a_trial_does_not_complete_onboarding()
    {
        var store = new RecordingOnboarding();
        var games = new HeartsGameService(new Roster(), new Clock(), store);
        var trial = await games.StartTrialAsync("alice");

        Assert.True(trial.IsTrial);
        Assert.Equal("passing", trial.Phase);
        Assert.Equal(["Trial bot 1", "Trial bot 2", "Trial bot 3"],
            trial.Players.Where(player => player.Seat != trial.HumanSeat).Select(player => player.DisplayName));
        Assert.True((await store.GetAsync("alice", default)).ShouldAskPlayedBefore);
        await games.PassAsync("alice", trial.MatchId, trial.Hand.Take(3).Select(card => card.Code).ToArray());
        Assert.Equal(0, store.Attempts);
        await Assert.ThrowsAsync<HeartsAccessException>(() => games.GetAsync("bruno", trial.MatchId));
        await Assert.ThrowsAsync<HeartsRuleException>(() => games.NextRoundAsync("alice", trial.MatchId, null));
    }

    [Fact]
    public async Task A_real_completed_free_play_hand_does_not_satisfy_trial_onboarding()
    {
        var store = new RecordingOnboarding();
        var clock = new Clock();
        var games = new HeartsGameService(new Roster(), clock, store);
        var match = await games.StartAsync("alice", new StartHeartsMatchRequest(1931, 100));
        var finished = await PlayUntil(games, clock, "alice", match, value => value.Phase == "complete");

        Assert.False(finished.IsTrial);
        Assert.Equal(13, finished.CompletedTricks.Count);
        Assert.True((await store.GetAsync("alice", default)).ShouldAskPlayedBefore);
        Assert.Equal(0, store.Attempts);
        var next = await games.NextRoundAsync("alice", match.MatchId, 1942);
        Assert.Equal(2, next.RoundNumber);
    }

    [Fact]
    public async Task A_completed_dedicated_trial_satisfies_only_its_account_and_cannot_start_another_hand()
    {
        var store = new RecordingOnboarding();
        var clock = new Clock();
        var roster = new Roster();
        var games = new HeartsGameService(roster, clock, store);
        var trial = await games.StartTrialAsync("alice");
        var finished = await PlayUntil(games, clock, "alice", trial, value => value.Phase == "complete");

        Assert.Equal(13, finished.CompletedTricks.Count);
        Assert.True(finished.IsTrial);
        Assert.False(finished.OpponentsThinking);
        Assert.False((await store.GetAsync("alice", default)).ShouldAskPlayedBefore);
        Assert.True((await store.GetAsync("bruno", default)).ShouldAskPlayedBefore);
        Assert.Equal(trial.MatchId, store.Completed["alice"]);
        Assert.Equal(1, store.Attempts);
        Assert.Equal(1, roster.Releases);
        await Assert.ThrowsAsync<HeartsRuleException>(() => games.NextRoundAsync("alice", trial.MatchId, null));
        await Task.WhenAll(Enumerable.Range(0, 4).Select(_ => games.GetAsync("alice", trial.MatchId)));
        await games.AdvanceDueManagedTurnsAsync();
        Assert.Equal(1, store.Attempts);
    }

    [Fact]
    public async Task A_failed_final_bot_card_record_retries_from_background_without_replaying_the_card()
    {
        // Actual opponent decisions are private/random. Find a genuine trial
        // whose final actor is an opponent, without changing its server state.
        for (var attempt = 0; attempt < 32; attempt++)
        {
            var store = new RecordingOnboarding();
            var clock = new Clock();
            var roster = new Roster();
            var games = new HeartsGameService(roster, clock, store);
            var trial = await games.StartTrialAsync("alice");
            var beforeLast = await PlayUntil(games, clock, "alice", trial,
                value => value.CompletedTricks.Count == 12 && value.CurrentTrick.Plays.Count == 3);
            if (beforeLast.YourTurn) continue;
            store.FailuresRemaining = 1;

            clock.Advance();
            await games.AdvanceDueManagedTurnsAsync();
            Assert.Equal(1, store.Attempts);
            Assert.True((await store.GetAsync("alice", default)).ShouldAskPlayedBefore);
            Assert.Equal(1, roster.Releases);

            await games.AdvanceDueManagedTurnsAsync();
            Assert.Equal(2, store.Attempts);
            Assert.False((await store.GetAsync("alice", default)).ShouldAskPlayedBefore);
            var finished = await games.GetAsync("alice", trial.MatchId);
            Assert.Equal("complete", finished.Phase);
            Assert.Equal(13, finished.CompletedTricks.Count);
            Assert.Empty(finished.Hand);
            await games.AdvanceDueManagedTurnsAsync();
            Assert.Equal(2, store.Attempts);
            Assert.Equal(1, roster.Releases);
            return;
        }
        Assert.Fail("No opponent finished any of the 32 actual trial hands.");
    }

    [Fact]
    public async Task Retried_trial_start_reuses_active_session_and_cancel_releases_profiles_without_onboarding()
    {
        var store = new RecordingOnboarding();
        var clock = new Clock();
        var roster = new Roster();
        var games = new HeartsGameService(roster, clock, store);
        var started = await Task.WhenAll(Enumerable.Range(0, 6).Select(_ => games.StartTrialAsync("alice")));
        Assert.Single(started.Select(value => value.MatchId).Distinct());
        Assert.Equal(1, roster.Reservations);
        var trial = started[0];
        await games.PassAsync("alice", trial.MatchId, trial.Hand.Take(3).Select(card => card.Code).ToArray());
        await Assert.ThrowsAsync<HeartsAccessException>(() => games.CancelTrialAsync("bruno", trial.MatchId));
        await games.CancelTrialAsync("alice", trial.MatchId);
        await games.CancelTrialAsync("alice", trial.MatchId);
        clock.Advance();
        await games.AdvanceDueManagedTurnsAsync();
        Assert.Equal(1, roster.Releases);
        Assert.Equal(0, store.Attempts);
        Assert.True((await store.GetAsync("alice", default)).ShouldAskPlayedBefore);
        await Assert.ThrowsAsync<HeartsRuleException>(() => games.AdvanceAsync("alice", trial.MatchId));
        await Assert.ThrowsAsync<HeartsRuleException>(() => games.PlayCardAsync("alice", trial.MatchId, trial.Hand[0].Code));
        Assert.False((await games.GetAsync("alice", trial.MatchId)).OpponentsThinking);
        var replacement = await games.StartTrialAsync("alice");
        Assert.NotEqual(trial.MatchId, replacement.MatchId);
        Assert.Equal(2, roster.Reservations);
    }

    [Fact]
    public async Task Failed_trial_cancel_cleanup_retries_without_advancing_or_satisfying_onboarding()
    {
        var store = new RecordingOnboarding();
        var clock = new Clock();
        var roster = new Roster { ReleaseFailuresRemaining = 1 };
        var games = new HeartsGameService(roster, clock, store);
        var trial = await games.StartTrialAsync("alice");
        await games.PassAsync("alice", trial.MatchId, trial.Hand.Take(3).Select(card => card.Code).ToArray());
        await Assert.ThrowsAsync<InvalidOperationException>(() => games.CancelTrialAsync("alice", trial.MatchId));
        await Assert.ThrowsAsync<HeartsRuleException>(() => games.AdvanceAsync("alice", trial.MatchId));
        clock.Advance();
        await games.AdvanceDueManagedTurnsAsync();
        await games.CancelTrialAsync("alice", trial.MatchId);
        Assert.Equal(1, roster.Releases);
        Assert.Equal(0, store.Attempts);
        Assert.Empty((await games.GetAsync("alice", trial.MatchId)).CompletedTricks);
    }

    private static async Task<HeartsMatchResponse> PlayUntil(HeartsGameService games, Clock clock,
        string userId, HeartsMatchResponse state, Func<HeartsMatchResponse, bool> done)
    {
        for (var action = 0; action < 120; action++)
        {
            if (done(state)) return state;
            if (state.Phase == "passing" && !state.SubmittedPasses.Contains(state.HumanSeat))
                state = await games.PassAsync(userId, state.MatchId, state.Hand.Take(3).Select(card => card.Code).ToArray());
            else if (state.Phase == "playing" && state.YourTurn)
                state = await games.PlayCardAsync(userId, state.MatchId, state.LegalCards[0].Code);
            else
            {
                clock.Advance();
                await games.AdvanceDueManagedTurnsAsync();
                state = await games.GetAsync(userId, state.MatchId);
            }
        }
        throw new InvalidOperationException("The trial did not reach the expected legal board position.");
    }

    private sealed class RecordingOnboarding : IHeartsOnboardingStore
    {
        public ConcurrentDictionary<string, Guid> Completed { get; } = new();
        public int Attempts { get; private set; }
        public int FailuresRemaining { get; set; }
        public Task<HeartsOnboardingResponse> GetAsync(string userId, CancellationToken cancellationToken) =>
            Task.FromResult(new HeartsOnboardingResponse(!Completed.ContainsKey(userId)));
        public Task<HeartsOnboardingResponse> ConfirmExperienceAsync(string userId, DateTime nowUtc, CancellationToken cancellationToken)
        { Completed.TryAdd(userId, Guid.Empty); return Task.FromResult(new HeartsOnboardingResponse(false)); }
        public Task CompleteTrialAsync(string userId, Guid trialMatchId, DateTime nowUtc, CancellationToken cancellationToken)
        {
            Attempts++;
            if (FailuresRemaining-- > 0) throw new InvalidOperationException("A transient onboarding write failed.");
            Completed.TryAdd(userId, trialMatchId);
            return Task.CompletedTask;
        }
    }

    private sealed class Clock : TimeProvider
    {
        private DateTimeOffset now = new(2026, 10, 21, 12, 0, 0, TimeSpan.Zero);
        public override DateTimeOffset GetUtcNow() => now;
        public void Advance() => now = now.AddSeconds(2);
    }

    private sealed class Roster : IHeartsManagedPlayerRoster
    {
        public int Reservations { get; private set; }
        public int Releases { get; private set; }
        public int ReleaseFailuresRemaining { get; set; }
        public Task<IReadOnlyList<HeartsManagedPlayer>> ReserveAsync(string assignmentId, int count,
            IReadOnlyCollection<string> excluded, DateTime now, CancellationToken cancellationToken)
        {
            Reservations++;
            return Task.FromResult<IReadOnlyList<HeartsManagedPlayer>>([
                new("managed-east", "MossRiver", 3), new("managed-south", "CopperFinch", 3), new("managed-west", "NightWindow", 3)]);
        }
        public Task HeartbeatAsync(string assignmentId, IReadOnlyCollection<string> ids, DateTime now, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task ReleaseAsync(string assignmentId, IReadOnlyCollection<string> ids, DateTime now, CancellationToken cancellationToken)
        {
            if (ReleaseFailuresRemaining-- > 0) throw new InvalidOperationException("A transient release failed.");
            Releases++; return Task.CompletedTask;
        }
    }
}
