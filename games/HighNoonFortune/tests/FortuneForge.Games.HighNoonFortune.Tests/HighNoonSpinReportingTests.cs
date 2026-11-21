using System.Text.Json;
using System.Text.Json.Nodes;
using FortuneForge.Games.Abstractions.Auditing;
using FortuneForge.Games.Abstractions.Wallet;
using FortuneForge.Games.HighNoonFortune.Server;
using FortuneForge.Games.Slots;
using FortuneForge.Games.Slots.Persistence;
using FortuneForge.Games.Slots.Runtime;
using Xunit;

namespace FortuneForge.Games.HighNoonFortune.Tests;

public sealed class HighNoonSpinReportingTests
{
    private const string Account = "high-noon-report-compatibility";

    [Fact]
    public void AllTwentyThreeClassicsRetainTheirPositionalPayoutIds()
    {
        var game = HighNoonFortuneEngine.CreateGame();
        var patterns = ClassicSlotDefinitionFactory.CreatePaylines();
        Assert.Equal(23, game.Layout.PaylineCount);
        Assert.Equal(23, game.Paylines.Count);
        for (var index = 0; index < patterns.Count; index++)
        {
            var path = patterns[index];
            Assert.Equal(path, game.Paylines[index]);
            var board = path.Select((winningRow, reel) => (IReadOnlyList<string>)Enumerable.Range(0, 4)
                .Select(row => row == winningRow ? "7" : ((row + reel) % 5 + 2).ToString()).ToArray()).ToArray();
            var evaluated = ClassicSlotEngine.Evaluate(board, game, HighNoonFortuneEngine.CreateSymbolSet());
            var paid = ClassicSlotEngine.CalculatePayout(evaluated, game, HighNoonFortuneEngine.CreatePaytable(), 100);
            var line = Assert.Single(paid.Paylines, candidate => candidate.PaylineId == index + 1);
            var match = Assert.Single(line.Matches).Match;
            Assert.Equal("7", match.SymbolId);
            Assert.Equal(path.Select((row, reel) => new GridPosition(reel, row)), match.Positions);
        }
    }

    [Fact]
    public void CollectorUsesEnginePriceOnceAndDormantEnergyAndSealsStayOutOfThePublicResult()
    {
        var before = new ClassicSlotRoundState(0, 0, null, 0, 100, 0,
            new Dictionary<string, long> { ["sync"] = 4 }, new Dictionary<string, long> { ["sync"] = 400 });
        var request = new HighNoonRoundRequest("high-noon-cash-collector", 100, false);
        var round = HighNoonRoundPolicy.Prepare(Account, request, before, DateTime.UtcNow, null,
            new ScriptedDraws(new int[5]));
        var settled = Settle(round);
        var response = HighNoonSpinResponse.From(settled);
        var report = HighNoonSpinReport.Capture(round, settled, round.Outcome.Audit.GeneratedAtUtc);
        Assert.Equal(2, response.MonkeyPawCount);
        Assert.Equal(111, response.MoneyGrabPoints);
        Assert.Equal(111, Assert.Single(response.Payout.Paylines, line => line.PaylineId == 901).AmountPoints);
        Assert.Equal(round.Outcome.Payout.TotalPoints, response.Payout.TotalPoints);
        Assert.Equal(response.Payout.TotalPoints * 25, round.PayoutCents);
        Assert.False(response.EnergyMultiplierApplied);
        Assert.Equal(1m, response.PayoutMultiplier);
        Assert.Equal(0, response.EnergyAwarded);
        Assert.Equal(0, response.EnergyBalance);
        Assert.Empty(response.SealsAwarded);
        Assert.Empty(response.SealCollections);
        Assert.Equal(4, round.Progress.After.SealCounts["sync"]);
        Assert.Equal(400, round.Progress.After.SealWagerPoints["sync"]);
        Assert.Equal("high-noon-fortune-v1", report.Result["gameId"]);
        Assert.Equal((double)(round.PayoutCents / 100m), report.Result["wonSlotsCredits"]);
        Assert.DoesNotContain(report.GameplayWitnesses.Keys, key => key.Contains("-energy-", StringComparison.Ordinal));
        Assert.False(JsonSerializer.SerializeToElement(response, RoundAuditCapture.Json).TryGetProperty("chargedWagerCents", out _));

        var summary = report.Round.Audit.Summary;
        Assert.Equal("high-noon-fortune", summary.GameId);
        var participant = Assert.Single(summary.Participants);
        Assert.Equal(2_500, participant.WagerCents);
        Assert.Equal(round.PayoutCents, participant.PayoutCents);
        using var proof = JsonDocument.Parse(report.Round.Audit.PayloadJson);
        var data = proof.RootElement.GetProperty("data");
        var audit = data.GetProperty("engineAudit");
        Assert.Equal(100, audit.GetProperty("energyBalanceBefore").GetInt64());
        Assert.Equal(23, audit.GetProperty("baseGame").GetProperty("layout").GetProperty("paylineCount").GetInt32());
        Assert.Empty(HighNoonSpinEvidence.InspectNative(summary, data));

        var changed = JsonNode.Parse(data.GetRawText())!.AsObject();
        changed["highNoonProgress"]!["after"]!["sealCounts"]!["sync"] = 5;
        AssertMismatch(summary, changed);
        changed["highNoonProgress"]!["after"]!["sealCounts"]!["sync"] = 4;
        changed["result"]!["moneyGrabPoints"] = 110;
        AssertMismatch(summary, changed);
    }

    [Fact]
    public void ImportedOddSyncWagerRetriggersFiveAndWritesOnlyTheAwardWitness()
    {
        var before = new ClassicSlotRoundState(0, 2, new(2, 3, "sync"), 0, 0, 0,
            new Dictionary<string, long>(), new Dictionary<string, long>());
        var stops = HighNoonFortuneEngine.CreateReelSet().Reels.Select(reel => reel.IndexOf("FREE")).ToArray();
        var round = HighNoonRoundPolicy.Prepare(Account, new("high-noon-sync-retrigger", 3, true), before, DateTime.UtcNow,
            null, new ScriptedDraws(stops));
        var settled = Settle(round);
        var response = HighNoonSpinResponse.From(settled);
        var report = HighNoonSpinReport.Capture(round, settled, round.Outcome.Audit.GeneratedAtUtc);

        Assert.True(response.IsFreeSpin);
        Assert.Equal(3, response.WagerPoints);
        Assert.Equal(3, response.FreeSpinWagerPoints);
        Assert.Equal("sync", response.FreeSpinFeatureMode);
        Assert.Equal(5, response.FreeSpinsAwarded);
        Assert.Equal(6, response.FreeSpinsRemaining);
        Assert.Equal("sync", report.Result["sealFreeSpinFeatureMode"]);
        Assert.Equal(0, report.Result["sealFreeSpinsAwarded"]);
        Assert.Equal(0, round.WagerCents);
        Assert.Equal(1, settled.Wallet.LegacyFreeGamesRemaining);
        Assert.Equal($"{round.Outcome.SpinId:N}-free-game-use", settled.Wallet.LegacyFreeGameTransactionId);
        Assert.DoesNotContain(report.GameplayWitnesses.Keys, key => key.EndsWith("-free-game-use", StringComparison.Ordinal));
        var award = report.GameplayWitnesses[$"{round.Outcome.SpinId:N}-free-games-award"];
        Assert.Equal("freeGames:high-noon-fortune-v1", award["currencyId"]);
        Assert.Equal(5L, award["amount"]);
        Assert.Equal(6L, award["balanceAfter"]);

        using var proof = JsonDocument.Parse(report.Round.Audit.PayloadJson);
        var data = proof.RootElement.GetProperty("data");
        Assert.Empty(HighNoonSpinEvidence.InspectNative(report.Round.Audit.Summary, data));
        var old = JsonNode.Parse(data.GetRawText())!.AsObject();
        old.Remove("highNoonDraws");
        old.Remove("highNoonProgress");
        old.Remove("engineOutcomeSha256");
        old.Remove("walletSettlementId");
        Assert.False(HighNoonSpinEvidence.IsNative(JsonSerializer.SerializeToElement(old)));
    }

    private static HighNoonRoundResult Settle(HighNoonPreparedRound round)
    {
        const long before = 50_000;
        var spend = before - round.WagerCents;
        var spin = round.Outcome.SpinId.ToString("N");
        var receipt = new GameWalletSettlementReceipt(GameWalletSettlementIdentity.CreateId(HighNoonFortuneEngine.GameId, Account, spin),
            round.WagerCents > 0 ? $"{spin}-wager" : null, round.PayoutCents > 0 ? $"{spin}-payout" : null,
            before, spend, spend + round.PayoutCents, DateTimeOffset.UtcNow, false, false,
            LegacyFreeGameTransactionId: round.SpendsLegacy ? $"{spin}-free-game-use" : null,
            LegacyFreeGamesRemaining: round.Progress.After.LegacyRemaining);
        return new(HighNoonRoundStateReply.From(round.Progress.After, balanceCents: receipt.BalanceAfterCents), receipt)
        {
            Round = round,
            ChargedWagerCents = round.WagerCents
        };
    }

    private static void AssertMismatch(RoundAuditSummary summary, JsonObject data) =>
        Assert.Equal("slot-high-noon-native-proof-mismatch", Assert.Single(HighNoonSpinEvidence.InspectNative(
            summary, JsonSerializer.SerializeToElement(data))).Code);

    private sealed class ScriptedDraws(int[] stops) : IGameRandomSource
    {
        private int position;
        private bool directValueRolled;

        public int Next(int maximumExclusive)
        {
            if (position < stops.Length) return stops[position++];
            if (maximumExclusive != 100 || directValueRolled) return 0;
            directValueRolled = true;
            return 79;
        }
    }
}
