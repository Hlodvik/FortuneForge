using System.Text.Json;
using FortuneForge.Games.Abstractions.Auditing;
using FortuneForge.Games.Abstractions.ManagedPlayers;
using FortuneForge.Server.Games.Hearts;
using FortuneForge.Server.Tests.Blackjack.Table;
using Google.Cloud.Firestore;
using Xunit;

namespace FortuneForge.Games.Hearts.Tests;

[Collection("Blackjack table Firestore emulator")]
public sealed class HeartsFreePersistenceTests(BlackjackTableFirestoreEmulatorFixture fixture)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    [Fact]
    public async Task Lost_start_reply_replays_original_private_hand_and_changed_body_conflicts_after_restart()
    {
        var db = Database(); var clock = new Clock();
        var start = await As("alice", "open", "/matches", "{seed:1931}", sha => Store(db, clock).StartAsync("alice", new(1931, 100), sha, default));
        var retry = await As("alice", "open", "/matches", "{seed:1931}", sha => Store(db, clock).StartAsync("alice", new(1931, 100), sha, default));
        Assert.Equal(JsonSerializer.Serialize(start, Json), JsonSerializer.Serialize(retry, Json));
        Assert.Single((await db.Collection(FirestoreHeartsFreeStore.Sessions).GetSnapshotAsync()).Documents);
        await Assert.ThrowsAsync<HeartsCommandConflictException>(() => As("alice", "open", "/matches", "{seed:1942}", sha => Store(db, clock).StartAsync("alice", new(1942, 100), sha, default)));
        await Assert.ThrowsAsync<HeartsAccessException>(() => Store(db, clock).GetAsync("bruno", start.MatchId, null, default));
    }

    [Fact]
    public async Task Concurrent_trial_starts_share_one_durable_trial_and_cancel_never_satisfies_onboarding()
    {
        var db = Database(); var clock = new Clock();
        var results = await Task.WhenAll(Enumerable.Range(0, 3).Select(index => As("alice", "trial-" + index, "/trial", "", sha => Store(db, clock).TrialAsync("alice", sha, default))));
        var id = Assert.Single(results.Select(result => result.MatchId).Distinct());
        var cancelled = await As("alice", "cancel", "/trial/" + id + "/cancel", "", sha => Store(db, clock).CancelTrialAsync("alice", id, sha, default));
        Assert.True(cancelled.ShouldAskPlayedBefore);
        Assert.True((await Store(db, clock).OnboardingAsync("alice", null, default)).ShouldAskPlayedBefore);
        await Assert.ThrowsAsync<HeartsRuleException>(() => Store(db, clock).AdvanceAsync("alice", id, null, default));
    }

    [Fact]
    public async Task Private_rng_and_scheduled_turn_survive_restart_and_same_pass_never_applies_twice()
    {
        var db = Database(); var clock = new Clock();
        var start = await As("alice", "open", "/matches", "1931", sha => Store(db, clock).StartAsync("alice", new(1931, 100), sha, default));
        var cards = start.Hand.Take(3).Select(card => card.Code).ToArray();
        var passed = await As("alice", "pass", "/matches/" + start.MatchId + "/pass", JsonSerializer.Serialize(cards), sha => Store(db, clock).PassAsync("alice", start.MatchId, cards, sha, default));
        var before = Assert.Single((await db.Collection(FirestoreHeartsFreeStore.Sessions).GetSnapshotAsync()).Documents).GetValue<string>("payloadJson");
        await As("alice", "pass", "/matches/" + start.MatchId + "/pass", JsonSerializer.Serialize(cards), sha => Store(db, clock).PassAsync("alice", start.MatchId, cards, sha, default));
        Assert.Equal(before, Assert.Single((await db.Collection(FirestoreHeartsFreeStore.Sessions).GetSnapshotAsync()).Documents).GetValue<string>("payloadJson"));
        var twin = Database();
        var saved = Assert.Single((await db.Collection(FirestoreHeartsFreeStore.Sessions).GetSnapshotAsync()).Documents);
        await twin.Collection(FirestoreHeartsFreeStore.Sessions).Document(saved.Id).SetAsync(saved.ToDictionary());
        clock.Advance(TimeSpan.FromSeconds(2));
        await Task.WhenAll(Enumerable.Range(0, 3).Select(_ => Store(db, clock).AdvanceDueAsync(default)));
        await Store(twin, clock).AdvanceDueAsync(default);
        Assert.Equal((await saved.Reference.GetSnapshotAsync()).GetValue<string>("payloadJson"),
            (await twin.Collection(FirestoreHeartsFreeStore.Sessions).Document(saved.Id).GetSnapshotAsync()).GetValue<string>("payloadJson"));
        var advanced = await Store(db, clock).GetAsync("alice", start.MatchId, null, default);
        Assert.Equal(2, advanced.SubmittedPasses.Count);
        Assert.Single(passed.SubmittedPasses);
        Assert.True(advanced.OpponentsThinking);
    }

    [Fact]
    public async Task Completed_trial_commits_board_onboarding_audit_and_outbox_once_across_restarts()
    {
        var db = Database(); var clock = new Clock();
        var state = await As("alice", "trial", "/trial", "", sha => Store(db, clock).TrialAsync("alice", sha, default));
        for (var action = 0; action < 120 && state.Phase != "complete"; action++)
        {
            var store = Store(db, clock);
            if (state.Phase == "passing" && !state.SubmittedPasses.Contains(state.HumanSeat))
                state = await store.PassAsync("alice", state.MatchId, state.Hand.Take(3).Select(card => card.Code).ToArray(), null, default);
            else if (state.Phase == "playing" && state.YourTurn)
                state = await store.CardAsync("alice", state.MatchId, state.LegalCards[0].Code, null, default);
            else
            {
                clock.Advance(TimeSpan.FromSeconds(2));
                await store.AdvanceDueAsync(default);
                state = await Store(db, clock).GetAsync("alice", state.MatchId, null, default);
            }
        }
        Assert.Equal("complete", state.Phase); Assert.Equal(13, state.CompletedTricks.Count);
        Assert.False((await Store(db, clock).OnboardingAsync("alice", null, default)).ShouldAskPlayedBefore);
        Assert.True((await Store(db, clock).OnboardingAsync("bruno", null, default)).ShouldAskPlayedBefore);
        var audit = Assert.Single((await db.Collection("gameRoundAudits").GetSnapshotAsync()).Documents);
        var evidence = Assert.Single((await db.Collection("gameRoundEvidence").GetSnapshotAsync()).Documents);
        var original = evidence.GetValue<string>("payloadJson");
        await Store(db, clock).AdvanceDueAsync(default);
        await Store(db, clock).GetAsync("alice", state.MatchId, null, default);
        Assert.Single((await db.Collection("gameAnalyticsSettlementOutbox").GetSnapshotAsync()).Documents);
        Assert.Single((await db.Collection("gameRoundAudits").GetSnapshotAsync()).Documents);
        Assert.Equal(original, (await evidence.Reference.GetSnapshotAsync()).GetValue<string>("payloadJson"));
        await Assert.ThrowsAsync<HeartsRuleException>(() => Store(db, clock).NextAsync("alice", state.MatchId, 1942, null, default));
    }

    [Fact]
    public async Task Paused_new_tables_retain_retries_and_live_trial_and_expiry_cannot_complete_it()
    {
        var db = Database(); var clock = new Clock(); var allowed = true;
        FirestoreHeartsFreeStore Current() => new(db, clock, () => allowed);
        var trial = await As("alice", "trial", "/trial", "", sha => Current().TrialAsync("alice", sha, default));
        allowed = false;
        Assert.Equal(trial.MatchId, (await As("alice", "trial", "/trial", "", sha => Current().TrialAsync("alice", sha, default))).MatchId);
        Assert.Equal(trial.MatchId, (await Current().TrialAsync("alice", null, default)).MatchId);
        await Assert.ThrowsAsync<HeartsAdmissionPausedException>(() => Current().StartAsync("alice", new(1931, 100), null, default));
        clock.Advance(HeartsGameService.AbandonedSessionTimeout);
        await Current().AdvanceDueAsync(default);
        Assert.Empty((await db.Collection(FirestoreHeartsFreeStore.Sessions).GetSnapshotAsync()).Documents);
        Assert.True((await Current().OnboardingAsync("alice", null, default)).ShouldAskPlayedBefore);
        Assert.Empty((await db.Collection("gameRoundAudits").GetSnapshotAsync()).Documents);
    }

    [Fact]
    public async Task A_corrupt_account_does_not_starve_another_accounts_due_turn()
    {
        var db = Database(); var clock = new Clock();
        var state = await Store(db, clock).StartAsync("bruno", new(1931, 100), null, default);
        await Store(db, clock).PassAsync("bruno", state.MatchId, state.Hand.Take(3).Select(card => card.Code).ToArray(), null, default);
        await db.Collection(FirestoreHeartsFreeStore.Sessions).Document("corrupt-alice").SetAsync(new Dictionary<string, object>
        { ["userId"] = "alice", ["payloadJson"] = "{}", ["sha256"] = "invalid", ["nextSweepAt"] = Timestamp.FromDateTime(clock.GetUtcNow().UtcDateTime) });
        clock.Advance(TimeSpan.FromSeconds(2));
        await Store(db, clock).AdvanceDueAsync(default);
        Assert.Equal(2, (await Store(db, clock).GetAsync("bruno", state.MatchId, null, default)).SubmittedPasses.Count);
    }

    private static FirestoreHeartsFreeStore Store(FirestoreDb db, Clock clock) => new(db, clock);
    private static async Task<T> As<T>(string user, string key, string path, string body, Func<string, Task<T>> action)
    {
        using var context = GameSecurityContext.Push(new(key, user, null, null, DateTime.UtcNow, "POST", "/api/games/hearts" + path,
            "hearts", null, key, null, null, "observed", "unknown"));
        return await action(GameSecurityEventDocuments.Hash(body));
    }
    private FirestoreDb Database() => new FirestoreDbBuilder
    { ProjectId = "demo-hearts-free-" + Guid.NewGuid().ToString("N"), Endpoint = fixture.Endpoint,
        ChannelCredentials = Grpc.Core.ChannelCredentials.Insecure, EmulatorDetection = Google.Api.Gax.EmulatorDetection.None }.Build();
    private sealed class Clock : TimeProvider
    {
        private DateTimeOffset now = new(2026, 11, 21, 12, 0, 0, TimeSpan.Zero);
        public override DateTimeOffset GetUtcNow() => now;
        internal void Advance(TimeSpan span) => now += span;
    }
}
