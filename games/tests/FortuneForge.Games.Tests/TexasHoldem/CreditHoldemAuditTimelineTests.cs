using System.Text.Json;
using System.Text.Json.Nodes;
using FortuneForge.Games.TexasHoldem;

namespace FortuneForge.Games.Tests.TexasHoldem;

public sealed class CreditHoldemAuditTimelineTests
{
    private static readonly DateTime Start = new(2026, 10, 26, 12, 0, 0, DateTimeKind.Utc);
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    [Fact]
    public void DealRecordsEveryPrivateCardAndBlindAtTheSuppliedServerTime()
    {
        var match = Deal();

        Assert.Equal(1, match.EventTimelineVersion);
        Assert.Equal("hand-started", match.EventTimeline[0].Kind);
        var deals = match.EventTimeline.Where(value => value.Kind == "hole-card-dealt").ToArray();
        Assert.Equal(match.Deck.Take(6), deals.Select(value => value.Card));
        Assert.Equal(Enumerable.Range(0, 6), deals.Select(value => value.DeckIndex!.Value));
        Assert.All(deals, value => Assert.Contains(value.Card!,
            match.Players.Single(player => player.ActorId == value.ActorId).HoleCards));
        Assert.All(match.EventTimeline, value => Assert.Equal(Start, value.OccurredAtUtc));
        Assert.Equal(new[] { "small-blind", "big-blind" },
            match.EventTimeline.Where(value => value.Kind == "wager-committed").Select(value => value.Action));
        Assert.All(match.ActionLog, value =>
        {
            Assert.Equal(Start, value.OccurredAtUtc);
            Assert.Equal("server-deal", value.Source);
        });
        AssertSequence(match);
    }

    [Fact]
    public void AcceptedRaiseRetainsRequestedTargetAndActualCommittedAmount()
    {
        var match = Deal();
        var actor = match.Players.Single(player => player.Seat == match.ActiveSeat);
        var before = actor.CommittedHand;
        var now = Start.AddSeconds(2);

        var committed = CreditHoldemEngine.ApplyAction(match, actor.ActorId, "raise", 300, now);

        var accepted = Assert.Single(match.EventTimeline, value => value.Kind == "action-accepted");
        Assert.Equal(300, accepted.RequestedRaiseTo);
        Assert.Equal(committed, accepted.AmountCents);
        Assert.Equal(300 - before, committed);
        Assert.Equal(actor.CommittedHand, accepted.CommittedHandAfter);
        Assert.Equal(actor.Stack, accepted.StackAfter);
        Assert.Equal(now, accepted.OccurredAtUtc);
        Assert.Equal("account-action", accepted.Source);
        Assert.Equal(actor.PublicSeatId, accepted.PublicSeatId);
        Assert.Equal(now, match.ActionLog[^1].OccurredAtUtc);
        AssertSequence(match);
    }

    [Fact]
    public void ExpiredTurnRecordsTheDeadlineAndAutomaticActionWithoutInventingHumanTiming()
    {
        var match = Deal();
        var actor = match.Players.Single(player => player.Seat == match.ActiveSeat);
        var deadline = match.ActionDeadlineAtUtc!.Value;
        var observed = deadline.AddSeconds(3);

        Assert.True(CreditHoldemEngine.AdvanceExpiredTurn(match, observed));

        var timeout = Assert.Single(match.EventTimeline, value => value.Kind == "action-deadline-expired");
        var accepted = Assert.Single(match.EventTimeline, value => value.Kind == "action-accepted");
        Assert.Equal(deadline.ToString("O"), timeout.Detail);
        Assert.Equal(observed, timeout.OccurredAtUtc);
        Assert.Equal(observed, accepted.OccurredAtUtc);
        Assert.Equal(actor.ActorId, accepted.ActorId);
        Assert.Equal("server-timeout", accepted.Source);
        Assert.Equal("fold", accepted.Action);
        Assert.Equal("server-timeout", match.ActionLog[^1].Source);
        Assert.True(timeout.Sequence < accepted.Sequence);
        AssertSequence(match);
    }

    [Fact]
    public void FullHandRecordsBurnsBoardShowdownAndIdempotentSettlement()
    {
        var match = Deal();
        var actionCount = 0;
        while (match.Status == "active")
        {
            Assert.True(++actionCount < 30);
            var actor = match.Players.Single(player => player.Seat == match.ActiveSeat);
            var action = CreditHoldemEngine.LegalActions(match, actor).Contains("check") ? "check" : "call";
            CreditHoldemEngine.ApplyAction(match, actor.ActorId, action, null, Start.AddSeconds(actionCount));
        }
        var settledAt = match.CompletedAtUtc!.Value.AddSeconds(1);
        CreditHoldemEngine.ApplyFinancialSettlement(match, settledAt);
        var countAfterSettlement = match.EventTimeline.Count;
        CreditHoldemEngine.ApplyFinancialSettlement(match, settledAt.AddSeconds(1));

        Assert.Equal(countAfterSettlement, match.EventTimeline.Count);
        Assert.Equal(3, match.EventTimeline.Count(value => value.Kind == "card-burned"));
        Assert.Equal(match.Community, match.EventTimeline.Where(value => value.Kind == "community-card-dealt").Select(value => value.Card));
        var draws = match.EventTimeline.Where(value => value.DeckIndex is not null).ToArray();
        Assert.Equal(Enumerable.Range(0, match.NextCardIndex), draws.Select(value => value.DeckIndex!.Value));
        Assert.All(draws, value => Assert.Equal(match.Deck[value.DeckIndex!.Value], value.Card));
        Assert.Single(match.EventTimeline, value => value.Kind == "showdown-started");
        Assert.Single(match.EventTimeline, value => value.Kind == "hand-completed");
        Assert.Equal(match.Players.Sum(player => (long)player.CommittedHand),
            match.EventTimeline.Where(value => value.Kind == "pot-awarded").Sum(value => value.AmountCents!.Value));
        var settlement = Assert.Single(match.EventTimeline, value => value.Kind == "financial-settlement-completed");
        Assert.Equal(settledAt, settlement.OccurredAtUtc);
        Assert.Equal("supplied-settlement-time", settlement.Detail);
        AssertSequence(match);
    }

    [Fact]
    public void MatchDeadlineForcesCompletionAndLegacySettlementLabelsItsTimeBasis()
    {
        var match = Deal();
        var observed = match.MatchDeadlineAtUtc.AddSeconds(7);

        Assert.True(CreditHoldemEngine.AdvanceExpiredTurn(match, observed));
        CreditHoldemEngine.ApplyFinancialSettlement(match);

        Assert.Equal("completed", match.Status);
        var forced = Assert.Single(match.EventTimeline, value => value.Kind == "forced-completion");
        Assert.Equal("match-deadline", forced.Detail);
        Assert.Equal(observed, forced.OccurredAtUtc);
        Assert.All(match.EventTimeline.Where(value => value.Kind == "financial-settlement-completed"), value =>
        {
            Assert.Equal(observed, value.OccurredAtUtc);
            Assert.Equal("stored-round-completion-time", value.Detail);
        });
        AssertSequence(match);
    }

    [Fact]
    public void NextHandStartsItsOwnSequenceAndKeepsThePreviousEvidenceIntact()
    {
        var prior = Deal();
        CreditHoldemEngine.ForceComplete(prior, Start.AddSeconds(2));
        CreditHoldemEngine.ApplyFinancialSettlement(prior, Start.AddSeconds(3));
        var priorJson = JsonSerializer.Serialize(prior.EventTimeline, Json);
        var balances = prior.Players.ToDictionary(player => player.ActorId, _ => 5_000L);

        var next = CreditHoldemEngine.StartNextHand(prior, balances, 99, 1, Start.AddSeconds(5));

        Assert.NotNull(next);
        Assert.Equal(2, next.HandNumber);
        Assert.All(next.EventTimeline, value => Assert.Equal(2, value.HandNumber));
        Assert.Equal("hand-started", next.EventTimeline[0].Kind);
        Assert.Equal(priorJson, JsonSerializer.Serialize(prior.EventTimeline, Json));
        AssertSequence(next);
    }

    [Fact]
    public void LegacyDocumentsDoNotGainFabricatedHistoryOrActionTimes()
    {
        var current = Deal();
        var legacy = JsonNode.Parse(JsonSerializer.Serialize(current, Json))!.AsObject();
        legacy.Remove("eventTimeline");
        legacy.Remove("eventTimelineVersion");
        foreach (var action in legacy["actionLog"]!.AsArray())
        {
            action!.AsObject().Remove("occurredAtUtc");
            action.AsObject().Remove("source");
        }

        var restored = JsonSerializer.Deserialize<CreditHoldemMatch>(legacy.ToJsonString(), Json)!;

        Assert.Equal(0, restored.EventTimelineVersion);
        Assert.Empty(restored.EventTimeline);
        Assert.All(restored.ActionLog, value => Assert.Null(value.OccurredAtUtc));
        CreditHoldemEngine.AdvanceExpiredTurn(restored, restored.ActionDeadlineAtUtc!.Value);
        Assert.Equal(0, restored.EventTimelineVersion);
        Assert.DoesNotContain(restored.EventTimeline, value => value.Kind == "hole-card-dealt");
        AssertSequence(restored);
    }

    [Fact]
    public void RejectedActionDoesNotPretendToBeAnAcceptedEngineEvent()
    {
        var match = Deal();
        var before = JsonSerializer.Serialize(match, Json);
        var actor = match.Players.Single(player => player.Seat == match.ActiveSeat);

        Assert.Throws<CreditHoldemIllegalActionException>(() =>
            CreditHoldemEngine.ApplyAction(match, actor.ActorId, "raise", 101, Start.AddSeconds(1)));

        Assert.Equal(before, JsonSerializer.Serialize(match, Json));
    }

    [Fact]
    public void NonAccountSeatActionsAreDistinguishableFromAccountAndTimeoutActions()
    {
        var match = CreditHoldemEngine.Deal("mixed-table",
            Enumerable.Range(0, 3).Select(index => new CreditHoldemSeatAssignment(
                $"actor-{index}", $"seat-{index}", $"Player {index}", index != 0, index, 5_000)).ToArray(),
            "audit-test", 12345, Start);

        CreditHoldemEngine.ApplyAction(match, "actor-0", "call", null, Start.AddSeconds(1));

        var accepted = Assert.Single(match.EventTimeline, value => value.Kind == "action-accepted");
        Assert.Equal("automated-seat-action", accepted.Source);
        Assert.Equal("automated-seat-action", match.ActionLog[^1].Source);
    }

    [Fact]
    public void LeavingPreservesAnExplicitAccountDecisionAndItsResultingFold()
    {
        var match = Deal();
        var now = Start.AddSeconds(1);

        CreditHoldemEngine.Leave(match, "user-0", now);

        var requested = Assert.Single(match.EventTimeline, value => value.Kind == "leave-requested");
        var accepted = Assert.Single(match.EventTimeline, value => value.Kind == "action-accepted");
        Assert.Equal("user-0", requested.ActorId);
        Assert.Equal(now, accepted.OccurredAtUtc);
        Assert.Equal("account-action", accepted.Source);
        Assert.Equal("left", accepted.Action);
        Assert.Equal(0, accepted.AmountCents);
        Assert.Equal("folded", match.Players[0].Status);
        Assert.True(requested.Sequence < accepted.Sequence);
        AssertSequence(match);
    }

    private static CreditHoldemMatch Deal() => CreditHoldemEngine.Deal("audit-match",
        Enumerable.Range(0, 3).Select(index => new CreditHoldemSeatAssignment(
            $"user-{index}", $"seat-{index}", $"Player {index}", true, index, 5_000)).ToArray(),
        "audit-test", 12345, Start);

    private static void AssertSequence(CreditHoldemMatch match)
    {
        Assert.Equal(Enumerable.Range(1, match.EventTimeline.Count), match.EventTimeline.Select(value => value.Sequence));
        Assert.All(match.EventTimeline, value => Assert.Equal(match.HandNumber, value.HandNumber));
    }
}
