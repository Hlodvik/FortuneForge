using System.Text.Json.Nodes;
using FortuneForge.Games.Abstractions.Auditing;
using FortuneForge.Server.Bots.LiarsDice;
using FortuneForge.Server.Games.LiarsDice;
using FortuneForge.Server.SecurityAudit;
using Xunit;

namespace FortuneForge.Server.Tests.SecurityAudit;

public sealed class LiarsDiceEvidenceReplayAuditTests
{
    [Fact]
    public async Task Database_replays_seeded_dice_and_every_bid_challenge_and_new_round()
    {
        var clock = new Clock(); var journal = new Journal();
        var service = new LiarsDiceGameService(new Roster(), clock, null, journal);
        var match = await service.StartAsync("player-a", new(21, 1));
        for (var action = 0; action < 500 && match.Winner is null; action++)
        {
            if (match.Phase == "resolved") match = await service.NextRoundAsync("player-a", match.MatchId, (uint)(action + 1));
            else if (match.CurrentPlayerId == "you")
                match = match.CurrentBid is null
                    ? await service.BidAsync("player-a", match.MatchId, new(1, 1))
                    : await service.ChallengeAsync("player-a", match.MatchId);
            else
            {
                clock.Advance(TimeSpan.FromSeconds(2));
                await service.AdvanceDueManagedTurnsAsync();
                match = await service.GetAsync("player-a", match.MatchId);
            }
        }
        Assert.NotNull(match.Winner);
        var capture = Assert.Single(journal.Completions);
        Assert.Equal("liars-dice-private-hands-and-actions-v2", capture.Summary.EvidenceKind);
        var events = journal.Events.Select(item => (string)item.Fields["payloadJson"]).ToArray();

        var clean = LiarsDiceEvidenceReplayAudit.Inspect(capture.Summary, capture.PayloadJson, events);
        Assert.DoesNotContain(clean, finding => finding.Severity == "error");
        Assert.Contains(clean, finding => finding.Code == "liars-dice-in-memory-state-journal-non-atomic");

        var alteredSeed = events.ToArray();
        var first = JsonNode.Parse(alteredSeed[0])!;
        first["state"]!["roundSeed"] = 22;
        alteredSeed[0] = first.ToJsonString();
        Assert.Contains(LiarsDiceEvidenceReplayAudit.Inspect(capture.Summary, capture.PayloadJson, alteredSeed),
            finding => finding.Code == "liars-dice-initial-deal-mismatch");

        var alteredBid = events.ToArray();
        var bidIndex = Array.FindIndex(journal.Events.ToArray(), item => item.Fields["eventKind"] as string == "human-bid");
        Assert.True(bidIndex > 0);
        var bid = JsonNode.Parse(alteredBid[bidIndex])!;
        bid["state"]!["state"]!["round"]!["currentBid"]!["quantity"] = 100;
        alteredBid[bidIndex] = bid.ToJsonString();
        Assert.Contains(LiarsDiceEvidenceReplayAudit.Inspect(capture.Summary, capture.PayloadJson, alteredBid),
            finding => finding.Code == "liars-dice-action-replay-mismatch");
    }

    private sealed class Journal : IGameSecurityEventSink
    {
        public List<GameSecurityEventDocument> Events { get; } = [];
        public List<RoundAuditCapture> Completions { get; } = [];
        public Task WriteAsync(GameSecurityEventDocument observation, CancellationToken cancellationToken = default)
        { Events.Add(observation); return Task.CompletedTask; }
        public Task WriteCompletedAsync(GameSecurityEventDocument observation, RoundAuditCapture capture,
            CancellationToken cancellationToken = default)
        { Events.Add(observation); Completions.Add(capture); return Task.CompletedTask; }
    }

    private sealed class Roster : ILiarsDiceManagedPlayerRoster
    {
        public Task<IReadOnlyList<LiarsDiceManagedPlayer>> ReserveAsync(string assignmentId, int count,
            IReadOnlyCollection<string> excludedProfileIds, DateTime nowUtc, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<LiarsDiceManagedPlayer>>([
                new("managed-liars-a", "MossRiver", 4),
                new("managed-liars-b", "CopperFinch", 3),
                new("managed-liars-c", "NightWindow", 4)
            ]);
        public Task HeartbeatAsync(string assignmentId, IReadOnlyCollection<string> profileIds,
            DateTime nowUtc, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task ReleaseAsync(string assignmentId, IReadOnlyCollection<string> profileIds,
            DateTime nowUtc, CancellationToken cancellationToken) => Task.CompletedTask;
    }

    private sealed class Clock : TimeProvider
    {
        private DateTimeOffset now = new(2026, 10, 26, 12, 0, 0, TimeSpan.Zero);
        public override DateTimeOffset GetUtcNow() => now;
        public void Advance(TimeSpan amount) => now += amount;
    }
}
