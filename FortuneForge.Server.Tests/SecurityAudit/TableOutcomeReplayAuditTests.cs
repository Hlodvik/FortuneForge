using System.Text.Json.Nodes;
using FortuneForge.Games.Abstractions.Analytics;
using FortuneForge.Games.Blackjack;
using FortuneForge.Games.ThreeCardPoker;
using FortuneForge.Server.RoundAudit;
using FortuneForge.Server.SecurityAudit;
using Xunit;

namespace FortuneForge.Server.Tests.SecurityAudit;

public sealed class TableOutcomeReplayAuditTests
{
    private static readonly DateTime Started = new(2026, 10, 26, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void ThreeCardPokerReplaysSavedDealAndGrossReturnAndDetectsTampering()
    {
        var table = new ThreeCardPokerTableState
        {
            TableId = "table-replay", CreatedAtUtc = Started, UpdatedAtUtc = Started,
            Players = [new ThreeCardPokerTablePlayer
            {
                ActorId = "alice", PublicSeatId = "seat-alice", DisplayName = "Alice", Seat = 0,
                SessionId = "session", SessionStartedAtUtc = Started,
                NextWagerCents = 500, NextPairPlusCents = 100
            }]
        };
        var prefix = new[] { "2|hearts", "Q|clubs", "2|clubs", "9|spades", "4|spades", "3|hearts" };
        var deck = prefix.Concat(ThreeCardPokerRules.CreateShuffledDeck().Where(card => !prefix.Contains(card))).ToArray();
        ThreeCardPokerTableEngine.Deal(table, deck, 11, Started);
        ThreeCardPokerTableEngine.ApplyAction(table, "alice", ThreeCardPokerActions.Fold, Started.AddMilliseconds(10));
        ThreeCardPokerTableEngine.AdvanceAutomatedTurns(table, Started.AddSeconds(1));
        ThreeCardPokerTableEngine.AdvanceAutomatedTurns(table, Started.AddSeconds(2));
        table.RoundAccountingSettled = true;
        var capture = TableRoundAudit.Capture(table, Started.AddSeconds(2));
        Assert.DoesNotContain(TableOutcomeReplayAudit.Inspect(capture.Summary, capture.PayloadJson),
            finding => finding.Severity == "error");

        var changedReturn = JsonNode.Parse(capture.PayloadJson)!;
        changedReturn["players"]![0]!["payoutCents"] = 9999;
        Assert.Contains(TableOutcomeReplayAudit.Inspect(capture.Summary, changedReturn.ToJsonString()),
            finding => finding.Code == "three-card-poker-return-mismatch");

        var changedDeal = JsonNode.Parse(capture.PayloadJson)!;
        changedDeal["dealerCards"]![0] = "A|clubs";
        Assert.Contains(TableOutcomeReplayAudit.Inspect(capture.Summary, changedDeal.ToJsonString()),
            finding => finding.Code == "three-card-poker-dealer-deal");
    }

    [Fact]
    public void BlackjackTableReplaysEngineDealDealerDrawAndSettlementAndDetectsTampering()
    {
        var table = new BlackjackTableState
        {
            TableId = "blackjack-table-replay", CreatedAtUtc = Started, UpdatedAtUtc = Started,
            Players = [new BlackjackTablePlayer
            {
                ActorId = "alice", PublicSeatId = "seat-alice", DisplayName = "Alice", Seat = 0,
                SessionId = "session", SessionStartedAtUtc = Started, NextWagerCents = 500
            }]
        };
        var deck = BlackjackDeck("10|hearts", "6|clubs", "7|spades", "9|diamonds", "3|hearts");
        BlackjackTableEngine.Deal(table, deck, 11, Started);
        BlackjackTableEngine.ApplyAction(table, "alice", BlackjackActions.Stand, Started.AddMilliseconds(10));
        for (var second = 1; second <= 5; second++)
            BlackjackTableEngine.AdvanceAutomatedTurns(table, Started.AddSeconds(second));
        Assert.Equal("settlement", table.Phase);
        table.RoundAccountingSettled = true;
        var capture = TableRoundAudit.Capture(table, Started.AddSeconds(5));
        Assert.DoesNotContain(TableOutcomeReplayAudit.Inspect(capture.Summary, capture.PayloadJson),
            finding => finding.Severity == "error");

        var changedReturn = JsonNode.Parse(capture.PayloadJson)!;
        changedReturn["players"]![0]!["payoutCents"] = 9999;
        Assert.Contains(TableOutcomeReplayAudit.Inspect(capture.Summary, changedReturn.ToJsonString()),
            finding => finding.Code == "blackjack-return-mismatch");

        var changedDeal = JsonNode.Parse(capture.PayloadJson)!;
        changedDeal["dealerCards"]![2] = "A|clubs";
        Assert.Contains(TableOutcomeReplayAudit.Inspect(capture.Summary, changedDeal.ToJsonString()),
            finding => finding.Code == "blackjack-deal-mismatch");
    }

    [Fact]
    public void LegacyBlackjackReplaysEngineDealAndFinalActionAndDetectsTampering()
    {
        var deck = BlackjackDeck("10|hearts", "6|clubs", "7|spades", "9|diamonds", "3|hearts");
        var game = BlackjackRules.Deal("legacy-replay", "alice", 500, deck, Started);
        game = BlackjackRules.ApplyAction(game, BlackjackActions.Stand, Started.AddSeconds(1));
        var record = SinglePlayerRoundRecord.Create("blackjack", game.GameId, game.UserId,
            game.TotalWagerCents, game.PayoutCents, game.CreatedAtUtc, game.UpdatedAtUtc,
            "blackjackGames", game.GameId, new { game, terminalAction = BlackjackActions.Stand },
            "legacy-blackjack-cards-and-final-action-v1");
        var capture = record.Audit;
        Assert.DoesNotContain(TableOutcomeReplayAudit.Inspect(capture.Summary, capture.PayloadJson),
            finding => finding.Severity == "error");

        var changedReturn = JsonNode.Parse(capture.PayloadJson)!;
        changedReturn["data"]!["game"]!["payoutCents"] = 9999;
        Assert.Contains(TableOutcomeReplayAudit.Inspect(capture.Summary, changedReturn.ToJsonString()),
            finding => finding.Code == "blackjack-return-mismatch");

        var changedDeal = JsonNode.Parse(capture.PayloadJson)!;
        changedDeal["data"]!["game"]!["dealerCards"]![2] = "A|clubs";
        Assert.Contains(TableOutcomeReplayAudit.Inspect(capture.Summary, changedDeal.ToJsonString()),
            finding => finding.Code == "blackjack-deal-mismatch");
    }

    [Fact]
    public void BlackjackTableReplaysSplitDrawAssignment()
    {
        var table = new BlackjackTableState
        {
            TableId = "blackjack-split-replay", CreatedAtUtc = Started, UpdatedAtUtc = Started,
            Players = [new BlackjackTablePlayer
            {
                ActorId = "alice", PublicSeatId = "seat-alice", DisplayName = "Alice", Seat = 0,
                SessionId = "session", SessionStartedAtUtc = Started, NextWagerCents = 500
            }]
        };
        var deck = BlackjackDeck("8|hearts", "10|clubs", "8|spades", "7|diamonds", "3|hearts", "2|diamonds");
        BlackjackTableEngine.Deal(table, deck, 11, Started);
        BlackjackTableEngine.ApplyAction(table, "alice", BlackjackActions.Split, Started.AddMilliseconds(10));
        BlackjackTableEngine.AdvanceAutomatedTurns(table, Started.AddSeconds(1));
        BlackjackTableEngine.ApplyAction(table, "alice", BlackjackActions.Stand, Started.AddSeconds(1).AddMilliseconds(10));
        BlackjackTableEngine.AdvanceAutomatedTurns(table, Started.AddSeconds(2));
        BlackjackTableEngine.ApplyAction(table, "alice", BlackjackActions.Stand, Started.AddSeconds(2).AddMilliseconds(10));
        for (var second = 3; second <= 6; second++)
            BlackjackTableEngine.AdvanceAutomatedTurns(table, Started.AddSeconds(second));
        Assert.Equal("settlement", table.Phase);
        table.RoundAccountingSettled = true;
        var capture = TableRoundAudit.Capture(table, Started.AddSeconds(6));
        Assert.DoesNotContain(TableOutcomeReplayAudit.Inspect(capture.Summary, capture.PayloadJson),
            finding => finding.Severity == "error");
        var changedSplit = JsonNode.Parse(capture.PayloadJson)!;
        changedSplit["players"]![0]!["secondaryHand"]!["cards"]![1] = "A|clubs";
        Assert.Contains(TableOutcomeReplayAudit.Inspect(capture.Summary, changedSplit.ToJsonString()),
            finding => finding.Code == "blackjack-deal-mismatch");
    }

    [Fact]
    public void BlackjackTableReplaysInsuranceAgainstDealerNatural()
    {
        var table = new BlackjackTableState
        {
            TableId = "blackjack-insurance-replay", CreatedAtUtc = Started, UpdatedAtUtc = Started,
            Players = [new BlackjackTablePlayer
            {
                ActorId = "alice", PublicSeatId = "seat-alice", DisplayName = "Alice", Seat = 0,
                SessionId = "session", SessionStartedAtUtc = Started, NextWagerCents = 500
            }]
        };
        BlackjackTableEngine.Deal(table, BlackjackDeck("9|hearts", "A|clubs", "7|spades", "K|diamonds"), 11, Started);
        BlackjackTableEngine.ApplyAction(table, "alice", BlackjackActions.Insurance, Started.AddMilliseconds(10));
        for (var second = 1; second <= 4; second++)
            BlackjackTableEngine.AdvanceAutomatedTurns(table, Started.AddSeconds(second));
        Assert.Equal("settlement", table.Phase);
        table.RoundAccountingSettled = true;
        var capture = TableRoundAudit.Capture(table, Started.AddSeconds(4));
        Assert.DoesNotContain(TableOutcomeReplayAudit.Inspect(capture.Summary, capture.PayloadJson),
            finding => finding.Severity == "error");
        var changedInsurance = JsonNode.Parse(capture.PayloadJson)!;
        changedInsurance["players"]![0]!["insurancePayoutCents"] = 0;
        Assert.Contains(TableOutcomeReplayAudit.Inspect(capture.Summary, changedInsurance.ToJsonString()),
            finding => finding.Code == "blackjack-wager-mismatch");
    }

    private static string[] BlackjackDeck(params string[] prefix) => prefix.Concat(
        BlackjackRules.CreateShuffledDeck().Where(card => !prefix.Contains(card))).ToArray();
}
