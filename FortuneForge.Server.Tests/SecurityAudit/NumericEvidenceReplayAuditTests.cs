using System.Text.Json.Nodes;
using FortuneForge.Games.Abstractions.Auditing;
using FortuneForge.Games.Keno;
using FortuneForge.Games.SicBo;
using FortuneForge.Server.Dice.SicBo;
using FortuneForge.Server.Numbers.Keno;
using FortuneForge.Server.SecurityAudit;
using Xunit;

namespace FortuneForge.Server.Tests.SecurityAudit;

public sealed class NumericEvidenceReplayAuditTests
{
    [Fact]
    public void Database_recomputes_keno_ticket_draw_and_payout()
    {
        var round = new KenoStoreRound("round-1", "player-1", new KenoTicket([1]),
            new KenoDraw(Enumerable.Range(1, 20)), 1, 100, 250, "fortuneforge-keno-v3");
        var capture = Capture("keno", round.RoundId, round.UserId, round.WagerCents, round.PayoutCents,
            "kenoRounds", round, "keno-ticket-and-draw-v1", KenoOutcomeAudit.Version);
        Assert.Empty(NumericEvidenceReplayAudit.Inspect(capture.Summary, capture.PayloadJson));

        var changed = JsonNode.Parse(capture.PayloadJson)!;
        changed["data"]!["hitCount"] = 0;
        Assert.Contains(NumericEvidenceReplayAudit.Inspect(capture.Summary, changed.ToJsonString()),
            finding => finding.Code == "keno-draw-replay-mismatch");
    }

    [Fact]
    public void Database_recomputes_sic_bo_bets_dice_and_gross_return()
    {
        var round = new SicBoStoreRound("round-2", "player-2",
            [new SicBoStoredBet("small", 100, null, null, null, null)], new SicBoRoll(1, 2, 3));
        var capture = Capture("sic-bo", round.RoundId, round.UserId, 100, 200,
            "sicBoRounds", round, "sic-bo-bets-and-dice-v1", SicBoOutcomeAudit.Version);
        Assert.Empty(NumericEvidenceReplayAudit.Inspect(capture.Summary, capture.PayloadJson));

        var changed = JsonNode.Parse(capture.PayloadJson)!;
        changed["data"]!["roll"]!["values"]![0] = 6;
        Assert.Contains(NumericEvidenceReplayAudit.Inspect(capture.Summary, changed.ToJsonString()),
            finding => finding.Code == "sic-bo-dice-replay-mismatch");
    }

    private static RoundAuditCapture Capture(string gameId, string roundId, string userId,
        long wager, long payout, string sourceCollection, object data, string evidenceKind, string version)
    {
        var now = DateTime.SpecifyKind(new DateTime(2026, 10, 26), DateTimeKind.Utc);
        return RoundAuditCapture.Create(gameId, roundId, roundId, 1, now, now,
            new { sourceCollection, sourceDocumentId = roundId, wagerCents = wager,
                payoutCents = payout, netCents = payout - wager, data }, evidenceKind, version,
            [new RoundAuditParticipant(userId, userId, true, wager, payout, payout - wager)], []);
    }
}
