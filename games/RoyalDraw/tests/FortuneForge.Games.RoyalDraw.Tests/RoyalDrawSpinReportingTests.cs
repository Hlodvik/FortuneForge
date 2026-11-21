using System.Text.Json;
using System.Text.Json.Nodes;
using FortuneForge.Games.Abstractions.Auditing;
using FortuneForge.Games.Abstractions.Wallet;
using FortuneForge.Games.RoyalDraw.Server;
using FortuneForge.Games.Slots;
using FortuneForge.Games.Slots.Persistence;
using FortuneForge.Games.Slots.Runtime;
using Xunit;

namespace FortuneForge.Games.RoyalDraw.Tests;

public sealed class RoyalDrawSpinReportingTests
{
    private const string Account = "royal-draw-report-compatibility";

    [Fact]
    public void AllTwentyThreeClassicPatternsRetainTheirPositionalPayoutIds()
    {
        var game = RoyalDrawEngine.CreateGame();
        var patterns = ClassicSlotDefinitionFactory.CreatePaylines();
        Assert.Equal(23, game.Layout.PaylineCount);
        Assert.Equal(23, game.Paylines.Count);
        for (var index = 0; index < patterns.Count; index++)
        {
            var path = patterns[index];
            Assert.Equal(path, game.Paylines[index]);
            var board = path.Select((winningRow, reel) => (IReadOnlyList<string>)Enumerable.Range(0, 4)
                .Select(row => row == winningRow ? "7" : ((row + reel) % 5 + 2).ToString()).ToArray()).ToArray();
            var evaluated = ClassicSlotEngine.Evaluate(board, game, RoyalDrawEngine.CreateSymbolSet());
            var paid = ClassicSlotEngine.CalculatePayout(evaluated, game, RoyalDrawEngine.CreatePaytable(), 100);
            var line = Assert.Single(paid.Paylines, candidate => candidate.PaylineId == index + 1);
            var match = Assert.Single(line.Matches).Match;
            Assert.Equal("7", match.SymbolId);
            Assert.Equal(path.Select((row, reel) => new GridPosition(reel, row)), match.Positions);
        }
    }

    [Fact]
    public void EnergyMultipliesCollectorAndAwardedModeKeepsExactHistoryAndSignedCents()
    {
        var before = new ClassicSlotRoundState(0, 0, null, 0, 99, 0,
            new Dictionary<string, long> { ["sync"] = 7 }, new Dictionary<string, long> { ["sync"] = 70 });
        var request = new RoyalDrawRoundRequest("royal-draw-energy-receipt", 10, false);
        var stops = new int[5];
        stops[0] = RoyalDrawEngine.CreateReelSet().Reels[0].IndexOf("BOLT");
        var round = RoyalDrawRoundPolicy.Prepare(Account, request, before, DateTime.UtcNow, null, new ScriptedDraws(stops));
        var settled = Settle(round);
        var response = RoyalDrawSpinResponse.From(settled);
        var report = RoyalDrawSpinReport.Capture(round, settled, round.Outcome.Audit.GeneratedAtUtc);
        Assert.True(response.EnergyMultiplierApplied);
        Assert.Equal(1.5m, response.PayoutMultiplier);
        Assert.Equal(0, response.EnergyBalance);
        Assert.Equal(7, response.FreeSpinsRemaining);
        Assert.Equal("rows-rand", response.FreeSpinFeatureMode);
        Assert.Equal(10, response.FreeSpinWagerPoints);
        Assert.Equal(0, response.FreeSpinsAwarded);
        Assert.Equal(7, report.Result["sealFreeSpinsAwarded"]);
        Assert.Equal("rows-rand", report.Result["sealFreeSpinFeatureMode"]);
        Assert.Equal(9, response.MoneyGrabPoints);
        Assert.Equal(round.Outcome.CollectorPoints, response.MoneyGrabPoints);
        Assert.Equal(14, Assert.Single(response.Payout.Paylines, line => line.PaylineId == 901).AmountPoints);
        Assert.Equal(round.Outcome.Payout.Paylines.Sum(line => line.Matches.Sum(match => Multiply(match.AmountPoints))),
            response.Payout.TotalPoints);
        Assert.Equal(response.Payout.TotalPoints * 25, round.PayoutCents);
        Assert.Equal((double)(round.PayoutCents / 100m), report.Result["wonSlotsCredits"]);
        Assert.Equal("royal-draw-v1", report.Result["gameId"]);
        Assert.False(JsonSerializer.SerializeToElement(response, RoundAuditCapture.Json).TryGetProperty("chargedWagerCents", out _));

        var spin = round.Outcome.SpinId.ToString("N");
        AssertWitness(report, $"{spin}-energy-award", "energy:royal-draw-v1", 1, 100);
        AssertWitness(report, $"{spin}-energy-multiplier-use", "energy:royal-draw-v1", -100, 0);
        AssertWitness(report, $"{spin}-free-games-award", "freeGames:royal-draw-v1", 7, 7);
        var summary = report.Round.Audit.Summary;
        Assert.Equal("royal-draw", summary.GameId);
        var participant = Assert.Single(summary.Participants);
        Assert.Equal(250, participant.WagerCents);
        Assert.Equal(round.PayoutCents, participant.PayoutCents);
        using var proof = JsonDocument.Parse(report.Round.Audit.PayloadJson);
        var data = proof.RootElement.GetProperty("data");
        var audit = data.GetProperty("engineAudit");
        Assert.Equal(23, audit.GetProperty("baseGame").GetProperty("layout").GetProperty("paylineCount").GetInt32());
        Assert.Equal(0.942m, audit.GetProperty("featureProfile").GetProperty("payoutMultiplier").GetDecimal());
        Assert.Empty(RoyalDrawSpinEvidence.InspectNative(summary, data));

        var changed = JsonNode.Parse(data.GetRawText())!.AsObject();
        changed["royalDrawProgress"]!["collectionFreeSpinWagerPoints"] = 12;
        AssertMismatch(summary, changed);
        changed["royalDrawProgress"]!["collectionFreeSpinWagerPoints"] = 10;
        changed["royalDrawDraws"] = Convert.ToBase64String(round.Engine.Draws.Append((byte)0).ToArray());
        AssertMismatch(summary, changed);
    }

    [Fact]
    public void OddImportedWagerRetriggersSixScatterSpinsAndUsesOnlyOneLegacyDebit()
    {
        var before = new ClassicSlotRoundState(0, 2, new(2, 3, "rows-rand"), 0, 0, 0,
            new Dictionary<string, long>(), new Dictionary<string, long>());
        var stops = RoyalDrawEngine.CreateReelSet().Reels.Select(reel => reel.IndexOf("FREE")).ToArray();
        var round = RoyalDrawRoundPolicy.Prepare(Account, new("royal-draw-imported-odd-wager", 3, true), before,
            DateTime.UtcNow, null, new ScriptedDraws(stops));
        var settled = Settle(round);
        var response = RoyalDrawSpinResponse.From(settled);
        var report = RoyalDrawSpinReport.Capture(round, settled, round.Outcome.Audit.GeneratedAtUtc);
        Assert.True(response.IsFreeSpin);
        Assert.Equal(3, response.WagerPoints);
        Assert.Equal(3, response.FreeSpinWagerPoints);
        Assert.Equal(6, response.FreeSpinsAwarded);
        Assert.Equal(7, response.FreeSpinsRemaining);
        Assert.Equal("rows-rand", response.FreeSpinFeatureMode);
        Assert.Equal(1, settled.Wallet.LegacyFreeGamesRemaining);
        Assert.Equal($"{round.Outcome.SpinId:N}-free-game-use", settled.Wallet.LegacyFreeGameTransactionId);
        Assert.DoesNotContain(report.GameplayWitnesses.Keys, key => key.EndsWith("-free-game-use", StringComparison.Ordinal));
        AssertWitness(report, $"{round.Outcome.SpinId:N}-free-games-award", "freeGames:royal-draw-v1", 6, 7);

        using var proof = JsonDocument.Parse(report.Round.Audit.PayloadJson);
        var summary = report.Round.Audit.Summary;
        var data = proof.RootElement.GetProperty("data");
        Assert.Empty(RoyalDrawSpinEvidence.InspectNative(summary, data));
        var old = JsonNode.Parse(data.GetRawText())!.AsObject();
        old.Remove("royalDrawDraws");
        old.Remove("royalDrawProgress");
        old.Remove("engineOutcomeSha256");
        old.Remove("walletSettlementId");
        Assert.False(RoyalDrawSpinEvidence.IsNative(JsonSerializer.SerializeToElement(old)));
    }

    private static RoyalDrawRoundResult Settle(RoyalDrawPreparedRound round)
    {
        const long before = 50_000;
        var spend = before - round.WagerCents;
        var spin = round.Outcome.SpinId.ToString("N");
        var receipt = new GameWalletSettlementReceipt(GameWalletSettlementIdentity.CreateId(RoyalDrawEngine.GameId, Account, spin),
            round.WagerCents > 0 ? $"{spin}-wager" : null, round.PayoutCents > 0 ? $"{spin}-payout" : null,
            before, spend, spend + round.PayoutCents, DateTimeOffset.UtcNow, false, false,
            LegacyFreeGameTransactionId: round.SpendsLegacy ? $"{spin}-free-game-use" : null,
            LegacyFreeGamesRemaining: round.Progress.After.LegacyRemaining);
        return new(RoyalDrawRoundStateReply.From(round.Progress.After, balanceCents: receipt.BalanceAfterCents), receipt)
        {
            Round = round,
            ChargedWagerCents = round.WagerCents
        };
    }

    private static void AssertWitness(ClassicSlotPaidReport report, string id, string currency, long amount, long after)
    {
        var witness = report.GameplayWitnesses[id];
        Assert.Equal(id, witness["transactionId"]);
        Assert.Equal(id, witness["idempotencyKey"]);
        Assert.Equal(currency, witness["currencyId"]);
        Assert.Equal(amount, witness["amount"]);
        Assert.Equal(after, witness["balanceAfter"]);
    }

    private static void AssertMismatch(RoundAuditSummary summary, JsonObject data) =>
        Assert.Equal("slot-royal-draw-native-proof-mismatch", Assert.Single(RoyalDrawSpinEvidence.InspectNative(
            summary, JsonSerializer.SerializeToElement(data))).Code);

    private static long Multiply(long points) => checked((long)Math.Round(points * 1.5m, MidpointRounding.AwayFromZero));

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
