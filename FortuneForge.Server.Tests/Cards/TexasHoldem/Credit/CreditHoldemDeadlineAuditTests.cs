using System.Text.Json;
using System.Text.Json.Nodes;
using FortuneForge.Games.TexasHoldem;
using FortuneForge.Server.Cards.TexasHoldem.Credit;
using FortuneForge.Server.RoundAudit;
using FortuneForge.Server.SecurityAudit;
using Xunit;

namespace FortuneForge.Server.Tests.Cards.TexasHoldem.Credit;

public sealed class CreditHoldemDeadlineAuditTests
{
    private static readonly DateTime Start = new(2026, 11, 1, 12, 0, 0, DateTimeKind.Utc);

    [Theory]
    [InlineData(false, false, 0)]
    [InlineData(true, false, 1)]
    [InlineData(false, true, 1)]
    [InlineData(true, true, 0)]
    public void DeadlineCompletionPreservesTheDealAndMoneyWithReplayableProvenance(
        bool activeIsManaged, bool usesBuyInEscrow, int millisecondsAfterDeadline)
    {
        var match = Deal(activeIsManaged, usesBuyInEscrow);
        var priorCompletion = Deal(activeIsManaged, usesBuyInEscrow);
        var at = match.MatchDeadlineAtUtc.AddMilliseconds(millisecondsAfterDeadline);
        var originalVersion = match.Version;

        var advance = CreditHoldemTableCoordinator.AdvanceIfDue(match, at);
        _ = CreditHoldemEngine.ApplyFinancialSettlement(match, at);

        Assert.True(advance.Changed);
        Assert.Empty(advance.ReleasedHumanActorIds);
        Assert.Equal("completed", match.Status);
        Assert.Equal(originalVersion + 1, match.Version);
        Assert.Equal(at, match.CompletedAtUtc);
        var deadline = Assert.Single(match.EventTimeline, entry => entry.Kind == "match-deadline-expired");
        Assert.Equal("server-timeout", deadline.Source);
        Assert.Equal(at, deadline.OccurredAtUtc);
        Assert.Equal(match.MatchDeadlineAtUtc.ToString("O"), deadline.Detail);
        var completion = Assert.Single(match.EventTimeline, entry => entry.Kind == "forced-completion");
        Assert.Equal("match-deadline", completion.Detail);
        Assert.Equal("server-progress", completion.Source);
        Assert.Equal(deadline.Sequence + 1, completion.Sequence);
        Assert.DoesNotContain(match.EventTimeline, entry => entry.Kind is "action-deadline-expired" or "action-accepted");

        // The coordinator previously used this completion directly. Its cards,
        // awards, commitments and settlement stay unchanged by the provenance fix.
        CreditHoldemEngine.ForceComplete(priorCompletion, at);
        _ = CreditHoldemEngine.ApplyFinancialSettlement(priorCompletion, at);
        Assert.Equal(FinancialAndDealSnapshot(priorCompletion), FinancialAndDealSnapshot(match));
        Assert.Equal(match.Players.Sum(player => (long)player.CommittedHand),
            match.Players.Sum(player => (long)player.WonHandChips));

        var replay = HoldemActionReplay.Check(match);
        Assert.True(replay.Complete);
        Assert.Empty(replay.Findings);
        Assert.Equal("passed", HoldemRoundAudit.Capture(match, at).Summary.AuditStatus);
        var unsupported = HoldemActionReplay.Check(priorCompletion);
        Assert.False(unsupported.Complete);
        Assert.Contains(unsupported.Findings, finding => finding.Code == "holdem-replay-external-completion");
    }

    [Fact]
    public void PollBeforeTheHumanActionAndMatchDeadlinesDoesNotInventDeadlineEvidence()
    {
        var match = Deal(activeIsManaged: false, usesBuyInEscrow: true);
        var before = JsonSerializer.Serialize(match);

        var advance = CreditHoldemTableCoordinator.AdvanceIfDue(match, Start.AddSeconds(1));

        Assert.False(advance.Changed);
        Assert.Empty(advance.ReleasedHumanActorIds);
        Assert.Equal(before, JsonSerializer.Serialize(match));
        Assert.DoesNotContain(match.EventTimeline, entry => entry.Kind == "match-deadline-expired");
    }

    [Fact]
    public void DeadlineAndSettlementRetriesRetainTheFirstPrivateHandAcrossTheNextDeal()
    {
        var match = Deal(activeIsManaged: true, usesBuyInEscrow: true);
        var at = match.MatchDeadlineAtUtc;
        Assert.True(CreditHoldemTableCoordinator.AdvanceIfDue(match, at).Changed);
        _ = CreditHoldemEngine.ApplyFinancialSettlement(match, at);
        var original = HoldemRoundAudit.Capture(match, at);
        Assert.Equal("passed", original.Summary.AuditStatus);
        var events = match.EventTimeline.ToArray();
        var originalVersion = match.Version;
        var originalMoney = FinancialAndDealSnapshot(match);

        for (var retry = 1; retry <= 3; retry++)
        {
            var observed = at.AddSeconds(retry);
            Assert.False(CreditHoldemTableCoordinator.AdvanceIfDue(match, observed).Changed);
            _ = CreditHoldemEngine.ApplyFinancialSettlement(match, observed);
            var repeated = HoldemRoundAudit.Capture(match, observed);
            Assert.Equal(original.Summary.Id, repeated.Summary.Id);
            Assert.Equal(original.Summary.EvidenceSha256, repeated.Summary.EvidenceSha256);
            Assert.Equal(original.PayloadJson, repeated.PayloadJson);
        }
        Assert.Equal(originalVersion, match.Version);
        Assert.Equal(events, match.EventTimeline.ToArray());
        Assert.Equal(originalMoney, FinancialAndDealSnapshot(match));
        Assert.Single(match.EventTimeline, entry => entry.Kind == "financial-settlement-completed");
        Assert.Equal(match.Players.Count(player => player.IsAccountBacked),
            match.EventTimeline.Count(entry => entry.Kind == "financial-payout-recognized"));

        var next = CreditHoldemEngine.StartNextHand(match, new Dictionary<string, long>(), 777,
            minimumHumans: 1, nowUtc: at.AddSeconds(4));
        Assert.NotNull(next);
        Assert.True(CreditHoldemTableCoordinator.AdvanceIfDue(next, next.MatchDeadlineAtUtc).Changed);
        _ = CreditHoldemEngine.ApplyFinancialSettlement(next, next.MatchDeadlineAtUtc);
        var nextCapture = HoldemRoundAudit.Capture(next, next.MatchDeadlineAtUtc);
        Assert.Equal("passed", nextCapture.Summary.AuditStatus);
        Assert.Equal(original.Summary.TableId, nextCapture.Summary.TableId);
        Assert.NotEqual(original.Summary.Id, nextCapture.Summary.Id);
        Assert.NotEqual(original.Summary.EvidenceSha256, nextCapture.Summary.EvidenceSha256);
        Assert.Equal(original.PayloadJson, HoldemRoundAudit.Capture(match, next.MatchDeadlineAtUtc).PayloadJson);
        using var payload = JsonDocument.Parse(original.PayloadJson);
        Assert.Equal(1, payload.RootElement.GetProperty("handNumber").GetInt32());
        Assert.Equal(52, payload.RootElement.GetProperty("deck").GetArrayLength());
        Assert.All(payload.RootElement.GetProperty("players").EnumerateArray(), player =>
            Assert.Equal(2, player.GetProperty("holeCards").GetArrayLength()));
        Assert.DoesNotContain("HoleCards", JsonSerializer.Serialize(original.Summary));
        Assert.DoesNotContain("DealSeed", JsonSerializer.Serialize(original.Summary));
    }

    [Fact]
    public void LegalEscrowWinningsAboveTheBuyInCapRemainAuditableAfterTheNextDeal()
    {
        var match = Deal(activeIsManaged: false, usesBuyInEscrow: true, startingStack: 10_000);
        // The folded seat's committed chips guarantee a winner gains chips even
        // if the remaining two hands tie, without prescribing any card outcome.
        CreditHoldemEngine.ApplyAction(match, "actor-0", CreditHoldemActions.Call, null, Start.AddSeconds(1));
        CreditHoldemEngine.ApplyAction(match, "actor-1", CreditHoldemActions.Call, null, Start.AddSeconds(2));
        CreditHoldemEngine.ApplyAction(match, "actor-2", CreditHoldemActions.Check, null, Start.AddSeconds(3));
        CreditHoldemEngine.ApplyAction(match, "actor-1", CreditHoldemActions.Raise, 100, Start.AddSeconds(4));
        CreditHoldemEngine.ApplyAction(match, "actor-2", CreditHoldemActions.Fold, null, Start.AddSeconds(5));
        CreditHoldemEngine.ApplyAction(match, "actor-0", CreditHoldemActions.Call, null, Start.AddSeconds(6));
        Assert.True(CreditHoldemTableCoordinator.AdvanceIfDue(match, match.MatchDeadlineAtUtc).Changed);
        _ = CreditHoldemEngine.ApplyFinancialSettlement(match, match.MatchDeadlineAtUtc);
        var priorCapture = HoldemRoundAudit.Capture(match, match.MatchDeadlineAtUtc);
        Assert.Equal("passed", priorCapture.Summary.AuditStatus);
        var cap = CreditHoldemTableRules.Resolve(match.TableRuleId).MaximumStackCents;
        Assert.Contains(match.Players, player => player.Stack > cap);
        var endingStacks = match.Players.ToDictionary(player => player.ActorId, player => player.Stack);

        var next = CreditHoldemEngine.StartNextHand(match,
            new Dictionary<string, long> { ["actor-0"] = 0, ["actor-1"] = 1_000_000 }, 777,
            minimumHumans: 1, nowUtc: match.MatchDeadlineAtUtc.AddSeconds(1));
        Assert.NotNull(next);
        Assert.Contains(next.Players, player => player.StartingStack > cap);
        Assert.All(next.Players, player => Assert.Equal(endingStacks[player.ActorId], player.StartingStack));
        Assert.True(CreditHoldemTableCoordinator.AdvanceIfDue(next, next.MatchDeadlineAtUtc).Changed);
        _ = CreditHoldemEngine.ApplyFinancialSettlement(next, next.MatchDeadlineAtUtc);
        var beforeReplay = FinancialAndDealSnapshot(next);

        var replay = HoldemActionReplay.Check(next);
        var capture = HoldemRoundAudit.Capture(next, next.MatchDeadlineAtUtc);

        Assert.True(replay.Complete);
        Assert.Empty(replay.Findings);
        Assert.Equal("passed", capture.Summary.AuditStatus);
        Assert.Equal(beforeReplay, FinancialAndDealSnapshot(next));
        Assert.Equal(next.Players.Sum(player => (long)player.CommittedHand),
            next.Players.Sum(player => (long)player.WonHandChips));
        Assert.Equal(priorCapture.PayloadJson, HoldemRoundAudit.Capture(match, next.MatchDeadlineAtUtc).PayloadJson);
        Assert.NotEqual(priorCapture.Summary.Id, capture.Summary.Id);
        Assert.NotEqual(priorCapture.Summary.EvidenceSha256, capture.Summary.EvidenceSha256);
    }

    [Theory]
    [InlineData(false, 1, 10_001)]
    [InlineData(false, 2, 10_001)]
    [InlineData(true, 1, 10_001)]
    [InlineData(true, 0, 10_001)]
    [InlineData(true, 2, -1)]
    public void InvalidOriginalStacksStillFailReplay(bool usesBuyInEscrow, int handNumber, int startingStack)
    {
        var match = Deal(activeIsManaged: false, usesBuyInEscrow: usesBuyInEscrow);
        Assert.True(CreditHoldemTableCoordinator.AdvanceIfDue(match, match.MatchDeadlineAtUtc).Changed);
        _ = CreditHoldemEngine.ApplyFinancialSettlement(match, match.MatchDeadlineAtUtc);
        var recorded = WithRecordedStartingStack(match, startingStack);
        SetRecordedHandNumber(recorded, handNumber);

        var result = HoldemActionReplay.Check(recorded);

        Assert.False(result.Complete);
        Assert.Contains(result.Findings, finding => finding.Code == "holdem-replay-initial-state" && finding.Severity == "error");
        Assert.Equal("flagged", HoldemRoundAudit.Capture(recorded, recorded.MatchDeadlineAtUtc).Summary.AuditStatus);
    }

    [Fact]
    public void EscrowCarryoverDoesNotPermitAnUnoccupiedDealerSeat()
    {
        var match = Deal(activeIsManaged: false, usesBuyInEscrow: true);
        Assert.True(CreditHoldemTableCoordinator.AdvanceIfDue(match, match.MatchDeadlineAtUtc).Changed);
        _ = CreditHoldemEngine.ApplyFinancialSettlement(match, match.MatchDeadlineAtUtc);
        SetRecordedHandNumber(match, 2);
        match.DealerSeat = 42;

        var result = HoldemActionReplay.Check(match);

        Assert.False(result.Complete);
        Assert.Contains(result.Findings, finding => finding.Code == "holdem-replay-initial-state" && finding.Severity == "error");
    }

    private static CreditHoldemMatch WithRecordedStartingStack(CreditHoldemMatch match, int startingStack)
    {
        var document = JsonNode.Parse(JsonSerializer.Serialize(match))!;
        document["Players"]![0]!["StartingStack"] = startingStack;
        return JsonSerializer.Deserialize<CreditHoldemMatch>(document.ToJsonString())!;
    }

    private static void SetRecordedHandNumber(CreditHoldemMatch match, int handNumber)
    {
        match.HandNumber = handNumber;
        for (var index = 0; index < match.EventTimeline.Count; index++)
            match.EventTimeline[index] = match.EventTimeline[index] with { HandNumber = handNumber };
        for (var index = 0; index < match.ActionLog.Count; index++)
            match.ActionLog[index] = match.ActionLog[index] with { HandNumber = handNumber };
    }

    private static CreditHoldemMatch Deal(bool activeIsManaged, bool usesBuyInEscrow, int startingStack = 5_000) =>
        CreditHoldemEngine.Deal("deadline-table", Enumerable.Range(0, 3).Select(seat =>
            new CreditHoldemSeatAssignment($"actor-{seat}", $"seat-{seat}", $"Player {seat}",
                seat != 0 || !activeIsManaged, seat, startingStack,
                EscrowTicketId: seat != 0 || !activeIsManaged ? $"reservation-{seat}" : null)).ToArray(),
            CreditHoldemMoney.OpenPartition, 12345, Start, usesBuyInEscrow: usesBuyInEscrow);

    private static string FinancialAndDealSnapshot(CreditHoldemMatch match) => JsonSerializer.Serialize(new
    {
        match.MatchId, match.HandNumber, match.DealSeed, match.Deck, match.Community, match.NextCardIndex,
        match.Status, match.Street, match.Version, match.CompletedAtUtc, match.ActionDeadlineAtUtc,
        match.AccountingSettled, match.HumanCommittedCents, match.HumanPayoutCents, match.HouseNetCents,
        payouts = match.HumanPayoutsCents.OrderBy(pair => pair.Key).ToArray(),
        actions = match.ActionLog.ToArray(),
        players = match.Players.Select(player => new
        {
            player.ActorId, player.HoleCards, player.StartingStack, player.Stack,
            player.CommittedHand, player.CommittedRound, player.Status, player.LastAction,
            player.WonHandChips, player.AccountPayoutCents, player.RevealAtShowdown
        }).ToArray()
    });
}
