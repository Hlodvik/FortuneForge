using System.Text.Json.Nodes;
using FortuneForge.Games.Abstractions.Auditing;
using FortuneForge.Server.Games.Craps;
using FortuneForge.Server.Games.Roulette;
using FortuneForge.Server.Matchmaking;
using FortuneForge.Server.SecurityAudit;
using Xunit;

namespace FortuneForge.Server.Tests.SecurityAudit;

public sealed class CasinoPracticeActionReplayAuditTests
{
    [Fact]
    public async Task Craps_real_service_rolls_and_odds_replay_and_detect_tampered_placement()
    {
        RecordingSink? sink = null;
        CrapsGameService? service = null;
        CrapsRoundResponse? round = null;
        for (var attempt = 0; attempt < 25; attempt++)
        {
            var candidateSink = new RecordingSink();
            var candidateService = new CrapsGameService(securityEvents: candidateSink);
            var candidateRound = await candidateService.StartAuditedAsync("alice", new(2m, [new("field", 3m)]));
            candidateRound = await candidateService.RollAuditedAsync("alice", candidateRound.RoundId);
            if (candidateRound.Phase != "point") continue;
            sink = candidateSink;
            service = candidateService;
            round = candidateRound;
            break;
        }
        Assert.NotNull(sink);
        Assert.NotNull(service);
        Assert.NotNull(round);
        round = await service.PlaceOddsAuditedAsync("alice", round.RoundId, 5m);
        for (var roll = 0; roll < 1000 && round.Phase != "resolved"; roll++)
            round = await service.RollAuditedAsync("alice", round.RoundId);
        Assert.Equal("resolved", round.Phase);
        var completion = Assert.Single(sink.Completions);
        var events = Payloads(sink);
        Assert.True(CasinoPracticeActionReplayAudit.Supports(completion.Summary));
        var clean = CasinoPracticeActionReplayAudit.Inspect(completion.Summary, completion.PayloadJson, events);
        Assert.DoesNotContain(clean, finding => finding.Severity == "error");
        Assert.Contains(clean, finding => finding.Code == "casino-practice-in-memory-journal-non-atomic");
        Assert.Contains(clean, finding => finding.Code == "casino-practice-command-arguments-absent");

        var odds = JsonNode.Parse(events[2])!;
        odds["state"]!["extraBets"]![1]!["stake"] = 99m;
        events[2] = odds.ToJsonString();
        var tampered = CasinoPracticeActionReplayAudit.Inspect(completion.Summary, completion.PayloadJson, events);
        Assert.Contains(tampered, finding => finding.Severity == "error" &&
            finding.Code is "craps-odds-action-mismatch" or "craps-roll-action-mismatch");
    }

    [Fact]
    public async Task Roulette_real_service_bet_remove_clear_and_spin_replay_and_detect_balance_tampering()
    {
        var sink = new RecordingSink();
        var service = new RouletteGameService(new MultiplayerMatchmaker(), securityEvents: sink);
        var alice = await service.StartAuditedAsync("alice");
        var bob = await service.StartAuditedAsync("bob");
        Assert.Equal(alice.RoundId, bob.RoundId);
        await service.PlaceBetAuditedAsync("alice", alice.RoundId, new("red", 10m, null, []));
        await service.PlaceBetAuditedAsync("bob", bob.RoundId, new("black", 5m, null, []));
        await service.PlaceBetAuditedAsync("bob", bob.RoundId, new("even", 2m, null, []));
        using (GameSecurityContext.Push(new GameRequestObservation(
            "remove-bob-first-bet", "bob", null, null, DateTime.UtcNow, "DELETE",
            $"/api/games/roulette/rounds/{bob.RoundId:N}/bets/0", "roulette", null, null, null,
            null, "empty", "direct")))
            await service.RemoveBetAuditedAsync("bob", bob.RoundId, 0);
        await service.PlaceBetAuditedAsync("bob", bob.RoundId, new("black", 5m, null, []));
        await service.ClearBetsAuditedAsync("alice", alice.RoundId);
        await service.PlaceBetAuditedAsync("alice", alice.RoundId, new("red", 10m, null, []));
        await service.SpinAuditedAsync("alice", alice.RoundId);
        var completion = Assert.Single(sink.Completions);
        var events = Payloads(sink);
        Assert.True(CasinoPracticeActionReplayAudit.Supports(completion.Summary));
        var clean = CasinoPracticeActionReplayAudit.Inspect(completion.Summary, completion.PayloadJson, events);
        Assert.DoesNotContain(clean, finding => finding.Severity == "error");
        Assert.Contains(clean, finding => finding.Code == "roulette-managed-actions-not-fully-replayed");

        var wrongIndex = JsonNode.Parse(events[5])!;
        wrongIndex["request"]!["path"] = $"/api/games/roulette/rounds/{bob.RoundId:N}/bets/1";
        var pathTampered = events.ToList();
        pathTampered[5] = wrongIndex.ToJsonString();
        Assert.Contains(CasinoPracticeActionReplayAudit.Inspect(completion.Summary, completion.PayloadJson, pathTampered),
            finding => finding.Code == "roulette-remove-action-mismatch" && finding.Severity == "error");

        var firstBet = JsonNode.Parse(events[2])!;
        firstBet["state"]!["balances"]!["alice"] = 999m;
        events[2] = firstBet.ToJsonString();
        var tampered = CasinoPracticeActionReplayAudit.Inspect(completion.Summary, completion.PayloadJson, events);
        Assert.Contains(tampered, finding => finding.Code == "roulette-bet-action-mismatch" && finding.Severity == "error");
    }

    private static List<string> Payloads(RecordingSink sink) => sink.Events
        .Select(item => Assert.IsType<string>(item.Fields["payloadJson"])).ToList();

    private sealed class RecordingSink : IGameSecurityEventSink
    {
        public List<GameSecurityEventDocument> Events { get; } = [];
        public List<RoundAuditCapture> Completions { get; } = [];
        public Task WriteAsync(GameSecurityEventDocument observation, CancellationToken cancellationToken = default)
        {
            Events.Add(observation);
            return Task.CompletedTask;
        }
        public Task WriteCompletedAsync(GameSecurityEventDocument observation, RoundAuditCapture capture,
            CancellationToken cancellationToken = default)
        {
            Events.Add(observation);
            Completions.Add(capture);
            return Task.CompletedTask;
        }
    }
}
