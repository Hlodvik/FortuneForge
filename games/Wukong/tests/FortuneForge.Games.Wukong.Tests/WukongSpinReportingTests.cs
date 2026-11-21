using System.Text.Json;
using System.Text.Json.Nodes;
using FortuneForge.Games.Abstractions.Auditing;
using FortuneForge.Games.Abstractions.Wallet;
using FortuneForge.Games.Slots;
using FortuneForge.Games.Slots.Runtime;
using FortuneForge.Games.Wukong.Server;
using Xunit;

namespace FortuneForge.Games.Wukong.Tests;

public sealed class WukongSpinReportingTests
{
    private const string Account = "wukong-report-compatibility";

    [Fact]
    public void CollectorAndFullMeterMultiplyEachPaidMatchAndCompleteTheNearestSealTrack()
    {
        var before = new WukongRoundBefore(0, 0, null, 0, 99, 0,
            new Dictionary<string, long> { ["sync"] = 7, ["rows"] = 2 },
            new Dictionary<string, long> { ["sync"] = 700, ["rows"] = 200 });
        var round = Prepare(before, new("wukong-report-paid-meter", 100, false),
            outcome => outcome.CollectorPoints > 0 && outcome.EnergyAwarded > 0);
        var settled = Settle(round);
        var response = WukongSpinResponse.From(settled);
        var report = WukongSpinReport.Capture(round, settled, round.Outcome.Audit.GeneratedAtUtc);
        Assert.True(response.EnergyMultiplierApplied);
        Assert.Equal(ClassicSlotEnergy.PayoutMultiplier, response.PayoutMultiplier);
        Assert.Equal(0, response.EnergyBalance);
        Assert.Equal(10, response.FreeSpinsRemaining);
        Assert.Equal("sync", response.FreeSpinFeatureMode);
        Assert.Equal(100, response.FreeSpinWagerPoints);
        Assert.Equal(10, report.Result["sealFreeSpinsAwarded"]);
        Assert.Equal("sync", report.Result["sealFreeSpinFeatureMode"]);
        Assert.Equal(round.Outcome.CollectorPoints, response.MoneyGrabPoints);
        var collector = Assert.Single(response.Payout.Paylines, line => line.PaylineId == 901);
        Assert.Equal(Multiply(response.MoneyGrabPoints), collector.AmountPoints);
        Assert.Equal(round.Outcome.Payout.Paylines.Sum(line => line.Matches.Sum(match => Multiply(match.AmountPoints))),
            response.Payout.TotalPoints);
        Assert.Equal(response.Payout.TotalPoints * 25, round.PayoutCents);
        Assert.Equal((double)(round.PayoutCents / 100m), report.Result["wonSlotsCredits"]);
        Assert.False(JsonSerializer.SerializeToElement(response, RoundAuditCapture.Json).TryGetProperty("chargedWagerCents", out _));

        var spin = round.Outcome.SpinId.ToString("N");
        AssertWitness(report, $"{spin}-energy-award", "energy:classic-demo-v1", 1, 100);
        AssertWitness(report, $"{spin}-energy-multiplier-use", "energy:classic-demo-v1", -100, 0);
        AssertWitness(report, $"{spin}-free-games-award", "freeGames:classic-demo-v1", 10, 10);
        Assert.Equal("wukong-journey-v1", report.Result["gameId"]);
        var summary = report.Round.Audit.Summary;
        Assert.Equal("wukong-journey-to-the-west", summary.GameId);
        var actor = Assert.Single(summary.Participants);
        Assert.Equal(2500, actor.WagerCents);
        Assert.Equal(round.PayoutCents, actor.PayoutCents);

        using var frozen = JsonDocument.Parse((string)report.Result["slotSettlementJson"]);
        Assert.Equal(response.Payout.TotalPoints, frozen.RootElement.GetProperty("Payout").GetProperty("TotalPoints").GetInt64());
        Assert.Equal(1.5m, frozen.RootElement.GetProperty("PayoutMultiplier").GetDecimal());
        using var proof = JsonDocument.Parse(report.Round.Audit.PayloadJson);
        var data = proof.RootElement.GetProperty("data");
        Assert.True(WukongSpinEvidence.IsNative(data));
        Assert.Equal(2500, data.GetProperty("result").GetProperty("chargedWagerCents").GetInt64());
        Assert.Equal(round.Outcome.Payout.TotalPoints, data.GetProperty("result").GetProperty("payout").GetProperty("totalPoints").GetInt64());
        Assert.Equal(WukongEngine.EngineVersion, data.GetProperty("engineAudit").GetProperty("engineVersion").GetString());
        Assert.Empty(WukongSpinEvidence.InspectNative(summary, data));

        var changed = JsonNode.Parse(data.GetRawText())!.AsObject();
        var draws = round.Engine.Draws.Append((byte)0).ToArray();
        changed["wukongDraws"] = Convert.ToBase64String(draws);
        AssertMismatch(summary, changed);
        changed["wukongDraws"] = Convert.ToBase64String(round.Engine.Draws);
        changed["payoutMultiplier"] = 1m;
        AssertMismatch(summary, changed);
        changed["payoutMultiplier"] = 1.5m;
        changed["wukongProgress"]!["energyAddedToMeter"] = 2;
        AssertMismatch(summary, changed);
    }

    [Fact]
    public void ImportedOddLockedWagerUsesTheSignedLegacyDebitAndReportsNoSecondDebit()
    {
        var before = new WukongRoundBefore(0, 2, new(2, 101, "paw"), 9, 0, 0, Empty(), Empty());
        var round = Prepare(before, new("wukong-imported-odd-lock", 101, true, UseSpecialBoost: true),
            outcome => outcome.CollectorPoints > 0);
        var settled = Settle(round);
        var response = WukongSpinResponse.From(settled);
        var report = WukongSpinReport.Capture(round, settled, round.Outcome.Audit.GeneratedAtUtc);
        Assert.True(response.IsFreeSpin);
        Assert.True(response.SpecialBoostApplied);
        Assert.Equal(101, response.WagerPoints);
        Assert.Equal(101, response.FreeSpinWagerPoints);
        Assert.Equal(1, response.FreeSpinsRemaining);
        Assert.Equal("paw", response.FreeSpinFeatureMode);
        Assert.Equal(0, round.WagerCents);
        Assert.Equal(1, settled.Wallet.LegacyFreeGamesRemaining);
        Assert.Equal($"{round.Outcome.SpinId:N}-free-game-use", settled.Wallet.LegacyFreeGameTransactionId);
        Assert.DoesNotContain(report.GameplayWitnesses.Keys, id => id.EndsWith("-free-game-use", StringComparison.Ordinal));
        AssertWitness(report, $"{round.AdmissionId:N}-special-boost-use", "specialPoints:classic-demo-v1", -8, 1);

        using var proof = JsonDocument.Parse(report.Round.Audit.PayloadJson);
        var data = proof.RootElement.GetProperty("data");
        Assert.Empty(WukongSpinEvidence.InspectNative(report.Round.Audit.Summary, data));
        var changed = JsonNode.Parse(data.GetRawText())!.AsObject();
        changed["wukongProgress"]!["spendsLegacy"] = false;
        AssertMismatch(report.Round.Audit.Summary, changed);
        var historical = JsonNode.Parse(data.GetRawText())!.AsObject();
        historical.Remove("wukongDraws");
        historical.Remove("wukongProgress");
        historical.Remove("engineOutcomeSha256");
        historical.Remove("walletSettlementId");
        var old = JsonSerializer.SerializeToElement(historical);
        Assert.False(WukongSpinEvidence.IsNative(old));
        Assert.Empty(WukongSpinEvidence.InspectNative(report.Round.Audit.Summary, old));
    }

    [Fact]
    public void FullMeterOnLossRemainsFullAndNativeConsumptionKeepsItsGameplayWitness()
    {
        var before = new WukongRoundBefore(2, 0, new(2, 100, "rows"), 0, 100, 0, Empty(), Empty());
        var round = Prepare(before, new("wukong-native-loss-meter", 100, true), outcome => outcome.Payout.TotalPoints == 0);
        var settled = Settle(round);
        var response = WukongSpinResponse.From(settled);
        var report = WukongSpinReport.Capture(round, settled, round.Outcome.Audit.GeneratedAtUtc);
        Assert.Equal(100, response.EnergyBalance);
        Assert.False(response.EnergyMultiplierApplied);
        Assert.Equal(1m, response.PayoutMultiplier);
        Assert.Equal(1, response.FreeSpinsRemaining);
        Assert.Equal("loss", report.Result["result"]);
        Assert.Null(settled.Wallet.LegacyFreeGameTransactionId);
        Assert.DoesNotContain(report.GameplayWitnesses.Keys, id => id.Contains("energy-", StringComparison.Ordinal));
        AssertWitness(report, $"{round.AdmissionId:N}-free-game-use", "freeGames:classic-demo-v1", -1, 1);
    }

    private static long Multiply(long points) => checked((long)Math.Round(points * 1.5m, MidpointRounding.AwayFromZero));

    private static WukongPreparedRound Prepare(WukongRoundBefore before, WukongRoundRequest request,
        Func<ClassicSlotSpinOutcome, bool> needed)
    {
        var draws = new SeededDraws();
        return Enumerable.Range(0, 512)
            .Select(_ => WukongRoundPolicy.Prepare(Account, request, before, DateTime.UtcNow, null, draws))
            .First(candidate => needed(candidate.Outcome));
    }

    private static WukongRoundResult Settle(WukongPreparedRound round)
    {
        const long before = 50_000;
        var spend = before - round.WagerCents;
        var spin = round.Outcome.SpinId.ToString("N");
        var receipt = new GameWalletSettlementReceipt(GameWalletSettlementIdentity.CreateId(WukongEngine.GameId, Account, spin),
            round.WagerCents > 0 ? $"{spin}-wager" : null, round.PayoutCents > 0 ? $"{spin}-payout" : null,
            before, spend, spend + round.PayoutCents, DateTimeOffset.UtcNow, false, false,
            LegacyFreeGameTransactionId: round.SpendsLegacy ? $"{spin}-free-game-use" : null,
            LegacyFreeGamesRemaining: round.Progress.After.LegacyRemaining);
        return new(WukongRoundStateReply.From(round.Progress.After, balanceCents: receipt.BalanceAfterCents), receipt)
        {
            Round = round,
            ChargedWagerCents = round.WagerCents
        };
    }

    private static void AssertWitness(WukongSpinReporting report, string id, string currency, long amount, long after)
    {
        var witness = report.GameplayWitnesses[id];
        Assert.Equal(id, witness["transactionId"]);
        Assert.Equal(id, witness["idempotencyKey"]);
        Assert.Equal(currency, witness["currencyId"]);
        Assert.Equal(amount, witness["amount"]);
        Assert.Equal(after, witness["balanceAfter"]);
    }

    private static void AssertMismatch(RoundAuditSummary summary, JsonObject data) =>
        Assert.Equal("slot-wukong-native-proof-mismatch", Assert.Single(WukongSpinEvidence.InspectNative(
            summary, JsonSerializer.SerializeToElement(data))).Code);

    private static IReadOnlyDictionary<string, long> Empty() => new Dictionary<string, long>();

    private sealed class SeededDraws : IGameRandomSource
    {
        private readonly Random random = new(7429);
        public int Next(int maximumExclusive) => random.Next(maximumExclusive);
    }
}
