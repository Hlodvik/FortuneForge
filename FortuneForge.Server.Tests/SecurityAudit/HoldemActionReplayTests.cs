using FortuneForge.Games.TexasHoldem;
using FortuneForge.Server.SecurityAudit;
using Xunit;

namespace FortuneForge.Server.Tests.SecurityAudit;

public sealed class HoldemActionReplayTests
{
    private static readonly DateTime Start = new(2026, 10, 26, 14, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void CompleteCallAndCheckHandHasIndependentlyReplayableBetting()
    {
        var match = PlayToCompletion(Deal());

        var result = HoldemActionReplay.Check(match);

        Assert.True(result.Complete);
        Assert.Empty(result.Findings);
    }

    [Fact]
    public void LegalLookingZeroCallThatPreservesEveryWagerTotalIsRejected()
    {
        var match = PlayToCompletion(Deal());
        var eventIndex = match.EventTimeline.FindIndex(entry => entry.Kind == "action-accepted" && entry.Action == "check");
        var original = match.EventTimeline[eventIndex];
        match.EventTimeline[eventIndex] = original with { Action = "call" };
        var logIndex = match.ActionLog.FindIndex(entry => entry.PublicSeatId == original.PublicSeatId && entry.OccurredAtUtc == original.OccurredAtUtc);
        match.ActionLog[logIndex] = match.ActionLog[logIndex] with { Action = "call" };
        Assert.All(match.Players, player => Assert.Equal(player.CommittedHand,
            match.ActionLog.Where(entry => entry.PublicSeatId == player.PublicSeatId).Sum(entry => entry.Amount)));

        var result = HoldemActionReplay.Check(match);

        Assert.Contains(result.Findings, finding => finding.Code == "holdem-replay-illegal-call");
    }

    [Fact]
    public void SwappingZeroWagerActorsCannotHideOutOfTurnPlay()
    {
        var match = PlayToCompletion(Deal());
        var checkIndexes = match.EventTimeline.Select((entry, index) => (entry, index))
            .Where(value => value.entry.Kind == "action-accepted" && value.entry.Action == "check").Take(2).ToArray();
        var first = checkIndexes[0];
        var second = checkIndexes[1];
        SwapActor(first.index, second.entry);
        SwapActor(second.index, first.entry);

        var result = HoldemActionReplay.Check(match);

        Assert.Contains(result.Findings, finding => finding.Code == "holdem-replay-out-of-turn");

        void SwapActor(int index, CreditHoldemAuditEvent replacement)
        {
            var original = match.EventTimeline[index];
            match.EventTimeline[index] = original with { ActorId = replacement.ActorId, PublicSeatId = replacement.PublicSeatId };
            var logIndex = match.ActionLog.FindIndex(entry => entry.PublicSeatId == original.PublicSeatId && entry.OccurredAtUtc == original.OccurredAtUtc);
            match.ActionLog[logIndex] = match.ActionLog[logIndex] with { PublicSeatId = replacement.PublicSeatId! };
        }
    }

    [Fact]
    public void CumulativeShortAllInsReopenTheEarlierCallerAfterAFullIncrement()
    {
        var match = Deal([500, 150, 200]);
        CreditHoldemEngine.ApplyAction(match, "user-0", "call", null, Start.AddSeconds(1));
        CreditHoldemEngine.ApplyAction(match, "user-1", "raise", 150, Start.AddSeconds(2));
        CreditHoldemEngine.ApplyAction(match, "user-2", "raise", 200, Start.AddSeconds(3));
        CreditHoldemEngine.ApplyAction(match, "user-0", "raise", 300, Start.AddSeconds(4));
        CreditHoldemEngine.ApplyFinancialSettlement(match, Start.AddSeconds(5));

        var result = HoldemActionReplay.Check(match);

        Assert.True(result.Complete);
        Assert.Empty(result.Findings);
    }

    [Fact]
    public void ShortRaiseThatIsNotAllInIsRejectedEvenWithAnAcceptedActionName()
    {
        var match = Deal();
        CreditHoldemEngine.ApplyAction(match, "user-0", "raise", 300, Start.AddSeconds(1));
        CreditHoldemEngine.ForceComplete(match, match.MatchDeadlineAtUtc, reason: "match-deadline");
        CreditHoldemEngine.ApplyFinancialSettlement(match, match.MatchDeadlineAtUtc);
        var eventIndex = match.EventTimeline.FindIndex(entry => entry.Kind == "action-accepted");
        match.EventTimeline[eventIndex] = match.EventTimeline[eventIndex] with { RequestedRaiseTo = 150 };

        var result = HoldemActionReplay.Check(match);

        Assert.Contains(result.Findings, finding => finding.Code == "holdem-replay-illegal-raise");
    }

    [Fact]
    public void ServerTimeoutAndMatchDeadlineRemainDistinctFromHumanActions()
    {
        var match = Deal();
        CreditHoldemEngine.AdvanceExpiredTurn(match, match.ActionDeadlineAtUtc!.Value);
        CreditHoldemEngine.AdvanceExpiredTurn(match, match.MatchDeadlineAtUtc);
        CreditHoldemEngine.ApplyFinancialSettlement(match, match.MatchDeadlineAtUtc);

        var result = HoldemActionReplay.Check(match);

        Assert.True(result.Complete);
        Assert.Empty(result.Findings);
    }

    [Fact]
    public void OutOfTurnLeaveAndAutomatedSeatActionsReplayWithoutTreatingThemAsHumanWagers()
    {
        var match = Deal(automatedSeat: 0);
        CreditHoldemEngine.ApplyAction(match, "user-0", "call", null, Start.AddSeconds(1));
        CreditHoldemEngine.Leave(match, "user-2", Start.AddSeconds(2));
        PlayToCompletion(match, 3);

        var result = HoldemActionReplay.Check(match);

        Assert.True(result.Complete);
        Assert.Empty(result.Findings);
    }

    [Fact]
    public void MissingLegacyTimelineAndUnknownConfigurationAreIncomplete()
    {
        var match = PlayToCompletion(Deal());
        var unknownRules = HoldemActionReplay.Check(match, new("unrecognized-engine", 50, 100, 10_000));
        var alteredRules = HoldemActionReplay.Check(match, new("credit-holdem-v1", 25, 50, 10_000));
        match.EventTimeline.Clear();
        var legacy = HoldemActionReplay.Check(match);

        Assert.False(unknownRules.Complete);
        Assert.False(alteredRules.Complete);
        Assert.False(legacy.Complete);
        Assert.Contains(legacy.Findings, finding => finding.Code == "holdem-replay-timeline-unavailable" && finding.Severity == "warning");
    }

    [Fact]
    public void MissingTurnStartCannotCertifyActionTimingCoverage()
    {
        var match = PlayToCompletion(Deal());
        match.EventTimeline.RemoveAt(match.EventTimeline.FindIndex(entry => entry.Kind == "turn-started"));
        for (var index = 0; index < match.EventTimeline.Count; index++)
            match.EventTimeline[index] = match.EventTimeline[index] with { Sequence = index + 1 };

        var result = HoldemActionReplay.Check(match);

        Assert.False(result.Complete);
        Assert.Contains(result.Findings, finding => finding.Code == "holdem-replay-turn-evidence-unavailable");
    }

    [Fact]
    public void ArbitraryHostForcedFinishRequiresAdditionalAuthorizationEvidence()
    {
        var match = Deal();
        CreditHoldemEngine.ForceComplete(match, Start.AddSeconds(1));
        CreditHoldemEngine.ApplyFinancialSettlement(match, Start.AddSeconds(2));

        var result = HoldemActionReplay.Check(match);

        Assert.False(result.Complete);
        Assert.Contains(result.Findings, finding => finding.Code == "holdem-replay-external-completion");
    }

    private static CreditHoldemMatch Deal(int[]? stacks = null, int automatedSeat = -1) => CreditHoldemEngine.Deal(
        "replay-match", Enumerable.Range(0, 3).Select(index => new CreditHoldemSeatAssignment(
            $"user-{index}", $"seat-{index}", $"Player {index}", index != automatedSeat, index,
            stacks?[index] ?? 5_000)).ToArray(), "replay-test", 12345, Start);

    private static CreditHoldemMatch PlayToCompletion(CreditHoldemMatch match, int nextSecond = 1)
    {
        for (var count = 0; match.Status == "active"; count++)
        {
            Assert.True(count < 30);
            var actor = match.Players.Single(player => player.Seat == match.ActiveSeat);
            var action = CreditHoldemEngine.LegalActions(match, actor).Contains("check") ? "check" : "call";
            CreditHoldemEngine.ApplyAction(match, actor.ActorId, action, null, Start.AddSeconds(nextSecond++));
        }
        CreditHoldemEngine.ApplyFinancialSettlement(match, Start.AddSeconds(nextSecond));
        return match;
    }
}
