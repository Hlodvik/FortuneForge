using System.Text.Json;
using System.Text.Json.Nodes;
using FortuneForge.Games.Abstractions.Auditing;
using FortuneForge.Games.Abstractions.Wallet;
using FortuneForge.Games.RobotRevolution.Server;
using FortuneForge.Games.Slots;
using FortuneForge.Games.Slots.Persistence;
using FortuneForge.Games.Slots.Runtime;
using Xunit;

namespace FortuneForge.Games.RobotRevolution.Tests;

public sealed class RobotRevolutionSpinReportingTests
{
    private const string Account = "robot-revolution-report-compatibility";

    [Fact]
    public void AllTwentyThreeClassicsRetainTheirPositionalPayoutIds()
    {
        var game = RobotRevolutionEngine.CreateGame();
        var patterns = ClassicSlotDefinitionFactory.CreatePaylines();
        Assert.Equal(23, game.Layout.PaylineCount);
        Assert.Equal(23, game.Paylines.Count);
        for (var index = 0; index < patterns.Count; index++)
        {
            var path = patterns[index];
            Assert.Equal(path, game.Paylines[index]);
            var board = path.Select((winningRow, reel) => (IReadOnlyList<string>)Enumerable.Range(0, 4)
                .Select(row => row == winningRow ? "JAGUAR" : new[] { "GLYPH_FLAME", "GLYPH_RAIN", "GLYPH_WIND", "GLYPH_EARTH", "GLYPH_REED" }[(row + reel) % 5]).ToArray()).ToArray();
            var evaluated = ClassicSlotEngine.Evaluate(board, game, RobotRevolutionEngine.CreateSymbolSet());
            var paid = ClassicSlotEngine.CalculatePayout(evaluated, game, RobotRevolutionEngine.CreatePaytable(), 100);
            var line = Assert.Single(paid.Paylines, candidate => candidate.PaylineId == index + 1);
            var match = Assert.Single(line.Matches).Match;
            Assert.Equal("JAGUAR", match.SymbolId);
            Assert.Equal(path.Select((row, reel) => new GridPosition(reel, row)), match.Positions);
        }
    }

    [Fact]
    public void DirectValuesStayDisabledAndDormantCountersStayOutOfThePublicResult()
    {
        var before = new ClassicSlotRoundState(0, 0, null, 0, 100, 0,
            new Dictionary<string, long> { ["rand"] = 4 }, new Dictionary<string, long> { ["rand"] = 400 });
        var request = new RobotRevolutionRoundRequest("robot-revolution-cash-collector", 100, false);
        var round = RobotRevolutionRoundPolicy.Prepare(Account, request, before, DateTime.UtcNow, null,
            new ScriptedDraws(new int[5]));
        var settled = Settle(round);
        var response = RobotRevolutionSpinResponse.From(settled);
        var report = RobotRevolutionSpinReport.Capture(round, settled, round.Outcome.Audit.GeneratedAtUtc);
        Assert.Equal(0, response.MonkeyPawCount);
        Assert.Equal(0, response.MoneyGrabPoints);
        Assert.DoesNotContain(round.Outcome.Reels.SelectMany(reel => reel), symbol => symbol.StartsWith("RAND_", StringComparison.Ordinal));
        Assert.DoesNotContain(response.Payout.Paylines, line => line.PaylineId == 901);
        Assert.Equal(round.Outcome.Payout.TotalPoints, response.Payout.TotalPoints);
        Assert.Equal(response.Payout.TotalPoints * 25, round.PayoutCents);
        Assert.False(response.EnergyMultiplierApplied);
        Assert.Equal(1m, response.PayoutMultiplier);
        Assert.Equal(0, response.EnergyAwarded);
        Assert.Equal(0, response.EnergyBalance);
        Assert.Empty(response.SealsAwarded);
        Assert.Empty(response.SealCollections);
        Assert.Equal(4, round.Progress.After.SealCounts["rand"]);
        Assert.Equal(400, round.Progress.After.SealWagerPoints["rand"]);
        Assert.Equal("robot-revolution-v1", report.Result["gameId"]);
        Assert.Equal((double)(round.PayoutCents / 100m), report.Result["wonSlotsCredits"]);
        Assert.DoesNotContain(report.GameplayWitnesses.Keys, key => key.Contains("-energy-", StringComparison.Ordinal));
        Assert.False(JsonSerializer.SerializeToElement(response, RoundAuditCapture.Json).TryGetProperty("chargedWagerCents", out _));

        var summary = report.Round.Audit.Summary;
        Assert.Equal("robot-revolution", summary.GameId);
        var participant = Assert.Single(summary.Participants);
        Assert.Equal(2_500, participant.WagerCents);
        Assert.Equal(round.PayoutCents, participant.PayoutCents);
        using var proof = JsonDocument.Parse(report.Round.Audit.PayloadJson);
        var data = proof.RootElement.GetProperty("data");
        var audit = data.GetProperty("engineAudit");
        Assert.Equal(100, audit.GetProperty("energyBalanceBefore").GetInt64());
        Assert.Equal(23, audit.GetProperty("baseGame").GetProperty("layout").GetProperty("paylineCount").GetInt32());
        Assert.Empty(RobotRevolutionSpinEvidence.InspectNative(summary, data));

        var changed = JsonNode.Parse(data.GetRawText())!.AsObject();
        changed["robotRevolutionProgress"]!["after"]!["sealCounts"]!["rand"] = 5;
        AssertMismatch(summary, changed);
        changed["robotRevolutionProgress"]!["after"]!["sealCounts"]!["rand"] = 4;
        changed["result"]!["moneyGrabPoints"] = 1;
        AssertMismatch(summary, changed);
        changed = JsonNode.Parse(data.GetRawText())!.AsObject();
        changed["robotRevolutionDraws"] = Convert.ToBase64String(round.Engine.Draws.Append((byte)0).ToArray());
        AssertMismatch(summary, changed);
        changed = JsonNode.Parse(data.GetRawText())!.AsObject();
        changed["walletSettlementId"] = "unrelated-wallet-receipt";
        AssertMismatch(summary, changed);
    }

    [Fact]
    public void ImportedOddRowsWagerRetriggersSevenAndWritesOnlyTheAwardWitness()
    {
        var before = new ClassicSlotRoundState(0, 2, new(2, 3, "rows"), 0, 0, 0,
            new Dictionary<string, long>(), new Dictionary<string, long>());
        var strips = ClassicSlotDefinitionFactory.Create(RobotRevolutionEngine.LegacyProfile).CreateReelSet().Reels
            .Select(reel => reel.Where(symbol => symbol != "BOLT").ToArray()).ToArray();
        var stops = strips.Select((reel, index) => index == 0 ? 0 : Array.IndexOf(reel, "FREE")).ToArray();
        var round = RobotRevolutionRoundPolicy.Prepare(Account, new("robot-revolution-rows-retrigger", 3, true), before, DateTime.UtcNow,
            null, new ScriptedDraws(stops));
        var settled = Settle(round);
        var response = RobotRevolutionSpinResponse.From(settled);
        var report = RobotRevolutionSpinReport.Capture(round, settled, round.Outcome.Audit.GeneratedAtUtc);

        Assert.True(response.IsFreeSpin);
        Assert.Equal(3, response.WagerPoints);
        Assert.Equal(3, response.FreeSpinWagerPoints);
        Assert.Equal("rows", response.FreeSpinFeatureMode);
        Assert.Equal(6, round.Outcome.Audit.EffectiveGame.Layout.VisibleRows);
        Assert.Equal(7, response.FreeSpinsAwarded);
        Assert.Equal(8, response.FreeSpinsRemaining);
        Assert.Equal("rows", report.Result["sealFreeSpinFeatureMode"]);
        Assert.Equal(0, report.Result["sealFreeSpinsAwarded"]);
        Assert.Equal(0, round.WagerCents);
        Assert.Equal(1, settled.Wallet.LegacyFreeGamesRemaining);
        Assert.Equal($"{round.Outcome.SpinId:N}-free-game-use", settled.Wallet.LegacyFreeGameTransactionId);
        Assert.DoesNotContain(report.GameplayWitnesses.Keys, key => key.EndsWith("-free-game-use", StringComparison.Ordinal));
        var award = report.GameplayWitnesses[$"{round.Outcome.SpinId:N}-free-games-award"];
        Assert.Equal("freeGames:robot-revolution-v1", award["currencyId"]);
        Assert.Equal(7L, award["amount"]);
        Assert.Equal(8L, award["balanceAfter"]);

        using var proof = JsonDocument.Parse(report.Round.Audit.PayloadJson);
        var data = proof.RootElement.GetProperty("data");
        Assert.Empty(RobotRevolutionSpinEvidence.InspectNative(report.Round.Audit.Summary, data));
        var old = JsonNode.Parse(data.GetRawText())!.AsObject();
        old.Remove("robotRevolutionDraws");
        old.Remove("robotRevolutionProgress");
        old.Remove("engineOutcomeSha256");
        old.Remove("walletSettlementId");
        Assert.False(RobotRevolutionSpinEvidence.IsNative(JsonSerializer.SerializeToElement(old)));
    }

    private static RobotRevolutionRoundResult Settle(RobotRevolutionPreparedRound round)
    {
        const long before = 50_000;
        var spend = before - round.WagerCents;
        var spin = round.Outcome.SpinId.ToString("N");
        var receipt = new GameWalletSettlementReceipt(GameWalletSettlementIdentity.CreateId(RobotRevolutionEngine.GameId, Account, spin),
            round.WagerCents > 0 ? $"{spin}-wager" : null, round.PayoutCents > 0 ? $"{spin}-payout" : null,
            before, spend, spend + round.PayoutCents, DateTimeOffset.UtcNow, false, false,
            LegacyFreeGameTransactionId: round.SpendsLegacy ? $"{spin}-free-game-use" : null,
            LegacyFreeGamesRemaining: round.Progress.After.LegacyRemaining);
        return new(RobotRevolutionRoundStateReply.From(round.Progress.After, balanceCents: receipt.BalanceAfterCents), receipt)
        {
            Round = round,
            ChargedWagerCents = round.WagerCents
        };
    }

    private static void AssertMismatch(RoundAuditSummary summary, JsonObject data) =>
        Assert.Equal("slot-robot-revolution-native-proof-mismatch", Assert.Single(RobotRevolutionSpinEvidence.InspectNative(
            summary, JsonSerializer.SerializeToElement(data))).Code);

    private sealed class ScriptedDraws(int[] stops) : IGameRandomSource
    {
        private int position;
        public int Next(int maximumExclusive) => position < stops.Length ? stops[position++] : 0;
    }
}
