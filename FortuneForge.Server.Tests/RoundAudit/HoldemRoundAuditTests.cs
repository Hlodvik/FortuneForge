using System.Text.Json;
using FortuneForge.Games.Abstractions.Auditing;
using FortuneForge.Server.RoundAudit;
using Xunit;

namespace FortuneForge.Server.Tests.RoundAudit;

public sealed class HoldemRoundAuditTests
{
    private static readonly DateTime Start = new(2026, 10, 26, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void CleanHandCapturesPrivateDealAndActionsWithoutPuttingCardsInTheSummary()
    {
        var hand = Completed();
        var capture = HoldemRoundAudit.Capture(hand, Start.AddSeconds(20));
        Assert.Equal("incomplete", capture.Summary.AuditStatus);
        Assert.Contains(capture.Summary.Findings, finding => finding.Code == "holdem-replay-external-completion");
        Assert.Null(capture.Summary.EvidenceDeleteAfterUtc);
        Assert.Null(capture.Summary.ReviewedAtUtc);
        Assert.Equal(3, capture.Summary.Participants.Count);
        using var payload = JsonDocument.Parse(capture.PayloadJson);
        Assert.Equal(52, payload.RootElement.GetProperty("deck").GetArrayLength());
        Assert.Equal(5, payload.RootElement.GetProperty("community").GetArrayLength());
        Assert.Equal(2, payload.RootElement.GetProperty("actions").GetArrayLength());
        Assert.All(payload.RootElement.GetProperty("players").EnumerateArray(), player =>
            Assert.Equal(2, player.GetProperty("holeCards").GetArrayLength()));
        var summaryJson = JsonSerializer.Serialize(capture.Summary);
        Assert.DoesNotContain("HoleCards", summaryJson);
        Assert.DoesNotContain("DealSeed", summaryJson);
        var initialJson = capture.PayloadJson;
        hand.Community.Clear();
        hand.ActionLog.Clear();
        Assert.Equal(initialJson, capture.PayloadJson);
    }

    [Fact]
    public void DuplicatedCardsAndAlteredSeedAreFlaggedWithoutMutatingTheHand()
    {
        var hand = Completed();
        hand.Community[0] = hand.Players[0].HoleCards[0];
        var before = JsonSerializer.Serialize(hand);
        var capture = HoldemRoundAudit.Capture(hand, Start.AddSeconds(20));
        Assert.Equal("flagged", capture.Summary.AuditStatus);
        Assert.Contains(capture.Summary.Findings, finding => finding.Code == "invalid-dealt-cards");
        Assert.Equal(before, JsonSerializer.Serialize(hand));
        Assert.Null(capture.Summary.EvidenceDeleteAfterUtc);
    }

    [Fact]
    public void WrongActionsAndPayoutsAreFlaggedWithSpecificFindings()
    {
        var hand = Completed();
        hand.ActionLog[0] = hand.ActionLog[0] with { Amount = 1, PublicSeatId = "unknown" };
        hand.Players[0].AccountPayoutCents += 100;
        hand.Players[0].Stack += 100;
        var findings = HoldemRoundAudit.Check(hand);
        Assert.Contains(findings, finding => finding.Code == "invalid-action-order");
        Assert.Contains(findings, finding => finding.Code == "action-wager-mismatch");
        Assert.Contains(findings, finding => finding.Code == "stack-conservation");
        Assert.Contains(findings, finding => finding.Code == "human-payout-mismatch");
        Assert.Contains(findings, finding => finding.Code == "accounting-totals");
    }

    [Fact]
    public void MissingLegacyActionLogIsIncompleteRatherThanAnAccusation()
    {
        var hand = Completed();
        hand.ActionLog.Clear();
        hand.EventTimeline.Clear(); // A legacy hand lacks both the action log and the newer private timeline.
        var audit = HoldemRoundAudit.Capture(hand, Start.AddSeconds(20));
        Assert.Equal("incomplete", audit.Summary.AuditStatus);
        Assert.Contains(audit.Summary.Findings, finding => finding.Code == "missing-actions" && finding.Severity == "warning");
        Assert.Null(audit.Summary.EvidenceDeleteAfterUtc);
    }

    [Fact]
    public void CaptureIdIsStablePerRoundAndHashChangesWhenPrivateEvidenceChanges()
    {
        var hand = Completed();
        var first = HoldemRoundAudit.Capture(hand, Start.AddSeconds(20));
        var retry = HoldemRoundAudit.Capture(hand, Start.AddSeconds(21));
        Assert.Equal(first.Summary.Id, retry.Summary.Id);
        Assert.Equal(first.Summary.EvidenceSha256, retry.Summary.EvidenceSha256);
        hand.Players[0].Stack++;
        var altered = HoldemRoundAudit.Capture(hand, Start.AddSeconds(22));
        Assert.Equal(first.Summary.Id, altered.Summary.Id);
        Assert.NotEqual(first.Summary.EvidenceSha256, altered.Summary.EvidenceSha256);
        var documents = RoundAuditDocuments.Create(first);
        Assert.Null(documents.Summary["evidenceDeleteAfterUtc"]);
        Assert.Equal(first.PayloadJson, documents.Evidence["payloadJson"]);
    }

    [Fact]
    public void EveryHandInAnOpenTableGetsDifferentEvidenceIdentity()
    {
        var prior = Completed();
        var first = HoldemRoundAudit.Capture(prior, Start.AddSeconds(20));
        var next = CreditHoldemEngine.StartNextHand(prior, new Dictionary<string, long>(), 919, 1, Start.AddSeconds(21));
        Assert.NotNull(next);
        CreditHoldemEngine.ForceComplete(next, Start.AddSeconds(40));
        _ = CreditHoldemEngine.ApplyFinancialSettlement(next);
        var second = HoldemRoundAudit.Capture(next, Start.AddSeconds(40));
        Assert.Equal(first.Summary.TableId, second.Summary.TableId);
        Assert.NotEqual(first.Summary.Id, second.Summary.Id);
        Assert.Equal("incomplete", second.Summary.AuditStatus);
        Assert.Contains(second.Summary.Findings, finding => finding.Code == "holdem-replay-external-completion");
    }

    private static CreditHoldemMatch Completed()
    {
        var hand = CreditHoldemEngine.Deal("audit-table",
            Enumerable.Range(0, 3).Select(seat => new CreditHoldemSeatAssignment(
                $"actor-{seat}", $"seat-{seat}", $"Player {seat}", seat < 2, seat, 2_500,
                EscrowTicketId: seat < 2 ? $"reservation-{seat}" : null)).ToArray(),
            CreditHoldemMoney.OpenPartition, 913, Start, usesBuyInEscrow: true);
        CreditHoldemEngine.ForceComplete(hand, hand.MatchDeadlineAtUtc);
        _ = CreditHoldemEngine.ApplyFinancialSettlement(hand);
        return hand;
    }
}
