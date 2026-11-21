using System.Security.Cryptography;
using System.Text;
using FortuneForge.Server.Bots.Hearts;
using FortuneForge.Server.Games.Hearts;
using FortuneForge.Server.Tests.Blackjack.Table;
using Google.Api.Gax;
using Google.Cloud.Firestore;
using Xunit;

namespace FortuneForge.Server.Tests.Games.Hearts;

[Collection("Blackjack table Firestore emulator")]
public sealed class HeartsOnboardingFirestoreTests(BlackjackTableFirestoreEmulatorFixture fixture)
{
    [Fact]
    public async Task New_accounts_keep_prompt_and_yes_is_persistent_monotonic_and_account_scoped()
    {
        var (database, store) = Create();
        Assert.True((await store.GetAsync("alice", default)).ShouldAskPlayedBefore);
        Assert.True((await store.GetAsync("alice", default)).ShouldAskPlayedBefore);
        Assert.Empty((await database.Collection("heartsAccountOnboarding").GetSnapshotAsync()).Documents);
        var now = DateTime.UtcNow;
        await Task.WhenAll(Enumerable.Range(0, 6).Select(_ => store.ConfirmExperienceAsync("alice", now, default)));
        var restarted = new FirestoreHeartsOnboardingStore(database);
        Assert.False((await restarted.GetAsync("alice", default)).ShouldAskPlayedBefore);
        Assert.True((await restarted.GetAsync("bruno", default)).ShouldAskPlayedBefore);
        var before = Assert.Single((await database.Collection("heartsAccountOnboarding").GetSnapshotAsync()).Documents);
        await store.CompleteTrialAsync("alice", Guid.NewGuid(), now.AddMinutes(1), default);
        await store.ConfirmExperienceAsync("alice", now.AddMinutes(2), default);
        var after = Assert.Single((await database.Collection("heartsAccountOnboarding").GetSnapshotAsync()).Documents);
        Assert.Equal(before.GetValue<Timestamp>("completedAt"), after.GetValue<Timestamp>("completedAt"));
        Assert.Equal("experienced", after.GetValue<string>("completionReason"));
    }

    [Fact]
    public async Task Completed_trial_records_its_authoritative_match_id_idempotently()
    {
        var (database, store) = Create();
        var matchId = Guid.NewGuid();
        await Task.WhenAll(Enumerable.Range(0, 6).Select(_ => store.CompleteTrialAsync("alice", matchId, DateTime.UtcNow, default)));
        Assert.False((await store.GetAsync("alice", default)).ShouldAskPlayedBefore);
        var recorded = Assert.Single((await database.Collection("heartsAccountOnboarding").GetSnapshotAsync()).Documents);
        Assert.Equal("trial-completed", recorded.GetValue<string>("completionReason"));
        Assert.Equal(matchId.ToString("N"), recorded.GetValue<string>("trialMatchId"));
        Assert.Equal("alice", recorded.GetValue<string>("userId"));
    }

    [Fact]
    public async Task An_actual_completed_trial_hand_persists_onboarding_at_trick_thirteen_only()
    {
        var (database, store) = Create();
        var clock = new TrialClock();
        var games = new HeartsGameService(new TrialRoster(), clock, store);
        var state = await games.StartTrialAsync("alice");
        var trialId = state.MatchId;
        Assert.True((await store.GetAsync("alice", default)).ShouldAskPlayedBefore);
        for (var action = 0; action < 120 && state.Phase != "complete"; action++)
        {
            if (state.CompletedTricks.Count == 12)
                Assert.True((await store.GetAsync("alice", default)).ShouldAskPlayedBefore);
            if (state.Phase == "passing" && !state.SubmittedPasses.Contains(state.HumanSeat))
                state = await games.PassAsync("alice", trialId, state.Hand.Take(3).Select(card => card.Code).ToArray());
            else if (state.Phase == "playing" && state.YourTurn)
                state = await games.PlayCardAsync("alice", trialId, state.LegalCards[0].Code);
            else
            {
                clock.Advance();
                await games.AdvanceDueManagedTurnsAsync();
                state = await games.GetAsync("alice", trialId);
            }
        }
        Assert.Equal(13, state.CompletedTricks.Count);
        Assert.Equal("complete", state.Phase);
        Assert.False((await new FirestoreHeartsOnboardingStore(database).GetAsync("alice", default)).ShouldAskPlayedBefore);
        Assert.True((await store.GetAsync("bruno", default)).ShouldAskPlayedBefore);
        var recorded = Assert.Single((await database.Collection("heartsAccountOnboarding").GetSnapshotAsync()).Documents);
        Assert.Equal(trialId.ToString("N"), recorded.GetValue<string>("trialMatchId"));
    }

    private (FirestoreDb Database, FirestoreHeartsOnboardingStore Store) Create()
    {
        var database = new FirestoreDbBuilder { ProjectId = $"demo-hearts-onboarding-{Guid.NewGuid():N}", Endpoint = fixture.Endpoint, ChannelCredentials = Grpc.Core.ChannelCredentials.Insecure, EmulatorDetection = EmulatorDetection.None }.Build();
        return (database, new FirestoreHeartsOnboardingStore(database));
    }

    private sealed class TrialClock : TimeProvider
    {
        private DateTimeOffset now = new(2026, 10, 21, 12, 0, 0, TimeSpan.Zero);
        public override DateTimeOffset GetUtcNow() => now;
        public void Advance() => now = now.AddSeconds(2);
    }

    private sealed class TrialRoster : IHeartsManagedPlayerRoster
    {
        public Task<IReadOnlyList<HeartsManagedPlayer>> ReserveAsync(string assignmentId, int count,
            IReadOnlyCollection<string> excluded, DateTime now, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<HeartsManagedPlayer>>([
                new("managed-east", "MossRiver", 3), new("managed-south", "CopperFinch", 3), new("managed-west", "NightWindow", 3)]);
        public Task HeartbeatAsync(string assignmentId, IReadOnlyCollection<string> ids, DateTime now, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task ReleaseAsync(string assignmentId, IReadOnlyCollection<string> ids, DateTime now, CancellationToken cancellationToken) => Task.CompletedTask;
    }
}
