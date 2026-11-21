using System.Text.Json;
using System.Text.Json.Nodes;
using FortuneForge.Games.Abstractions.Auditing;
using FortuneForge.Games.Abstractions.Wallet;
using FortuneForge.Games.ReelRiches.Server;
using FortuneForge.Games.Slots;
using FortuneForge.Games.Slots.Persistence;
using FortuneForge.Games.Slots.Runtime;
using Xunit;

namespace FortuneForge.Games.ReelRiches.Tests;

public sealed class ReelRichesSpinReportingTests
{
    private const string Account = "reel-riches-report-compatibility";

    [Fact]
    public void NineteenAuthoredPatternsPreserveTheLastThreePositionalPayoutIds()
    {
        int[] authoredIds = [1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15, 16, 21, 22, 23];
        Assert.Equal(authoredIds, ReelRichesEngine.Profile.PaylinePatternIds);
        var allPatterns = ClassicSlotDefinitionFactory.CreatePaylines();
        var game = ReelRichesEngine.CreateGame();
        Assert.Equal(19, game.Layout.PaylineCount);
        for (var index = 0; index < authoredIds.Length; index++)
            Assert.Equal(allPatterns[authoredIds[index] - 1], game.Paylines[index]);

        for (var index = 16; index < authoredIds.Length; index++)
        {
            var path = allPatterns[authoredIds[index] - 1];
            var board = path.Select((winningRow, reel) => (IReadOnlyList<string>)Enumerable.Range(0, 4)
                .Select(row => row == winningRow ? "7" : ((row + reel) % 5 + 2).ToString()).ToArray()).ToArray();
            var evaluated = ClassicSlotEngine.Evaluate(board, game, ReelRichesEngine.CreateSymbolSet());
            var payout = ClassicSlotEngine.CalculatePayout(evaluated, game, ReelRichesEngine.CreatePaytable(), 100);
            var paid = Assert.Single(payout.Paylines, line => line.PaylineId == index + 1);
            var match = Assert.Single(paid.Matches).Match;
            Assert.Equal("7", match.SymbolId);
            Assert.Equal(5, match.MatchLength);
            Assert.Equal(path.Select((row, reel) => new GridPosition(reel, row)), match.Positions);
        }
    }

    [Fact]
    public void EnergyMultipliesCollectorAndAwardedModeKeepsExactHistoryAndSignedCents()
    {
        var before = new ClassicSlotRoundState(0, 0, null, 0, 99, 0,
            new Dictionary<string, long> { ["sync"] = 7 }, new Dictionary<string, long> { ["sync"] = 700 });
        var request = new ReelRichesRoundRequest("reel-riches-energy-receipt", 100, false);
        var draws = new SeededDraws();
        var round = Enumerable.Range(0, 512)
            .Select(_ => ReelRichesRoundPolicy.Prepare(Account, request, before, DateTime.UtcNow, null, draws))
            .First(sample => sample.Outcome.CollectorPoints > 0 && sample.Outcome.EnergyAwarded > 0 && sample.Outcome.FreeSpinsAwarded == 0);
        var settled = Settle(round);
        var response = ReelRichesSpinResponse.From(settled);
        var report = ReelRichesSpinReport.Capture(round, settled, round.Outcome.Audit.GeneratedAtUtc);
        Assert.True(response.EnergyMultiplierApplied);
        Assert.Equal(1.5m, response.PayoutMultiplier);
        Assert.Equal(0, response.EnergyBalance);
        Assert.Equal(10, response.FreeSpinsRemaining);
        Assert.Equal("sync-rows-rand", response.FreeSpinFeatureMode);
        Assert.Equal(100, response.FreeSpinWagerPoints);
        Assert.Equal(0, response.FreeSpinsAwarded);
        Assert.Equal(10, report.Result["sealFreeSpinsAwarded"]);
        Assert.Equal("sync-rows-rand", report.Result["sealFreeSpinFeatureMode"]);
        Assert.Equal(round.Outcome.CollectorPoints, response.MoneyGrabPoints);
        Assert.Equal(Multiply(response.MoneyGrabPoints), Assert.Single(response.Payout.Paylines, line => line.PaylineId == 901).AmountPoints);
        Assert.Equal(round.Outcome.Payout.Paylines.Sum(line => line.Matches.Sum(match => Multiply(match.AmountPoints))),
            response.Payout.TotalPoints);
        Assert.Equal(response.Payout.TotalPoints * 25, round.PayoutCents);
        Assert.Equal((double)(round.PayoutCents / 100m), report.Result["wonSlotsCredits"]);
        Assert.Equal("reel-riches-v1", report.Result["gameId"]);
        Assert.False(JsonSerializer.SerializeToElement(response, RoundAuditCapture.Json).TryGetProperty("chargedWagerCents", out _));

        var spin = round.Outcome.SpinId.ToString("N");
        AssertWitness(report, $"{spin}-energy-award", "energy:reel-riches-v1", 1, 100);
        AssertWitness(report, $"{spin}-energy-multiplier-use", "energy:reel-riches-v1", -100, 0);
        AssertWitness(report, $"{spin}-free-games-award", "freeGames:reel-riches-v1", 10, 10);
        var summary = report.Round.Audit.Summary;
        Assert.Equal("reel-riches", summary.GameId);
        var participant = Assert.Single(summary.Participants);
        Assert.Equal(2_500, participant.WagerCents);
        Assert.Equal(round.PayoutCents, participant.PayoutCents);
        using var proof = JsonDocument.Parse(report.Round.Audit.PayloadJson);
        var data = proof.RootElement.GetProperty("data");
        Assert.Equal(ReelRichesEngine.Profile.PaylinePatternIds, data.GetProperty("engineAudit").GetProperty("featureProfile")
            .GetProperty("paylinePatternIds").Deserialize<int[]>());
        Assert.Empty(ReelRichesSpinEvidence.InspectNative(summary, data));

        var changed = JsonNode.Parse(data.GetRawText())!.AsObject();
        changed["reelRichesProgress"]!["collectionFreeSpinWagerPoints"] = 102;
        AssertMismatch(summary, changed);
        changed["reelRichesProgress"]!["collectionFreeSpinWagerPoints"] = 100;
        changed["reelRichesDraws"] = Convert.ToBase64String(round.Engine.Draws.Append((byte)0).ToArray());
        AssertMismatch(summary, changed);
    }

    [Fact]
    public void OddImportedWagerRetriggersFiveScatterSpinsAndUsesOnlyOneLegacyDebit()
    {
        var before = new ClassicSlotRoundState(0, 2, new(2, 3, "sync-rows-rand"), 0, 0, 0,
            new Dictionary<string, long>(), new Dictionary<string, long>());
        var stops = ReelRichesEngine.CreateReelSet().Reels.Select(reel => reel.IndexOf("FREE")).ToArray();
        var round = ReelRichesRoundPolicy.Prepare(Account, new("reel-riches-imported-odd-wager", 3, true), before,
            DateTime.UtcNow, null, new ScriptedDraws(stops));
        var settled = Settle(round);
        var response = ReelRichesSpinResponse.From(settled);
        var report = ReelRichesSpinReport.Capture(round, settled, round.Outcome.Audit.GeneratedAtUtc);
        Assert.True(response.IsFreeSpin);
        Assert.Equal(3, response.WagerPoints);
        Assert.Equal(3, response.FreeSpinWagerPoints);
        Assert.Equal(5, response.FreeSpinsAwarded);
        Assert.Equal(6, response.FreeSpinsRemaining);
        Assert.Equal("sync-rows-rand", response.FreeSpinFeatureMode);
        Assert.Equal(1, settled.Wallet.LegacyFreeGamesRemaining);
        Assert.Equal($"{round.Outcome.SpinId:N}-free-game-use", settled.Wallet.LegacyFreeGameTransactionId);
        Assert.DoesNotContain(report.GameplayWitnesses.Keys, key => key.EndsWith("-free-game-use", StringComparison.Ordinal));
        AssertWitness(report, $"{round.Outcome.SpinId:N}-free-games-award", "freeGames:reel-riches-v1", 5, 6);

        using var proof = JsonDocument.Parse(report.Round.Audit.PayloadJson);
        var summary = report.Round.Audit.Summary;
        var data = proof.RootElement.GetProperty("data");
        Assert.Empty(ReelRichesSpinEvidence.InspectNative(summary, data));
        var old = JsonNode.Parse(data.GetRawText())!.AsObject();
        old.Remove("reelRichesDraws");
        old.Remove("reelRichesProgress");
        old.Remove("engineOutcomeSha256");
        old.Remove("walletSettlementId");
        Assert.False(ReelRichesSpinEvidence.IsNative(JsonSerializer.SerializeToElement(old)));
    }

    private static ReelRichesRoundResult Settle(ReelRichesPreparedRound round)
    {
        const long before = 50_000;
        var spend = before - round.WagerCents;
        var spin = round.Outcome.SpinId.ToString("N");
        var receipt = new GameWalletSettlementReceipt(GameWalletSettlementIdentity.CreateId(ReelRichesEngine.GameId, Account, spin),
            round.WagerCents > 0 ? $"{spin}-wager" : null, round.PayoutCents > 0 ? $"{spin}-payout" : null,
            before, spend, spend + round.PayoutCents, DateTimeOffset.UtcNow, false, false,
            LegacyFreeGameTransactionId: round.SpendsLegacy ? $"{spin}-free-game-use" : null,
            LegacyFreeGamesRemaining: round.Progress.After.LegacyRemaining);
        return new(ReelRichesRoundStateReply.From(round.Progress.After, balanceCents: receipt.BalanceAfterCents), receipt)
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
        Assert.Equal("slot-reel-riches-native-proof-mismatch", Assert.Single(ReelRichesSpinEvidence.InspectNative(
            summary, JsonSerializer.SerializeToElement(data))).Code);

    private static long Multiply(long points) => checked((long)Math.Round(points * 1.5m, MidpointRounding.AwayFromZero));

    private sealed class SeededDraws : IGameRandomSource
    {
        private readonly Random random = new(7429);
        public int Next(int maximumExclusive) => random.Next(maximumExclusive);
    }

    private sealed class ScriptedDraws(int[] stops) : IGameRandomSource
    {
        private int position;
        public int Next(int maximumExclusive) => position < stops.Length ? stops[position++] : 0;
    }
}
