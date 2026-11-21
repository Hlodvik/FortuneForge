using System.Text.Json;
using System.Text.Json.Nodes;
using FortuneForge.Games.Abstractions.Auditing;
using FortuneForge.Games.Abstractions.Wallet;
using FortuneForge.Games.GoldFoundry.Server;
using FortuneForge.Games.Slots;
using Xunit;

namespace FortuneForge.Games.GoldFoundry.Tests;

public sealed class GoldFoundrySpinReportingTests
{
    private const string Account = "foundry-report-compatibility";

    [Fact]
    public void CrucibleOnlyWinKeepsThePublicReelValueAtZeroAndReportsTheExactCentAward()
    {
        var round = Prepare(outcome => outcome.Payout.TotalPoints == 0);
        var result = Settle(round);
        var response = GoldFoundrySpinResponse.From(result);
        var report = GoldFoundrySpinReport.Capture(round, result, result.Wallet.SettledAtUtc.UtcDateTime);

        Assert.Equal(25m, response.PointValueInCents);
        Assert.Equal(0, response.Payout.TotalPoints);
        Assert.Equal(14_977, response.CruciblePayoutCents);
        Assert.Equal(174.77m, response.SlotsCreditsBalance);
        Assert.True(response.CruciblePoured);
        Assert.Equal(0, response.EnergyBalance);
        Assert.Equal(0, response.CrucibleWageredCents);
        Assert.Equal(0, response.CruciblePourValueCents);

        var publicJson = JsonSerializer.SerializeToElement(response, RoundAuditCapture.Json);
        Assert.False(publicJson.TryGetProperty("chargedWagerCents", out _));
        Assert.False(publicJson.TryGetProperty("payoutPointValueInCents", out _));
        Assert.Equal("win", report.Result["result"]);
        Assert.Equal(25d, report.Result["wageredSlotsCredits"]);
        Assert.Equal(149.77d, report.Result["wonSlotsCredits"]);
        Assert.Equal(124.77d, report.Result["netSlotsCredits"]);
        Assert.Equal(0L, PayoutBreakdown(report)["totalPoints"]);
        Assert.Equal(14_977L, report.Result["cruciblePayoutCents"]);

        var participant = Assert.Single(report.Round.Audit.Summary.Participants);
        Assert.Equal(2_500, participant.WagerCents);
        Assert.Equal(14_977, participant.PayoutCents);
        Assert.Equal(12_477, participant.NetCents);
        var spinId = round.Outcome.SpinId.ToString("N");
        Assert.Equal(2, report.EnergyWitnesses.Count);
        AssertWitness(report, $"{spinId}-energy-award", "energy-award", 1, 100);
        AssertWitness(report, $"{spinId}-energy-crucible-pour", "energy-crucible-pour", -100, 0);

        using var frozen = JsonDocument.Parse((string)report.Result["goldFoundrySettlementJson"]);
        Assert.Equal(0, frozen.RootElement.GetProperty("Payout").GetProperty("TotalPoints").GetInt64());
        Assert.Equal(14_977, frozen.RootElement.GetProperty("CruciblePayoutCents").GetInt64());
    }

    [Fact]
    public void ArchivedEvidenceReplaysReelAndCrucibleWinsAndRejectsAnAlteredReceipt()
    {
        var round = Prepare(outcome => outcome.CollectorPayoutPoints > 0 && outcome.Payout.Paylines.Count > 0);
        var result = Settle(round);
        var publicSpin = GoldFoundrySpinResponse.From(result);
        var report = GoldFoundrySpinReport.Capture(round, result, result.Wallet.SettledAtUtc.UtcDateTime);
        Assert.Equal(publicSpin.Payout.Paylines.Sum(line => line.AmountPoints) + publicSpin.MoneyGrabPoints,
            publicSpin.Payout.TotalPoints);
        Assert.True(publicSpin.MoneyGrabPoints > 0);
        Assert.Equal(publicSpin.Payout.TotalPoints, PayoutBreakdown(report)["totalPoints"]);
        var crucibleCents = Assert.IsType<long>(publicSpin.CruciblePayoutCents);
        Assert.Equal((double)((publicSpin.Payout.TotalPoints * 25 + crucibleCents) / 100m),
            report.Result["wonSlotsCredits"]);

        var summary = report.Round.Audit.Summary;
        using var proof = JsonDocument.Parse(report.Round.Audit.PayloadJson);
        var data = proof.RootElement.GetProperty("data");
        var raw = data.GetProperty("result");
        Assert.Equal(2_500, raw.GetProperty("chargedWagerCents").GetInt64());
        Assert.Equal(JsonValueKind.Null, raw.GetProperty("slotsCreditsBalance").ValueKind);
        Assert.False(raw.TryGetProperty("cruciblePayoutCents", out _));
        Assert.Equal(GoldFoundryCrucibleRules.RuleVersion,
            data.GetProperty("engineAudit").GetProperty("crucibleRuleVersion").GetString());
        Assert.Empty(GoldFoundrySpinEvidence.InspectNative(summary, data));

        var altered = JsonNode.Parse(data.GetRawText())!.AsObject();
        altered["walletSettlementId"] = "another-wallet-receipt";
        AssertNativeMismatch(altered, summary);
        altered["walletSettlementId"] = result.Wallet.SettlementId;
        altered["crucible"]!["settlement"]!["payoutCents"] = round.Crucible.PayoutCents + 1;
        AssertNativeMismatch(altered, summary);
        altered["crucible"]!["settlement"]!["payoutCents"] = round.Crucible.PayoutCents;
        altered["result"]!["payout"]!["totalPoints"] = round.Outcome.Payout.TotalPoints + 1;
        AssertNativeMismatch(altered, summary);

        var legacyEnvelope = JsonNode.Parse(data.GetRawText())!.AsObject();
        legacyEnvelope.Remove("engineOutcomeSha256");
        legacyEnvelope.Remove("walletSettlementId");
        Assert.Empty(GoldFoundrySpinEvidence.InspectNative(summary, JsonSerializer.SerializeToElement(legacyEnvelope)));

        var incorrectWallet = result with { Wallet = result.Wallet with { BalanceAfterCents = result.Wallet.BalanceAfterCents - 1 } };
        Assert.Throws<InvalidOperationException>(() => GoldFoundrySpinReport.Capture(round, incorrectWallet,
            result.Wallet.SettledAtUtc.UtcDateTime));
    }

    private static GoldFoundryPreparedRound Prepare(Func<GoldFoundrySpinOutcome, bool> needed)
    {
        var request = new GoldFoundryRoundRequest("foundry-reported-spin", 100, false);
        var before = new GoldFoundryRoundBefore(99, 97_349, 5);
        var random = new SeededDraws();
        for (var attempt = 0; attempt < 512; attempt++)
        {
            var round = GoldFoundryRoundPolicy.Prepare(Account, request, before, DateTime.UtcNow, null, random);
            if (round.Crucible.Poured && needed(round.Outcome)) return round;
        }
        throw new InvalidOperationException("The sample did not contain the required Foundry outcome.");
    }

    private static GoldFoundryRoundResult Settle(GoldFoundryPreparedRound round)
    {
        const long before = 5_000;
        var spend = before - round.WagerCents;
        var spinId = round.Outcome.SpinId.ToString("N");
        var receipt = new GameWalletSettlementReceipt(
            GameWalletSettlementIdentity.CreateId(GoldFoundryEngine.GameId, Account, spinId), $"{spinId}-wager",
            $"{spinId}-payout", before, spend, spend + round.PayoutCents, DateTimeOffset.UtcNow, false, false);
        return new(GoldFoundryRoundStateReply.From(round.After, balanceCents: receipt.BalanceAfterCents), receipt)
        {
            Round = round,
            ChargedWagerCents = round.WagerCents
        };
    }

    private static Dictionary<string, object> PayoutBreakdown(GoldFoundrySpinReporting report) =>
        Assert.IsType<Dictionary<string, object>>(report.Result["payoutBreakdown"]);

    private static void AssertWitness(GoldFoundrySpinReporting report, string id, string type, long amount, long after)
    {
        var witness = report.EnergyWitnesses[id];
        Assert.Equal(id, witness["transactionId"]);
        Assert.Equal(id, witness["idempotencyKey"]);
        Assert.Equal(Account, witness["userId"]);
        Assert.Equal($"energy:{GoldFoundryEngine.GameId}", witness["currencyId"]);
        Assert.Equal(type, witness["type"]);
        Assert.Equal(amount, witness["amount"]);
        Assert.Equal(after, witness["balanceAfter"]);
    }

    private static void AssertNativeMismatch(JsonObject altered, RoundAuditSummary summary) =>
        Assert.Equal("slot-foundry-native-proof-mismatch", Assert.Single(GoldFoundrySpinEvidence.InspectNative(
            summary, JsonSerializer.SerializeToElement(altered))).Code);

    private sealed class SeededDraws : IGameRandomSource
    {
        private readonly Random random = new(4029);
        public int Next(int maximumExclusive) => random.Next(maximumExclusive);
    }
}
