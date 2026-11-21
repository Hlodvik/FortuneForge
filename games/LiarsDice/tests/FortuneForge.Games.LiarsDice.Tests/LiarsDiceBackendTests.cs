using System.Text.Json;
using FortuneForge.Games.Abstractions;
using FortuneForge.Games.Abstractions.Auditing;
using FortuneForge.Games.Dice;
using FortuneForge.Games.LiarsDice.Server;
using Google.Api.Gax;
using Google.Cloud.Firestore;
using Xunit;

namespace FortuneForge.Games.LiarsDice.Tests;

public sealed class LiarsDiceBackendTests
{
    [Fact]
    public void SavedPrivateMatchReplaysEveryHumanAndManagedDecisionThroughCompletion()
    {
        var now = new DateTime(2026, 11, 21, 12, 0, 0, DateTimeKind.Utc);
        var session = LiarsDiceSession.Start(Guid.NewGuid(), "alice", 77, 1, Players, now);
        for (var count = 0; count < 500 && session.State.Winner is null; count++)
        {
            now = now.AddSeconds(2);
            session = session.State.Round.Phase == LiarsDiceRoundPhase.Resolved ? session.NextRound((uint)(count + 1), now)
                : session.State.CurrentPlayerId == "you" ? session.HumanAction(session.State.Round.CurrentBid is null
                    ? new PlaceLiarsDiceBid("you", new(1, new DieValue(1))) : new ChallengeLiarsDiceBid("you"), now)
                : session.Advance(now);
            var json = JsonSerializer.Serialize(session, RoundAuditCapture.Json);
            session = JsonSerializer.Deserialize<LiarsDiceSession>(json, LiarsDiceStateJson.Options)!;
            Assert.All(session.State.Round.Hands.Values.SelectMany(dice => dice), die => Assert.InRange(die.Value, 1, 6));
            Assert.Empty(LiarsDiceSessionReplay.Check(session));
        }
        Assert.NotNull(session.State.Winner);
        Assert.Equal(session.State.RoundNumber, session.ToResponse().ResolvedRounds.Count);
        Assert.Equal("passed", LiarsDiceSessionReplay.Capture(session).Summary.AuditStatus);
        var tampered = session with { RoundSeed = session.RoundSeed + 1 };
        Assert.Contains(LiarsDiceSessionReplay.Check(tampered), finding => finding.Severity == "error");
        Assert.Contains(LiarsDiceSessionReplay.Check(session with { State = null! }), finding => finding.Severity == "error");
    }

    [FirestoreEmulatorFact]
    public async Task DurableReplicasPreservePrivateHandsExactRetryAndOneCompletion()
    {
        var database = new FirestoreDbBuilder { ProjectId = $"demo-liars-{Guid.NewGuid():N}"[..29],
            EmulatorDetection = EmulatorDetection.EmulatorOnly }.Build();
        var roster = new Roster(); var clock = new Clock();
        var first = new LiarsDiceGameService(database, roster, clock);
        var replica = new LiarsDiceGameService(database, roster, clock);
        var start = Request("start", "/api/games/liars-dice/matches");
        LiarsDiceMatchResponse match;
        using (GameSecurityContext.Push(start))
            match = await first.StartAsync("alice", new(77, 1), default, new string('a', 64));
        Assert.True(PuzzleSessionIdentity.IsDurableId(match.MatchId));
        using (GameSecurityContext.Push(start with { RequestId = "lost-start-response" }))
            Assert.Equal(match.MatchId, (await replica.StartAsync("alice", new(77, 1), default, new string('a', 64))).MatchId);
        Assert.Single(roster.ActiveAssignments);
        Assert.Equal(1, roster.ReserveCalls);
        await Assert.ThrowsAsync<LiarsDiceAccessException>(() => replica.GetAsync("bob", match.MatchId));
        Assert.Equal(match.Hand, (await replica.GetAsync("alice", match.MatchId)).Hand);

        var bid = Request("bid-1", $"/api/games/liars-dice/matches/{match.MatchId:D}/bid");
        using (GameSecurityContext.Push(bid))
            match = await first.BidAsync("alice", match.MatchId, new(1, 1), default, new string('b', 64));
        using (GameSecurityContext.Push(bid with { RequestId = "retry-bid" }))
        {
            var retry = await replica.BidAsync("alice", match.MatchId, new(1, 1), default, new string('b', 64));
            Assert.Equal(match.CurrentPlayerId, retry.CurrentPlayerId);
            await Assert.ThrowsAsync<LiarsDiceRuleException>(() => replica.BidAsync("alice", match.MatchId, new(2, 1), default, new string('c', 64)));
        }
        for (var count = 0; count < 500 && match.Winner is null; count++)
        {
            clock.Advance();
            if (match.Phase == "resolved") match = await replica.NextRoundAsync("alice", match.MatchId, (uint)(count + 1));
            else if (match.CurrentPlayerId == "you") match = match.CurrentBid is null
                ? await first.BidAsync("alice", match.MatchId, new(1, 1)) : await first.ChallengeAsync("alice", match.MatchId);
            else { await replica.AdvanceDueManagedTurnsAsync(); match = await first.GetAsync("alice", match.MatchId); }
        }
        Assert.NotNull(match.Winner);
        Assert.Equal(match.RoundNumber, (await replica.GetAsync("alice", match.MatchId)).ResolvedRounds.Count);
        Assert.Empty(roster.ActiveAssignments);
        var summaries = await database.Collection(RoundAuditDocuments.Summaries).GetSnapshotAsync();
        var summary = Assert.Single(summaries.Documents);
        Assert.Equal("passed", summary.GetValue<string>("auditStatus"));
        Assert.Equal(LiarsDiceSessionReplay.EvidenceKind, summary.GetValue<string>("evidenceKind"));
        var privateEvidence = await database.Collection(RoundAuditDocuments.Evidence).Document(summary.Id).GetSnapshotAsync();
        var saved = JsonSerializer.Deserialize<LiarsDiceSession>(privateEvidence.GetValue<string>("payloadJson"), LiarsDiceStateJson.Options)!;
        Assert.Empty(LiarsDiceSessionReplay.Check(saved));
        var storedSummary = RoundAuditCapture.Create("liars-dice", match.MatchId.ToString("N"), match.MatchId.ToString("N"),
            match.RoundNumber, summary.GetValue<Timestamp>("startedAtUtc").ToDateTime(),
            summary.GetValue<Timestamp>("completedAtUtc").ToDateTime(), saved, LiarsDiceSessionReplay.EvidenceKind,
            LiarsDiceSessionReplay.Version, [new RoundAuditParticipant("alice", "alice", true, 0, 0, 0)], []).Summary;
        Assert.Empty(LiarsDiceSessionReplay.Inspect(storedSummary, privateEvidence.GetValue<string>("payloadJson")));
        Assert.Single((await database.Collection(FortuneForge.Games.Abstractions.Analytics.GameAnalyticsOutbox.CollectionName).GetSnapshotAsync()).Documents);
    }

    private static readonly LiarsDiceManagedPlayer[] Players = [new("bot-a", "MossRiver", 2), new("bot-b", "CopperFinch", 3), new("bot-c", "NightWindow", 4)];
    private static GameRequestObservation Request(string command, string path) => new(Guid.NewGuid().ToString("N"), "alice",
        null, null, DateTime.UtcNow, "POST", path, "liars-dice", null, command, null, null, "sanitized", "direct");
    private sealed class Clock : TimeProvider
    {
        private DateTimeOffset now = new DateTimeOffset(2026, 11, 21, 12, 0, 0, TimeSpan.Zero).AddTicks(7);
        public override DateTimeOffset GetUtcNow() => now;
        public void Advance() => now = now.AddSeconds(2);
    }
    private sealed class Roster : ILiarsDiceManagedPlayerRoster
    {
        public int ReserveCalls { get; private set; }
        public HashSet<string> ActiveAssignments { get; } = [];
        public Task<IReadOnlyList<LiarsDiceManagedPlayer>> ReserveAsync(string assignmentId, int count,
            IReadOnlyCollection<string> excludedProfileIds, DateTime nowUtc, CancellationToken token)
        { ReserveCalls++; ActiveAssignments.Add(assignmentId); return Task.FromResult<IReadOnlyList<LiarsDiceManagedPlayer>>(Players); }
        public Task HeartbeatAsync(string assignmentId, IReadOnlyCollection<string> profileIds, DateTime nowUtc, CancellationToken token) => Task.CompletedTask;
        public Task ReleaseAsync(string assignmentId, IReadOnlyCollection<string> profileIds, DateTime nowUtc, CancellationToken token)
        { ActiveAssignments.Remove(assignmentId); return Task.CompletedTask; }
    }
}

public sealed class FirestoreEmulatorFactAttribute : FactAttribute
{
    public FirestoreEmulatorFactAttribute()
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("FIRESTORE_EMULATOR_HOST")))
            Skip = "A Firestore emulator is required for durable Liar's Dice verification.";
    }
}
